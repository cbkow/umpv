using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Views
{
    public class ProcessingPopup : Window
    {
        private ProgressBar _progressBar;
        private Button _cancelButton;

        public ProcessingPopup()
        {
            Width = 300;
            Height = 125;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
            ExtendClientAreaToDecorationsHint = true;

            var mainBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0)
            };

            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),  // For the text label
                    new RowDefinition(GridLength.Auto),  // For the progress bar
                    new RowDefinition(GridLength.Auto)   // For the button
                },
                Margin = new Thickness(20)
            };

            var statusText = new TextBlock
            {
                Text = "Generating cache...",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };

            _progressBar = new ProgressBar
            {
                Height = 8,
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                CornerRadius = new CornerRadius(0)
            };

            _cancelButton = new Button
            {
                Content = "Cancel",
                Height = 30,
                Width = 260,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center, 
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                CornerRadius = new CornerRadius(4)
            };

            _cancelButton.Click += CancelButton_Click;


            _cancelButton.Click += CancelButton_Click;

            // Set the Grid.Row for each element
            Grid.SetRow(statusText, 0);
            Grid.SetRow(_progressBar, 1);
            Grid.SetRow(_cancelButton, 2);

            // Add all elements to the grid
            grid.Children.Add(statusText);
            grid.Children.Add(_progressBar);
            grid.Children.Add(_cancelButton);

            Content = grid;
        }

        public void UpdateProgress(double value)
        {
            _progressBar.Value = value;
        }

        public event EventHandler CancelClicked;

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
