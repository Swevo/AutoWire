// Convention scanning: every non-abstract class in AutoWire.Sample.Api.Services
// is registered as Scoped automatically — no per-class attributes needed.
[assembly: AutoWire.AutoWireScan("AutoWire.Sample.Api.Services")]
