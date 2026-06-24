using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AutoWire;

/// <summary>
/// Equatable wrapper around a diagnostic, safe to pass through Roslyn incremental pipelines.
/// </summary>
internal sealed class DiagnosticInfo
{
    private readonly string _id;
    private readonly string[] _messageArgs;
    private readonly string _filePath;
    private readonly TextSpan _sourceSpan;
    private readonly LinePositionSpan _lineSpan;

    public DiagnosticInfo(string id, Location location, string[] messageArgs)
    {
        _id = id;
        _messageArgs = messageArgs;
        var mapped = location.GetLineSpan();
        _filePath = mapped.Path ?? string.Empty;
        _sourceSpan = location.SourceSpan;
        _lineSpan = mapped.Span;
    }

    public Diagnostic Create(DiagnosticDescriptor descriptor) =>
        Diagnostic.Create(
            descriptor,
            Location.Create(_filePath, _sourceSpan, _lineSpan),
            _messageArgs);

    public override bool Equals(object? obj) =>
        obj is DiagnosticInfo other &&
        _id == other._id &&
        _filePath == other._filePath &&
        _sourceSpan == other._sourceSpan;

    public override int GetHashCode()
    {
        unchecked
        {
            var h = _id?.GetHashCode() ?? 0;
            h = h * 397 ^ (_filePath?.GetHashCode() ?? 0);
            h = h * 397 ^ _sourceSpan.GetHashCode();
            return h;
        }
    }
}
