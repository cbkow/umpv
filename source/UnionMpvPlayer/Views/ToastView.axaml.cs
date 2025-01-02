using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UnionMpvPlayer.ViewModels;

namespace UnionMpvPlayer.Views
{
    public partial class ToastView : Window
    {
        private DispatcherTimer _timer;

        public ToastView()
        {
            InitializeComponent();
            DataContext = new ToastViewModel();
        }

        public void ShowToast(string title, string message, Window mainWindow)
        {
            // Update the ViewModel properties
            if (DataContext is ToastViewModel viewModel)
            {
                viewModel.Title = title;
                viewModel.Message = message;
            }

            // Position the toast in the bottom-right corner
            var screens = mainWindow.Screens;
            var screen = screens.Primary ?? screens.All.FirstOrDefault();
            if (screen != null)
            {
                var scalingFactor = screen.Scaling;
                var screenBounds = screen.WorkingArea;

                // Apply margins for Fluent Design
                const int margin = 16;
                var xPos = screenBounds.Width - (Width * scalingFactor) - (margin * scalingFactor);
                var yPos = screenBounds.Height - (Height * scalingFactor) - (margin * scalingFactor);

                Position = new PixelPoint((int)xPos, (int)yPos);
            }

            Show();

            // Timer for auto-dismissal
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _timer.Tick += (s, e) =>
            {
                Close();
                _timer.Stop();
            };
            _timer.Start();
        }


        public void SetContent(string title, string message)
        {
            // Update the ToastTitle and ToastMessage TextBlocks
            ToastTitle.Text = title;
            ToastMessage.Text = message;
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            Close();
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            _timer?.Stop(); // Stop the auto-dismissal timer
            Close();        // Close the toast window
        }
    }
}
