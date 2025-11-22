using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OpenGaugeAbstractions;

[DataSourceName("DerailValley")]
public class DerailValleyDataSource : DataSourceBase
{
    private ClientWebSocket _socket;
    private CancellationTokenSource? _cts;
    private readonly object _sendLock = new();
    private readonly Uri _uri = new("ws://localhost:9450/dv"); // TODO: Configure
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    public override string? CurrentVehicleName { get; set; } = "???";
    private readonly Dictionary<(string VarName, string Unit), List<Action<object>>> _varCallbacksByKey = new();
    private readonly Dictionary<string, List<Action<object>>> _eventCallbacksByKey = new();
    private Action<string>? _vehicleCallback;

    public DerailValleyDataSource(Config config) {}

    public override async Task Connect()
    {
        try
        {
            if (IsConnected) return;
            _cts = new CancellationTokenSource();

            Console.WriteLine($"[DerailValley] Connecting");

            _socket = new ClientWebSocket();

            await _socket.ConnectAsync(_uri, CancellationToken.None);
            Console.WriteLine($"[DerailValley] Socket has opened");
            IsConnected = true;
            
            SubscribeToEvent("CAR_NAME_CHANGED", value =>
            {
                string? vehicleName = value switch
                {
                    JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                    string s => s,
                    null => null,
                    _ => value.ToString()
                };

                NotifyNewVehicle(vehicleName);
            });

            Console.WriteLine($"[DerailValley] Telling server we want to init...");

            Send(new { Type = MessageType.Init });

            _ = Task.Run(() => ReceiveLoop(_cts!.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerailValley] Failed to connect: {ex}");
        }
    }

    public override async Task SubscribeToVehicle(Action<string> callback)
    {
        _vehicleCallback += callback;
        Console.WriteLine($"[DerailValley] Subscribed to vehicle change");
    }

    public override async Task Disconnect()
    {
        if (!IsConnected) return;
        IsConnected = false;
        _cts?.Cancel();
        _socket.Abort();
        _socket.Dispose();
        _socket = new ClientWebSocket();
    }

    public override async Task Listen()
    {
    }

    public override async Task SubscribeToVar(string varName, string unit, Action<object> callback)
    {
        var key = GetKey(varName, unit);

        if (!_varCallbacksByKey.ContainsKey(key))
        {
            _varCallbacksByKey[key] = new List<Action<object>>();
        }

        _varCallbacksByKey[key].Add(callback);

        Send(new Message<SubscribeToVarPayload>
        {
            Type = MessageType.SubscribeToVar,
            Payload = new SubscribeToVarPayload {
                Name = varName,
                Unit = unit
            }
        });
        
        Console.WriteLine($"[DerailValley] Subscribed to var '{varName}' ({unit})");
    }

    public override async Task UnsubscribeFromVar(string varName, string unit, Action<object?> callback)
    {
        Send(new Message<UnsubscribeFromVarPayload>
        {
            Type = MessageType.SubscribeToVar,
            Payload = new UnsubscribeFromVarPayload {
                Name = varName,
                Unit = unit
            }
        });
        
        Console.WriteLine($"[DerailValley] Unsubscribed from var '{varName}' ({unit})");
    }

    private void NotifyNewVehicle(string? vehicleName)
    {
        Console.WriteLine($"[DerailValley] New train '{vehicleName}'");
        CurrentVehicleName = vehicleName;
        _vehicleCallback?.Invoke(vehicleName);
    }

    private void NotifyVarSubscribers(string varName, string unit, object value)
    {
        foreach (var kvp in _varCallbacksByKey)
        {
            var (VarName, Unit) = kvp.Key;

            if (VarName == varName && Unit == unit)
            {
                var callbacks = kvp.Value;

                foreach (var cb in callbacks)
                {
                    cb.Invoke(value);
                }
            }
        }
    }

    public override async Task SubscribeToEvent(string eventName, Action<object> callback)
    {
        var key = eventName;

        if (!_eventCallbacksByKey.ContainsKey(key))
        {
            _eventCallbacksByKey[key] = new List<Action<object>>();
        }

        _eventCallbacksByKey[key].Add(callback);

        Send(new Message<SubscribeToEventPayload>
        {
            Type = MessageType.SubscribeToEvent,
            Payload = new SubscribeToEventPayload {
                Name = eventName
            }
        });
    }

    public override async Task UnsubscribeFromEvent(string eventName, Action<object> callback)
    {
        var key = eventName;

        if (!_eventCallbacksByKey.ContainsKey(key))
        {
            return;
        }

        Send(new Message<UnsubscribeFromEventPayload>
        {
            Type = MessageType.UnsubscribeFromEvent,
            Payload = new UnsubscribeFromEventPayload {
                Name = eventName
            }
        });
        
        _eventCallbacksByKey[key].Remove(callback);
    }

    private void NotifyEventSubscribers(string eventName, object value)
    {
        foreach (var kvp in _eventCallbacksByKey)
        {
            var EventName = kvp.Key;

            if (EventName == eventName)
            {
                var callbacks = kvp.Value;

                foreach (var cb in callbacks)
                {
                    cb.Invoke(value);
                }
            }
        }
    }

    private void Send(object payload)
    {
        try {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var json = JsonSerializer.Serialize(payload, options);
            var bytes = Encoding.UTF8.GetBytes(json);

            lock (_sendLock)
            {
                _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerailValley] Failed to send: {ex}");
        }
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        try
        {
            var buffer = new byte[8192];

            while (!token.IsCancellationRequested)
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(buffer, token);

                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Text:
                            string json = "";

                            try {
                                json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    IncludeFields = true,
                                    Converters = { new JsonStringEnumConverter() }
                                };

                                var message = JsonSerializer.Deserialize<Message<object>>(json, options);

                                switch (message.Type)
                                {
                                    case MessageType.Init:
                                        var initPayload = ((JsonElement)message.Payload).Deserialize<InitPayload>(options);

                                        CurrentVehicleName = initPayload.CarName;
                                        
                                        Console.WriteLine($"[DerailValley] Initialize with vehicle '{CurrentVehicleName}'");
                                        break;

                                    case MessageType.Var:
                                        var varPayload = ((JsonElement)message.Payload).Deserialize<VarPayload>(options);

                                        // Console.WriteLine($"[DerailValley] Var name={varPayload.Name} unit={varPayload.Unit} value={varPayload.Value}");

                                        NotifyVarSubscribers(varPayload.Name, varPayload.Unit, varPayload.Value);
                                        break;

                                    case MessageType.Event:
                                        var eventPayload = ((JsonElement)message.Payload).Deserialize<EventPayload>(options);

                                        NotifyEventSubscribers(eventPayload.Name, eventPayload.Value);
                                        break;

                                    case MessageType.Error:
                                        var errorPayload = ((JsonElement)message.Payload).Deserialize<ErrorPayload>(options);

                                        Console.WriteLine($"[DerailValley] Remote error: {errorPayload.Message}");
                                        break;

                                    default:
                                        throw new Exception($" Unknown message type '{message.Type}'");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[DerailValley] Failed: {ex} json={json}");
                            }
                        break;

                        case WebSocketMessageType.Close:
                            Console.WriteLine($"[DerailValley] Socket wants to close");

                            IsConnected = false;

                            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerailValley] Failed: {ex}");
        }
    }

    private static (string VarName, string? Unit) GetKey(string varName, string? unit)
    {
        return (varName.ToLower(), unit?.ToLower());
    }
}