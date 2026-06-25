namespace AutoWire;

internal sealed class HttpClientInfo
{
    public string ImplementationType { get; }
    public string? Name { get; }
    public string? BaseAddress { get; }

    public HttpClientInfo(string implementationType, string? name, string? baseAddress)
    {
        ImplementationType = implementationType;
        Name = name;
        BaseAddress = baseAddress;
    }

    public override bool Equals(object? obj) =>
        obj is HttpClientInfo other &&
        ImplementationType == other.ImplementationType &&
        Name == other.Name &&
        BaseAddress == other.BaseAddress;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ImplementationType.GetHashCode();
            h = h * 397 ^ (Name?.GetHashCode() ?? 0);
            h = h * 397 ^ (BaseAddress?.GetHashCode() ?? 0);
            return h;
        }
    }
}
