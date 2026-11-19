using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using NativeShellKit.Abstractions.Navigation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace NativeShellKit.WinUI.Theming;

public sealed class WinUiThemeService : IThemeService, IDisposable
{
    public const string DefaultAccentHex = "#2563EB";
    private readonly List<WeakReference<FrameworkElement>> _attachedRoots = [];
    private readonly List<WeakReference<Window>> _attachedWindows = [];
    private readonly Func<bool>? _highContrastOverride;
    private readonly Func<bool>? _animationsEnabledOverride;
    private UISettings? _uiSettings;
    private string? _customAccentHex;
    private string _brandAccentHex = DefaultAccentHex;
    private bool _disposed;

    public WinUiThemeService()
        : this(null, null, attachSystemListener: true)
    {
    }

    internal WinUiThemeService(
        Func<bool>? highContrastOverride = null,
        Func<bool>? animationsEnabledOverride = null,
        bool attachSystemListener = false)
    {
        _highContrastOverride = highContrastOverride;
        _animationsEnabledOverride = animationsEnabledOverride;

        CurrentMode = AppThemeMode.System;
        AccentMode = AccentSourceMode.System;
        PreferredBackdrop = ShellBackdropKind.MicaBase;
        EffectiveBackdrop = ShellBackdropKind.Solid;
        IsDarkEffective = ResolveSystemIsDark();
        CurrentAccent = ComputePalette(DefaultAccentHex, IsDarkEffective);

        if (attachSystemListener)
        {
            try
            {
                _uiSettings = new UISettings();
                _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
            }
            catch
            {
                _uiSettings = null;
            }
        }
    }

    public AppThemeMode CurrentMode { get; private set; }
    public AccentSourceMode AccentMode { get; private set; }
    public ShellBackdropKind PreferredBackdrop { get; private set; }
    public ShellBackdropKind EffectiveBackdrop { get; private set; }
    public bool IsDarkEffective { get; private set; }
    public AccentColorPalette CurrentAccent { get; private set; }
    public bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22000 && MicaController.IsSupported();

