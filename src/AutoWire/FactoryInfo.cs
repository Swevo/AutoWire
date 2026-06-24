namespace AutoWire;

internal sealed class FactoryInfo
{
    public string FactoryType { get; }
    public string ProductType { get; }
    public string ProductLifetime { get; }
    public string FactoryLifetime { get; }

    public FactoryInfo(string factoryType, string productType, string productLifetime, string factoryLifetime)
    {
        FactoryType = factoryType;
        ProductType = productType;
        ProductLifetime = productLifetime;
        FactoryLifetime = factoryLifetime;
    }

    public override bool Equals(object? obj) =>
        obj is FactoryInfo other &&
        FactoryType == other.FactoryType &&
        ProductType == other.ProductType &&
        ProductLifetime == other.ProductLifetime &&
        FactoryLifetime == other.FactoryLifetime;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = FactoryType.GetHashCode();
            h = h * 397 ^ ProductType.GetHashCode();
            h = h * 397 ^ ProductLifetime.GetHashCode();
            h = h * 397 ^ FactoryLifetime.GetHashCode();
            return h;
        }
    }
}
