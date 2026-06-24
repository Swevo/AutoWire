namespace AutoWire;

internal sealed class DecoratorInfo
{
    public string DecoratorType { get; }
    public string ServiceType { get; }
    public string Lifetime { get; }
    public int Order { get; }

    public DecoratorInfo(string decoratorType, string serviceType, string lifetime, int order = 0)
    {
        DecoratorType = decoratorType;
        ServiceType = serviceType;
        Lifetime = lifetime;
        Order = order;
    }

    public override bool Equals(object? obj) =>
        obj is DecoratorInfo other &&
        DecoratorType == other.DecoratorType &&
        ServiceType == other.ServiceType &&
        Lifetime == other.Lifetime &&
        Order == other.Order;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = DecoratorType.GetHashCode();
            h = h * 397 ^ ServiceType.GetHashCode();
            h = h * 397 ^ Lifetime.GetHashCode();
            h = h * 397 ^ Order.GetHashCode();
            return h;
        }
    }
}
