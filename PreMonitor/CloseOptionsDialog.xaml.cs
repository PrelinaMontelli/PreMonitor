using System.Windows;

namespace PreMonitor
{
    public partial class CloseOptionsDialog : Window
    {
        public CloseOption SelectedOption { get; private set; } = CloseOption.Cancel;
        public bool RememberChoice { get; private set; } = false;

        public CloseOptionsDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (MinimizeToTrayRadio.IsChecked == true)
            {
                SelectedOption = CloseOption.MinimizeToTray;
            }
            else if (ExitRadio.IsChecked == true)
            {
                SelectedOption = CloseOption.Exit;
            }

            RememberChoice = RememberChoiceCheckBox.IsChecked == true;
            
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = CloseOption.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
