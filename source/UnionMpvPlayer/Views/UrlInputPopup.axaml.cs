using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace UnionMpvPlayer.Views
{
    public partial class UrlInputPopup : Window
    {
        public string? EnteredUrl { get; private set; }

        public UrlInputPopup()
        {
            InitializeComponent();

            // Bind events
            var okButton = this.FindControl<Button>("OkButton");
            var cancelButton = this.FindControl<Button>("CancelButton");
            var urlTextBox = this.FindControl<TextBox>("UrlTextBox");

            okButton.Click += (_, __) =>
            {
                EnteredUrl = urlTextBox.Text;
                Close();
            };

            cancelButton.Click += (_, __) =>
            {
                EnteredUrl = null;
                Close();
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }
}
