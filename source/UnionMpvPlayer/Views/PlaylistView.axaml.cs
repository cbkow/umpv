using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Platform.Storage;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Styling;
using UnionMpvPlayer.Views;
using ShapePath = Avalonia.Controls.Shapes.Path;
using System.Diagnostics;

namespace UnionMpvPlayer.Views
{

    public partial class PlaylistView : UserControl
    {
        private readonly ObservableCollection<PlaylistItem> _playlistItems = new();
        private Action<string, bool>? _playCallback;
        private Action? _togglePlayPauseCallback;
        private int _currentPlayingIndex = -1;
        private bool _isCurrentlyPlaying;
        private bool _hasActivePlaylistItem;
        private static readonly string[] AcceptedExtensions = { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
        public event EventHandler<PlaylistItem>? PlaylistItemSelected;
        public MainWindow? ParentWindow { get; set; }


        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set
            {
                if (_isCurrentlyPlaying != value)
                {
                    _isCurrentlyPlaying = value;
                    UpdatePlayingState(); 
                }
            }
        }

        private bool _isPlaylistModeActive = true;
        public bool IsPlaylistModeActive
        {
            get => _isPlaylistModeActive;
            set
            {
                if (_isPlaylistModeActive != value)
                {
                    _isPlaylistModeActive = value;
                    UpdatePlaylistModeIcon();  
                }
            }
        }

        public bool IsPlaylistMode { get; set; }
        public string KeepOpenValue { get; set; }

        public class PlaylistModeChangedEventArgs : EventArgs
        {
            public bool IsPlaylistMode { get; set; }
            public string KeepOpenValue { get; set; }

            public PlaylistModeChangedEventArgs(bool isPlaylistMode)
            {
                IsPlaylistMode = isPlaylistMode;
                KeepOpenValue = isPlaylistMode ? "no" : "always";
            }
        }

        public event EventHandler<PlaylistModeChangedEventArgs>? PlaylistModeStateChanged;

        public PlaylistView()
        {
            InitializeComponent();
            PlaylistListBox.ItemsSource = _playlistItems;
            InitializeEventHandlers();
            InitializeDragDrop();
        }

        private void InitializeEventHandlers()
        {
            AddToPlaylistButton.Click += AddToPlaylist_Click;
            ClearPlaylistButton.Click += ClearPlaylist_Click;
            MoveUpButton.Click += MoveUp_Click;
            MoveDownButton.Click += MoveDown_Click;
            RemoveFromPlaylistButton.Click += RemoveFromPlaylist_Click;
            ForwardSpotButton.Click += ForwardSpot_Click;
            BackwardSpotButton.Click += BackwardSpot_Click;
            PlaylistListBox.DoubleTapped += OnPlaylistItemDoubleTapped;
            TogglePlaylistModeButton.Click += TogglePlaylistMode_Click;
        }

        private void InitializeDragDrop()
        {
            AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
        }

        public void SetCallbacks(Action<string, bool> playCallback, Action togglePlayPauseCallback)
        {
            _playCallback = playCallback;
            _togglePlayPauseCallback = togglePlayPauseCallback;
        }

        private void UpdatePlaylistModeIcon()
        {
            if (TogglePlaylistModeButton?.Content is ShapePath playlistModePath)
            {
                var iconKey = _isPlaylistModeActive ? "checkmark_circle_regular" : "circle_regular";
                if (App.Current?.Resources.TryGetResource(iconKey, ThemeVariant.Default, out object? resource) == true &&
                    resource is StreamGeometry geometry)
                {
                    playlistModePath.Data = geometry;
                }
            }
        }

        private void viewPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            ParentWindow?.PlaylistButton_Click(sender, e);
        }

        public void TriggerRemoveFromPlaylist()
        {
            RemoveFromPlaylist_Click(this, null);
        }

        private void TogglePlaylistMode_Click(object? sender, RoutedEventArgs e)
        {
            IsPlaylistModeActive = !IsPlaylistModeActive;

            if (IsPlaylistModeActive && _playlistItems.Any())
            {
                var currentlyPlaying = _playlistItems.FirstOrDefault(x => x.IsPlaying);
                if (currentlyPlaying != null)
                {
                    _currentPlayingIndex = _playlistItems.IndexOf(currentlyPlaying);
                    _hasActivePlaylistItem = true;
                    UpdateCurrentItemPlayState(_isCurrentlyPlaying);
                    UpdatePlayingState();
                }
            }

            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                var toast = new ToastView();
                if (IsPlaylistModeActive)
                {
                    toast.ShowToast("Success", "Playlist mode enabled.", window);
                }
                else
                {
                    toast.ShowToast("Success", "Playlist mode disabled.", window);
                }
            }

