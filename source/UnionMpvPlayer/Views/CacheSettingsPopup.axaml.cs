using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnionMpvPlayer.Helpers;

namespace UnionMpvPlayer.Views
{
    public partial class CacheSettingsPopup : Window
    {
        private static readonly string CacheConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "umpv", "exrcachepath.json"
        );

        public CacheSettingsPopup()
        {
            InitializeComponent();
            LoadCurrentCachePath();
        }

        // Load the current cache path into the UI
        private void LoadCurrentCachePath()
        {
            CurrentCachePath.Text = EXRSequenceHandler.GetCacheFolderPath();
        }

        // Browse and set a new cache path
        private async void BrowseNewPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Cache Folder"
            };
            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                SaveCachePathToJson(result, this);
                LoadCurrentCachePath();
            }
        }


        // Revert to the default cache path
        private void RevertToDefaultPath_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (File.Exists(CacheConfigPath))
            {
                File.Delete(CacheConfigPath);
                LoadCurrentCachePath();
            }
        }

        // Empty the cache directory
        private void EmptyCache_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            EmptyCache(this);
        }

        // Save the selected cache path to the JSON file
        private void SaveCachePathToJson(string path, Window window)
        {
            var config = new Dictionary<string, string> { { "CachePath", path } };
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(CacheConfigPath)!);
                File.WriteAllText(CacheConfigPath, json);

                // Show success toast
                var toast = new ToastView();
                toast.ShowToast("Info", "Cache path saved successfully.", window);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving cache path: {ex.Message}");

                // Show error toast
                var toast = new ToastView();
                toast.ShowToast("Error", $"Failed to save cache path: {ex.Message}", window);
            }
        }


        // Empty the cache folder
        public static void EmptyCache(Window window)
        {
            var cacheDir = EXRSequenceHandler.GetCacheFolderPath();
            if (Directory.Exists(cacheDir))
            {
                try
                {
                    // Delete all subdirectories and files
                    foreach (var directory in Directory.GetDirectories(cacheDir))
                    {
                        Directory.Delete(directory, true); // Recursive delete
                    }

                    // Delete all files in the root of the cache directory
                    foreach (var file in Directory.GetFiles(cacheDir))
                    {
                        File.Delete(file);
                    }

                    Debug.WriteLine("Cache emptied successfully.");
                    var toast = new ToastView();
                    toast.ShowToast("Info", "Cache emptied successfully.", window);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to empty cache: {ex.Message}");
                    var toast = new ToastView();
                    toast.ShowToast("Error", $"Failed to empty cache: {ex.Message}", window);
                }
            }
            else
            {
                Debug.WriteLine("Cache directory does not exist.");
                var toast = new ToastView();
                toast.ShowToast("Info", "Cache directory does not exist.", window);
            }
        }



        // Close the popup
        private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

    }
}
