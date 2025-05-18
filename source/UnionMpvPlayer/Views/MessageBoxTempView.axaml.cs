using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;

namespace UnionMpvPlayer.Views
{
    public partial class MessageBoxTempView : Window
    {
        private DispatcherTimer _timer;

        public MessageBoxTempView()
        {
            InitializeComponent();
        }

        public void ShowMessage(string title, string message, string filePathToOpen = null, Window owner = null)
        {
            TitleText.Text = title;
            MessageText.Text = message;

            // Clear and set up buttons
            ButtonPanel.Children.Clear();

            if (!string.IsNullOrWhiteSpace(filePathToOpen))
            {
                var openButton = new Button
                {
                    Content = "Open File",
                    Width = 160,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                openButton.Click += (_, __) =>
                {
                    OpenFile(filePathToOpen);
                    Close();
                };
                ButtonPanel.Children.Add(openButton);
            }

            var closeButton = new Button
            {
                Content = "OK",
                Width = 160,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            closeButton.Click += (_, __) =>
            {
                Close();
            };
            ButtonPanel.Children.Add(closeButton);

            // Position the window
            PositionWindow();

            // Show the window
            Show();

            // Set up auto-dismiss timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _timer.Tick += (s, e) =>
            {
                Close();
                _timer.Stop();
            };
            _timer.Start();
        }

        private void PositionWindow()
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                var scale = screen.Scaling;

                double margin = 16;
                double rightEdge = workingArea.Right / scale - Width - margin;
                double bottomEdge = workingArea.Bottom / scale - Height - margin;

                Position = new PixelPoint(
                    (int)(rightEdge * scale),
                    (int)(bottomEdge * scale)
                );
            }
        }

        private void OpenFile(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageText.Text = $"Failed to open the file: {ex.Message}";
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}