// Convention scan: registers all classes in AutoWire.Sample.Worker.Services
// as Scoped without needing per-class attributes.
// Explicit [Singleton] / [Transient] attributes override the scan default.
[assembly: AutoWire.AutoWireScan("AutoWire.Sample.Worker.Services")]
