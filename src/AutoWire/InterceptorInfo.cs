using System.Collections.Immutable;

namespace AutoWire;

internal sealed class MethodSignature
{
    public string Name { get; }
    public string ReturnType { get; }
    public ImmutableArray<(string Type, string Name)> Parameters { get; }
    public bool IsVoid { get; }
    public bool IsTask { get; }
    public bool IsTaskOfT { get; }
    public string? TaskResultType { get; }

    public MethodSignature(
        string name,
        string returnType,
        ImmutableArray<(string Type, string Name)> parameters,
        bool isVoid,
        bool isTask,
        bool isTaskOfT,
        string? taskResultType)
    {
        Name = name;
        ReturnType = returnType;
        Parameters = parameters;
        IsVoid = isVoid;
        IsTask = isTask;
        IsTaskOfT = isTaskOfT;
        TaskResultType = taskResultType;
    }
}

internal sealed class InterceptorInfo
{
    public string InterceptorType { get; }
    public string ServiceType { get; }
    public string Lifetime { get; }
    public string ProxyClassName { get; }
    public ImmutableArray<MethodSignature> Methods { get; }

    public InterceptorInfo(
        string interceptorType,
        string serviceType,
        string lifetime,
        string proxyClassName,
        ImmutableArray<MethodSignature> methods)
    {
        InterceptorType = interceptorType;
        ServiceType = serviceType;
        Lifetime = lifetime;
        ProxyClassName = proxyClassName;
        Methods = methods;
    }

    public override bool Equals(object? obj) =>
        obj is InterceptorInfo other &&
        InterceptorType == other.InterceptorType &&
        ServiceType == other.ServiceType &&
        Lifetime == other.Lifetime;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = InterceptorType.GetHashCode();
            h = h * 397 ^ ServiceType.GetHashCode();
            h = h * 397 ^ Lifetime.GetHashCode();
            return h;
        }
    }
}
