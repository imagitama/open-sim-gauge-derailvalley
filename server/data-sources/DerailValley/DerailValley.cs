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
    private readonly string _ipAddress = "localhost";
    private readonly int _port = 9450;
    private readonly Uri _uri;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    public override string? CurrentVehicleName { get; set; } = "???";
    private readonly Dictionary<(string VarName, string? Unit), List<Action<object>>> _varCallbacksByKey = new();
    private readonly Dictionary<string, List<Action<object>>> _eventCallbacksByKey = new();
    private Action<string?>? _vehicleCallback;
    private DataSourceOptions? _options;

    public class DataSourceOptions
    {
        public string? IpAddress { get; set; }
        public int? Port { get; set; }
    }

    public DerailValleyDataSource(Config config)
    {
        // TODO: have caller deserialize and provide this to us
        if (config.SourceOptions is not null)
        {
            var jsonOptions = new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            _options = config.SourceOptions.Value.Deserialize<DataSourceOptions>(jsonOptions);

            if (_options is null)
                throw new InvalidOperationException("Invalid SourceOptions");

            if (_options.IpAddress != null)
                _ipAddress = _options.IpAddress;

            if (_options.Port != null)
                _port = (int)_options.Port;
        }

        _uri = new($"ws://{_ipAddress}:{_port}/dv");
    }

    public override async Task Connect()
    {
        try
        {
            if (IsConnected) return;
            _cts = new CancellationTokenSource();

            Console.WriteLine($"[DerailValley] Connecting to {_uri}...");

            _socket = new ClientWebSocket();

            await _socket.ConnectAsync(_uri, CancellationToken.None);
            Console.WriteLine($"[DerailValley] Socket has opened");
            IsConnected = true;
            
            _ = SubscribeToEvent("CAR_NAME_CHANGED", value =>
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

            _ = Send(new { Type = MessageType.Init });

            _ = Task.Run(() => ReceiveLoop(_cts!.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerailValley] Failed to connect: {ex.Message}");
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

    public override async Task SubscribeToVar(string varName, string? unit, Action<object> callback)
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

    public override async Task UnsubscribeFromVar(string varName, string? unit, Action<object?> callback)
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

    private void NotifyNewVehicle(string? newVehicleName)
    {
        var oldVehicleName = CurrentVehicleName;
        CurrentVehicleName = newVehicleName;
        Console.WriteLine($"[DerailValley] New train {(oldVehicleName == null ? "null" : $"'{oldVehicleName}'")} => {(newVehicleName == null ? "null" : $"'{newVehicleName}'")}");
        _vehicleCallback?.Invoke(newVehicleName);
    }

    private void NotifyVarSubscribers(string varName, string? unit, object value)
    {
        var varCallbacksByKey = _varCallbacksByKey.ToDictionary(); // avoid InvalidOperationException
        foreach (var kvp in varCallbacksByKey)
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
        var eventCallbacksByKey = _eventCallbacksByKey.ToDictionary(); // avoid InvalidOperationException
        foreach (var kvp in eventCallbacksByKey)
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

    private async Task Send(object payload)
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

            // TODO: wait for a reply
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerailValley] Failed to send: {ex.Message}");
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

                                var message = JsonSerializer.Deserialize<Message<object>>(json, options) ?? throw new Exception("Message is null");

                                switch (message.Type)
                                {
                                    case MessageType.Init:
                                    {
                                        var payload = ((JsonElement)message.Payload).Deserialize<InitPayload>(options) ?? throw new Exception("Payload is null");

                                        CurrentVehicleName = payload.CarName;
                                        
                                        Console.WriteLine($"[DerailValley] Initialize with vehicle {(CurrentVehicleName == null ? "null" : $"'{CurrentVehicleName}'")}");
                                        break;
                                    }

                                    case MessageType.Var:
                                    {
                                        var payload = ((JsonElement)message.Payload).Deserialize<VarPayload>(options) ?? throw new Exception("Payload is null");
                                        // Console.WriteLine($"[DerailValley] Var name={payload.Name} unit={payload.Unit} value={payload.Value}");

                                        NotifyVarSubscribers(payload.Name, payload.Unit, payload.Value);
                                        break;
                                    }

                                    case MessageType.Event:
                                    {
                                        var payload = ((JsonElement)message.Payload).Deserialize<EventPayload>(options) ?? throw new Exception("Payload is null");

                                        NotifyEventSubscribers(payload.Name, payload.Value);
                                        break;
                                    }

                                    case MessageType.Error:
                                    {
                                        var payload = ((JsonElement)message.Payload).Deserialize<ErrorPayload>(options) ?? throw new Exception("Payload is null");

                                        Console.WriteLine($"[DerailValley] Remote error: {payload.Message}");
                                        break;
                                    }

                                    default:
                                        throw new Exception($"[DerailValley] Unknown message type '{message.Type}'");
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
            Console.WriteLine($"[DerailValley] Failed: {ex.Message}");
        }
    }

    private static (string VarName, string? Unit) GetKey(string varName, string? unit)
    {
        return (varName.ToLower(), unit?.ToLower());
    }
}