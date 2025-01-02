using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using ReactiveUI;
using UnionMpvPlayer.ViewModels;
using UnionMpvPlayer.Helpers;
using DrawingImage = System.Drawing.Image;
using DrawingPoint = System.Drawing.Point;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;

namespace UnionMpvPlayer.Views
{
    public partial class MainWindow : Window
    {
        private const ulong PAUSE_PROPERTY_ID = 3;
        private const double UPDATE_THRESHOLD = 0.1;
        private IPlatformHandle? windowHandle;
        private IntPtr mpvHandle;
        private IntPtr childWindowHandle;
        private bool _isLoadingVideo = false;
        private bool _currentVideoInitialized = false;
        private bool isSliderDragging = false;
        private bool isUpdatingSlider = false;
        private bool isVolumeSliderDragging = false;
        private bool _isInitialized = false;
        private bool isFilterActive = false;
        private bool isFullScreen = false;
        private bool isLooping = false;
        private bool _isPhotoFilterActive = false;
        private bool _isPlaylistMode = false;
        private bool _isDisposing = false;
        private Button playButton;
        private Button prevFrameButton;
        private Button nextFrameButton;
        private Button toStartButton;
        private Button toEndButton;
        private Button volumeButton;
        private Button? _playlistToggleButton;
        private Slider volumeSlider;
        private Slider playbackSlider;
        private MenuItem openMenuItem;
        private Panel videoContainer;
        private DateTime _lastDragEventTime = DateTime.MinValue;
        private CancellationTokenSource? _eventLoopCancellation;
        private TaskCompletionSource<bool> _cleanupComplete = new();
        private readonly double _defaultSpeed = 1.0;
        private double _lastPosition = 0;
        private double _lastDuration = 0;
        private double _currentSpeed = 1.0;
        private string? _currentLutPath = null;
        private string _lutsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "umpv",
            "luts"
        );
        private PlaylistView? _playlistView;
        private BackgroundWindow? _backgroundWindow;

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
            if (_playlistView != null)
            {
                _playlistView.SetCallbacks(
                    filePath =>
                    {
                        Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            try
                            {
                                await LoadVideo(filePath, true);
                            }
                            catch (Exception ex)
                            {
                                //Debug.WriteLine($"MainWindow: Error in LoadVideo: {ex.Message}");
                            }
                        });
                    },
                    () =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            TogglePlayPause();
                        });
                    }
                );
            }
            else
            {
                //Debug.WriteLine("Warning: PlaylistView not found in XAML");
            }
            if (_playlistView != null)
            {
                _playlistView.PlaylistModeChanged += (s, isActive) =>
                {
                    _isPlaylistMode = isActive;
                    // Update MPV's keep-open state
                    var keepOpenValue = isActive ? "no" : "always";
                    MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", keepOpenValue);
                };
            }
            InitializeMPV();
            // Check for a passed file or load blank.mp4
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1]))
            {
                LoadVideo(args[1], false);  // Single file mode for command line
            }
            else
            {
                LoadBlankVideo();
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            playButton = this.FindControl<Button>("PlayButton");
            prevFrameButton = this.FindControl<Button>("PrevFrameButton");
            nextFrameButton = this.FindControl<Button>("NextFrameButton");
            toStartButton = this.FindControl<Button>("ToStartButton");
            toEndButton = this.FindControl<Button>("ToEndButton");
            volumeButton = this.FindControl<Button>("VolumeButton");
            FullScreenButton = this.FindControl<Button>("FullScreenButton");
            openMenuItem = this.FindControl<MenuItem>("OpenMenuItem");
            playbackSlider = this.FindControl<Slider>("PlaybackSlider");
            volumeSlider = this.FindControl<Slider>("VolumeSlider");
            videoContainer = this.FindControl<Panel>("VideoContainer");
            videoContainer.Background = Avalonia.Media.Brushes.Black;
            UpdateVolumeIcon(false); // Initialize with unmuted state
            CurrentTimeTextBlock = this.FindControl<TextBlock>("CurrentTimeTextBlock");
            CurrentFrameTextBlock = this.FindControl<TextBlock>("CurrentFrameTextBlock");
            LoopingPath = this.FindControl<Avalonia.Controls.Shapes.Path>("LoopingPath");
            PhotoFilterIcon = this.FindControl<Avalonia.Controls.Shapes.Path>("PhotoFilterIcon");
            playButton.Click += PlayButton_Click;
            prevFrameButton.Click += PrevFrameButton_Click;
            nextFrameButton.Click += NextFrameButton_Click;
            openMenuItem.Click += OpenMenuItem_Click;
            volumeButton.Click += VolumeButton_Click;
            playbackSlider.PropertyChanged += PlaybackSlider_ValueChanged;
            playbackSlider.PointerPressed += PlaybackSlider_PointerPressed;
            playbackSlider.PointerReleased += PlaybackSlider_PointerReleased;
            volumeSlider.PropertyChanged += VolumeSlider_PropertyChanged;
            volumeSlider.PointerPressed += VolumeSlider_PointerPressed;
            volumeSlider.PointerReleased += VolumeSlider_PointerReleased;
            KeyDown += MainWindow_KeyDown;
            _playlistView = this.FindControl<PlaylistView>("PlaylistView");
            _playlistToggleButton = this.FindControl<Button>("PlaylistToggle");

            // Subscribe to container size changes
            videoContainer.PropertyChanged += (sender, args) =>
            {
                if (args.Property.Name == nameof(videoContainer.Bounds) && childWindowHandle != IntPtr.Zero)
                {
                    UpdateChildWindowBounds();
                }
            };

            videoContainer = this.FindControl<Panel>("VideoContainer");
            videoContainer.LayoutUpdated += (s, e) =>
            {
                if (childWindowHandle != IntPtr.Zero)
                {
                    UpdateChildWindowBounds();
                }
            };
        }

        // Background Window

        private void InitializeBackgroundWindow()
        {
            _backgroundWindow = new BackgroundWindow();
            _backgroundWindow.Initialize(this, videoContainer);
            _backgroundWindow.Show();
            OnVideoModeChanged();
        }

        public void OnVideoModeChanged()
        {
            // Ensure the background window is updated
            _backgroundWindow?.TriggerUpdate();
        }

        private void EnsureCorrectWindowOrder()
        {
            if (_backgroundWindow == null || childWindowHandle == IntPtr.Zero) return;

            try
            {
                // Get the background window's platform handle
                var bgHandle = _backgroundWindow.TryGetPlatformHandle();
                if (bgHandle != null)
                {
                    // Move the MPV window to the topmost position
                    WindowManagement.SetWindowPos(
                        childWindowHandle,
                        WindowManagement.HWND_TOP,  // Move to the very top
                        0, 0, 0, 0,
                        WindowManagement.SWP_NOMOVE | WindowManagement.SWP_NOSIZE | WindowManagement.SWP_NOACTIVATE
                    );

                    // Move the background window immediately beneath the MPV window
                    WindowManagement.SetWindowPos(
                        bgHandle.Handle,
                        childWindowHandle,  // Place directly under the MPV window
                        0, 0, 0, 0,
                        WindowManagement.SWP_NOMOVE | WindowManagement.SWP_NOSIZE | WindowManagement.SWP_NOACTIVATE
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adjusting window positions: {ex.Message}");
            }

        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property.Name == nameof(WindowState))
            {
                // Small delay to let the window state change complete
                Task.Delay(50).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _backgroundWindow?.UpdatePosition();
                        EnsureCorrectWindowOrder();
                    });
                });
            }
        }

        // Sync playback in PlaylistView
        private async Task UpdatePlayState()
        {
            try
            {
                if (mpvHandle != IntPtr.Zero)
                {
                    var isPaused = GetPropertyAsInt("pause") == 1;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_playlistView != null)
                        {
                            _playlistView.IsCurrentlyPlaying = !isPaused;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error updating play state: {ex.Message}");
            }
        }

        public void TogglePlayPause()
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                var args = new[] { "cycle", "pause" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"MPV Error: {MPVInterop.GetError(result)}");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in TogglePlayPause: {ex.Message}");
            }
        }

        // Toast message
        public void ShowToast(string title, string message)
        {
            var toast = new ToastView();
            toast.SetContent(title, message);
            toast.Show();
            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5) // Adjust duration if needed
            };
            timer.Tick += (s, e) =>
            {
                toast.Close();
                timer.Stop();
            };
            timer.Start();
        }


        // Draggable Menu Bar
        private void Menu_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
            }
        }

        private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        // Drag and drop main window
        private void MainGrid_DragEnter(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFileNames();
            if (files != null)
            {
                var acceptedExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
                if (files.Any(file => acceptedExtensions.Contains(Path.GetExtension(file).ToLower())))
                {
                    e.DragEffects = DragDropEffects.Copy;
                }
                else
                {
                    e.DragEffects = DragDropEffects.None;
                }
            }
        }

        private void MainGrid_DragOver(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFileNames();
            if (files != null)
            {
                var acceptedExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
                e.DragEffects = files.Any(file => acceptedExtensions.Contains(Path.GetExtension(file).ToLower()))
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void MainGrid_Drop(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFileNames()?.ToList();
            if (files != null && files.Any())
            {
                var acceptedExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
                var validFile = files.FirstOrDefault(file => acceptedExtensions.Contains(Path.GetExtension(file).ToLower()));

                if (validFile != null)
                {
                    //Debug.WriteLine($"Valid video file dropped: {validFile}");
                    try
                    {
                        await LoadVideo(validFile, false);  // Single file mode
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error loading video: {ex.Message}");
                    }
                }
            }
        }

        // A graceful exit
        private async Task CleanupMpv()
        {
            if (_isDisposing) return;
            _isDisposing = true;

            try
            {
                _eventLoopCancellation?.Cancel();
                await Task.Delay(100);
                // Cleanup child window
                if (childWindowHandle != IntPtr.Zero)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DestroyWindow(childWindowHandle);
                        childWindowHandle = IntPtr.Zero;
                    });
                }

                if (mpvHandle != IntPtr.Zero)
                {
                    MPVInterop.mpv_terminate_destroy(mpvHandle);
                    mpvHandle = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                _cleanupComplete.TrySetResult(true);
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            if (_isDisposing) return;

            e.Cancel = true;
            await CleanupMpv();

            _backgroundWindow?.Close();
            _backgroundWindow = null;

            await _cleanupComplete.Task;
            base.OnClosing(e);
            e.Cancel = false;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        // Lut Menu
        private async Task EnsureLutsExtracted()
        {
            try
            {
                // Create the LUTs directory if it doesn't exist
                if (!Directory.Exists(_lutsDirectory))
                {
                    Directory.CreateDirectory(_lutsDirectory);
                }

                // Check if LUTs are already extracted by looking for a marker file
                string markerFile = Path.Combine(_lutsDirectory, ".extracted");
                if (File.Exists(markerFile))
                {
                    return; // LUTs are already extracted
                }

                // Get the path to the luts.zip in the Assets folder
                string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                string lutsZipPath = Path.Combine(assetsPath, "luts.zip");

                if (!File.Exists(lutsZipPath))
                {
                    //Debug.WriteLine("LUTs zip file not found in Assets folder");
                    return;
                }

                // Extract the ZIP file
                await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(lutsZipPath, _lutsDirectory, true);
                });

                // Create marker file to indicate successful extraction
                File.WriteAllText(markerFile, DateTime.Now.ToString());
                //Debug.WriteLine("LUTs extracted successfully");
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error extracting LUTs: {ex.Message}");
            }
        }

        private async Task ApplyLut(string lutFileName)
        {
            try
            {
                await EnsureLutsExtracted();

                string lutPath = Path.Combine(_lutsDirectory, lutFileName);
                if (!File.Exists(lutPath))
                {
                    //Debug.WriteLine($"LUT file not found: {lutPath}");
                    return;
                }

                // Store video state before reloading
                var currentPath = MPVInterop.GetStringProperty(mpvHandle, "path");
                var currentPosition = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
                var isPaused = GetPropertyAsInt("pause") == 1;
                var currentVolume = MPVInterop.GetDoubleProperty(mpvHandle, "volume");

                if (string.IsNullOrEmpty(currentPath))
                {
                    //Debug.WriteLine("No video is currently loaded");
                    return;
                }

                // Update current LUT path and apply LUT parameter
                _currentLutPath = lutPath;

                // Set the LUT parameter
                var result = MPVInterop.mpv_set_option_string(mpvHandle, "lut", lutPath);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to set LUT: {MPVInterop.GetError(result)}");
                    return;
                }

                // Reload the video to apply the LUT
                var args = new[] { "loadfile", currentPath, "replace" };
                result = MPVInterop.mpv_command(mpvHandle, args);

                if (string.IsNullOrEmpty(currentPath))
                {
                    //Debug.WriteLine("No video is currently loaded");
                    return;
                }

                // Wait for the file to load
                await WaitForFileLoadedAndSeek();

                // Restore previous state
                if (currentPosition.HasValue)
                {
                    SeekToPosition(currentPosition.Value);
                }

                if (currentVolume.HasValue)
                {
                    SetVolume(currentVolume.Value);
                }

                if (isPaused)
                {
                    MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "yes" });
                }

                //Debug.WriteLine($"LUT applied successfully: {lutFileName}");
                var toast = new ToastView();
                toast.ShowToast("Success", "Color transform applied.", this);
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error applying LUT: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error applying LUT.", this);
            }
        }

        private async Task RemoveLut()
        {
            try
            {
                // Store video state before removing LUT
                var currentPath = MPVInterop.GetStringProperty(mpvHandle, "path");
                var currentPosition = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
                var isPaused = GetPropertyAsInt("pause") == 1;
                var currentVolume = MPVInterop.GetDoubleProperty(mpvHandle, "volume");

                if (string.IsNullOrEmpty(currentPath))
                {
                    //Debug.WriteLine("No video is currently loaded");
                    return;
                }

                // Clear the current LUT path
                _currentLutPath = null;

                // Set empty LUT parameter to remove LUT
                var result = MPVInterop.mpv_set_option_string(mpvHandle, "lut", "");
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to clear LUT: {MPVInterop.GetError(result)}");
                    return;
                }

                // Reload the video to apply the change
                var args = new[] { "loadfile", currentPath, "replace" };
                result = MPVInterop.mpv_command(mpvHandle, args);

                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to reload video: {MPVInterop.GetError(result)}");
                    return;
                }

                // Wait for file to load
                await WaitForFileLoadedAndSeek();

                // Restore previous state
                if (currentPosition.HasValue)
                {
                    SeekToPosition(currentPosition.Value);
                }

                if (currentVolume.HasValue)
                {
                    SetVolume(currentVolume.Value);
                }

                if (isPaused)
                {
                    MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "yes" });
                }

                //Debug.WriteLine("LUT removed successfully");
                var toast = new ToastView();
                toast.ShowToast("Success", "Color transforms removed.", this);
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error removing LUT: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error removing color transforms.", this);
            }
        }

        // Click handlers for different LUT options
        private async void noColor_Click(object? sender, RoutedEventArgs e)
        {
            RemoveLut();
        }

        private async void sRGB_rec709_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("709_sRGB.cube");
        }

        private async void sRGB_ACES2065_1_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACES2065_1_to_sRGB.cube");
        }

        private async void sRGB_ACEScg_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACEScg_to_sRGB.cube");
        }

        private async void sRGB_AGX_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("AGX_to_sRGB.cube");
        }

        private async void sRGB_Linear_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("Linear_to_sRGB.cube");
        }

        private async void sRGB_ArriLogc3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC3_to_sRGB.cube");
        }

        private async void sRGB_ArriLogc4_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC4_to_sRGB.cube");
        }

        private async void sRGB_CanonLog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("CanonLog3_to_sRGB.cube");
        }

        private async void sRGB_PanasonicVlog_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("PanasonicVlog_to_sRGB.cube");
        }

        private async void sRGB_RedLog3G10_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("RedLog3G10_to_sRGB.cube");
        }

        private async void sRGB_SonySlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3_to_sRGB.cube");
        }

        private async void sRGB_SonyVeniceSlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3Venice_to_sRGB.cube");
        }

        // Rec709 conversion handlers
        private async void rec709_ACES2065_1_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACES2065_1_to_rec709.cube");
        }

        private async void rec709_ACEScg_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACEScg_to_rec709.cube");
        }

        private async void rec709_AGX_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("AGX_to_bt1886.cube");
        }

        private async void rec709_Linear_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("Linear_to_Rec1886.cube");
        }

        private async void rec709_ArriLogc3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC3_to_rec709.cube");
        }

        private async void rec709_ArriLogc4_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC4_to_rec709.cube");
        }

        private async void rec709_CanonLog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("CanonLog3_to_rec709.cube");
        }

        private async void rec709_PanasonicVlog_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("PanasonicVlog_to_rec709.cube");
        }

        private async void rec709_RedLog3G10_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("RedLog3G10_to_rec709.cube");
        }

        private async void rec709_SonySlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3_to_rec709.cube");
        }

        private async void rec709_SonyVeniceSlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3Venice_to_rec709.cube");
        }

        // File Menu
        private async void OpenMenuItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Video File",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Video Files", Extensions = new List<string> { "mp4", "mov", "mxf", "gif", "mkv", "avi" } }
                }
            };
            var result = await openFileDialog.ShowAsync((Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
            if (result != null && result.Length > 0)
            {
                await LoadVideo(result[0], false);  // Single file mode
            }
        }

        private void OnExitMenuItemClick(object? sender, RoutedEventArgs e)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.Shutdown(); // Properly shuts down the application
            }
        }

        private async void OnOpenUrlPopupClick(object? sender, RoutedEventArgs e)
        {
            var popup = new UrlInputPopup(); 
            await popup.ShowDialog(this);  
            if (!string.IsNullOrEmpty(popup.EnteredUrl))
            {
                PlayUrl(popup.EnteredUrl);
            }
        }

        private void PlayUrl(string url)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                string fileName = GetFileNameFromUrl(url);
                string newTitle = $"umpv - {fileName}";
                LoadVideo(url, false).ConfigureAwait(false);  // Single file mode
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error playing URL: {ex.Message}");
            }
        }
        
        private string GetFileNameFromUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return Path.GetFileName(uri.LocalPath); // Extract the last segment of the path
            }
            catch
            {
                return "Unknown"; // Fallback if the URL cannot be parsed
            }
        }

        public async Task PlayImageSequence(string imagePath, string frameRate)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                //Debug.WriteLine($"Starting image sequence load process for: {imagePath}");
                // Reset state
                _lastDuration = 0;
                _lastPosition = 0;
                // Derive sequence path
                var directory = Path.GetDirectoryName(imagePath);
                var fileName = Path.GetFileNameWithoutExtension(imagePath);
                var extension = Path.GetExtension(imagePath);
                var fileNameWithoutNumericSequence = System.Text.RegularExpressions.Regex.Replace(fileName, @"_\d+$", "");
                var sequencePath = $"mf://{directory}/{fileNameWithoutNumericSequence}_*{extension}";
                var newTitle = $"umpv - {fileNameWithoutNumericSequence}_*{extension}";
                // Update UI elements
                await Dispatcher.UIThread.InvokeAsync(() => {
                    playbackSlider.Value = 0;
                    playbackSlider.Maximum = 0;
                    CurrentTimeTextBlock.Text = "00:00:00:00";
                    CurrentFrameTextBlock.Text = "00000000";
                    var viewModel = DataContext as MainWindowViewModel;
                    if (viewModel != null)
                    {
                        viewModel.TopTitle = newTitle;
                    }
                    // Update playlist state
                    _playlistView?.SetCurrentlyPlaying(sequencePath);
                });
                // Cancel any existing timecode updates
                if (_eventLoopCancellation != null)
                {
                    await _eventLoopCancellation.CancelAsync();
                    _eventLoopCancellation = new CancellationTokenSource();
                }
                // Load the image sequence
                //Debug.WriteLine("Sending loadfile command for image sequence to MPV");
                var fpsOption = $"--mf-fps={frameRate}";
                MPVInterop.mpv_set_option_string(mpvHandle, "mf-fps", frameRate);
                var args = new[] { "loadfile", sequencePath, "replace" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to load image sequence: error code {result}");
                    return;
                }
                bool success = await WaitForDurationAndInitialize();
                if (success)
                {
                    //Debug.WriteLine("Duration initialized successfully, starting timecode updates");
                    _ = UpdateTimecodeAsync();
                    var playArgs = new[] { "set", "pause", "no" };
                    int playResult = MPVInterop.mpv_command(mpvHandle, playArgs);
                    if (playResult < 0)
                    {
                        //Debug.WriteLine($"Failed to start playback: {MPVInterop.GetError(playResult)}");
                    }
                    else
                    {
                        //Debug.WriteLine("Successfully started playback of image sequence");
                        _ = UpdatePlayPauseIcon();
                    }
                }
                else
                {
                    //Debug.WriteLine("Failed to initialize duration for image sequence");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in PlayImageSequence: {ex.Message}");
                //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // App Menu

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new SettingsPopup();
            popup.ShowDialog(this);
        }

        private void RegistryButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new RegistryPopup();
            popup.ShowDialog(this);
        }

        // Speed Menu
        private void OnFF20Click(object? sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(_defaultSpeed + 2.0);
        }

        private void OnFF05Click(object? sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(_defaultSpeed + 0.5);
        }

        private void OnFF025Click(object? sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(_defaultSpeed + 0.25);
        }

        private void OnFF40Click(object? sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(_defaultSpeed + 4.0);
        }

        private void OnSpeedResetClick(object? sender, RoutedEventArgs e)
        {
            SetPlaybackSpeed(_defaultSpeed);
        }

        private void SetPlaybackSpeed(double newSpeed)
        {
            _currentSpeed = Math.Clamp(newSpeed, 0.1, 5.0); // Clamp speed between 0.1x and 5.0x
            var speedStr = _currentSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                var result = MPVInterop.mpv_command(mpvHandle, new[] { "set", "speed", speedStr });
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to set playback speed: {MPVInterop.GetError(result)}");
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Failed to set playback speed.", this);
                }
                else
                {
                    //Debug.WriteLine($"Playback speed set to {_currentSpeed}x");
                    var toast = new ToastView();
                    toast.ShowToast("Success", $"Playback speed set to {_currentSpeed}x", this);
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error setting playback speed: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error setting playback speed.", this);
            }
        }

        private void SeekForward_Click(object? sender, RoutedEventArgs e)
        {
            SeekRelative(1); // Move forward by 1 second
        }


        private void SeekBackward_Click(object? sender, RoutedEventArgs e)
        {
            SeekRelative(-1); // Move backward by 1 second
        }

        private void SeekRelative(double seconds)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                var secondsStr = seconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                var args = new[] { "seek", secondsStr, "relative" }; // Use "relative" mode for seeking
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to seek {seconds} seconds: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine($"Seeked {seconds} seconds successfully.");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error seeking: {ex.Message}");
            }
        }

        // Subtitle Menu
        private void ToggleSubtitle_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                // Cycle the "sub-visibility" property to toggle subtitles
                var args = new[] { "cycle", "sub-visibility" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to toggle subtitles: {MPVInterop.GetError(result)}");
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Failed to toggle subtitles.", this);
                }
                else
                {
                    //Debug.WriteLine("Toggled subtitles successfully");
                    var toast = new ToastView();
                    toast.ShowToast("Success", "Subtitles Toggled", this);
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error toggling subtitles: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error toggling subtitles.", this);
            }
        }

        private async void LoadSubtitle_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Subtitle File",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Subtitle Files", Extensions = new List<string> { "srt", "ass", "sub" } }
                }
            };
            var result = await openFileDialog.ShowAsync((Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);
            if (result != null && result.Length > 0)
            {
                string subtitlePath = result[0];
                try
                {
                    // Use the "sub-add" command to load the subtitle file
                    var args = new[] { "sub-add", subtitlePath };
                    int commandResult = MPVInterop.mpv_command(mpvHandle, args);
                    if (commandResult < 0)
                    {
                        //Debug.WriteLine($"Failed to load subtitle: {MPVInterop.GetError(commandResult)}");
                        var toast = new ToastView();
                        toast.ShowToast("Warning", "Failed to load subtitle.", this);
                    }
                    else
                    {
                        //Debug.WriteLine($"Loaded subtitle file: {subtitlePath}");
                        var toast = new ToastView();
                        toast.ShowToast("Success", "Subtitles loaded.", this);
                    }
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error loading subtitle: {ex.Message}");
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Error loading subtitle.", this);
                }
            }
        }

        private void DecreaseSubFont_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                // Decrease the "sub-font-size" property
                var args = new[] { "add", "sub-font-size", "-2" };
                int result = MPVInterop.mpv_command(mpvHandle, args);

                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to decrease subtitle font size: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine("Decreased subtitle font size");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error decreasing subtitle font size: {ex.Message}");
            }
        }

        private void IncreaseSubFont_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                // Increase the "sub-font-size" property
                var args = new[] { "add", "sub-font-size", "2" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to increase subtitle font size: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine("Increased subtitle font size");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error increasing subtitle font size: {ex.Message}");
            }
        }

        // Screenshot Menu
        private void ScreenShotDesktop_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                // Set the screenshot directory to the desktop
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                int dirResult = MPVInterop.mpv_set_option_string(mpvHandle, "screenshot-directory", desktopPath);
                if (dirResult < 0)
                {
                    //Debug.WriteLine($"Failed to set screenshot directory: {MPVInterop.GetError(dirResult)}");
                    return;
                }
                // Set the screenshot template for naming files
                int templateResult = MPVInterop.mpv_set_option_string(mpvHandle, "screenshot-template", "%F-%04n");
                if (templateResult < 0)
                {
                    //Debug.WriteLine($"Failed to set screenshot template: {MPVInterop.GetError(templateResult)}");
                    return;
                }
                // Use MPV's built-in "screenshot" command
                var args = new[] { "screenshot" };
                int commandResult = MPVInterop.mpv_command(mpvHandle, args);
                if (commandResult < 0)
                {
                    //Debug.WriteLine($"Failed to take a screenshot: {MPVInterop.GetError(commandResult)}");
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Failed to take screenshot.", this);
                }
                else
                {
                    //Debug.WriteLine($"Screenshot saved to desktop successfully.");
                    var toast = new ToastView();
                    toast.ShowToast("Success", "Screenshot saved.", this);
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error taking screenshot: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error taking screenshot.", this);
            }
        }

        // Playlist
        private void PlaylistButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_playlistView != null)
            {
                _playlistView.IsVisible = !_playlistView.IsVisible;
                // Use a small delay to let the layout update complete
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    // Give the UI a moment to update layout
                    await Task.Delay(50);
                    UpdateChildWindowBounds();
                    OnVideoModeChanged();
                });
                // Update the button icon based on visibility
                if (_playlistToggleButton?.Content is AvaloniaPath playlistPath)
                {
                    var iconKey = _playlistView.IsVisible ?
                        "slide_hide_regular" : "slide_text_regular";

                    if (App.Current?.Resources.TryGetResource(iconKey,
                        ThemeVariant.Default, out object? resource) == true &&
                        resource is StreamGeometry geometry)
                    {
                        playlistPath.Data = geometry;
                    }
                }
            }
        }

        private void ForwardSpotButton_Click(object? sender, RoutedEventArgs e)
        {
            _playlistView?.PlayNext();
        }

        private void BackwardSpotButton_Click(object? sender, RoutedEventArgs e)
        {
            _playlistView?.PlayPrevious();
        }


        // Buttons
        private async void ImageSeq_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select an Image",
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Images", Extensions = { "jpg", "tif", "tiff", "png" } }
                }
            };
            var filePaths = await openFileDialog.ShowAsync(this);
            if (filePaths is { Length: > 0 })
            {
                var selectedFile = filePaths[0];
                var frameRatePopup = new FrameRatePopup
                {
                    OnFrameRateSelected = (frameRate) => { PlayImageSequence(selectedFile, frameRate); },
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                await frameRatePopup.ShowDialog(this); // Set 'this' as Owner

            }
        }

        private async void PhotoFilter_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _isPhotoFilterActive = !_isPhotoFilterActive;
                // Update the icon
                if (_isPhotoFilterActive)
                {
                    PhotoFilterIcon.Data = Application.Current.FindResource("checkmark_circle_regular") as StreamGeometry;
                    await ApplyLut("709_sRGB.cube");
                    var toast = new ToastView();
                    toast.ShowToast("Success", "Color transformed applied.", this);
                }
                else
                {
                    PhotoFilterIcon.Data = Application.Current.FindResource("photo_filter_regular") as StreamGeometry;
                    await RemoveLut();
                    var toast = new ToastView();
                    toast.ShowToast("Success", "Color transform removed.", this);
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error toggling photo filter: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error toggling color transform.", this);
            }
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            var filePath = MPVInterop.GetStringProperty(mpvHandle, "path");
            if (string.IsNullOrEmpty(filePath))
            {
                //Debug.WriteLine("No file currently loaded.");
                return;
            }
            var popup = new VideoInfoPopup();
            popup.LoadMetadata(filePath);
            popup.ShowDialog(this);
        }

        private void PlayButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                var args = new[] { "cycle", "pause" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    Debug.WriteLine($"MPV Error: {MPVInterop.GetError(result)}");
                    EnsureCorrectWindowOrder();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayButton_Click: {ex.Message}");
            }
        }

        private async Task UpdatePlayPauseIcon()
        {
            var isPaused = GetPropertyAsInt("pause");
            if (isPaused.HasValue && playButton?.Content is AvaloniaPath playPausePath)
            {
                var iconKey = isPaused.Value == 1 ? "play_regular" : "pause_regular";
                object? resource = null;
                if (App.Current?.Resources.TryGetResource(iconKey, ThemeVariant.Default, out resource) == true &&
                    resource is StreamGeometry geometry)
                {
                    playPausePath.Data = geometry;
                }
            }
        }

        private void PrevFrameButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
        {
            if (mpvHandle != IntPtr.Zero)
            {
                var args = new[] { "frame-back-step" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Frame back failed with error: {MPVInterop.GetError(result)}");
                }
                // Ensure we're paused after frame step
                var pauseArgs = new[] { "set_property", "pause", "yes" };
                MPVInterop.mpv_command(mpvHandle, pauseArgs);
                _ = UpdatePlayPauseIcon();
            }
        }

        private void NextFrameButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
        {
            if (mpvHandle != IntPtr.Zero)
            {
                var args = new[] { "frame-step" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Frame step failed with error: {MPVInterop.GetError(result)}");
                }
                // Ensure we're paused after frame step
                var pauseArgs = new[] { "set_property", "pause", "yes" };
                MPVInterop.mpv_command(mpvHandle, pauseArgs);
                _ = UpdatePlayPauseIcon();
            }
        }

        private void ToStartButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
        {
            if (mpvHandle != IntPtr.Zero)
            {
                var args = new[] { "seek", "0", "absolute" }; // Seek to the beginning
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Seek to start failed with error: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine("Successfully navigated to the beginning of the video.");
                    _ = UpdatePlayPauseIcon();
                }
            }
        }

        private void ToEndButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
        {
            if (mpvHandle != IntPtr.Zero)
            {
                // Get the video duration and seek to it
                var duration = MPVInterop.GetDoubleProperty(mpvHandle, "duration");
                if (duration.HasValue)
                {
                    var args = new[] { "seek", duration.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "absolute" };
                    int result = MPVInterop.mpv_command(mpvHandle, args);

                    if (result < 0)
                    {
                        //Debug.WriteLine($"Seek to end failed with error: {MPVInterop.GetError(result)}");
                    }
                    else
                    {
                        //Debug.WriteLine("Successfully navigated to the end of the video.");
                        _ = UpdatePlayPauseIcon();
                    }
                }
                else
                {
                    //Debug.WriteLine("Failed to retrieve video duration.");
                }
            }
        }

        private async Task HandleScreenshot()
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    return;
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "umpv");
                Directory.CreateDirectory(tempDir);
                var screenshotPath = Path.Combine(tempDir, $"screenshot_{DateTime.Now:yyyyMMddHHmmss}.png");

                var result = MPVInterop.mpv_command(mpvHandle, new[]
                {
            "screenshot-to-file",
            screenshotPath
        });

                if (result < 0)
                {
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Screenshot command failed.", this);
                    return;
                }

                // Wait for file with timeout
                var waitStart = DateTime.Now;
                while (!File.Exists(screenshotPath))
                {
                    if ((DateTime.Now - waitStart).TotalSeconds > 5)
                    {
                        throw new Exception("Timed out waiting for screenshot file");
                    }
                    await Task.Delay(100);
                }

                await Task.Delay(200); // Give file time to be written

                // Load the image and copy to clipboard
                using (var image = DrawingImage.FromFile(screenshotPath))
                {
                    NativeClipboard.SetImage(image);
                }

                var toastSuccess = new ToastView();
                toastSuccess.ShowToast("Success", "Screenshot copied to clipboard.", this);

                // Clean up the temp file
                try
                {
                    File.Delete(screenshotPath);
                }
                catch (Exception ex)
                {
                    // Log cleanup error if necessary
                }
            }
            catch (Exception ex)
            {
                var toastError = new ToastView();
                toastError.ShowToast("Warning", "Screenshot failed.", this);
            }
        }


        // Old Screenshot method with Powershell
        //private async Task HandleScreenshot()
        //{
        //    try
        //    {
        //        if (mpvHandle == IntPtr.Zero)
        //        {
        //            //Debug.WriteLine("MPV handle is not initialized");
        //            return;
        //        }
        //        var tempDir = Path.Combine(Path.GetTempPath(), "umpv");
        //        Directory.CreateDirectory(tempDir);
        //        var screenshotPath = Path.Combine(tempDir, $"screenshot_{DateTime.Now:yyyyMMddHHmmss}.png");
        //        //Debug.WriteLine($"Attempting to take screenshot to: {screenshotPath}");
        //        var result = MPVInterop.mpv_command(mpvHandle, new[]
        //        {
        //            "screenshot-to-file",
        //            screenshotPath
        //        });
        //        if (result < 0)
        //        {
        //            //Debug.WriteLine($"Screenshot command failed: {MPVInterop.GetError(result)}");
        //            var toast = new ToastView();
        //            toast.ShowToast("Warning", "Screenshot command failed.", this);
        //            return;
        //        }
        //        // Wait for file with timeout
        //        var waitStart = DateTime.Now;
        //        while (!File.Exists(screenshotPath))
        //        {
        //            if ((DateTime.Now - waitStart).TotalSeconds > 5)
        //            {
        //                throw new Exception("Timed out waiting for screenshot file");
        //            }
        //            await Task.Delay(100);
        //        }
        //        await Task.Delay(200); // Give file time to be written
        //        var command = $"-command \"Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; $bmp = New-Object System.Drawing.Bitmap('{screenshotPath}'); [System.Windows.Forms.Clipboard]::SetImage($bmp)\"";
        //        //Debug.WriteLine($"PowerShell command: {command}");
        //        var startInfo = new ProcessStartInfo
        //        {
        //            FileName = "powershell",
        //            Arguments = command,
        //            UseShellExecute = false,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true,
        //            CreateNoWindow = true
        //        };
        //        using (var process = Process.Start(startInfo))
        //        {
        //            if (process != null)
        //            {
        //                var output = await process.StandardOutput.ReadToEndAsync();
        //                var error = await process.StandardError.ReadToEndAsync();
        //                await process.WaitForExitAsync();

        //                //Debug.WriteLine($"PowerShell output: {output}");
        //                //Debug.WriteLine($"PowerShell error: {error}");
        //                //Debug.WriteLine($"PowerShell exit code: {process.ExitCode}");

        //                var toast = new ToastView();
        //                toast.ShowToast("Success", "Screenshot saved to clipboard.", this);
        //            }
        //        }
        //        // Clean up the temp file
        //        try
        //        {
        //            File.Delete(screenshotPath);
        //            //Debug.WriteLine("Temporary screenshot file deleted");
        //        }
        //        catch (Exception ex)
        //        {
        //            //Debug.WriteLine($"Failed to delete temporary file: {ex.Message}");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        //Debug.WriteLine($"Screenshot failed: {ex.Message}");
        //        //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        //        var toast = new ToastView();
        //        toast.ShowToast("Warning", "Screenshot failed.", this);
        //    }
        //}

        private void CameraButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _ = HandleScreenshot();
        }

        private void SafetyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized");
                return;
            }
            try
            {
                // Toggle the filter state
                isFilterActive = !isFilterActive;
                string filterCommand;
                if (isFilterActive)
                {
                    // Apply the filter
                    filterCommand = "lavfi=[drawbox=x=(iw-((iw*0.9)))/2:y=(ih-((ih*0.9)))/2:w=iw*.9:h=ih*.9:color=Thistle@1,drawbox=x=(iw-((iw*0.93)))/2:y=(ih-((ih*0.93)))/2:w=iw*.93:h=ih*.93:color=Orchid@1]";
                }
                else
                {
                    // Remove the filter
                    filterCommand = "";
                }
                // Send the command to MPV
                var args = new[] { "vf", "set", filterCommand };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to toggle filter: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine(isFilterActive
                    //    ? "Filter enabled successfully."
                    //    : "Filter removed successfully.");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error toggling filter: {ex.Message}");
            }
        }

        private void FullScreenButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero || childWindowHandle == IntPtr.Zero)
                {
                    // Debug.WriteLine("MPV handle or child window handle is not initialized");
                    return;
                }
                // Toggle full-screen state
                isFullScreen = !isFullScreen;
                if (isFullScreen)
                {
                    // Get the current screen dimensions
                    var screen = Screens.Primary;
                    var screenBounds = screen.Bounds;
                    // Expand the main window to full screen
                    this.WindowState = WindowState.FullScreen;
                    // Expand the MPV child window to cover the main window
                    MoveWindow(childWindowHandle,
                        0, // X position
                        0, // Y position
                        screenBounds.Width,
                        screenBounds.Height,
                        true);
                    // Show specific UI elements
                    ToggleUIElementsVisibility(false);
                    OnVideoModeChanged();
                }
                else
                {
                    // Restore the main window to its original size
                    this.WindowState = WindowState.Normal;
                    // Restore the MPV child window to its original bounds
                    UpdateChildWindowBounds();
                    // Hide specific UI elements
                    ToggleUIElementsVisibility(true);
                    OnVideoModeChanged();
                }
                // Update the full-screen icon
                UpdateFullScreenIcon(isFullScreen);
                // Debug.WriteLine(isFullScreen
                //     ? "Application entered full-screen mode."
                //     : "Application exited full-screen mode.");
            }
            catch (Exception ex)
            {
                // Debug.WriteLine($"Error in FullScreenButton_Click: {ex.Message}");
            }
        }

        private void ToggleUIElementsVisibility(bool isVisible)
        {
            try
            {
                // Example: Update visibility of UI elements
                var TopTitlebar = this.FindControl<Control>("TopTitlebar");
                var BottomToolbar = this.FindControl<Control>("BottomToolbar");
                var Topmenu = this.FindControl<Control>("Topmenu");
                if (TopTitlebar != null)
                {
                    TopTitlebar.IsVisible = isVisible;
                }
                if (BottomToolbar != null)
                {
                    BottomToolbar.IsVisible = isVisible;
                }
                if (Topmenu != null)
                {
                    Topmenu.IsVisible = isVisible;
                }
            }
            catch (Exception ex)
            {
                // Debug.WriteLine($"Error toggling UI element visibility: {ex.Message}");
            }
        }


        private void UpdateFullScreenIcon(bool isFullScreen)
        {
            if (FullScreenButton == null)
            {
                //Debug.WriteLine("FullScreenButton is not initialized.");
                return;
            }
            if (FullScreenButton.Content is not PathIcon pathIcon)
            {
                pathIcon = new PathIcon
                {
                    Width = 14,
                    Height = 14,
                };
                FullScreenButton.Content = pathIcon;
            }
            var iconKey = isFullScreen ? "full_screen_zoom_regular" : "arrow_expand_regular";
            var geometry = Application.Current.FindResource(iconKey) as StreamGeometry;
            if (geometry != null)
            {
                pathIcon.Data = geometry;
            }
            else
            {
                //Debug.WriteLine($"Icon resource '{iconKey}' not found.");
            }
        }

        private void LoopingButton_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                //Debug.WriteLine("MPV handle is not initialized.");
                return;
            }
            try
            {
                // Toggle loop state
                isLooping = !isLooping;
                var loopState = isLooping ? "yes" : "no";
                // Set the loop-file property
                var args = new[] { "set", "loop-file", loopState };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result >= 0)
                {
                    //Debug.WriteLine($"Looping toggled: {loopState}");
                    UpdateLoopingIcon(); // Update the button icon
                }
                else
                {
                    //Debug.WriteLine($"Failed to toggle looping. MPV Error: {MPVInterop.GetError(result)}");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error toggling looping: {ex.Message}");
            }
        }

        private void UpdateLoopingIcon()
        {
            //Debug.WriteLine("UpdateLoopingIcon method called.");
            if (LoopingPath != null)
            {
                var iconKey = isLooping ? "arrow_repeat_all_regular" : "arrow_repeat_all_off_regular";
                var geometry = Application.Current.FindResource(iconKey) as StreamGeometry;
                if (geometry != null)
                {
                    LoopingPath.Data = geometry;
                    //Debug.WriteLine($"Updated looping icon to: {iconKey}");
                }
                else
                {
                    //Debug.WriteLine($"Icon resource '{iconKey}' not found.");
                }
            }
            else
            {
                //Debug.WriteLine("LoopingPath is null.");
            }
        }

        // Timecode
        private async Task UpdateTimecodeAsync()
        {
            try
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                while (mpvHandle != IntPtr.Zero && !cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var currentTime = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
                        var currentFrame = MPVInterop.GetDoubleProperty(mpvHandle, "estimated-frame-number");
                        var fps = MPVInterop.GetDoubleProperty(mpvHandle, "container-fps"); // Fallback FPS
                        if (currentTime.HasValue && currentTime >= 0)
                        {
                            var formattedTimecode = FormatTimecode(currentTime.Value, currentFrame, fps);
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                try
                                {
                                    CurrentTimeTextBlock.Text = formattedTimecode;
                                    if (currentFrame.HasValue)
                                    {
                                        CurrentFrameTextBlock.Text = currentFrame.Value.ToString("F0"); // Frame count
                                    }
                                    else
                                    {
                                        CurrentFrameTextBlock.Text = "N/A";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    //Debug.WriteLine($"UI update error: {ex.Message}");
                                }
                            });
                        }

                        // Adjust update frequency based on playback state
                        bool isPaused = GetPropertyAsInt("pause") == 1;
                        await Task.Delay(isPaused ? 250 : 100);
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Update loop error: {ex.Message}");
                        await Task.Delay(100); // Continue loop even after error
                    }
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Fatal error in UpdateTimecodeAsync: {ex.Message}");
            }
        }

        private string FormatTimecode(double time, double? frameNumber = null, double? fps = null)
        {
            int hours = (int)(time / 3600);
            int minutes = (int)((time % 3600) / 60);
            int seconds = (int)(time % 60);
            // If frameNumber is provided, calculate frames; otherwise, fallback to fps calculation
            int frames = frameNumber.HasValue
                ? (int)(frameNumber % (fps ?? 24.0)) // Frames derived from frame number
                : (int)((time * (fps ?? 24.0)) % (fps ?? 24.0)); // Fallback to fps-based frames
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
        }

        // Volume Slider
        private void VolumeSlider_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Value" && !isVolumeSliderDragging && mpvHandle != IntPtr.Zero)
            {
                SetVolume(volumeSlider.Value);
            }
        }

        private void VolumeSlider_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            isVolumeSliderDragging = true;
        }

        private void VolumeSlider_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (!isVolumeSliderDragging) return;
            isVolumeSliderDragging = false;
            SetVolume(volumeSlider.Value);
        }

        private double _lastVolume = 100; // Add this field at the top with other fields
        private bool _isMuted = false;

        private void VolumeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                _isMuted = !_isMuted;
                if (_isMuted)
                {
                    _lastVolume = volumeSlider.Value; // Store current volume
                    volumeSlider.Value = 0;
                    SetVolume(0);
                }
                else
                {
                    volumeSlider.Value = _lastVolume; // Restore previous volume
                    SetVolume(_lastVolume);
                }
                UpdateVolumeIcon(_isMuted);
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in VolumeButton_Click: {ex.Message}");
            }
        }

        private void UpdateVolumeIcon(bool isMuted)
        {
            if (volumeButton.Content is not PathIcon pathIcon)
            {
                pathIcon = new PathIcon
                {
                    Width = 14,
                    Height = 14,
                };
                volumeButton.Content = pathIcon;
            }

            var iconKey = isMuted ? "speaker_off_regular" : "speaker_1_regular";
            var geometry = Application.Current.FindResource(iconKey) as StreamGeometry;
            if (geometry != null)
            {
                pathIcon.Data = geometry;
            }
            else
            {
                //Debug.WriteLine($"Icon resource '{iconKey}' not found.");
            }
        }

        private void SetVolume(double volume)
        {
            try
            {
                var volumeStr = volume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                var args = new[] { "set", "volume", volumeStr };
                var result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Volume change failed with error: {MPVInterop.GetError(result)}");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error setting volume: {ex.Message}");
            }
        }

        // Keyboard commands
        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.W:
                    PlayButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.O:
                    OpenMenuItem_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.I:
                    InfoButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Q: // "Q" key
                    PrevFrameButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.E: // "E" key
                    NextFrameButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.S: // "S" key
                    CameraButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.A: // "A" key
                    SeekBackward_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.D: // "D" key
                    SeekForward_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Z: // "Z" key
                    ToStartButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.C: // "C" key
                    ToEndButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.B: // "B" key
                    PlaylistButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.F: // "F" key
                    FullScreenButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.R: // "R" key
                    LoopingButton_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Escape: // Escape key
                    if (isFullScreen) // Exit full-screen mode
                    {
                        FullScreenButton_Click(null, null);
                        e.Handled = true;
                    }
                    break;
            }
        }

        // Playback and the Slider

        private void InitializePlaybackControl()
        {
            if (mpvHandle == IntPtr.Zero) return;
            //Debug.WriteLine("Initializing playback control...");
            var result = MPVInterop.mpv_request_event(mpvHandle, MPVInterop.mpv_event_id.MPV_EVENT_END_FILE, 1);
            if (result < 0)
            {
                //Debug.WriteLine($"Failed to register for end file events: {MPVInterop.GetError(result)}");
            }
            else
            {
                //Debug.WriteLine("Successfully registered for end file events");
            }
            // Observe time-pos property for playback position
            result = MPVInterop.mpv_observe_property(mpvHandle, 1, "time-pos", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
            //Debug.WriteLine($"Observing time-pos: {result}");
            // Observe duration property
            result = MPVInterop.mpv_observe_property(mpvHandle, 2, "duration", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
            //Debug.WriteLine($"Observing duration: {result}");
            // Observe pause state
            result = MPVInterop.mpv_observe_property(mpvHandle, 3, "pause", MPVInterop.mpv_format.MPV_FORMAT_FLAG);
            //Debug.WriteLine($"Observing pause state: {result}");
            StartEventLoop();
        }

        private void StartEventLoop()
        {
            _eventLoopCancellation = new CancellationTokenSource();
            var token = _eventLoopCancellation.Token;

            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && mpvHandle != IntPtr.Zero)
                    {
                        var evPtr = MPVInterop.mpv_wait_event(mpvHandle, 0.1);
                        if (evPtr != IntPtr.Zero)
                        {
                            var evt = Marshal.PtrToStructure<MPVInterop.mpv_event>(evPtr);
                            //Debug.WriteLine($"MPV Event received: {evt.event_id}");  // Add this line
                            await HandleMpvEvent(evt);
                        }
                        await Task.Delay(1, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    //Debug.WriteLine("Event loop cancelled");
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error in event loop: {ex.Message}\n{ex.StackTrace}");
                }
            }, token);
        }

        private async Task HandleMpvEvent(MPVInterop.mpv_event evt)
        {
            try
            {
                switch (evt.event_id)
                {
                    case MPVInterop.mpv_event_id.MPV_EVENT_START_FILE:
                        _isLoadingVideo = true;
                        _currentVideoInitialized = false;
                        break;
                    case MPVInterop.mpv_event_id.MPV_EVENT_PROPERTY_CHANGE:
                        if (evt.data != IntPtr.Zero)
                        {
                            var prop = Marshal.PtrToStructure<MPVInterop.mpv_event_property>(evt.data);
                            await HandlePropertyChange(prop, evt.reply_userdata);
                        }
                        break;
                    case MPVInterop.mpv_event_id.MPV_EVENT_FILE_LOADED:
                        _currentVideoInitialized = true;
                        await UpdatePlaybackControls();
                        await GetAndValidateDuration();
                        break;
                    case MPVInterop.mpv_event_id.MPV_EVENT_END_FILE:
                        // Cancel any existing timecode updates
                        if (_eventLoopCancellation != null)
                        {
                            await _eventLoopCancellation.CancelAsync();
                            _eventLoopCancellation = new CancellationTokenSource();
                        }
                        if (evt.data != IntPtr.Zero)
                        {
                            var endFile = Marshal.PtrToStructure<MPVInterop.mpv_end_file_event>(evt.data);
                            //Debug.WriteLine($"End file event received with reason: {endFile.reason}");
                            // Only process END_FILE if we're not loading and the current video is initialized
                            if (!_isLoadingVideo && _currentVideoInitialized &&
                                endFile.reason == MPVInterop.mpv_end_file_reason.MPV_END_FILE_REASON_EOF)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    _playlistView?.PlayNext();
                                });
                            }
                        }
                        _isLoadingVideo = false;
                        break;
                    case MPVInterop.mpv_event_id.MPV_EVENT_PLAYBACK_RESTART:
                        _isLoadingVideo = false;
                        await UpdatePlaybackControls();
                        break;
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error handling MPV event: {ex.Message}");
            }
        }

        private async Task GetAndValidateDuration()
        {
            //Debug.WriteLine("Starting duration validation...");
            var durationPtr = MPVInterop.mpv_get_property_string(mpvHandle, "duration");
            if (durationPtr != IntPtr.Zero)
            {
                try
                {
                    string durationStr = Marshal.PtrToStringAnsi(durationPtr);
                    //Debug.WriteLine($"Raw duration string: {durationStr}");
                    if (double.TryParse(durationStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                    {
                        //Debug.WriteLine($"Parsed duration value: {duration}");
                        if (duration > 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                //Debug.WriteLine($"Current slider maximum: {playbackSlider.Maximum}");
                                //Debug.WriteLine($"Current _lastDuration: {_lastDuration}");
                                _lastDuration = duration;
                                playbackSlider.Maximum = duration;
                                //Debug.WriteLine($"Updated slider maximum to: {playbackSlider.Maximum}");
                                //Debug.WriteLine($"Updated _lastDuration to: {_lastDuration}");
                            });
                        }
                        else
                        {
                            //Debug.WriteLine("Duration was <= 0");
                        }
                    }
                    else
                    {
                        //Debug.WriteLine("Failed to parse duration string");
                    }
                }
                finally
                {
                    MPVInterop.mpv_free(durationPtr);
                }
            }
            else
            {
                //Debug.WriteLine("Duration property returned null");
            }
        }

        private async Task HandlePropertyChange(MPVInterop.mpv_event_property prop, ulong userData)
        {
            try
            {
                string? propName = Marshal.PtrToStringUTF8(prop.name);
                if (prop.data == IntPtr.Zero) return;
                switch (prop.format)
                {
                    case MPVInterop.mpv_format.MPV_FORMAT_DOUBLE:
                        var value = Marshal.PtrToStructure<double>(prop.data);
                        await HandleDoubleProperty(propName, value, userData);
                        break;
                    case MPVInterop.mpv_format.MPV_FORMAT_FLAG:
                        var flag = Marshal.PtrToStructure<int>(prop.data);
                        await HandleFlagProperty(propName, flag != 0, userData);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in HandlePropertyChange: {ex.Message}");
            }
        }

        private async Task HandleFlagProperty(string? propertyName, bool value, ulong userData)
        {
            if (propertyName == "pause" && userData == PAUSE_PROPERTY_ID)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (playButton?.Content is AvaloniaPath playPausePath)
                    {
                        var iconKey = value ? "play_regular" : "pause_regular";
                        object? resource = null;
                        if (App.Current?.Resources.TryGetResource(iconKey, ThemeVariant.Default, out resource) == true &&
                            resource is StreamGeometry geometry)
                        {
                            playPausePath.Data = geometry;
                        }
                    }
                    // Update playlist view play state
                    _playlistView?.UpdateCurrentItemPlayState(!value);  // !value because 'value' is the pause state
                });
            }
        }

        private async Task HandleDoubleProperty(string? propertyName, double value, ulong userData)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    switch (propertyName)
                    {
                        case "time-pos":
                            ////Debug.WriteLine($"Time position update - Value: {value}, Last: {_lastPosition}, Duration: {_lastDuration}, Slider Max: {playbackSlider.Maximum}");
                            if (!isSliderDragging && Math.Abs(value - _lastPosition) > 0.01)
                            {
                                _lastPosition = value;
                                isUpdatingSlider = true;
                                try
                                {
                                    playbackSlider.Value = value;
                                }
                                finally
                                {
                                    isUpdatingSlider = false;
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // //Debug.WriteLine($"Error handling double property: {ex.Message}");
                }
            });
        }

        private async Task UpdatePlaybackControls()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var isPaused = GetPropertyAsInt("pause") == 1;
            });
        }

        private int? GetPropertyAsInt(string propertyName)
        {
            try
            {
                IntPtr result = MPVInterop.mpv_get_property_string(mpvHandle, propertyName);
                if (result != IntPtr.Zero)
                {
                    string value = Marshal.PtrToStringAnsi(result) ?? "0";
                    MPVInterop.mpv_free(result);  // Don't forget to free the memory
                    return int.TryParse(value, out var parsedValue) ? parsedValue : (int?)null;
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error getting property {propertyName}: {ex.Message}");
            }

            return null;
        }

        // Modified slider event handlers
        private void PlaybackSlider_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            isSliderDragging = true;
        }

        private void PlaybackSlider_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (!isSliderDragging) return;
            isSliderDragging = false;
            SeekToPosition(playbackSlider.Value);
        }

        private void PlaybackSlider_ValueChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == "Value" && !isUpdatingSlider && !isSliderDragging && mpvHandle != IntPtr.Zero)
            {
                SeekToPosition(playbackSlider.Value);
            }
        }

        private void SeekToPosition(double position)
        {
            try
            {
                var positionStr = position.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                var args = new[] { "seek", positionStr, "absolute" };
                var result = MPVInterop.mpv_command_async(mpvHandle, 0, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Seek failed with error: {MPVInterop.GetError(result)}");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error during seek: {ex.Message}");
            }
        }

        // Video Loading
        public async Task LoadVideo(string videoPath, bool isPlaylistItem = false)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                _isLoadingVideo = true;
                _currentVideoInitialized = false;
                //Debug.WriteLine($"Starting load process for: {videoPath}");
                //Debug.WriteLine($"Playback mode: {(isPlaylistItem ? "Playlist" : "Single File")}");
                // Clear playlist states if this is a single file
                if (!isPlaylistItem)
                {
                    _playlistView?.ClearAllPlayingStates();
                }
                // Set appropriate keep-open state based on mode
                if (_isPlaylistMode != isPlaylistItem)
                {
                    _isPlaylistMode = isPlaylistItem;
                    var keepOpenValue = isPlaylistItem ? "no" : "always";
                    MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", keepOpenValue);
                    //Debug.WriteLine($"Switched keep-open to: {keepOpenValue}");
                }
                // Reset state
                _lastDuration = 0;
                _lastPosition = 0;
                var fileName = Path.GetFileName(videoPath);
                var newTitle = fileName == "blank.mp4" ? "umpv" : $"umpv - {fileName}";
                // Update UI elements
                await Dispatcher.UIThread.InvokeAsync(() => {
                    playbackSlider.Value = 0;
                    playbackSlider.Maximum = 0;
                    CurrentTimeTextBlock.Text = "00:00:00:00";
                    CurrentFrameTextBlock.Text = "0";
                    var viewModel = DataContext as MainWindowViewModel;
                    if (viewModel != null)
                    {
                        viewModel.TopTitle = newTitle;
                    }
                    // Update playlist state
                    _playlistView?.SetCurrentlyPlaying(videoPath);
                });
                // Cancel any existing timecode updates
                if (_eventLoopCancellation != null)
                {
                    await _eventLoopCancellation.CancelAsync();
                    _eventLoopCancellation = new CancellationTokenSource();
                }
                // Load the file
                //Debug.WriteLine("Sending loadfile command to MPV");
                if (!string.IsNullOrEmpty(_currentLutPath))
                {
                    var lutResult = MPVInterop.mpv_set_option_string(mpvHandle, "lut", _currentLutPath);
                    if (lutResult < 0)
                    {
                        //Debug.WriteLine($"Failed to set LUT: {MPVInterop.GetError(lutResult)}");
                    }
                }
                else
                {
                    MPVInterop.mpv_set_option_string(mpvHandle, "lut", "");
                }
                var args = new[] { "loadfile", videoPath, "replace" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to load file: error code {result}");
                    return;
                }
                bool success = await WaitForDurationAndInitialize();
                if (success)
                {
                    Debug.WriteLine("Duration initialized successfully, starting timecode updates");
                    _ = UpdateTimecodeAsync();
                    var playArgs = new[] { "set", "pause", "no" };
                    int playResult = MPVInterop.mpv_command(mpvHandle, playArgs);
                    if (playResult < 0)
                    {
                        Debug.WriteLine($"Failed to start playback: {MPVInterop.GetError(playResult)}");
                    }
                    else
                    {
                        Debug.WriteLine("Successfully started playback of new video");
                        _ = UpdatePlayPauseIcon();
                        _ = UpdatePlayState();
                        EnsureCorrectWindowOrder();  // Add this line
                    }
                }
                else
                {
                    //Debug.WriteLine("Failed to initialize duration");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in LoadVideo: {ex.Message}");
                //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task<bool> WaitForDurationAndInitialize()
        {
            try
            {
                //Debug.WriteLine("Starting enhanced duration initialization...");
                const int MAX_WAIT_TIME_MS = 10000; // 10 seconds timeout
                const int POLL_INTERVAL_MS = 200; // Poll every 200ms
                var timeoutCts = new CancellationTokenSource(MAX_WAIT_TIME_MS);
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    var duration = MPVInterop.GetDoubleProperty(mpvHandle, "duration");
                    if (duration.HasValue && duration > 0)
                    {
                        //Debug.WriteLine($"Valid duration found: {duration}");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            playbackSlider.Maximum = duration.Value;
                            _lastDuration = duration.Value;
                        });
                        return true;
                    }
                    await Task.Delay(POLL_INTERVAL_MS, timeoutCts.Token);
                }
                //Debug.WriteLine("Timeout waiting for duration initialization.");
                return false;
            }
            catch (OperationCanceledException)
            {
                //Debug.WriteLine("Duration initialization canceled due to timeout.");
                return false;
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in WaitForDurationAndInitialize: {ex.Message}");
                return false;
            }
        }

        private async Task WaitForFileLoadedAndSeek()
        {
            try
            {
                const int maxRetries = 50;
                const int retryDelayMs = 100;
                int retryCount = 0;
                while (retryCount < maxRetries)
                {
                    var evPtr = MPVInterop.mpv_wait_event(mpvHandle, 0.1); // Wait for 100ms
                    if (evPtr != IntPtr.Zero)
                    {
                        var evt = Marshal.PtrToStructure<MPVInterop.mpv_event>(evPtr);

                        if (evt.event_id == MPVInterop.mpv_event_id.MPV_EVENT_FILE_LOADED)
                        {
                            //Debug.WriteLine("MPV_EVENT_FILE_LOADED received");
                            break;
                        }
                    }
                    // Check if duration is available
                    var duration = MPVInterop.GetDoubleProperty(mpvHandle, "duration");
                    if (duration.HasValue && duration > 0)
                    {
                        //Debug.WriteLine($"Duration available: {duration}");
                        break;
                    }
                    retryCount++;
                    await Task.Delay(retryDelayMs);
                }

                if (retryCount == maxRetries)
                {
                    //Debug.WriteLine("Timeout waiting for file to load.");
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in WaitForFileLoadedAndSeek: {ex.Message}");
            }
        }

        private void LoadBlankVideo()
        {
            string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string videoPath = Path.Combine(assetsPath, "blank.mp4");
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                if (childWindowHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("Child window handle is not initialized");
                    return;
                }
                // Set the child window handle (wid) to embed MPV rendering in the UI
                SetMpvOption("wid", childWindowHandle.ToString());
                SetMpvOption("volume", "100");
                //Debug.WriteLine($"Attempting to load video: {videoPath}");
                _isPlaylistMode = false;
                MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", "always");
                var args = new[] { "loadfile", videoPath, "replace" };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to load file: error code {result}");
                }
                else
                {
                    //Debug.WriteLine("Video loaded successfully");
                    playbackSlider.Value = 0;
                    InitializePlaybackControl();
                    _ = UpdatePlayPauseIcon();

                    // Wait for video to load and set the timecode
                    Task.Run(async () =>
                    {
                        await WaitForFileLoadedAndSeek();
                        _ = UpdateTimecodeAsync();
                    });
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in LoadBlankVideo: {ex.Message}");
                //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeMPV()
        {
            try
            {
                //Debug.WriteLine("Starting MPV initialization...");
                mpvHandle = MPVInterop.mpv_create();
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("Failed to create MPV instance");
                    throw new Exception("Failed to create MPV instance");
                }
                //Debug.WriteLine($"MPV instance created: {mpvHandle}");
                // Set MPV options before initialization
                SetMpvOption("terminal", "yes");
                SetMpvOption("msg-level", "all=v");
                SetMpvOption("background", "0.0,0.0,0.0,1.0");
                SetMpvOption("vid-end-pause", "yes");
                SetMpvOption("demuxer-readahead-secs", "2");
                SetMpvOption("screenshot-high-bit-depth", "yes");
                SetMpvOption("screenshot-jpeg-quality", "75");
                SetMpvOption("gpu-clear-color", "0/0/0/255");
                SetMpvOption("alpha", "no");
                SetMpvOption("force-window", "no");
                SetMpvOption("keep-open", "always");
                SetMpvOption("idle", "yes");
                SetMpvOption("pause", "no");
                SetMpvOption("vo", "gpu-next");
                SetMpvOption("video-unscaled", "no");
                SetMpvOption("keep-open-pause", "yes");
                SetMpvOption("hr-seek", "yes");
                SetMpvOption("input-default-bindings", "no");
                SetMpvOption("cursor-autohide", "no");
                SetMpvOption("keepaspect", "yes");
                SetMpvOption("volume", "100");
                //Debug.WriteLine("MPV options set successfully");
                // Initialize MPV
                int result = MPVInterop.mpv_initialize(mpvHandle);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to initialize MPV with error code: {result}");
                    throw new Exception($"Failed to initialize MPV: {result}");
                }
                // Register for end-file events
                result = MPVInterop.mpv_request_event(mpvHandle, MPVInterop.mpv_event_id.MPV_EVENT_END_FILE, 1);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to register for end file events: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine("Successfully registered for end file events");
                }
                // Register for property changes
                result = MPVInterop.mpv_observe_property(mpvHandle, 1, "time-pos", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
                result = MPVInterop.mpv_observe_property(mpvHandle, 2, "duration", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
                result = MPVInterop.mpv_observe_property(mpvHandle, 3, "pause", MPVInterop.mpv_format.MPV_FORMAT_FLAG);
                // Start event monitoring
                StartEventLoop();
                // Set up bounds change observation
                videoContainer.AttachedToVisualTree += async (s, e) =>
                {
                    //Debug.WriteLine("Video container attached to visual tree");
                    await Task.Delay(100);
                    try
                    {
                        var parentHandle = GetParentWindowHandle();
                        if (parentHandle != IntPtr.Zero)
                        {
                            InitializeBackgroundWindow();

                            CreateChildWindow(parentHandle);
                            if (childWindowHandle == IntPtr.Zero)
                            {
                                throw new Exception("Child window creation failed");
                            }
                            SetMpvOption("wid", childWindowHandle.ToString());
                            UpdateChildWindowBounds();
                            LoadBlankVideo();
                        }
                        else
                        {
                            //Debug.WriteLine("Failed to get parent window handle");
                        }
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error in window setup: {ex.Message}");
                        //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                };
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"MPV initialization failed: {ex.Message}");
                //Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void CreateChildWindow(IntPtr parentHandle)
        {
            // Create MPV child window
            childWindowHandle = CreateWindowEx(
                0,
                "STATIC",
                null,
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                0, 0, 0, 0,
                parentHandle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero
            );

            if (childWindowHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"CreateWindowEx failed with error code: {error}");
                throw new Exception($"Failed to create child window with error code: {error}");
            }
        }

        private void UpdateChildWindowBounds()
        {
            if (childWindowHandle != IntPtr.Zero)
            {
                if (videoContainer.GetVisualRoot() is Window window)
                {
                    var containerBounds = videoContainer.Bounds;
                    var containerPosition = videoContainer.TranslatePoint(new Avalonia.Point(0, 0), window);

                    if (containerPosition.HasValue)
                    {
                        var scaling = window.RenderScaling;
                        var x = (int)(containerPosition.Value.X * scaling);
                        var y = (int)(containerPosition.Value.Y * scaling);
                        var width = (int)(containerBounds.Width * scaling);
                        var height = (int)(containerBounds.Height * scaling);

                        MoveWindow(childWindowHandle, x, y, width, height, true);
                    }
                }
            }
        }

        private IntPtr GetParentWindowHandle()
        {
            if (videoContainer.GetVisualRoot() is Window window)
            {
                var nativeHandle = window.TryGetPlatformHandle();
                if (nativeHandle != null)
                {
                    return nativeHandle.Handle;
                }
            }
            return IntPtr.Zero;
        }

        // MPVinterop controls

        private void SetMpvOption(string name, string value)
        {
            int result = MPVInterop.mpv_set_option_string(mpvHandle, name, value);
            //Debug.WriteLine($"Setting MPV option {name}={value}, result: {result}");
            if (result < 0)
            {
                //Debug.WriteLine($"Warning: Failed to set MPV option {name}");
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);
    }
}
