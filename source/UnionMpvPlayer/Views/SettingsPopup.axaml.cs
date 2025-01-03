using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UnionMpvPlayer.Views
{
    public partial class SettingsPopup : Window
    {
        public Action? OnSettingsUpdated { get; set; }

        private readonly string KeyBindingsFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "umpv",
            "keybindings.json"
        );
        public ObservableCollection<KeyBindingItem> KeyBindings { get; } = new();

        public SettingsPopup()
        {
            DataContext = this;
            InitializeComponent();
            LoadKeyBindings();
        }

        // Save keybindings to JSON
        private void SaveKeyBindings()
        {
            try
            {
                var directory = Path.GetDirectoryName(KeyBindingsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(KeyBindings);
                File.WriteAllText(KeyBindingsFilePath, json);
                Debug.WriteLine($"Keybindings saved to {KeyBindingsFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving keybindings: {ex.Message}");
            }
        }

        // Load keybindings from JSON
        public void LoadKeyBindings()
        {
            try
            {
                if (File.Exists(KeyBindingsFilePath))
                {
                    var json = File.ReadAllText(KeyBindingsFilePath);
                    var loadedBindings = JsonSerializer.Deserialize<ObservableCollection<KeyBindingItem>>(json);
                    if (loadedBindings != null)
                    {
                        KeyBindings.Clear();
                        foreach (var binding in loadedBindings)
                        {
                            KeyBindings.Add(binding);
                        }
                        Debug.WriteLine($"Keybindings loaded from {KeyBindingsFilePath}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading keybindings: {ex.Message}");
            }

            // Load default keybindings if JSON loading fails
            LoadDefaultKeyBindings();
        }

        private void LoadDefaultKeyBindings()
        {
            KeyBindings.Clear();
            KeyBindings.Add(new KeyBindingItem("W", "Play / Pause"));
            KeyBindings.Add(new KeyBindingItem("K", "Play / Pause Alt"));
            KeyBindings.Add(new KeyBindingItem("O", "Open File"));
            KeyBindings.Add(new KeyBindingItem("I", "Info"));
            KeyBindings.Add(new KeyBindingItem("Q", "Previous Frame"));
            KeyBindings.Add(new KeyBindingItem("E", "Next Frame"));
            KeyBindings.Add(new KeyBindingItem("S", "Screenshot to Clipboard"));
            KeyBindings.Add(new KeyBindingItem("A", "Seek Backward 1 sec"));
            KeyBindings.Add(new KeyBindingItem("J", "Play Backward Interval Speeds"));
            KeyBindings.Add(new KeyBindingItem("D", "Seek Forward 1 sec"));
            KeyBindings.Add(new KeyBindingItem("L", "Play Forward Interval Speeds"));
            KeyBindings.Add(new KeyBindingItem("Z", "Go to Video Beginning"));
            KeyBindings.Add(new KeyBindingItem("C", "Go to Video End"));
            KeyBindings.Add(new KeyBindingItem("B", "Toggle Playlist"));
            KeyBindings.Add(new KeyBindingItem("R", "Toggle Looping"));
            KeyBindings.Add(new KeyBindingItem("Y", "Play Video 1:1 Size"));
            KeyBindings.Add(new KeyBindingItem("H", "Play Video 50% Screen Size"));
            KeyBindings.Add(new KeyBindingItem("T", "16:9 Title/Action Safety"));
            KeyBindings.Add(new KeyBindingItem("F", "Toggle Full-screen Mode"));
            KeyBindings.Add(new KeyBindingItem("esc", "Exit Full-screen Mode"));
            KeyBindings.Add(new KeyBindingItem("Back", "Delete Playlist Item"));
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            // Save the updated keybindings
            ValidateKeyBindings();
            SaveKeyBindings();
            OnSettingsUpdated?.Invoke();
        }

        private void ValidateKeyBindings()
        {
            var duplicateKeys = KeyBindings
                .GroupBy(k => k.Key)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateKeys.Any())
            {
                Debug.WriteLine($"Duplicate keys detected: {string.Join(", ", duplicateKeys)}");
            }
        }

        private void ResetToDefault_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                // Delete the JSON file
                if (File.Exists(KeyBindingsFilePath))
                {
                    File.Delete(KeyBindingsFilePath);
                    Debug.WriteLine($"Keybindings file deleted: {KeyBindingsFilePath}");
                }

                // Reload default keybindings
                LoadDefaultKeyBindings();

                // Refresh the DataGrid
                DataContext = null;
                DataContext = this;

                Debug.WriteLine("Keybindings reset to default.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resetting keybindings to default: {ex.Message}");
            }
        }
    }


    public class KeyBindingItem : INotifyPropertyChanged
    {
        private string _key;

        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public string Bindings { get; set; }

        public KeyBindingItem(string key, string bindings)
        {
            _key = key;
            Bindings = bindings;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
