// VoxScript/Infrastructure/ServiceLocator.cs
using Microsoft.Extensions.DependencyInjection;

namespace VoxScript.Infrastructure;

/// <summary>
/// Thin static accessor for the DI container -- used only in XAML code-behind
/// where constructor injection is not available (e.g., App.OnLaunched).
/// ViewModels should use constructor injection exclusively.
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _provider;

    public static void Initialize(IServiceProvider provider) => _provider = provider;

    public static T Get<T>() where T : notnull =>
        _provider is not null
            ? _provider.GetRequiredService<T>()
            : throw new InvalidOperationException("ServiceLocator not initialized.");
}
