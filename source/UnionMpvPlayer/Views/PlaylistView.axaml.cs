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

namespace UnionMpvPlayer.Views
{
    /// <summary>
    /// View component for managing video playlist functionality
    /// </summary>

    public partial class PlaylistView : UserControl
    {
        private readonly ObservableCollection<PlaylistItem> _playlistItems = new();
        private Action<string>? _playCallback;
        private Action? _togglePlayPauseCallback;
        private int _currentPlayingIndex = -1;
        private bool _isCurrentlyPlaying;
        private bool _hasActivePlaylistItem;
        // Accepted video file extensions
        private static readonly string[] AcceptedExtensions = { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
        public event EventHandler<PlaylistItem>? PlaylistItemSelected;

        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set
            {
                if (_isCurrentlyPlaying != value)
                {
                    _isCurrentlyPlaying = value;
                    UpdatePlayingIcons();
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

        public void SetCallbacks(Action<string> playCallback, Action togglePlayPauseCallback)
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

        private void TogglePlaylistMode_Click(object? sender, RoutedEventArgs e)
        {
            IsPlaylistModeActive = !IsPlaylistModeActive;

            // Get the parent window
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

            PlaylistModeChanged?.Invoke(this, IsPlaylistModeActive);
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
            _playCallback?.Invoke(item.FilePath);
            UpdatePlayingState();
        }

        private void UpdatePlayingState()
        {
            for (int i = 0; i < _playlistItems.Count; i++)
            {
                _playlistItems[i].IsPlaying = (i == _currentPlayingIndex);
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
                // Enable playlist mode if it's not already active
                if (!IsPlaylistModeActive)
                {
                    IsPlaylistModeActive = true;  // This will trigger UpdatePlaylistModeIcon() through the property setter

                    // Show toast notification
                    var window = TopLevel.GetTopLevel(this) as Window;
                    if (window != null)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Success", "Playlist mode enabled.", window);
                    }

                    // Notify main window
                    PlaylistModeChanged?.Invoke(this, true);
                }

                _currentPlayingIndex = PlaylistListBox.SelectedIndex;
                _playCallback?.Invoke(selectedItem.FilePath);
                UpdatePlayingState();
            }
        }

        public void UpdateCurrentItemPlayState(bool isPlaying)
        {
            if (!_hasActivePlaylistItem) return;
            var currentItem = _playlistItems.FirstOrDefault(x => x.IsPlaying);
            if (currentItem == null || App.Current?.Resources == null) return;
            var iconKey = isPlaying ? "play_regular" : "pause_regular";
            if (App.Current.Resources.TryGetResource(iconKey, ThemeVariant.Default, out object? resource) &&
                resource is StreamGeometry geometry)
            {
                currentItem.PlayingIconData = geometry;
            }
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

            // Show toast notification
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

        private void RemoveFromPlaylist_Click(object? sender, RoutedEventArgs e)
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

        public PlaylistItem(string filePath, Action<string> playCallback, Action<PlaylistItem> removeCallback)
        {
            FilePath = filePath;
            DisplayName = Path.GetFileName(filePath);
            IsPlaying = false;

            if (App.Current?.Resources.TryGetResource("play_regular", ThemeVariant.Default, out object? resource) == true &&
                resource is StreamGeometry geometry)
            {
                PlayingIconData = geometry;
            }

            PlayCommand = new RelayCommand(() => playCallback(FilePath));
            RemoveCommand = new RelayCommand(() => removeCallback(this));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}