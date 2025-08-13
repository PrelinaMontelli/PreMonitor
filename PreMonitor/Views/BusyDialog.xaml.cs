using System;
using System.Windows;
using PreMonitor.Services;

namespace PreMonitor.Views
{
    public partial class BusyDialog : Window
    {
        public BusyDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message;
        }
    }
}
