using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using NativeShellKit.Abstractions.Settings;

namespace NativeShellKit.Diagnostics.Verification;

public sealed class UiSelfTestRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly List<(string Name, Func<Task<(bool Passed, string Detail)>> Check)> _checks = [];

    public UiSelfTestRunner AddCheck(string name, Func<Task<(bool Passed, string Detail)>> check)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);
        _checks.Add((name.Trim(), check));
        return this;
    }

    public UiSelfTestRunner AddSyncCheck(string name, Func<(bool Passed, string Detail)> check) =>
        AddCheck(name, () => Task.FromResult(check()));

    public async Task<UiSelfTestReport> RunAsync(string? outputReportPath = null)
    {
        var results = new List<UiSelfTestStepResult>(_checks.Count);
        bool allPassed = true;

        foreach (var (name, check) in _checks)
        {
            try
            {
                var (passed, detail) = await check().ConfigureAwait(false);
                results.Add(new UiSelfTestStepResult(name, passed, detail));
                if (!passed)
                    allPassed = false;
            }
            catch (Exception ex)
            {
                results.Add(new UiSelfTestStepResult(name, false, ex.Message));
                allPassed = false;
            }
        }

        var report = new UiSelfTestReport(allPassed, DateTimeOffset.UtcNow, results);
        if (!string.IsNullOrWhiteSpace(outputReportPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputReportPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outputReportPath, JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
        }

        return report;
    }
}

public static class WindowScreenshotCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    public static bool TryCaptureWindowToPng(nint hwnd, string outputPngPath)
    {
        if (hwnd == nint.Zero || string.IsNullOrWhiteSpace(outputPngPath))
            return false;

        if (!GetWindowRect(hwnd, out var rect))
            return false;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return false;

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPngPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            nint hdc = g.GetHdc();
            try
            {
                const uint PW_RENDERFULLCONTENT = 0x00000002;
                if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
                {
                    PrintWindow(hwnd, hdc, 0);
                }
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }

            bmp.Save(outputPngPath, ImageFormat.Png);
            return File.Exists(outputPngPath);
        }
        catch
        {
            return false;
        }
    }
}
