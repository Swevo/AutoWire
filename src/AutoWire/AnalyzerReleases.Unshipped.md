### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
AW001   | AutoWire | Warning  | Abstract class decorated with AutoWire attribute
AW002   | AutoWire | Info     | Multiple non-keyed registrations for the same service type
AW003   | AutoWire | Error    | Class does not implement the specified service type
AW004   | AutoWire | Warning  | Singleton depends on a Scoped service (captive dependency)
AW012   | AutoWire | Error    | Decorator does not implement the decorated service type
AW013   | AutoWire | Warning  | Constructor parameter type is not registered with AutoWire
