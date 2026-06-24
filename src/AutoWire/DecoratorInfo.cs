namespace AutoWire;

internal sealed class DecoratorInfo
{
    public string DecoratorType { get; }
    public string ServiceType { get; }
    public string Lifetime { get; }

    public DecoratorInfo(string decoratorType, string serviceType, string lifetime)
    {
        DecoratorType = decoratorType;
        ServiceType = serviceType;
        Lifetime = lifetime;
    }

    public override bool Equals(object? obj) =>
        obj is DecoratorInfo other &&
        DecoratorType == other.DecoratorType &&
        ServiceType == other.ServiceType &&
        Lifetime == other.Lifetime;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = DecoratorType.GetHashCode();
            h = h * 397 ^ ServiceType.GetHashCode();
            h = h * 397 ^ Lifetime.GetHashCode();
            return h;
        }
    }
}
