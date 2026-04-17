using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MediaBitrateViewer.Core.Abstractions;

namespace MediaBitrateViewer.App.Services.Progress;

/// <summary>
/// macOS implementation: shows a percentage badge on the Dock tile via
/// NSApplication.sharedApplication.dockTile.badgeLabel. macOS does not expose a
/// native Dock progress bar, so a text badge (matching HandBrake's approach) is
/// the idiomatic fallback.
/// </summary>
/// <remarks>
/// All calls must be made on the UI (main) thread — AppKit requires it.
/// </remarks>
public sealed class MacOsDockBadgeProgressService : IAppProgressService
{
    private const string ObjC = "/usr/lib/libobjc.dylib";

    private readonly IntPtr _nsApplicationClass;
    private readonly IntPtr _nsStringClass;
    private readonly IntPtr _selSharedApplication;
    private readonly IntPtr _selDockTile;
    private readonly IntPtr _selSetBadgeLabel;
    private readonly IntPtr _selStringWithUtf8;

    private string? _lastLabel;

    public MacOsDockBadgeProgressService()
    {
        _nsApplicationClass = objc_getClass("NSApplication");
        _nsStringClass = objc_getClass("NSString");
        _selSharedApplication = sel_registerName("sharedApplication");
        _selDockTile = sel_registerName("dockTile");
        _selSetBadgeLabel = sel_registerName("setBadgeLabel:");
        _selStringWithUtf8 = sel_registerName("stringWithUTF8String:");
    }

    public void SetProgress(double fraction)
    {
        var clamped = double.IsFinite(fraction) ? Math.Clamp(fraction, 0.0, 1.0) : 0.0;
        var percent = (int)Math.Round(clamped * 100.0, MidpointRounding.AwayFromZero);
        var label = string.Create(CultureInfo.InvariantCulture, $"{percent}%");
        SetBadge(label);
    }

    public void Clear() => SetBadge(null);

    private void SetBadge(string? label)
    {
        // Avoid redundant AppKit calls while the percent integer is unchanged —
        // SetProgress is called on every frame flush during analysis.
        if (string.Equals(label, _lastLabel, StringComparison.Ordinal))
            return;
        _lastLabel = label;

        var dockTile = GetDockTile();
        if (dockTile == IntPtr.Zero) return;

        var nsLabel = label is null ? IntPtr.Zero : CreateNsString(label);
        objc_msgSend_ptr(dockTile, _selSetBadgeLabel, nsLabel);
    }

    private IntPtr GetDockTile()
    {
        var app = objc_msgSend(_nsApplicationClass, _selSharedApplication);
        if (app == IntPtr.Zero) return IntPtr.Zero;
        return objc_msgSend(app, _selDockTile);
    }

    private IntPtr CreateNsString(string value)
    {
        // stringWithUTF8String: expects a NUL-terminated UTF-8 buffer.
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var bytes = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return objc_msgSend_ptr(_nsStringClass, _selStringWithUtf8, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport(ObjC, CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC, CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);
}