            PlaylistModeStateChanged?.Invoke(this, new PlaylistModeChangedEventArgs(IsPlaylistModeActive));
        }

        public event EventHandler<bool>? PlaylistModeChanged;

        private async void AddToPlaylist_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Videos to Add",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Video Files")
                    {
                        Patterns = AcceptedExtensions.Select(ext => $"*{ext}").ToArray()
                    }
                }
            });

            foreach (var file in files)
            {
                AddPlaylistItem(file.Path.LocalPath);
            }
        }

        private void AddPlaylistItem(string filePath)
        {
            _playlistItems.Add(new PlaylistItem(filePath, _playCallback, RemovePlaylistItem));
        }

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            e.DragEffects = IsValidDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = IsValidDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDrop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFileNames();
            if (files == null) return;
            foreach (var file in files.Where(IsValidVideoFile))
            {
                AddPlaylistItem(file);
            }
        }

        private static bool IsValidDrop(DragEventArgs e)
        {
            var files = e.Data.GetFileNames();
            return files?.Any(IsValidVideoFile) == true;
        }

        private static bool IsValidVideoFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return AcceptedExtensions.Contains(Path.GetExtension(filePath.ToLower()));
        }

        public void PlayNext() => PlayAtOffset(1);
        public void PlayPrevious() => PlayAtOffset(-1);

        private void PlayAtOffset(int offset)
        {
            if (_playlistItems.Count == 0) return;
            _currentPlayingIndex = (_currentPlayingIndex + offset + _playlistItems.Count) % _playlistItems.Count;
            PlayCurrentItem();
        }

        private void PlayCurrentItem()
        {
            if (_currentPlayingIndex < 0 || _currentPlayingIndex >= _playlistItems.Count) return;
            var item = _playlistItems[_currentPlayingIndex];
            _playCallback?.Invoke(item.FilePath, true);  
            UpdatePlayingState();
        }

        private void UpdatePlayingState()
        {
            for (int i = 0; i < _playlistItems.Count; i++)
            {
                _playlistItems[i].IsPlaying = (i == _currentPlayingIndex);
                if (i == _currentPlayingIndex && App.Current?.Resources != null)
                {
                   
                    var iconKey = _isCurrentlyPlaying ? "pause_regular" : "play_regular";
                    if (App.Current.Resources.TryGetResource(iconKey, ThemeVariant.Default, out object? resource) &&
                        resource is StreamGeometry geometry)
                    {
                        _playlistItems[i].PlayingIconData = geometry;
                    }
                }
            }
            PlaylistListBox.SelectedIndex = _currentPlayingIndex;
        }

        public void PlayFirst()
        {
            if (_playlistItems.Count > 0)
            {
                _currentPlayingIndex = 0;
                PlayCurrentItem();
            }
        }

        private void OnPlaylistItemDoubleTapped(object? sender, RoutedEventArgs e)
        {
            if (PlaylistListBox.SelectedItem is PlaylistItem selectedItem)
            {
                if (IsPlaylistModeActive)
                {
                    _currentPlayingIndex = PlaylistListBox.SelectedIndex;
                    _playCallback?.Invoke(selectedItem.FilePath, true); 
                    UpdatePlayingState();
                }
                else
                {
                    // Single file mode - just play the selected file
                    _currentPlayingIndex = -1; // Reset the playing index

                    // Update the visual state for all items
                    foreach (var item in _playlistItems)
                    {
                        item.IsPlaying = (item == selectedItem);
                        if (item == selectedItem && App.Current?.Resources != null)
                        {
                            if (App.Current.Resources.TryGetResource("play_regular", ThemeVariant.Default, out object? resource) &&
                                resource is StreamGeometry geometry)
                            {
                                item.PlayingIconData = geometry;
                            }
                        }
                        else
                        {
                            item.PlayingIconData = null;
                        }
                    }

                    PlaylistListBox.SelectedIndex = _playlistItems.IndexOf(selectedItem);
                    _playCallback?.Invoke(selectedItem.FilePath, false);  // Single file mode
                }
            }
        }

        public void UpdateCurrentItemPlayState(bool isPlaying)
        {
            if (!_hasActivePlaylistItem) return;
            var currentItem = _playlistItems.FirstOrDefault(x => x.IsPlaying);
            if (currentItem == null || App.Current?.Resources == null) return;

            var iconKey = isPlaying ? "pause_regular" : "play_regular";
            if (App.Current.Resources.TryGetResource(iconKey, ThemeVariant.Default, out object? resource) &&
                resource is StreamGeometry geometry)
            {
                currentItem.PlayingIconData = geometry;
            }
            _isCurrentlyPlaying = isPlaying;
        }

        private void UpdatePlayingIcons() => UpdateCurrentItemPlayState(_isCurrentlyPlaying);

        public void ClearAllPlayingStates()
        {
            foreach (var item in _playlistItems)
            {
                item.IsPlaying = false;
                item.PlayingIconData = null;
            }
            _currentPlayingIndex = -1;
            _hasActivePlaylistItem = false;
        }

        public void SetCurrentlyPlaying(string filePath)
        {
            var index = _playlistItems.Select((item, i) => new { Item = item, Index = i })
                                    .FirstOrDefault(x => x.Item.FilePath == filePath)?.Index ?? -1;

            if (index >= 0)
            {
                _currentPlayingIndex = index;
                _hasActivePlaylistItem = true;
                UpdatePlayingState();
            }
            else
            {
                ClearAllPlayingStates();
            }
        }

        private void ClearPlaylist_Click(object? sender, RoutedEventArgs e)
        {
            _playlistItems.Clear();


            var window = TopLevel.GetTopLevel(this) as Window;
            if (window != null)
            {
                var toast = new ToastView();
                toast.ShowToast("Warning", "Playlist cleared.", window);
            }
        }

        private void MoveUp_Click(object? sender, RoutedEventArgs e) => MoveSelectedItem(-1);
        private void MoveDown_Click(object? sender, RoutedEventArgs e) => MoveSelectedItem(1);

        private void MoveSelectedItem(int offset)
        {
            if (PlaylistListBox.SelectedItem is not PlaylistItem selectedItem) return;
            var currentIndex = _playlistItems.IndexOf(selectedItem);
            var newIndex = currentIndex + offset;
            if (newIndex >= 0 && newIndex < _playlistItems.Count)
            {
                _playlistItems.Move(currentIndex, newIndex);
                PlaylistListBox.SelectedIndex = newIndex;
            }
        }

        private void RemovePlaylistItem(PlaylistItem item)
        {
            if (_playlistItems.Contains(item))
            {
                _playlistItems.Remove(item);
            }
        }

        public void RemoveFromPlaylist_Click(object? sender, RoutedEventArgs e)
        {
            if (PlaylistListBox.SelectedItem is PlaylistItem selectedItem)
            {
                RemovePlaylistItem(selectedItem);
            }
        }

        private void ForwardSpot_Click(object? sender, RoutedEventArgs e) => PlayNext();
        private void BackwardSpot_Click(object? sender, RoutedEventArgs e) => PlayPrevious();
    }

    public class PlaylistItem : INotifyPropertyChanged
    {
        public ICommand PlayCommand { get; }
        public ICommand RemoveCommand { get; }
        private string _filePath = string.Empty;
        private string _displayName = string.Empty;
        private bool _isPlaying;
        private StreamGeometry? _playingIconData;

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                }
            }
        }

        public StreamGeometry? PlayingIconData
        {
            get => _playingIconData;
            set
            {
                if (_playingIconData != value)
                {
                    _playingIconData = value;
                    OnPropertyChanged(nameof(PlayingIconData));
                }
            }
        }

        public PlaylistItem(string filePath, Action<string, bool> playCallback, Action<PlaylistItem> removeCallback)
        {
            FilePath = filePath;
            DisplayName = Path.GetFileName(filePath);
            IsPlaying = false;

            if (App.Current?.Resources.TryGetResource("play_regular", ThemeVariant.Default, out object? resource) == true &&
                resource is StreamGeometry geometry)
            {
                PlayingIconData = geometry;
            }

            PlayCommand = new RelayCommand(() => playCallback(FilePath, true)); // Default to playlist mode
            RemoveCommand = new RelayCommand(() => removeCallback(this));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}