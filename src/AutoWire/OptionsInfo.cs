namespace AutoWire;

internal sealed class OptionsInfo
{
    public string ImplementationType { get; }
    public string Section { get; }
    public bool ValidateDataAnnotations { get; }
    public bool ValidateOnStart { get; }

    public OptionsInfo(string implementationType, string section, bool validateDataAnnotations, bool validateOnStart)
    {
        ImplementationType = implementationType;
        Section = section;
        ValidateDataAnnotations = validateDataAnnotations;
        ValidateOnStart = validateOnStart;
    }

    public override bool Equals(object? obj) =>
        obj is OptionsInfo other &&
        ImplementationType == other.ImplementationType &&
        Section == other.Section &&
        ValidateDataAnnotations == other.ValidateDataAnnotations &&
        ValidateOnStart == other.ValidateOnStart;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = ImplementationType.GetHashCode();
            h = h * 397 ^ Section.GetHashCode();
            h = h * 397 ^ ValidateDataAnnotations.GetHashCode();
            h = h * 397 ^ ValidateOnStart.GetHashCode();
            return h;
        }
    }
}
