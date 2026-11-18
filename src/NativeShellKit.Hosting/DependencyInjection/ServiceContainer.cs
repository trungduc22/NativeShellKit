using System.Collections.Concurrent;
using System.Reflection;
using NativeShellKit.Abstractions.DependencyInjection;

namespace NativeShellKit.Hosting.DependencyInjection;

/// <summary>
/// Lightweight, thread-safe dependency injection container supporting Singletons,
/// Transients, factory registrations, multi-registration IEnumerable&lt;T&gt;, and reverse-order disposal.
/// </summary>
public sealed class ServiceContainer : IServiceRegistry, IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<ServiceDescriptorEntry> _descriptors = [];
    private readonly Dictionary<ServiceDescriptorEntry, object> _singletons = new();
    private readonly List<object> _disposables = [];
    private readonly HashSet<Type> _resolvingStack = [];
    private bool _disposed;

    public ServiceContainer()
    {
        AddSingleton<IServiceProvider>(this);
    }

    public void Add(ServiceDescriptorEntry descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_sync)
        {
            ThrowIfDisposed();
            _descriptors.Add(descriptor);
            if (descriptor.Instance is not null && descriptor.Lifetime == ServiceLifetimeKind.Singleton)
            {
                _singletons[descriptor] = descriptor.Instance;
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
        Add(new ServiceDescriptorEntry(typeof(TService), typeof(TImplementation), null, null, ServiceLifetimeKind.Singleton));
        return this;
    }

    public IServiceRegistry AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(new ServiceDescriptorEntry(typeof(TService), null, null, sp => factory(sp), ServiceLifetimeKind.Singleton));
        return this;
    }

    public IServiceRegistry AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Add(new ServiceDescriptorEntry(typeof(TService), typeof(TImplementation), null, null, ServiceLifetimeKind.Transient));
        return this;
    }

    public IServiceRegistry AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        Add(new ServiceDescriptorEntry(typeof(TService), null, null, sp => factory(sp), ServiceLifetimeKind.Transient));
        return this;
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_sync)
        {
            ThrowIfDisposed();

            if (serviceType == typeof(IServiceProvider))
                return this;

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GetGenericArguments()[0];
                var matches = _descriptors.Where(d => d.ServiceType == itemType).ToList();
                var array = Array.CreateInstance(itemType, matches.Count);
                for (int i = 0; i < matches.Count; i++)
                {
                    array.SetValue(ResolveDescriptor(matches[i]), i);
                }
                return array;
            }

            var descriptor = _descriptors.LastOrDefault(d => d.ServiceType == serviceType);
            if (descriptor is not null)
                return ResolveDescriptor(descriptor);

            if (!serviceType.IsAbstract && !serviceType.IsInterface && serviceType.GetConstructors().Length > 0)
                return CreateInstance(serviceType);

            return null;
        }
    }

    private object ResolveDescriptor(ServiceDescriptorEntry descriptor)
    {
        if (descriptor.Lifetime == ServiceLifetimeKind.Singleton && _singletons.TryGetValue(descriptor, out var existing))
            return existing;

        object instance = descriptor.Factory is not null
            ? descriptor.Factory(this)
            : descriptor.Instance ?? CreateInstance(descriptor.ImplementationType ?? descriptor.ServiceType);

        if (descriptor.Lifetime == ServiceLifetimeKind.Singleton)
        {
            _singletons[descriptor] = instance;
        }

        if (!ReferenceEquals(instance, this) && (instance is IDisposable || instance is IAsyncDisposable))
        {
            if (!_disposables.Any(d => ReferenceEquals(d, instance)))
                _disposables.Add(instance);
        }

        return instance;
    }

    private object CreateInstance(Type implementationType)
    {
        if (!_resolvingStack.Add(implementationType))
            throw new InvalidOperationException($"Circular dependency detected while resolving '{implementationType.FullName}'.");

        try
        {
            var constructors = implementationType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .ToArray();

            if (constructors.Length == 0)
                throw new InvalidOperationException($"No public constructor found for '{implementationType.FullName}'.");

            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var args = new object?[parameters.Length];
                bool matched = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    var resolved = GetService(p.ParameterType);
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
            _resolvingStack.Remove(implementationType);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        List<object> toDispose;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            toDispose = [.. _disposables];
            _disposables.Clear();
            _singletons.Clear();
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
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            toDispose = [.. _disposables];
            _disposables.Clear();
            _singletons.Clear();
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
