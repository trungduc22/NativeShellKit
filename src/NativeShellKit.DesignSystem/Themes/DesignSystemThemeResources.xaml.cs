using Microsoft.UI.Xaml;

namespace NativeShellKit.DesignSystem.Themes;

public sealed partial class DesignSystemThemeResources : ResourceDictionary
{
    public DesignSystemThemeResources()
    {
        InitializeComponent();
    }

    public static bool EnsureLoaded()
    {
        try
        {
            var merged = Application.Current?.Resources?.MergedDictionaries;
            if (merged is null)
                return false;

            if (!merged.OfType<DesignSystemThemeResources>().Any())
                merged.Add(new DesignSystemThemeResources());

            return true;
        }
        catch
        {
            return false;
        }
    }
}
