using Avalonia.Controls;
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
    public class ExportCompletePopup : Window
    {
        public event EventHandler<string>? OpenFileRequested;
        public event EventHandler<string>? OpenFolderRequested;

        public ExportCompletePopup(bool hideOpenButton = false)
        {
            Width = 370;
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
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
                Margin = new Thickness(20)
            };

            var messageText = new TextBlock
            {
                Text = "Export Complete",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 200))
            };

            var buttonStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10
            };

            if (!hideOpenButton)
            {
                var openButton = new Button
                {
                    Content = "Open",
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Width = 100,
                    Height = 30
                };

                openButton.Click += (s, e) =>
                {
                    OpenFileRequested?.Invoke(this, Tag as string ?? string.Empty);
                    Close();
                };

                buttonStack.Children.Add(openButton);
            }

            var openFolderButton = new Button
            {
                Content = "Open Folder",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Width = 100,
                Height = 30
            };

            var cancelButton = new Button
            {
                Content = "Close",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Width = 100,
                Height = 30
            };

            openFolderButton.Click += (s, e) =>
            {
                OpenFolderRequested?.Invoke(this, Tag as string ?? string.Empty);
                Close();
            };

            cancelButton.Click += (s, e) => Close();

            buttonStack.Children.Add(openFolderButton);
            buttonStack.Children.Add(cancelButton);

            Grid.SetRow(messageText, 0);
            Grid.SetRow(buttonStack, 1);
            grid.Children.Add(messageText);
            grid.Children.Add(buttonStack);

            Content = grid;
        }
    }
}
