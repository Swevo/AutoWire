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
AW019   | AutoWire | Info     | Multiple keyed registrations for the same service type and key
AW020   | AutoWire | Warning  | Multiple decorators use the same service/lifetime/order tuple
AW021   | AutoWire | Error    | Open generic decorator targets are not supported
AW022   | AutoWire | Warning  | Invalid runtime registration condition format
AW023   | AutoWire | Warning  | Runtime registration condition requires IConfiguration support
AW024   | AutoWire | Warning  | Profile is empty or whitespace
AW025   | AutoWire | Warning  | Module registration profile is ignored
AW026   | AutoWire | Info     | Scrutor scan registration can be migrated to AutoWire
