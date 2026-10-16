namespace AutoWire;

internal sealed class DecoratorInfo
{
    public string DecoratorType { get; }
    public string ServiceType { get; }
    public string Lifetime { get; }
    public int Order { get; }

    /// <summary>
    /// For open-generic decorators, the unbound service definition (e.g. <c>global::App.IRepo&lt;&gt;</c>).
    /// Null for closed decorators.
    /// </summary>
    public string? OpenServiceDefinition { get; }

    /// <summary>
    /// For open-generic decorators, maps each decorator type parameter (by position) to the index of the
    /// service type argument it is bound to, joined by commas (e.g. <c>"1,0"</c>).
    /// </summary>
    public string? ArgumentMap { get; }

    public bool IsOpenGeneric => OpenServiceDefinition is not null;

    public DecoratorInfo(
        string decoratorType,
        string serviceType,
        string lifetime,
        int order = 0,
        string? openServiceDefinition = null,
        string? argumentMap = null)
    {
        DecoratorType = decoratorType;
        ServiceType = serviceType;
        Lifetime = lifetime;
        Order = order;
        OpenServiceDefinition = openServiceDefinition;
        ArgumentMap = argumentMap;
    }

    public override bool Equals(object? obj) =>
        obj is DecoratorInfo other &&
        DecoratorType == other.DecoratorType &&
        ServiceType == other.ServiceType &&
        Lifetime == other.Lifetime &&
        Order == other.Order &&
        OpenServiceDefinition == other.OpenServiceDefinition &&
        ArgumentMap == other.ArgumentMap;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = DecoratorType.GetHashCode();
            h = h * 397 ^ ServiceType.GetHashCode();
            h = h * 397 ^ Lifetime.GetHashCode();
            h = h * 397 ^ Order.GetHashCode();
            h = h * 397 ^ (OpenServiceDefinition?.GetHashCode() ?? 0);
            h = h * 397 ^ (ArgumentMap?.GetHashCode() ?? 0);
            return h;
        }
    }
}
