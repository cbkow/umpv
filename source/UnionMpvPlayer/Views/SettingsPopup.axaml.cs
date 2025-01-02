using Avalonia.Controls;
using System.Collections.ObjectModel;

namespace UnionMpvPlayer.Views
{
    public partial class SettingsPopup : Window
    {
        public ObservableCollection<KeyBindingItem> KeyBindings { get; } = new();

        public SettingsPopup()
        {
            DataContext = this;
            InitializeComponent();
            LoadKeyBindings();
        }

        private void LoadKeyBindings()
        {
            KeyBindings.Add(new KeyBindingItem("W", "Play / Pause"));
            KeyBindings.Add(new KeyBindingItem("O", "Open File"));
            KeyBindings.Add(new KeyBindingItem("I", "Info"));
            KeyBindings.Add(new KeyBindingItem("Q", "Previous Frame"));
            KeyBindings.Add(new KeyBindingItem("E", "Next Frame"));
            KeyBindings.Add(new KeyBindingItem("S", "Screenshot to Clipboard"));
            KeyBindings.Add(new KeyBindingItem("A", "Seek Backward"));
            KeyBindings.Add(new KeyBindingItem("D", "Seek Forward"));
            KeyBindings.Add(new KeyBindingItem("Z", "Go to Video Beginning"));
            KeyBindings.Add(new KeyBindingItem("C", "Go to Video End"));
            KeyBindings.Add(new KeyBindingItem("B", "Toggle Playlist"));
            KeyBindings.Add(new KeyBindingItem("R", "Toggle Looping"));
            KeyBindings.Add(new KeyBindingItem("F", "Toggle Full-screen Mode"));
            KeyBindings.Add(new KeyBindingItem("esc", "Exit Full-screen Mode"));
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }

    public class KeyBindingItem
    {
        public string Key { get; set; }
        public string Bindings { get; set; }
        public KeyBindingItem(string key, string bindings)
        {
            Key = key;
            Bindings = bindings;
        }
    }
}
