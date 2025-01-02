using Avalonia.Controls;
using System;

namespace UnionMpvPlayer.Views
{
    public partial class FrameRatePopup : Window
    {
        public Action<string> OnFrameRateSelected;

        public FrameRatePopup()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var frameRate = FrameRateInput.Text;
            if (!string.IsNullOrEmpty(frameRate) && int.TryParse(frameRate, out _))
            {
                OnFrameRateSelected?.Invoke(frameRate);
                Close();
            }
            else
            {
                // Display your custom toast popup
                var toast = new ToastView();
                toast.ShowToast("Warning", "Please enter a valid fps.", this);
            }
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
