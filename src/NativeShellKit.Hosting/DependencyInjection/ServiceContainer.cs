using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NativeShellKit.Abstractions.DependencyInjection;

namespace NativeShellKit.Hosting.DependencyInjection;

internal interface IServiceCollectionBackedRegistry
{
    IServiceCollection ServiceCollection { get; }
    void TrackRawDescriptors(IEnumerable<ServiceDescriptor> descriptors);
}

/// <summary>
/// Thread-safe dependency injection container backed by
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceProvider"/>,
/// supporting Singleton, Scoped, and Transient lifetimes, captive-scope validation,
/// strict caller vs. container disposal ownership, and MEDI interoperability.
/// </summary>
public sealed class ServiceContainer :
    IServiceRegistry,
    IServiceCollectionBackedRegistry,
    IServiceProvider,
    IShellServiceScopeFactory,
    IServiceProviderIsService,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly object _buildLock = new();
    private readonly List<ServiceDescriptorEntry> _descriptors = [];
    private readonly ServiceCollection _services = [];
    private readonly List<ServiceProvider> _retiredProviders = [];
    private readonly bool _validateScopes;
    private readonly bool _validateOnBuild;

    private ServiceProvider? _currentProvider;
    private long _descriptorVersion;
    private long _builtVersion = -1;
    private bool _disposed;

    public ServiceContainer(bool validateScopes = true, bool validateOnBuild = false)
    {
        _validateScopes = validateScopes;
        _validateOnBuild = validateOnBuild;

        _services.AddSingleton<IServiceRegistry>(this);
        _services.AddSingleton<IShellServiceScopeFactory>(sp =>
            new MediShellServiceScopeFactory(ServiceProviderServiceExtensions.GetRequiredService<IServiceScopeFactory>(sp)));
        _descriptorVersion = 1;
    }

    IServiceCollection IServiceCollectionBackedRegistry.ServiceCollection => _services;

    public IReadOnlyList<ServiceDescriptorEntry> Descriptors
    {
        get
        {
            lock (_sync)
            {
                return _descriptors.ToArray();
            }
        }
    }

    public bool Contains(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_sync)
        {
            return _services.Any(d => d.ServiceType == serviceType);
        }
    }

    public bool IsService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IServiceProvider) ||
            serviceType == typeof(IServiceScopeFactory) ||
            serviceType == typeof(IShellServiceScopeFactory) ||
            serviceType == typeof(IServiceProviderIsService) ||
            serviceType == typeof(IServiceRegistry))
        {
            return true;
        }

        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return true;

        return Contains(serviceType);
    }

    public void Add(ServiceDescriptorEntry descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.ImplementationType is not null && descriptor.Instance is null && descriptor.Factory is null)
        {
            ValidateConstructible(descriptor.ImplementationType);
        }

        var msDescriptor = ToMicrosoftDescriptor(descriptor);
        lock (_sync)
        {
            ThrowIfDisposed();
            _descriptors.Add(descriptor);
            ((ICollection<ServiceDescriptor>)_services).Add(msDescriptor);
            _descriptorVersion++;
        }
    }

    void IServiceCollectionBackedRegistry.TrackRawDescriptors(IEnumerable<ServiceDescriptor> descriptors)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            foreach (var descriptor in descriptors)
            {
                if (!descriptor.IsKeyedService &&
                    descriptor.ImplementationType is not null &&
                    descriptor.ImplementationInstance is null &&
                    descriptor.ImplementationFactory is null &&
                    !descriptor.ServiceType.IsGenericTypeDefinition)
                {
                    ValidateConstructible(descriptor.ImplementationType);
                }

                ((ICollection<ServiceDescriptor>)_services).Add(descriptor);
                if (!descriptor.IsKeyedService)
                {
                    _descriptors.Add(new ServiceDescriptorEntry(
                        descriptor.ServiceType,
                        descriptor.ImplementationType,
                        descriptor.ImplementationInstance,
                        descriptor.ImplementationFactory,
                        FromMicrosoftLifetime(descriptor.Lifetime)));
                }
            }
            _descriptorVersion++;
        }
    }

    public IServiceRegistry AddSingleton<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        Add(new ServiceDescriptorEntry(typeof(TService), instance.GetType(), instance, null, ServiceLifetimeKind.Singleton));
        return this;
    }

    public IServiceRegistry AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        ValidateConstructible(typeof(TImplementation));
        Add(new ServiceDescriptorEntry(typeof(TService), typeof(TImplementation), null, null, ServiceLifetimeKind.Singleton));
        return this;
    }

    public IServiceRegistry AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(new ServiceDescriptorEntry(typeof(TService), null, null, sp => factory(sp), ServiceLifetimeKind.Singleton));
        return this;
    }

    public IServiceRegistry AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        ValidateConstructible(typeof(TImplementation));
        Add(new ServiceDescriptorEntry(typeof(TService), typeof(TImplementation), null, null, ServiceLifetimeKind.Scoped));
        return this;
    }

    public IServiceRegistry AddScoped<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(new ServiceDescriptorEntry(typeof(TService), null, null, sp => factory(sp), ServiceLifetimeKind.Scoped));
        return this;
    }

    public IServiceRegistry AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        ValidateConstructible(typeof(TImplementation));
        Add(new ServiceDescriptorEntry(typeof(TService), typeof(TImplementation), null, null, ServiceLifetimeKind.Transient));
        return this;
    }

    public IServiceRegistry AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(new ServiceDescriptorEntry(typeof(TService), null, null, sp => factory(sp), ServiceLifetimeKind.Transient));
        return this;
    }

    public IShellServiceScope CreateScope()
    {
        var provider = EnsureProvider();
        return new MediShellServiceScope(ServiceProviderServiceExtensions.GetRequiredService<IServiceScopeFactory>(provider).CreateScope());
    }

    IServiceScope IServiceScopeFactory.CreateScope() => CreateScope();

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IServiceScopeFactory))
            return this;
        if (serviceType == typeof(IShellServiceScopeFactory))
            return this;
        if (serviceType == typeof(IServiceProviderIsService))
            return this;

        var provider = EnsureProvider();
        return provider.GetService(serviceType);
    }

    internal ServiceProvider BuildFinalProvider() => EnsureProvider();

    private ServiceProvider EnsureProvider()
    {
        var current = Volatile.Read(ref _currentProvider);
        long ver = Volatile.Read(ref _descriptorVersion);
        if (current is not null && Volatile.Read(ref _builtVersion) == ver)
            return current;

        lock (_buildLock)
        {
            ThrowIfDisposed();
            long targetVersion;
            ServiceCollection snapshot;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_currentProvider is not null && _builtVersion == _descriptorVersion)
                    return _currentProvider;

                targetVersion = _descriptorVersion;
                snapshot = new ServiceCollection();
                foreach (var item in _services)
                    ((ICollection<ServiceDescriptor>)snapshot).Add(item);
            }

            var built = snapshot.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = _validateScopes,
                ValidateOnBuild = _validateOnBuild
            });

            lock (_sync)
            {
                if (_disposed)
                {
                    built.Dispose();
                    ThrowIfDisposed();
                }

                if (_currentProvider is not null)
                    _retiredProviders.Add(_currentProvider);

                _currentProvider = built;
                Volatile.Write(ref _builtVersion, targetVersion);
                return built;
            }
        }
    }

    private static ServiceDescriptor ToMicrosoftDescriptor(ServiceDescriptorEntry entry)
    {
        var lifetime = entry.Lifetime switch
        {
            ServiceLifetimeKind.Singleton => ServiceLifetime.Singleton,
            ServiceLifetimeKind.Scoped => ServiceLifetime.Scoped,
            ServiceLifetimeKind.Transient => ServiceLifetime.Transient,
            _ => ServiceLifetime.Transient
        };

        if (entry.Instance is not null)
            return new ServiceDescriptor(entry.ServiceType, entry.Instance);

        if (entry.Factory is not null)
            return new ServiceDescriptor(entry.ServiceType, entry.Factory, lifetime);

        var impl = entry.ImplementationType ?? entry.ServiceType;
        return new ServiceDescriptor(entry.ServiceType, impl, lifetime);
    }

    private static ServiceLifetimeKind FromMicrosoftLifetime(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.Singleton => ServiceLifetimeKind.Singleton,
        ServiceLifetime.Scoped => ServiceLifetimeKind.Scoped,
        _ => ServiceLifetimeKind.Transient
    };

    private static void ValidateConstructible(Type implementationType)
    {
        if (implementationType.IsAbstract || implementationType.IsInterface)
            throw new ArgumentException($"Cannot register abstract or interface implementation type '{implementationType.FullName}'.", nameof(implementationType));

        if (implementationType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length == 0)
            throw new ArgumentException($"Implementation type '{implementationType.FullName}' has no public instance constructors.", nameof(implementationType));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        List<ServiceProvider> toDispose;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            toDispose = [.. _retiredProviders];
            _retiredProviders.Clear();
            if (_currentProvider is not null)
            {
                toDispose.Add(_currentProvider);
                _currentProvider = null;
            }
        }

        for (int i = toDispose.Count - 1; i >= 0; i--)
        {
            try { toDispose[i].Dispose(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<ServiceProvider> toDispose;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            toDispose = [.. _retiredProviders];
            _retiredProviders.Clear();
            if (_currentProvider is not null)
            {
                toDispose.Add(_currentProvider);
                _currentProvider = null;
            }
        }

        for (int i = toDispose.Count - 1; i >= 0; i--)
        {
            try { await toDispose[i].DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    private sealed class MediShellServiceScopeFactory(IServiceScopeFactory inner) : IShellServiceScopeFactory
    {
        public IShellServiceScope CreateScope() => new MediShellServiceScope(inner.CreateScope());
        IServiceScope IServiceScopeFactory.CreateScope() => CreateScope();
    }

    private sealed class MediShellServiceScope(IServiceScope inner) : IShellServiceScope
    {
        public IServiceProvider ServiceProvider => inner.ServiceProvider;

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable asyncDisposable)
                return asyncDisposable.DisposeAsync();
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
