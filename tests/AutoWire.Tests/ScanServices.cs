// Services used to verify convention-based scanning via [assembly: AutoWireScan("AutoWireTests.Scan")]

namespace AutoWireTests.Scan
{
    public interface IScanService { string GetId(); }

    // Registered by scan (no explicit attribute)
    public class ScanServiceA : IScanService { public string GetId() => "A"; }
    public class ScanServiceB : IScanService { public string GetId() => "B"; }

    // Excluded via [AutoWireExclude] — should never appear in the container
    [AutoWireExclude]
    public class ExcludedScanService : IScanService { public string GetId() => "excluded"; }

    // Abstract — always skipped by scanning
    public abstract class AbstractScanService : IScanService { public abstract string GetId(); }

    // Has an explicit [Scoped] attribute — scan should not double-register it
    [Scoped]
    public class ExplicitlyScopedService : IScanService { public string GetId() => "explicit"; }
}

namespace AutoWireTests.Scan.Sub
{
    public interface ISubScanService { string GetLabel(); }

    // Sub-namespace is included by default (IncludeSubNamespaces = true)
    public class SubScanServiceX : ISubScanService { public string GetLabel() => "X"; }
}
