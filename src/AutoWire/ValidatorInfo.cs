namespace AutoWire;

internal sealed class ValidatorInfo
{
    public string ValidatorType { get; }
    public string ValidatedType { get; }

    public ValidatorInfo(string validatorType, string validatedType)
    {
        ValidatorType = validatorType;
        ValidatedType = validatedType;
    }

    public override bool Equals(object? obj) =>
        obj is ValidatorInfo other &&
        ValidatorType == other.ValidatorType &&
        ValidatedType == other.ValidatedType;

    public override int GetHashCode()
    {
        unchecked
        {
            return ValidatorType.GetHashCode() * 397 ^ ValidatedType.GetHashCode();
        }
    }
}
