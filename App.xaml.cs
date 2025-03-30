using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;

namespace TrayFileWatcher;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private IntPtr _dllHandle = IntPtr.Zero;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    static extern bool FreeLibrary(IntPtr hModule);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StartListeningDelegate([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopListeningDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void FileChangedCallback([MarshalAs(UnmanagedType.LPWStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void SetFileChangedCallbackDelegate(FileChangedCallback cb);

    private StartListeningDelegate _startListening;
    private StopListeningDelegate _stopListening;

    private string selectedPathToWatch = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetupTray();
        LoadAndCallDll();
    }

    private void LoadAndCallDll()
    {
        string dllFileName = "DirectoryListener.dll";
        string resourceName = "TrayFileWatcher.include." + dllFileName; // Change namespace if needed

        // Step 1: Extract embedded DLL to temp folder
        string tempPath = Path.Combine(Path.GetTempPath(), dllFileName);
        using (var resourceStream = GetType().Assembly.GetManifestResourceStream(resourceName))
        {
            if (resourceStream == null)
            {
                System.Windows.MessageBox.Show("Embedded DLL not found: " + resourceName);
                return;
            }

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                resourceStream.CopyTo(fileStream);
            }
        }

        // Step 2: Load the DLL manually
        _dllHandle = LoadLibrary(tempPath);
        if (_dllHandle == IntPtr.Zero)
        {
            System.Windows.MessageBox.Show("Failed to load extracted DLL.");
            return;
        }

        // Step 3: Get function pointers and bind delegates
        IntPtr startPtr = GetProcAddress(_dllHandle, "StartListening");
        IntPtr stopPtr = GetProcAddress(_dllHandle, "StopListening");
        IntPtr callbackSetterPtr = GetProcAddress(_dllHandle, "SetFileChangedCallback");

        if (callbackSetterPtr != IntPtr.Zero)
        {
            var setCallback = Marshal.GetDelegateForFunctionPointer<SetFileChangedCallbackDelegate>(callbackSetterPtr);

            // Create callback function
            FileChangedCallback onFileChanged = (string changedPath) =>
            {
                // Marshal to UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _notifyIcon?.ShowBalloonTip(
                        5000,
                        "File Change Detected",
                        $"Change in: {changedPath}",
                        ToolTipIcon.Info
                    );
                });
            };

            // Set the callback in the DLL
            setCallback(onFileChanged);
        }

        if (startPtr == IntPtr.Zero || stopPtr == IntPtr.Zero)
        {
            System.Windows.MessageBox.Show("Failed to bind exported functions.");
            return;
        }

        _startListening = Marshal.GetDelegateForFunctionPointer<StartListeningDelegate>(startPtr);
        _stopListening = Marshal.GetDelegateForFunctionPointer<StopListeningDelegate>(stopPtr);

        // Step 4: Start listening on a folder
        if (selectedPathToWatch != "")
        {
            _startListening(selectedPathToWatch);
        }
    }


    private void SetupTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configure watched folder", null, (s, e) => PickDirectory());
        menu.Items.Add("Enable Auto-Start", null, (s, e) => RegisterStartup(true));
        menu.Items.Add("Disable Auto-Start", null, (s, e) => RegisterStartup(false));
        menu.Items.Add("Exit", null, (s, e) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
            Text = "File Listener"
        };
    }

    private void PickDirectory()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.Description = "Select folder to monitor";

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            selectedPathToWatch = dialog.SelectedPath;
            _stopListening?.Invoke(); // Stop old watcher
            _startListening?.Invoke(selectedPathToWatch); // Start new one
            _notifyIcon.ShowBalloonTip(5000, "Watching", $"Now monitoring: {selectedPathToWatch}", ToolTipIcon.Info);
        }
    }

    private void RegisterStartup(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        string appName = "TrayFileWatcher";

        if (enable)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            key.SetValue(appName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(appName, false);
        }
    }

    private void ExitApp()
    {
        try
        {
            _stopListening?.Invoke();
            if (_dllHandle != IntPtr.Zero)
                FreeLibrary(_dllHandle);
        }
        catch { }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Shutdown();
    }
}
