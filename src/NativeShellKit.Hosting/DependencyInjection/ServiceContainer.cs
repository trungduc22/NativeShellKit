using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NativeShellKit.Abstractions.DependencyInjection;

namespace NativeShellKit.Hosting.DependencyInjection;

/// <summary>
/// Thread-safe dependency injection container supporting Singleton, Scoped, and Transient lifetimes,
/// strict caller vs. container disposal ownership, per-call-chain cycle detection, and
/// Microsoft.Extensions.DependencyInjection interoperability.
/// </summary>
public sealed class ServiceContainer :
    IServiceRegistry,
    IServiceProvider,
    IShellServiceScopeFactory,
    IServiceProviderIsService,
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<ServiceDescriptorEntry> _descriptors = [];
    private readonly ScopeBucket _rootScope;
    private bool _disposed;

    public ServiceContainer()
    {
        _rootScope = new ScopeBucket(this, isRoot: true);
        AddSingleton<IServiceProvider>(this);
        AddSingleton<IServiceScopeFactory>(this);
        AddSingleton<IShellServiceScopeFactory>(this);
        AddSingleton<IServiceProviderIsService>(this);
    }

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
            return _descriptors.Any(d => d.ServiceType == serviceType);
        }
    }

    public bool IsService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IServiceProvider) ||
            serviceType == typeof(IServiceScopeFactory) ||
            serviceType == typeof(IShellServiceScopeFactory) ||
            serviceType == typeof(IServiceProviderIsService))
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

        lock (_sync)
        {
            ThrowIfDisposed();
            _descriptors.Add(descriptor);
            if (descriptor.Instance is not null && descriptor.Lifetime == ServiceLifetimeKind.Singleton)
            {
                // Caller-provided instances are cached on the root scope without taking disposal ownership.
                _rootScope.SetCachedInstance(descriptor, descriptor.Instance);
            }
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
        lock (_sync)
        {
            ThrowIfDisposed();
            return new ScopeBucket(this, isRoot: false);
        }
    }

    IServiceScope IServiceScopeFactory.CreateScope() => CreateScope();

    public object? GetService(Type serviceType) =>
        GetServiceInternal(serviceType, _rootScope, resolutionChain: null);

    private object? GetServiceInternal(Type serviceType, ScopeBucket activeScope, HashSet<Type>? resolutionChain)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        lock (_sync)
        {
            ThrowIfDisposed();
            activeScope.ThrowIfDisposed();

            if (serviceType == typeof(IServiceProvider))
                return activeScope.ServiceProvider;

            if (serviceType == typeof(IServiceScopeFactory) || serviceType == typeof(IShellServiceScopeFactory))
                return this;

            if (serviceType == typeof(IServiceProviderIsService))
                return this;

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GetGenericArguments()[0];
                var matches = _descriptors.Where(d => d.ServiceType == itemType).ToList();
                var array = Array.CreateInstance(itemType, matches.Count);
                for (int i = 0; i < matches.Count; i++)
                {
                    array.SetValue(ResolveDescriptor(matches[i], activeScope, resolutionChain), i);
                }
                return array;
            }

            var descriptor = _descriptors.LastOrDefault(d => d.ServiceType == serviceType);
            if (descriptor is not null)
                return ResolveDescriptor(descriptor, activeScope, resolutionChain);

            if (!serviceType.IsAbstract && !serviceType.IsInterface && serviceType.GetConstructors().Length > 0)
                return CreateInstance(serviceType, activeScope, resolutionChain);

            return null;
        }
    }

    private object ResolveDescriptor(ServiceDescriptorEntry descriptor, ScopeBucket activeScope, HashSet<Type>? resolutionChain)
    {
        if (descriptor.Lifetime == ServiceLifetimeKind.Singleton)
        {
            if (_rootScope.TryGetCachedInstance(descriptor, out var existingSingleton))
                return existingSingleton;

            object singletonInstance = CreateFromDescriptor(descriptor, _rootScope, resolutionChain);
            _rootScope.SetCachedInstance(descriptor, singletonInstance);
            TrackDisposableIfOwned(descriptor, singletonInstance, _rootScope);
            return singletonInstance;
        }

        if (descriptor.Lifetime == ServiceLifetimeKind.Scoped)
        {
            if (activeScope.TryGetCachedInstance(descriptor, out var existingScoped))
                return existingScoped;

            object scopedInstance = CreateFromDescriptor(descriptor, activeScope, resolutionChain);
            activeScope.SetCachedInstance(descriptor, scopedInstance);
            TrackDisposableIfOwned(descriptor, scopedInstance, activeScope);
            return scopedInstance;
        }

        object transientInstance = CreateFromDescriptor(descriptor, activeScope, resolutionChain);
        TrackDisposableIfOwned(descriptor, transientInstance, activeScope);
        return transientInstance;
    }

    private object CreateFromDescriptor(ServiceDescriptorEntry descriptor, ScopeBucket scope, HashSet<Type>? resolutionChain)
    {
        if (descriptor.Factory is not null)
        {
            var chain = resolutionChain ?? [];
            if (!chain.Add(descriptor.ServiceType))
                throw new InvalidOperationException($"Circular dependency detected while resolving factory for '{descriptor.ServiceType.FullName}'.");
            try
            {
                return descriptor.Factory(scope.ServiceProvider);
            }
            finally
            {
                chain.Remove(descriptor.ServiceType);
            }
        }

        if (descriptor.Instance is not null)
            return descriptor.Instance;

        var targetType = descriptor.ImplementationType ?? descriptor.ServiceType;
        return CreateInstance(targetType, scope, resolutionChain);
    }

    private void TrackDisposableIfOwned(ServiceDescriptorEntry descriptor, object instance, ScopeBucket targetScope)
    {
        // Caller-provided instances (descriptor.Instance != null) are not owned by the container.
        if (descriptor.Instance is not null)
            return;

        if (!ReferenceEquals(instance, this) && !ReferenceEquals(instance, targetScope) &&
            (instance is IDisposable || instance is IAsyncDisposable))
        {
            targetScope.TrackDisposable(instance);
        }
    }

    private object CreateInstance(Type implementationType, ScopeBucket activeScope, HashSet<Type>? resolutionChain)
    {
        ValidateConstructible(implementationType);
        var chain = resolutionChain ?? [];
        if (!chain.Add(implementationType))
            throw new InvalidOperationException($"Circular dependency detected while resolving '{implementationType.FullName}'.");

        try
        {
            var constructors = implementationType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .ToArray();

            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var args = new object?[parameters.Length];
                bool matched = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    var resolved = GetServiceInternal(p.ParameterType, activeScope, chain);
                    if (resolved is null)
                    {
                        if (p.HasDefaultValue)
                        {
                            resolved = p.DefaultValue;
                        }
                        else
                        {
                            matched = false;
                            break;
                        }
                    }
                    args[i] = resolved;
                }

                if (matched)
                    return ctor.Invoke(args);
            }

            throw new InvalidOperationException($"Unable to resolve constructor parameters for '{implementationType.FullName}'.");
        }
        finally
        {
            chain.Remove(implementationType);
        }
    }

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
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _rootScope.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        await _rootScope.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class ScopeBucket : IShellServiceScope, IServiceProvider
    {
        private readonly ServiceContainer _owner;
        private readonly bool _isRoot;
        private readonly Dictionary<ServiceDescriptorEntry, object> _scopedCache = [];
        private readonly List<object> _disposables = [];
        private bool _scopeDisposed;

        public ScopeBucket(ServiceContainer owner, bool isRoot)
        {
            _owner = owner;
            _isRoot = isRoot;
        }

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) =>
            _isRoot ? _owner.GetServiceInternal(serviceType, this, null) : _owner.GetServiceInternal(serviceType, this, null);

        public bool TryGetCachedInstance(ServiceDescriptorEntry descriptor, out object instance) =>
            _scopedCache.TryGetValue(descriptor, out instance!);

        public void SetCachedInstance(ServiceDescriptorEntry descriptor, object instance) =>
            _scopedCache[descriptor] = instance;

        public void TrackDisposable(object instance)
        {
            if (!_disposables.Any(d => ReferenceEquals(d, instance)))
                _disposables.Add(instance);
        }

        public void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_scopeDisposed, this);
        }

        public void Dispose()
        {
            List<object> toDispose;
            lock (_owner._sync)
            {
                if (_scopeDisposed)
                    return;
                _scopeDisposed = true;
                toDispose = [.. _disposables];
                _disposables.Clear();
                _scopedCache.Clear();
            }

            for (int i = toDispose.Count - 1; i >= 0; i--)
            {
                if (toDispose[i] is IDisposable d)
                {
                    try { d.Dispose(); } catch { }
                }
                else if (toDispose[i] is IAsyncDisposable ad)
                {
                    try { ad.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            List<object> toDispose;
            lock (_owner._sync)
            {
                if (_scopeDisposed)
                    return;
                _scopeDisposed = true;
                toDispose = [.. _disposables];
                _disposables.Clear();
                _scopedCache.Clear();
            }

            for (int i = toDispose.Count - 1; i >= 0; i--)
            {
                if (toDispose[i] is IAsyncDisposable ad)
                {
                    try { await ad.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                else if (toDispose[i] is IDisposable d)
                {
                    try { d.Dispose(); } catch { }
                }
            }
        }
    }
}
