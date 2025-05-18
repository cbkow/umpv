using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace UnionMpvPlayer.Views
{
    public partial class TextInputPopup : Window
    {
        public string EnteredText { get; private set; } = "";
        public IBrush TextColor { get; private set; }
        public double FontSize { get; private set; } = 28;

        private TextBox? _textInput;
        private Slider? _fontSizeSlider;
        private TextBlock? _fontSizeText;

        public TextInputPopup(IBrush textColor)
        {
            InitializeComponent();
            TextColor = textColor;

            _textInput = this.FindControl<TextBox>("TextInput");
            _fontSizeSlider = this.FindControl<Slider>("FontSizeSlider");
            _fontSizeText = this.FindControl<TextBlock>("FontSizeText");

            // Handle font size changes
            if (_fontSizeSlider != null && _fontSizeText != null)
            {
                _fontSizeSlider.PropertyChanged += (s, e) => {
                    if (e.Property.Name == "Value")
                    {
                        _fontSizeText.Text = $"{(int)_fontSizeSlider.Value}px";
                    }
                };
            }

            // Focus text input when window opens
            this.Opened += (s, e) => _textInput?.Focus();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            EnteredText = _textInput?.Text ?? "";
            FontSize = _fontSizeSlider?.Value ?? 28;
            Close(true);
        }
    }
}