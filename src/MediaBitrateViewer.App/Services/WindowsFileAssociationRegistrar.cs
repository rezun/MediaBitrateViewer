using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MediaBitrateViewer.App.Services;

internal static partial class WindowsFileAssociationRegistrar
{
    private const string AppDisplayName = "Media Bitrate Viewer";
    private const string AppDescription = "Inspect video bitrate over time";
    private const string AppExecutableName = "MediaBitrateViewer.App.exe";
    private const string CapabilitiesPath = @"Software\MediaBitrateViewer\Capabilities";
    private const string RegisteredApplicationsPath = @"Software\RegisteredApplications";
    private const string ProgId = "MediaBitrateViewer.VideoFile";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static void RegisterCurrentInstallation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        var command = $"\"{executablePath}\" \"%1\"";
        var iconReference = $"{executablePath},0";

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue(string.Empty, "Media Bitrate Viewer Video File");

            using var defaultIconKey = progIdKey.CreateSubKey("DefaultIcon");
            defaultIconKey.SetValue(string.Empty, iconReference);

            using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, command);
        }

        using (var applicationKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{AppExecutableName}"))
        {
            applicationKey.SetValue("FriendlyAppName", AppDisplayName);

            using var defaultIconKey = applicationKey.CreateSubKey("DefaultIcon");
            defaultIconKey.SetValue(string.Empty, iconReference);

            using var commandKey = applicationKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, command);

            using var supportedTypesKey = applicationKey.CreateSubKey("SupportedTypes");
            foreach (var extension in VideoFileExtensions.Extensions)
                supportedTypesKey.SetValue(extension, string.Empty);
        }

        using (var capabilitiesKey = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
        {
            capabilitiesKey.SetValue("ApplicationName", AppDisplayName);
            capabilitiesKey.SetValue("ApplicationDescription", AppDescription);

            using var fileAssociationsKey = capabilitiesKey.CreateSubKey("FileAssociations");
            foreach (var extension in VideoFileExtensions.Extensions)
                fileAssociationsKey.SetValue(extension, ProgId);
        }

        using (var registeredApplicationsKey = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsPath))
        {
            registeredApplicationsKey.SetValue(AppDisplayName, CapabilitiesPath);
        }

        foreach (var extension in VideoFileExtensions.Extensions)
        {
            using var openWithProgIdsKey =
                Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            openWithProgIdsKey.SetValue(ProgId, string.Empty, RegistryValueKind.String);
        }

        NotifyShellAssociationsChanged();
    }

    public static void UnregisterCurrentInstallation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        foreach (var extension in VideoFileExtensions.Extensions)
        {
            using var openWithProgIdsKey =
                Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWithProgIdsKey?.DeleteValue(ProgId, throwOnMissingValue: false);
        }

        DeleteSubKeyTreeIfPresent(Registry.CurrentUser, $@"Software\Classes\Applications\{AppExecutableName}");
        DeleteSubKeyTreeIfPresent(Registry.CurrentUser, $@"Software\Classes\{ProgId}");
        DeleteSubKeyTreeIfPresent(Registry.CurrentUser, CapabilitiesPath);

        using var registeredApplicationsKey = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsPath, writable: true);
        registeredApplicationsKey?.DeleteValue(AppDisplayName, throwOnMissingValue: false);

        NotifyShellAssociationsChanged();
    }

    private static void DeleteSubKeyTreeIfPresent(RegistryKey root, string subKeyPath)
    {
        try
        {
            root.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Velopack runs the uninstall callback during teardown; best effort is
            // enough here because stale registration is less harmful than aborting
            // the uninstall flow.
        }
    }

    private static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