    public string BrandAccentHex
    {
        get => _brandAccentHex;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                _brandAccentHex = value;
        }
    }

    public bool IsHighContrast
    {
        get
        {
            if (_highContrastOverride is not null)
                return _highContrastOverride();

            try
            {
                return new AccessibilitySettings().HighContrast;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsAnimationsEnabled
    {
        get
        {
            if (_animationsEnabledOverride is not null)
                return _animationsEnabledOverride();

            try
            {
                return (_uiSettings ?? new UISettings()).AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }

    public event EventHandler? ThemeChanged;

    public void AttachRoot(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_attachedRoots.Any(r => r.TryGetTarget(out var existing) && ReferenceEquals(existing, element)))
            _attachedRoots.Add(new WeakReference<FrameworkElement>(element));

        element.RequestedTheme = ToElementTheme(CurrentMode);
    }

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!_attachedWindows.Any(w => w.TryGetTarget(out var existing) && ReferenceEquals(existing, window)))
        {
            _attachedWindows.Add(new WeakReference<Window>(window));
        }
        ApplyWindowTheme(window, CurrentMode, IsDarkEffective, IsHighContrast);
    }

    public void ApplyTheme(AppThemeMode mode, string? customAccentHex = null)
    {
        var accentMode = !string.IsNullOrWhiteSpace(customAccentHex)
            ? AccentSourceMode.Custom
            : AccentMode;
        ApplyTheme(mode, accentMode, customAccentHex, PreferredBackdrop);
    }

    public void ApplyTheme(
        AppThemeMode mode,
        AccentSourceMode accentMode,
        string? customAccentHex = null,
        ShellBackdropKind? backdrop = null)
    {
        CurrentMode = mode;
        AccentMode = accentMode;
        if (!string.IsNullOrWhiteSpace(customAccentHex))
            _customAccentHex = customAccentHex;
        if (backdrop.HasValue)
            PreferredBackdrop = backdrop.Value;

        bool highContrast = IsHighContrast;
        IsDarkEffective = mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => ResolveSystemIsDark()
        };

        string seedHex = ResolveSeedAccentHex(accentMode);
        CurrentAccent = ComputePalette(seedHex, IsDarkEffective);

        if (!highContrast)
        {
            ApplyToApplicationResources(CurrentAccent);
        }

        var requested = ToElementTheme(mode);
        for (int i = _attachedRoots.Count - 1; i >= 0; i--)
        {
            if (_attachedRoots[i].TryGetTarget(out var root))
            {
                if (root.DispatcherQueue is { HasThreadAccess: false } queue)
                    queue.TryEnqueue(() => root.RequestedTheme = requested);
                else
                    root.RequestedTheme = requested;
            }
            else
            {
                _attachedRoots.RemoveAt(i);
            }
        }

        for (int i = _attachedWindows.Count - 1; i >= 0; i--)
        {
            if (_attachedWindows[i].TryGetTarget(out var win))
            {
                ApplyWindowTheme(win, mode, IsDarkEffective, highContrast);
                if (backdrop.HasValue || highContrast)
                    TryApplyBackdrop(win, PreferredBackdrop);
            }
            else
            {
                _attachedWindows.RemoveAt(i);
            }
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public AccentColorPalette ComputePalette(string accentHex, bool isDark)
    {
        var baseColor = TryParseHex(accentHex, out var parsed) ? parsed : ParseOrDefault(DefaultAccentHex);
        var effective = isDark
            ? (Luminance(baseColor) < 0.20 ? BlendWhite(baseColor, 0.32) : baseColor)
            : (Luminance(baseColor) > 0.58 ? Scale(baseColor, 0.75) : baseColor);

        var hover = Scale(effective, isDark ? 0.90 : 1.10);
        var pressed = Scale(effective, isDark ? 0.78 : 0.86);
        var subtle = Color.FromArgb(isDark ? (byte)42 : (byte)28, effective.R, effective.G, effective.B);

        // Enforce WCAG AA contrast >= 4.5:1 for foreground text on accent fill.
        var white = Color.FromArgb(255, 255, 255, 255);
        var darkSlate = Color.FromArgb(255, 15, 23, 42);
        var black = Color.FromArgb(255, 0, 0, 0);
        double whiteRatio = ContrastRatio(effective, white);
        double slateRatio = ContrastRatio(effective, darkSlate);
        string contrast;
        if (whiteRatio >= slateRatio)
            contrast = whiteRatio >= 4.5 ? "#FFFFFF" : (ContrastRatio(effective, black) > whiteRatio ? "#000000" : "#FFFFFF");
        else
            contrast = slateRatio >= 4.5 ? "#0F172A" : "#000000";

        return new AccentColorPalette(
            ToHex(effective),
            ToHex(hover),
            ToHex(pressed),
            ToArgbHex(subtle),
            contrast);
    }

    public bool TryApplyBackdrop(Window window) => TryApplyBackdrop(window, PreferredBackdrop);

    public bool TryApplyBackdrop(Window window, ShellBackdropKind kind)
    {
        ArgumentNullException.ThrowIfNull(window);
        PreferredBackdrop = kind;
        AttachWindow(window);

        if (IsHighContrast || kind == ShellBackdropKind.Solid)
        {
            SetWindowBackdropSafe(window, null);
            EffectiveBackdrop = ShellBackdropKind.Solid;
            return false;
        }

        try
        {
            if (kind is ShellBackdropKind.MicaBase or ShellBackdropKind.MicaAlt && IsMicaSupported)
            {
                var micaKind = kind == ShellBackdropKind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base;
                window.SystemBackdrop = new MicaBackdrop { Kind = micaKind };
                EffectiveBackdrop = kind;
                return true;
            }

            if (DesktopAcrylicController.IsSupported())
            {
                window.SystemBackdrop = new DesktopAcrylicBackdrop();
                EffectiveBackdrop = ShellBackdropKind.Acrylic;
                return true;
            }
        }
        catch
        {
            // Headless or unsupported compositor falls back cleanly to solid surface background.
        }

        SetWindowBackdropSafe(window, null);
        EffectiveBackdrop = ShellBackdropKind.Solid;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
            _uiSettings = null;
        }
    }

    private string ResolveSeedAccentHex(AccentSourceMode accentMode) => accentMode switch
    {
        AccentSourceMode.Custom when !string.IsNullOrWhiteSpace(_customAccentHex) => _customAccentHex,
        AccentSourceMode.AppBrand => _brandAccentHex,
        AccentSourceMode.System => ResolveWindowsAccentHex() ?? _brandAccentHex,
        _ => !string.IsNullOrWhiteSpace(_customAccentHex) ? _customAccentHex : ResolveWindowsAccentHex() ?? _brandAccentHex
    };

    private void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        if (_disposed)
            return;

        if (CurrentMode == AppThemeMode.System || AccentMode == AccentSourceMode.System || IsHighContrast)
        {
            ApplyTheme(CurrentMode, AccentMode, _customAccentHex, PreferredBackdrop);
        }
    }

    private static void SetWindowBackdropSafe(Window window, SystemBackdrop? backdrop)
    {
        void Apply()
        {
            try { window.SystemBackdrop = backdrop; } catch { }
        }

        if (window.DispatcherQueue is { HasThreadAccess: false } queue)
            queue.TryEnqueue(Apply);
        else
            Apply();
    }

    private static void ApplyWindowTheme(Window window, AppThemeMode mode, bool isDark, bool isHighContrast)
    {
        var requested = ToElementTheme(mode);
        void Apply()
        {
            if (window.Content is FrameworkElement contentRoot)
            {
                contentRoot.RequestedTheme = requested;
            }

            if (isHighContrast)
                return;

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                if (hwnd != nint.Zero)
                {
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                    if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && appWindow.TitleBar.ExtendsContentIntoTitleBar)
                    {
                        var fg = isDark ? Color.FromArgb(255, 248, 250, 252) : Color.FromArgb(255, 15, 23, 42);
                        var inactiveFg = isDark ? Color.FromArgb(160, 248, 250, 252) : Color.FromArgb(160, 15, 23, 42);
                        var hoverBg = isDark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(20, 15, 23, 42);
                        appWindow.TitleBar.ButtonForegroundColor = fg;
                        appWindow.TitleBar.ButtonHoverForegroundColor = fg;
                        appWindow.TitleBar.ButtonPressedForegroundColor = fg;
                        appWindow.TitleBar.ButtonInactiveForegroundColor = inactiveFg;
                        appWindow.TitleBar.ButtonHoverBackgroundColor = hoverBg;
                    }
                }
            }
            catch
            {
                // Ignore if window handle is not yet created or closed
            }
        }

        if (window.DispatcherQueue is { HasThreadAccess: false } queue)
            queue.TryEnqueue(Apply);
        else
            Apply();
    }

    private static void ApplyToApplicationResources(AccentColorPalette palette)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res is null)
                return;

            if (TryParseHex(palette.PrimaryHex, out var primary))
            {
                res["SystemAccentColor"] = primary;
                SetBrush(res, "AccentFillColorDefaultBrush", primary);
                SetBrush(res, "NativeShellAccentBrush", primary);
                SetBrush(res, "NskAccentDefaultBrush", primary);
            }
            if (TryParseHex(palette.HoverHex, out var hover))
            {
                SetBrush(res, "AccentFillColorSecondaryBrush", hover);
                SetBrush(res, "NskAccentHoverBrush", hover);
            }
            if (TryParseHex(palette.PressedHex, out var pressed))
            {
                SetBrush(res, "AccentFillColorTertiaryBrush", pressed);
                SetBrush(res, "NskAccentPressedBrush", pressed);
            }
            if (TryParseHex(palette.SubtleTintHex, out var subtle))
            {
                SetBrush(res, "NskAccentSubtleBrush", subtle);
            }
            if (TryParseHex(palette.ContrastForegroundHex, out var fg))
            {
                SetBrush(res, "TextOnAccentFillColorPrimaryBrush", fg);
                SetBrush(res, "NskTextOnAccentBrush", fg);
            }
        }
        catch
        {
            // Safe when no WinUI Application instance is active (e.g. unit tests)
        }
    }

    private static void SetBrush(ResourceDictionary dict, string key, Color color)
    {
        if (dict.TryGetValue(key, out var existing) && existing is SolidColorBrush scb)
            scb.Color = color;
        else
            dict[key] = new SolidColorBrush(color);
    }

    private static ElementTheme ToElementTheme(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Light => ElementTheme.Light,
        AppThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private static bool ResolveSystemIsDark()
    {
        try
        {
            var ui = new UISettings();
            var bg = ui.GetColorValue(UIColorType.Background);
            return Luminance(bg) < 0.35;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveWindowsAccentHex()
    {
        try
        {
            var ui = new UISettings();
            return ToHex(ui.GetColorValue(UIColorType.Accent));
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }
        if (clean.Length == 8 && uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            color = Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            return true;
        }
        return false;
    }

    public static double ContrastRatio(Color first, Color second)
    {
        double l1 = Luminance(first);
        double l2 = Luminance(second);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static Color ParseOrDefault(string hex) =>
        TryParseHex(hex, out var c) ? c : Color.FromArgb(255, 0x25, 0x63, 0xEB);

    private static Color Scale(Color c, double factor) => Color.FromArgb(
        c.A,
        (byte)Math.Clamp((int)Math.Round(c.R * factor), 0, 255),
        (byte)Math.Clamp((int)Math.Round(c.G * factor), 0, 255),
        (byte)Math.Clamp((int)Math.Round(c.B * factor), 0, 255));

    private static Color BlendWhite(Color c, double weight) => Color.FromArgb(
        c.A,
        (byte)Math.Clamp((int)Math.Round(c.R + (255 - c.R) * weight), 0, 255),
        (byte)Math.Clamp((int)Math.Round(c.G + (255 - c.G) * weight), 0, 255),
        (byte)Math.Clamp((int)Math.Round(c.B + (255 - c.B) * weight), 0, 255));

    private static double Luminance(Color c)
    {
        static double Lin(byte b)
        {
            double s = b / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static string ToArgbHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}
