namespace PakkaHisaab.Maui.Helpers;

/// <summary>Service locator of last resort — used only where constructor injection is impossible
/// (e.g., notification tap callbacks raised by the OS before any page exists).</summary>
public static class ServiceHelper
{
    static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) => _services = services;

    public static T GetRequiredService<T>() where T : notnull =>
        (_services ?? throw new InvalidOperationException("ServiceHelper not initialized"))
        .GetRequiredService<T>();
}
