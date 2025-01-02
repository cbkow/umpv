using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;

namespace UnionMpvPlayer.Views
{
    public partial class RegistryPopup : Window
    {
        public RegistryPopup()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Debug.WriteLine("CloseButton_Click: Closing window.");
            this.Close();
        }

        private void InstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Debug.WriteLine("InstallButton_Click: Launching PowerShell script for installation.");
            LaunchPowerShell("install");
        }

        private void UninstallButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Debug.WriteLine("UninstallButton_Click: Launching PowerShell script for uninstallation.");
            LaunchPowerShell("uninstall");
        }

        private void LaunchPowerShell(string action)
        {
            try
            {
                // Extract the embedded script to a temporary file
                string scriptPath = ExtractEmbeddedScript();
                string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string iconPath = Path.Combine(Path.GetDirectoryName(appPath) ?? string.Empty, "Assets", "umpv.ico");

                if (string.IsNullOrEmpty(appPath) || string.IsNullOrEmpty(iconPath))
                {
                    Debug.WriteLine("LaunchPowerShell: Application or icon path could not be resolved.");
                    var toast = new ToastView();
                    toast.ShowToast("Error", "Failed to resolve application or icon path.", this);
                    return;
                }

                Debug.WriteLine($"LaunchPowerShell: Resolved app path: {appPath}");
                Debug.WriteLine($"LaunchPowerShell: Resolved icon path: {iconPath}");

                // Set up the PowerShell process with administrator privileges
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -action {action} -appPath \"{appPath}\" -iconPath \"{iconPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(processInfo);

                Debug.WriteLine($"LaunchPowerShell: Launched PowerShell script with action '{action}', app path '{appPath}', and icon path '{iconPath}'.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LaunchPowerShell: Failed to execute PowerShell script. Exception: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Error", "Failed to execute PowerShell script for administrative tasks.", this);
            }
        }

        private string ExtractEmbeddedScript()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "registry.ps1");
            Debug.WriteLine($"ExtractEmbeddedScript: Extracting script to {tempPath}");

            using (var stream = GetType().Assembly.GetManifestResourceStream("UnionMpvPlayer.Assets.registry.ps1"))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Failed to find embedded script resource.");
                }

                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            return tempPath;
        }
    }
}
