using System.Runtime.InteropServices;

namespace KGIOptionPricing.Infrastructure;

/// <summary>
/// Load the UI's existing Windows fonts into this process before WinForms creates
/// cached HFONT handles. This also works when session-wide GDI font mapping is stale.
/// No font files, registry entries or system services are changed.
/// </summary>
internal sealed class WindowsFontBootstrap : IDisposable
{
    private const uint PrivateFont = 0x10;
    private readonly List<string> loaded = new();

    [DllImport("gdi32.dll", EntryPoint = "AddFontResourceExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AddFontResource(string path, uint flags, IntPtr reserved);

    [DllImport("gdi32.dll", EntryPoint = "RemoveFontResourceExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveFontResource(string path, uint flags, IntPtr reserved);

    public static WindowsFontBootstrap Load(Action<string>? log = null)
    {
        var resources = new WindowsFontBootstrap();
        string fontsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (string name in new[]
        {
            "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf",
            "seguisym.ttf", "seguiemj.ttf", "msjh.ttc", "msjhbd.ttc"
        })
        {
            string path = Path.Combine(fontsRoot, name);
            if (!File.Exists(path) || AddFontResource(path, PrivateFont, IntPtr.Zero) <= 0)
            {
                log?.Invoke($"[Font startup] Could not load {name}");
                continue;
            }
            resources.loaded.Add(path);
        }
        log?.Invoke($"[Font startup] Loaded {resources.loaded.Count} private system font resources");
        return resources;
    }

    public void Dispose()
    {
        for (int i = loaded.Count - 1; i >= 0; i--)
            RemoveFontResource(loaded[i], PrivateFont, IntPtr.Zero);
        loaded.Clear();
    }
}
