using System.Windows;

namespace PreMonitor.Services
{
    public interface IDialogService
    {
        void ShowMessage(string title, string message);
        bool ShowConfirmation(string title, string message);
        string? ShowInputDialog(string title, string prompt, string defaultValue = "");
    }

    public class DialogService : IDialogService
    {
        public void ShowMessage(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public bool ShowConfirmation(string title, string message)
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        public string? ShowInputDialog(string title, string prompt, string defaultValue = "")
        {
            // 简单的输入对话框实现
            var inputDialog = new InputDialog(title, prompt, defaultValue);
            if (inputDialog.ShowDialog() == true)
            {
                return inputDialog.InputText;
            }
            return null;
        }
    }
}
