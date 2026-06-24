// ──────────────────────────────────────────────────────────────────────────────
// Services used by BOTH benchmarks.
// AutoWire registers these at compile time via [Scoped] / [Singleton].
// Scrutor registers these at runtime via assembly scanning.
// ──────────────────────────────────────────────────────────────────────────────

namespace AutoWire.Benchmarks.Services;

public interface IOrderService { }
public interface IProductService { }
public interface ICustomerService { }
public interface IInventoryService { }
public interface IPaymentService { }
public interface IShippingService { }
public interface INotificationService { }
public interface IReportService { }
public interface IAnalyticsService { }
public interface IAuditService { }
public interface ICacheService { }
public interface ISearchService { }
public interface IEmailService { }
public interface ISmsService { }
public interface IPushService { }
public interface IUserService { }
public interface IRoleService { }
public interface IPermissionService { }
public interface ISettingsService { }
public interface ILocalizationService { }

[Scoped] public class OrderService : IOrderService { }
[Scoped] public class ProductService : IProductService { }
[Scoped] public class CustomerService : ICustomerService { }
[Scoped] public class InventoryService : IInventoryService { }
[Scoped] public class PaymentService : IPaymentService { }
[Scoped] public class ShippingService : IShippingService { }
[Scoped] public class NotificationService : INotificationService { }
[Scoped] public class ReportService : IReportService { }
[Scoped] public class AnalyticsService : IAnalyticsService { }
[Scoped] public class AuditService : IAuditService { }
[Singleton] public class CacheService : ICacheService { }
[Singleton] public class SearchService : ISearchService { }
[Transient] public class EmailService : IEmailService { }
[Transient] public class SmsService : ISmsService { }
[Transient] public class PushService : IPushService { }
[Scoped] public class UserService : IUserService { }
[Scoped] public class RoleService : IRoleService { }
[Scoped] public class PermissionService : IPermissionService { }
[Singleton] public class SettingsService : ISettingsService { }
[Transient] public class LocalizationService : ILocalizationService { }
