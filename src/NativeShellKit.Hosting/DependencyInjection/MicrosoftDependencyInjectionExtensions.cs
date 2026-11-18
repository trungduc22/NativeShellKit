using Microsoft.Extensions.DependencyInjection;
using NativeShellKit.Abstractions.DependencyInjection;
using NativeShellKit.Hosting.Hosting;

namespace NativeShellKit.Hosting.DependencyInjection;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection <see cref="IServiceCollection"/> descriptors
/// with NativeShellKit <see cref="IServiceRegistry"/> and <see cref="NativeShellHostBuilder"/>.
/// </summary>
public static class MicrosoftDependencyInjectionExtensions
{
    public static NativeShellHostBuilder ConfigureServices(
        this NativeShellHostBuilder builder,
        Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.ConfigureServices(registry =>
        {
            var collection = new ServiceCollection();
            configure(collection);
            registry.PopulateFrom(collection);
        });
    }

    public static IServiceRegistry PopulateFrom(this IServiceRegistry registry, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
            {
                throw new NotSupportedException(
                    $"Keyed service '{descriptor.ServiceType.FullName}' is not supported by NativeShellKit.Hosting.ServiceContainer. Register via factory or Microsoft.Extensions.DependencyInjection directly.");
            }

            var lifetime = descriptor.Lifetime switch
            {
                ServiceLifetime.Singleton => ServiceLifetimeKind.Singleton,
                ServiceLifetime.Scoped => ServiceLifetimeKind.Scoped,
                ServiceLifetime.Transient => ServiceLifetimeKind.Transient,
                _ => throw new ArgumentOutOfRangeException(nameof(services), descriptor.Lifetime, "Unsupported ServiceLifetime.")
            };

            registry.Add(new ServiceDescriptorEntry(
                descriptor.ServiceType,
                descriptor.ImplementationType,
                descriptor.ImplementationInstance,
                descriptor.ImplementationFactory,
                lifetime));
        }

        return registry;
    }

    public static IServiceCollection CopyToServiceCollection(this IServiceRegistry registry, IServiceCollection target)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var entry in registry.Descriptors)
        {
            var msLifetime = entry.Lifetime switch
            {
                ServiceLifetimeKind.Singleton => ServiceLifetime.Singleton,
                ServiceLifetimeKind.Scoped => ServiceLifetime.Scoped,
                ServiceLifetimeKind.Transient => ServiceLifetime.Transient,
                _ => ServiceLifetime.Transient
            };

            if (entry.Instance is not null)
            {
                target.Add(new ServiceDescriptor(entry.ServiceType, entry.Instance));
            }
            else if (entry.Factory is not null)
            {
                target.Add(new ServiceDescriptor(entry.ServiceType, entry.Factory, msLifetime));
            }
            else if (entry.ImplementationType is not null)
            {
                target.Add(new ServiceDescriptor(entry.ServiceType, entry.ImplementationType, msLifetime));
            }
        }

        return target;
    }
}
