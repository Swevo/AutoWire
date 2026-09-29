### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AW001   | AutoWire | Warning  | Abstract class decorated with AutoWire attribute
AW002   | AutoWire | Info     | Multiple non-keyed registrations for the same service type
AW003   | AutoWire | Error    | Class does not implement the specified service type
AW004   | AutoWire | Warning  | Singleton depends on a Scoped service (captive dependency)
AW012   | AutoWire | Error    | Decorator does not implement the decorated service type
AW013   | AutoWire | Warning  | Constructor parameter type is not registered with AutoWire
AW014   | AutoWire | Error    | [ScanAssembly] marker must come from a referenced assembly
AW015   | AutoWire | Warning  | [ScanAssembly] found no attributed services
AW016   | AutoWire | Error    | Circular dependency detected between AutoWire-registered services
AW017   | AutoWire | Info     | AutoWire registration appears unused
AW018   | AutoWire | Info     | Manual IServiceCollection registration can be migrated to AutoWire
