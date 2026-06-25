using System.Collections.Immutable;

namespace AutoWire;

internal sealed class HttpClientInfo
{
    public string ImplementationType { get; }
    public string? Name { get; }
    public string? BaseAddress { get; }
    public bool Resilience { get; }
    public int? TimeoutSeconds { get; }
    public ImmutableArray<string> DefaultHeaders { get; }
    public bool UseFactory { get; }

    public HttpClientInfo(
        string implementationType,
        string? name,
        string? baseAddress,
        bool resilience = false,
        int? timeoutSeconds = null,
        ImmutableArray<string> defaultHeaders = default,
        bool useFactory = false)
    {
        ImplementationType = implementationType;
        Name = name;
        BaseAddress = baseAddress;
        Resilience = resilience;
        TimeoutSeconds = timeoutSeconds;
        DefaultHeaders = defaultHeaders.IsDefault ? ImmutableArray<string>.Empty : defaultHeaders;
        UseFactory = useFactory;
    }

    public override bool Equals(object? obj) =>
        obj is HttpClientInfo other &&
        ImplementationType == other.ImplementationType &&
        Name == other.Name &&
        BaseAddress == other.BaseAddress &&
        Resilience == other.Resilience &&
        TimeoutSeconds == other.TimeoutSeconds;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ImplementationType.GetHashCode();
            h = h * 397 ^ (Name?.GetHashCode() ?? 0);
            h = h * 397 ^ (BaseAddress?.GetHashCode() ?? 0);
            h = h * 397 ^ Resilience.GetHashCode();
            h = h * 397 ^ (TimeoutSeconds?.GetHashCode() ?? 0);
            return h;
        }
    }
}
