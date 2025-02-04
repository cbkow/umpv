using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Views
{
    public class RevertImagePopup : Window
    {
        private Button _confirmButton;
        private Button _cancelButton;

        public event EventHandler? ConfirmClicked;

        public RevertImagePopup()
        {
            Width = 450;
            Height = 105;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
            ExtendClientAreaToDecorationsHint = true;

            var grid = new Grid
            {
                RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),  // Warning text
                new RowDefinition(GridLength.Auto)   // Buttons
            },
                Margin = new Thickness(20)
            };

            var warningText = new TextBlock
            {
                Text = "Revert to the original image? This cannot be undone.",
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            _confirmButton = new Button
            {
                Content = "Revert",
                Height = 30,
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                CornerRadius = new CornerRadius(4)
            };

            _cancelButton = new Button
            {
                Content = "Cancel",
                Height = 30,
                Width = 200,
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45)),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230)),
                CornerRadius = new CornerRadius(4)
            };

            _confirmButton.Click += ConfirmButton_Click;
            _cancelButton.Click += CancelButton_Click;

            buttonPanel.Children.Add(_confirmButton);
            buttonPanel.Children.Add(_cancelButton);

            Grid.SetRow(warningText, 0);
            Grid.SetRow(buttonPanel, 1);

            grid.Children.Add(warningText);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            ConfirmClicked?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

}
