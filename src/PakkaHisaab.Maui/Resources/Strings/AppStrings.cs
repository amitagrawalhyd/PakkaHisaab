using System.Resources;

namespace PakkaHisaab.Maui.Resources.Strings;

/// <summary>
/// Strongly-typed accessor for AppStrings.resx. Kept hand-written (instead of the designer file)
/// so the project builds identically on CLI, CI and VS. Culture-specific satellite resources
/// (AppStrings.hi.resx, AppStrings.ta.resx, …) are resolved automatically by ResourceManager,
/// falling back to the neutral English resource for any untranslated key.
/// </summary>
public static class AppStrings
{
    static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager(
            "PakkaHisaab.Maui.Resources.Strings.AppStrings",
            typeof(AppStrings).Assembly);
}
