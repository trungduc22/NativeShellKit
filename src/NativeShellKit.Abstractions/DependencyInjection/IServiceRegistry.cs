namespace NativeShellKit.Abstractions.DependencyInjection;

public enum ServiceLifetimeKind
{
    Singleton,
    Transient
}

public sealed record ServiceDescriptorEntry(
    Type ServiceType,
    Type? ImplementationType,
    object? Instance,
    Func<IServiceProvider, object>? Factory,
    ServiceLifetimeKind Lifetime);

public interface IServiceRegistry
{
    void Add(ServiceDescriptorEntry descriptor);

    IServiceRegistry AddSingleton<TService>(TService instance) where TService : class;
    IServiceRegistry AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
    IServiceRegistry AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class;

    IServiceRegistry AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
    IServiceRegistry AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class;
}

public static class ServiceProviderExtensions
{
    public static T GetRequiredService<T>(this IServiceProvider provider) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(provider);
        var service = provider.GetService(typeof(T));
        if (service is null)
            throw new InvalidOperationException($"Required service '{typeof(T).FullName}' is not registered.");
        return (T)service;
    }

    public static T? GetService<T>(this IServiceProvider provider) where T : class
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.GetService(typeof(T)) as T;
    }

    public static IReadOnlyList<T> GetServices<T>(this IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider.GetService(typeof(IEnumerable<T>)) is IEnumerable<T> many)
            return many.ToArray();
        if (provider.GetService(typeof(T)) is T single)
            return [single];
        return Array.Empty<T>();
    }
}
