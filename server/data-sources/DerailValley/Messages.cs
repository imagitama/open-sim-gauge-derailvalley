public enum MessageType
{
    Init,
    Var,
    Event,
    SubscribeToVar,
    SubscribeToEvent,
    UnsubscribeFromVar,
    UnsubscribeFromEvent,
    Error
}

public class Message<TPayload>
{
    public MessageType Type;
    public TPayload Payload;
}

public class Payload {}

public class InitPayload : Payload
{
    public string? CarName;
}

public class SubscribeToVarPayload : Payload
{
    public string Name;
    public string Unit;
}

public class UnsubscribeFromVarPayload : Payload
{
    public string Name;
    public string Unit;
}

public class SubscribeToEventPayload : Payload
{
    public string Name;
}

public class UnsubscribeFromEventPayload : Payload
{
    public string Name;
}

public class VarPayload : Payload
{
    public string Name;
    public string Unit;
    public object Value;
}

public class EventPayload : Payload
{
    public string Name;
    public object Value;
}

public class ErrorPayload : Payload
{
    public string Message;
}
