using Microsoft.UI.Xaml;
using NativeShellKit.WinUI.Shell;

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
        catch (Exception ex)
        {
            if (Application.Current is not null)
                Console.Error.WriteLine($"DesignSystemThemeResources.EnsureLoaded failed: {ex}");
            return false;
        }
    }

    public static bool ApplyDensity(ShellDensity density)
    {
        try
        {
            var merged = Application.Current?.Resources?.MergedDictionaries;
            if (merged is null)
                return false;

            const string compactUri = "ms-appx:///NativeShellKit.DesignSystem/Themes/Tokens/DensityCompact.xaml";
            const string standardUri = "ms-appx:///NativeShellKit.DesignSystem/Themes/Tokens/DensityStandard.xaml";

            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.ToString();
                if (string.Equals(src, compactUri, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(src, standardUri, StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            var targetUri = density == ShellDensity.Compact ? compactUri : standardUri;
            merged.Add(new ResourceDictionary { Source = new Uri(targetUri) });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
