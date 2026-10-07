using System.Diagnostics;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace XenMotion.UI.Services;

/// <summary>
/// Keeps XenMotion's unpackaged Windows startup entry synchronized with the user setting.
/// Uses HKCU so no administrator elevation is required.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsStartupService : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XenMotion";
    private readonly ILogger<WindowsStartupService> _logger;
    private bool _disposed;
    private bool? _lastEnabled;
    private static string TaskName => "XenMotion Startup-" + WindowsIdentity.GetCurrent().User!.Value;

    public WindowsStartupService(ILogger<WindowsStartupService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsRegistered()
    {
        try
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            try { scheduler.Connect(); return (bool)scheduler.GetFolder("\\").GetTask(TaskName).Enabled; }
            finally { Marshal.ReleaseComObject(scheduler); }
        }
        catch (Exception) { /* The registry registration remains a supported fallback. */ }
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void Apply(bool enabled)
    {
        ThrowIfDisposed();
        if (_lastEnabled == enabled) return;

        // Never register a Visual Studio/debug process as the user's Windows startup executable.
        if (Debugger.IsAttached)
        {
            _logger.LogDebug("Skipping Windows startup synchronization while a debugger is attached.");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            throw new InvalidOperationException("Unable to open the current-user Windows startup registry key.");
        }

        if (!enabled)
        {
            ConfigureLogonTask(null);
            key.DeleteValue("Lumen", throwOnMissingValue: false);
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            _logger.LogInformation("XenMotion Windows startup registration disabled.");
            _lastEnabled = false;
            return;
        }

        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the XenMotion executable path.");

        if (ConfigureLogonTask(executablePath))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        else
            key.SetValue(ValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
        key.DeleteValue("Lumen", throwOnMissingValue: false);
        _logger.LogInformation("XenMotion Windows startup registration enabled.");
        _lastEnabled = true;
    }

    private bool ConfigureLogonTask(string? executable)
    {
        object? schedulerObject = null, folderObject = null;
        try
        {
            schedulerObject = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
            dynamic scheduler = schedulerObject;
            scheduler.Connect();
            folderObject = scheduler.GetFolder("\\");
            dynamic folder = folderObject;
            if (executable is null)
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { }
                return true;
            }
            string sid = WindowsIdentity.GetCurrent().User!.Value;
            string xml = CreateLogonTaskXml(executable, sid);
            // Interactive token: ordinary user privileges, no stored password, no delay.
            object task = folder.RegisterTask(TaskName, xml, 6, sid, null, 3, null);
            Marshal.ReleaseComObject(task);
            _logger.LogInformation("Registered immediate interactive logon task {Task}.", TaskName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logon task registration unavailable; using the Windows Run entry.");
            return false;
        }
        finally
        {
            if (folderObject is not null) Marshal.ReleaseComObject(folderObject);
            if (schedulerObject is not null) Marshal.ReleaseComObject(schedulerObject);
        }
    }

    public static string CreateLogonTaskXml(string executable, string userSid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object value) => new(ns + name, value);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("Triggers", E("LogonTrigger", new object[] { E("Enabled", true), E("UserId", userSid), E("Delay", "PT0S") })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                E("UserId", userSid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new object[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false),
                E("StopIfGoingOnBatteries", false), E("StartWhenAvailable", true), E("ExecutionTimeLimit", "PT0S") }),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                E("Exec", new object[] { E("Command", executable), E("Arguments", "--minimized"),
                    E("WorkingDirectory", Path.GetDirectoryName(executable) ?? "") }))).ToString();
    }

    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WindowsStartupService));
        }
    }
}
