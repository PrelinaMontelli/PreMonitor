using System.Windows;

namespace PreMonitor.Services
{
    public partial class InputDialog : Window
    {
        public string InputText { get; set; } = "";

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Content = prompt;
            InputTextBox.Text = defaultValue;
            InputText = defaultValue;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
