namespace AutoWire;

/// <summary>
/// Describes a class decorated with <c>[Endpoint(method, route)]</c> exposing a
/// <c>public static Handle</c>/<c>HandleAsync</c> method to be mapped as a minimal API endpoint.
/// </summary>
internal sealed class EndpointInfo
{
    public string ClassType { get; }
    public string Method { get; }
    public string Route { get; }
    public string HandlerMethodName { get; }

    public EndpointInfo(string classType, string method, string route, string handlerMethodName)
    {
        ClassType = classType;
        Method = method;
        Route = route;
        HandlerMethodName = handlerMethodName;
    }

    public override bool Equals(object? obj) =>
        obj is EndpointInfo other &&
        ClassType == other.ClassType &&
        Method == other.Method &&
        Route == other.Route &&
        HandlerMethodName == other.HandlerMethodName;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ClassType.GetHashCode();
            h = h * 397 ^ Method.GetHashCode();
            h = h * 397 ^ Route.GetHashCode();
            h = h * 397 ^ HandlerMethodName.GetHashCode();
            return h;
        }
    }
}
