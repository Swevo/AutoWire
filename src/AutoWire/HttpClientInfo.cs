namespace AutoWire;

internal sealed class HttpClientInfo
{
    public string ImplementationType { get; }
    public string? Name { get; }
    public string? BaseAddress { get; }
    public bool Resilience { get; }

    public HttpClientInfo(string implementationType, string? name, string? baseAddress, bool resilience = false)
    {
        ImplementationType = implementationType;
        Name = name;
        BaseAddress = baseAddress;
        Resilience = resilience;
    }

    public override bool Equals(object? obj) =>
        obj is HttpClientInfo other &&
        ImplementationType == other.ImplementationType &&
        Name == other.Name &&
        BaseAddress == other.BaseAddress &&
        Resilience == other.Resilience;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ImplementationType.GetHashCode();
            h = h * 397 ^ (Name?.GetHashCode() ?? 0);
            h = h * 397 ^ (BaseAddress?.GetHashCode() ?? 0);
            h = h * 397 ^ Resilience.GetHashCode();
            return h;
        }
    }
}
