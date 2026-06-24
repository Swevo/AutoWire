using System.Collections.Generic;
using System.Collections.Immutable;

namespace AutoWire;

internal sealed class RegistrationInfo
{
    public string ImplementationType { get; }
    public ImmutableArray<string> ServiceTypes { get; }
    public string Lifetime { get; }
    public string? Key { get; }
    public bool IsOpenGeneric { get; }
    public DuplicateStrategy DuplicateStrategy { get; }

    public RegistrationInfo(
        string implementationType,
        ImmutableArray<string> serviceTypes,
        string lifetime,
        string? key,
        bool isOpenGeneric = false,
        DuplicateStrategy duplicateStrategy = DuplicateStrategy.Add)
    {
        ImplementationType = implementationType;
        ServiceTypes = serviceTypes;
        Lifetime = lifetime;
        Key = key;
        IsOpenGeneric = isOpenGeneric;
        DuplicateStrategy = duplicateStrategy;
    }

    public override bool Equals(object? obj) =>
        obj is RegistrationInfo other &&
        ImplementationType == other.ImplementationType &&
        SequenceEqual(ServiceTypes, other.ServiceTypes) &&
        Lifetime == other.Lifetime &&
        Key == other.Key &&
        IsOpenGeneric == other.IsOpenGeneric &&
        DuplicateStrategy == other.DuplicateStrategy;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ImplementationType.GetHashCode();
            h = h * 397 ^ Lifetime.GetHashCode();
            h = h * 397 ^ (Key?.GetHashCode() ?? 0);
            h = h * 397 ^ IsOpenGeneric.GetHashCode();
            h = h * 397 ^ DuplicateStrategy.GetHashCode();
            foreach (var s in ServiceTypes)
                h = h * 397 ^ s.GetHashCode();
            return h;
        }
    }

    private static bool SequenceEqual(ImmutableArray<string> a, ImmutableArray<string> b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
