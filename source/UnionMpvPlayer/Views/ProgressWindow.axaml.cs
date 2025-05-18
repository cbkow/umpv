using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace UnionMpvPlayer.Views
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();

            // Set up window drag on the title bar
            var titleBar = this.FindControl<Border>("TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (s, e) =>
                {
                    if (e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed)
                    {
                        BeginMoveDrag(e);
                    }
                };
            }
        }

        public void Show(string title, string message, Window owner = null)
        {
            var titleText = this.FindControl<TextBlock>("TitleText");
            var messageText = this.FindControl<TextBlock>("MessageText");
            var progressBar = this.FindControl<ProgressBar>("ProgressBar");

            if (titleText != null) titleText.Text = title;
            if (messageText != null) messageText.Text = message;
            if (progressBar != null) progressBar.Value = 0;

            // Set window position
            if (owner != null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                // Note: We don't set Owner directly, we'll use it when showing
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Show the window
            if (owner != null)
            {
                this.Show(owner);
            }
            else
            {
                this.Show();
            }
        }

        public void UpdateProgress(double progress, string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var messageText = this.FindControl<TextBlock>("MessageText");
                var progressBar = this.FindControl<ProgressBar>("ProgressBar");

                if (messageText != null) messageText.Text = message;
                if (progressBar != null) progressBar.Value = progress;
            });
        }
    }
}