using System.Security.Principal;
using System.Diagnostics;
using System.Windows;

namespace PreMonitor.Services
{
    public interface IPrivilegeService
    {
        bool IsRunningAsAdministrator();
        void RestartAsAdministrator();
        void ShowAdministratorRequiredMessage();
    }

    public class PrivilegeService : IPrivilegeService
    {
        public bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public void RestartAsAdministrator()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName,
                    Verb = "runas"
                };

                Process.Start(processInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法以管理员身份重新启动程序: {ex.Message}", 
                              "错误", 
                              MessageBoxButton.OK, 
                              MessageBoxImage.Error);
            }
        }

        public void ShowAdministratorRequiredMessage()
        {
            var result = MessageBox.Show(
                "此程序需要管理员权限才能正常运行。\n\n是否要以管理员身份重新启动程序？",
                "需要管理员权限",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                RestartAsAdministrator();
            }
        }
    }
}
