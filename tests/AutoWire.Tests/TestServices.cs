// Services used to verify generator output — each case exercises a distinct code path.

// 1. Scoped registered against single implemented interface (auto-discovery)
public interface IOrderService { string GetStatus(); }
[Scoped]
public class OrderService : IOrderService { public string GetStatus() => "pending"; }

// 2. Singleton with explicit ServiceType (only registers as ICache, not ISecondaryCache)
public interface ICache { }
public interface ISecondaryCache { }
[Singleton(typeof(ICache))]
public class MemoryCache : ICache, ISecondaryCache { }

// 3. Transient with no interface — registers as concrete type
[Transient]
public class EmailSender { }

// 4. Scoped across two interfaces — registers as both
public interface IReader { }
public interface IWriter { }
[Scoped]
public class DataService : IReader, IWriter { }

// 5. Keyed scoped service (.NET 8+)
public interface IMessageBus { }
[Scoped(Key = "primary")]
public class PrimaryMessageBus : IMessageBus { }

// 6. Singleton with no interface — registers as concrete type
[Singleton]
public class AppSettings { }

// 7. Open generic - auto-discovers compatible generic interface
public interface IRepository<T> { T? Find(int id); }
[Scoped]
public class Repository<T> : IRepository<T> { public T? Find(int id) => default; }
// → services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

// 8. Open generic - explicit service type (IReadOnlyRepo only, not IRepository)
public interface IReadOnlyRepository<T> { }
[Singleton(typeof(IReadOnlyRepository<>))]
public class CachedRepository<T> : IRepository<T>, IReadOnlyRepository<T>
{
    public T? Find(int id) => default;
}
// → services.AddSingleton(typeof(IReadOnlyRepository<>), typeof(CachedRepository<>));

// 9. Open generic - no interface, registers as concrete type
[Transient]
public class EventProcessor<T> { }
// → services.AddTransient(typeof(EventProcessor<>));

