namespace AutoWire;

internal enum DuplicateStrategy
{
    Add     = 0, // AddScoped/Singleton/Transient (default, last-registration-wins)
    Skip    = 1, // TryAddScoped/Singleton/Transient (only registers if not already present)
    Replace = 2  // RemoveAll + AddScoped/Singleton/Transient (explicit winner)
}
