using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UnionMpvPlayer.ViewModels;
using UnionMpvPlayer.Helpers;
using DrawingImage = System.Drawing.Image;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using System.Collections.ObjectModel;
using Avalonia.Collections;
using static UnionMpvPlayer.Helpers.EXRSequenceHandler;
using static UnionMpvPlayer.Views.EXRLayerSelectionDialog;
using ReactiveUI;

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
        public Panel videoContainer;
        public Image overlayImage;
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

        private ObservableCollection<KeyBindingItem> KeyBindings = new();

        private DispatcherTimer? _speedTimer;
        private int currentSpeedIndex = 0;
        private readonly double[] SeekSteps = {
            0.04167, // 1 frame (1x)
            0.08333, // 2x
            0.125,   // 3x
            0.16667, // 4x
            0.20833, // 5x
            0.25     // 6x
        };
        private string? originalPausedState;
        private bool isAdjustingSpeed = false;
        private DispatcherTimer? _sliderTimer;

        private DispatcherTimer? _progressiveTimer;
        private double _currentSpeedStep = 0.0; 
        private int _rampIndex = 0; 
        private readonly double[] SpeedRamp = { 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };
        private ProcessingPopup _processingPopup;
        private string currentActiveFilter = string.Empty;
        private CancellationTokenSource _processingCts;
        private bool _isProcessingSequence;
        private string _currentTargetTRC = "auto"; 
        private bool _isBaseColorSequence = false;
        private readonly EXRSequenceHandler _sequenceHandler;
        private ToastView? _currentToast;
        private ColumnDefinition? PlaylistColumn;
        private double _lastVolume = 100;
        private bool _isMuted = false;
        private NotesView? _notesView;
        private ColumnDefinition? NotesColumn;
        private Button? _notesToggleButton;
        public static MainWindow Current { get; private set; }
        public event EventHandler PlaybackStarted;
        private bool _mpvHidden;
        private string _currentOverlayColor = "Gold";
        private bool _burnTimecode = false;

        private readonly Dictionary<string, SafetyOverlay> _overlays = new()
        {
            ["Broadcast16x9"] = new SafetyOverlay
            {
                Name = "Broadcast16x9",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw-((iw*0.9)))/2:y=(ih-((ih*0.9)))/2:w=iw*0.9:h=ih*0.9:color={color}@1:t=2,
                    drawbox=x=(iw-((iw*0.93)))/2:y=(ih-((ih*0.93)))/2:w=iw*0.93:h=ih*0.93:color={color}@1:t=2
                ]"
            },

            ["MetaReel9x16"] = new SafetyOverlay
            {
                Name = "MetaReel9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw-((iw*0.88)))/2:y=ih*0.14:w=iw*0.88:h=4:color={color}@1:t=2,
                    drawbox=x=(iw-((iw*0.88)))/2:y=ih*0.14:w=4:h=ih*0.51:color={color}@1:t=2,
                    drawbox=x=(iw-((iw*0.88)))/2:y=ih*0.65:w=iw*0.73:h=4:color={color}@1:t=2,
                    drawbox=x=iw*0.79:y=ih*0.6:w=4:h=ih*0.05:color={color}@1:t=2,
                    drawbox=x=iw*0.94:y=ih*0.14:w=4:h=ih*0.46:color={color}@1:t=2,
                    drawbox=x=iw*0.79:y=ih*0.6:w=4:h=ih*0.05:color={color}@1:t=2,
                    drawbox=x=iw*0.79:y=ih*0.60:w=iw*0.15:h=4:color={color}@1:t=2
                ]"
            },

            ["MetaStory9x16"] = new SafetyOverlay
            {
                Name = "MetaStory9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw*0.0604):y=(ih*0.1302):w=iw*0.8796:h=ih*0.6700:color={color}@1:t=2
                ]"
            },

            ["Pinterest9x16"] = new SafetyOverlay
            {
                Name = "Pinterest9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw*0.0603):y=(ih*0.1406):w=iw*0.7589:h=ih*0.4478:color={color}@1:t=2
                ]"
            },

            ["Pinterest1x1PremiumSpotlight"] = new SafetyOverlay
            {
                Name = "Pinterest1x1PremiumSpotlight",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=0:y=0:w=iw:h=ih*0.0981:color={color}@1:t=2,
                    drawbox=x=iw*0.0111:y=ih*0.1278:w=iw*0.9778:h=ih*0.1389:color={color}@1:t=2,
                    drawbox=x=iw*0.8815:y=ih*0.3093:w=iw*0.0741:h=ih*0.0537:color={color}@1:t=2,
                    drawbox=x=iw*0.0750:y=ih*0.6324:w=iw*0.8500:h=ih*0.1972:color={color}@1:t=2,
                    drawbox=x=iw*0.3824:y=ih*0.8296:w=iw*0.2352:h=ih*0.1380:color={color}@1:t=2
                ]"
            },

            ["Snapchat9x16"] = new SafetyOverlay
            {
                Name = "Snapchat9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw*0.0104):y=(ih*0.0835):w=iw*0.9793:h=ih*0.8662:color={color}@1:t=2
                ]"
            },

            ["TikTok9x16"] = new SafetyOverlay
            {
                Name = "TikTok9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=iw*0.1102:y=ih*0.1305:w=iw*0.7786:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.1102:y=ih*0.1305:w=iw*0.0019:h=ih*0.5369791666666667:color={color}@1:t=2,
                    drawbox=x=iw*0.7752:y=ih*0.1865:w=iw*0.1126:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.7752:y=ih*0.1865:w=iw*0.0019:h=ih*0.48046875:color={color}@1:t=2,
                    drawbox=x=iw*0.1099:y=ih*0.6657:w=iw*0.6681:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.8888888888888889:y=ih*0.1305:w=iw*0.0019:h=ih*0.05796875:color={color}@1:t=2
                ]"
            },

            ["Youtube1x1"] = new SafetyOverlay
            {
                Name = "Youtube1x1",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=iw*0.0444444444444444:y=ih*0.0444097222222222:w=iw*0.450017037037037:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.0444444444444444:y=ih*0.0444097222222222:w=iw*0.0019:h=ih*0.5947575:color={color}@1:t=2,
                    drawbox=x=iw*0.4944672222222222:y=ih*0.0444097222222222:w=iw*0.0019:h=ih*0.0528353703703704:color={color}@1:t=2,
                    drawbox=x=iw*0.4944672222222222:y=ih*0.0972450925925926:w=iw*0.4120569444444444:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.9065078703703704:y=ih*0.0972450925925926:w=iw*0.0019:h=ih*0.5424561111111111:color={color}@1:t=2,
                    drawbox=x=iw*0.0444444444444444:y=ih*0.638867962962963:w=iw*0.8621408333333333:h=ih*0.0019:color={color}@1:t=2
                ]"
            },

            ["Youtube9x16"] = new SafetyOverlay
            {
                Name = "Youtube9x16",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=(iw*0.0454):y=(ih*0.15):w=iw*0.7769:h=ih*0.4995:color={color}@1:t=2
                ]"
            },

            ["Youtube16x9"] = new SafetyOverlay
            {
                Name = "Youtube16x9",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=iw*0.019796875:y=ih*0.1522222222222222:w=iw*0.1635364583333333:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.019796875:y=ih*0.1522222222222222:w=iw*0.0019:h=ih*0.5342013888888889:color={color}@1:t=2,
                    drawbox=x=iw*0.1833333333333333:y=ih*0.0336111111111111:w=iw*0.591140625:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.1833333333333333:y=ih*0.0336111111111111:w=iw*0.0019:h=ih*0.1186111111111111:color={color}@1:t=2,
                    drawbox=x=iw*0.01984375:y=ih*0.6864259259259259:w=iw*0.4259791666666667:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.4458138020833333:y=ih*0.6864259259259259:w=iw*0.0019:h=ih*0.1791203703703704:color={color}@1:t=2,
                    drawbox=x=iw*0.4458125:y=ih*0.8655462962962963:w=iw*0.2115104166666667:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.6572864583333333:y=ih*0.8226111111111111:w=iw*0.1015364583333333:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.6572864583333333:y=ih*0.8226111111111111:w=iw*0.0019:h=ih*0.0435126851851852:color={color}@1:t=2,
                    drawbox=x=iw*0.7588229166666667:y=ih*0.7121851851851852:w=iw*0.1620416666666667:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.7588229166666667:y=ih*0.7121851851851852:w=iw*0.0019:h=ih*0.1104225:color={color}@1:t=2,
                    drawbox=x=iw*0.7744739583333333:y=ih*0.1213981481481481:w=iw*0.1463177083333333:h=ih*0.0019:color={color}@1:t=2,
                    drawbox=x=iw*0.9208691666666667:y=ih*0.1213981481481481:w=iw*0.0019:h=ih*0.5893055555555556:color={color}@1:t=2,
                    drawbox=x=iw*0.7744739583333333:y=ih*0.0336111111111111:w=iw*0.0019:h=ih*0.0877893518518519:color={color}@1:t=2
                ]"
            },

            ["Youtube16x9MastHead"] = new SafetyOverlay
            {
                Name = "Youtube16x9MastHead",
                GenerateFilter = color => $@"lavfi=[
                    drawbox=x=0:y=0:w=iw:h=(ih*0.25):color={color}@1:t=2,
                    drawbox=x=0:y=(ih*0.75):w=iw:h=(ih*0.25):color={color}@1:t=2
                ]"
            },

        };


        public MainWindow()
        {
            Current = this;
            InitializeComponent();
            Activated += (_, _) => SetActiveBorder(true);
            Deactivated += (_, _) => SetActiveBorder(false);
            DataContext = new MainWindowViewModel();
            // In your MainWindow constructor, after initializing DataContext:
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.IsBurnTimecodeEnabled = _burnTimecode;
            }
            _sequenceHandler = new EXRSequenceHandler();
            PlaylistSplitter = this.FindControl<GridSplitter>("PlaylistSplitter");
            PlaylistToggle = this.FindControl<Button>("PlaylistToggle");
            var grid = this.FindControl<Grid>("MainGrid");
            PlaylistColumn = grid?.ColumnDefinitions[0];
            InitializeKeyBindings();

            if (_playlistView != null)
            {
                _playlistView.SetCallbacks(
                    (filePath, isPlaylistItem) => 
                    {
                        Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            try
                            {
                                await LoadVideo(filePath, isPlaylistItem);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"MainWindow: Error in LoadVideo: {ex.Message}");
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

            if (_playlistView != null)
            {
                _playlistView.PlaylistModeStateChanged += (s, args) =>
                {
                    _isPlaylistMode = args.IsPlaylistMode;
                    var keepOpenValue = args.KeepOpenValue;
                    Debug.WriteLine($"MainWindow received playlist mode change: {_isPlaylistMode}, setting keep-open to: {keepOpenValue}");
                    var result = MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", keepOpenValue);
                    Debug.WriteLine($"MPV set_option_string result: {result}");
                };
            }

            InitializeMPV();

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1]))
            {
                LoadVideo(args[1], false); 
            }

    
            this.Loaded += (s, e) =>
            {
                InitializePlaybackControl();
                ChangeCancelButtonBackground();
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var focusTarget = this.FindControl<Control>("YourControlName");
                    focusTarget?.Focus();
                }, Avalonia.Threading.DispatcherPriority.Background);
                EnsureCorrectWindowOrder();
            };

            this.GotFocus += (s, e) => {
                EnsureCorrectWindowOrder();
            };

            this.LostFocus += (s, e) => {
                EnsureCorrectWindowOrder();
            };

            this.Activated += (s, e) => {
                EnsureCorrectWindowOrder();
            };

            this.Deactivated += (s, e) => {
                EnsureCorrectWindowOrder();
            };

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
            overlayImage = this.FindControl<Image>("OverlayImage");
            UpdateVolumeIcon(false); 
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
            playbackSlider.Tapped += PlaybackSlider_Tapped;
            volumeSlider.PropertyChanged += VolumeSlider_PropertyChanged;
            volumeSlider.PointerPressed += VolumeSlider_PointerPressed;
            volumeSlider.PointerReleased += VolumeSlider_PointerReleased;
            KeyDown += MainWindow_KeyDown;
            KeyUp += MainWindow_KeyUp;
            _playlistView = this.FindControl<PlaylistView>("PlaylistView");
            if (_playlistView != null)
            {
                _playlistView.ParentWindow = this;
            }
            _playlistToggleButton = this.FindControl<Button>("PlaylistToggle");
            Topmenu = this.FindControl<Grid>("Topmenu");
            BottomToolbar = this.FindControl<StackPanel>("BottomToolbar");
            
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

            _notesView = this.FindControl<NotesView>("NotesView");
            _notesToggleButton = this.FindControl<Button>("NotesToggleButton");
            NotesSplitter = this.FindControl<GridSplitter>("NotesSplitter");
            var grid = this.FindControl<Grid>("MainGrid");
            NotesColumn = grid?.ColumnDefinitions[4];

            if (_notesView != null)
            {
                _notesView.NotesToggleClicked += NotesView_ToggleClicked;
                _notesView.Initialize(this);
            }
            EnsureCorrectWindowOrder();
        }

        // Border and Fake Titlebar

        private void SetActiveBorder(bool isActive)
        {
            if (this.FindControl<Border>("MainBorder") is Border border)
            {
                border.BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromRgb(85, 64, 2))
                    : new SolidColorBrush(Colors.Transparent);
            }
        }

        private async Task FlashBorderAsync(int durationMs = 200)
        {
            // Get a reference to the main border
            var border = this.FindControl<Border>("MainBorder");
            if (border == null) return;

            // Store the original color
            var originalBrush = border.BorderBrush;

            // Set bright yellow flash color
            var flashColor = new SolidColorBrush(Color.FromRgb(255, 215, 0)); // Bright gold
            border.BorderBrush = flashColor;

            // Create smooth fade back animation
            var fadeSteps = 20; // Number of steps for smooth transition
            var stepDelay = durationMs / fadeSteps;

            // Get original color components
            var originalColor = ((SolidColorBrush)originalBrush).Color;
            var startR = 255;
            var startG = 215;
            var startB = 0;
            var endR = originalColor.R;
            var endG = originalColor.G;
            var endB = originalColor.B;

            // Gradually fade from flash color to original
            for (int i = 0; i < fadeSteps; i++)
            {
                // Calculate intermediate color
                float progress = (float)(i + 1) / fadeSteps;
                byte r = (byte)(startR - (startR - endR) * progress);
                byte g = (byte)(startG - (startG - endG) * progress);
                byte b = (byte)(startB - (startB - endB) * progress);

                // Apply intermediate color
                await Dispatcher.UIThread.InvokeAsync(() => {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
                });

                // Wait for the next step
                await Task.Delay(stepDelay);
            }

            // Ensure we're back to the original color
            await Dispatcher.UIThread.InvokeAsync(() => {
                border.BorderBrush = originalBrush;
            });
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestoreButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();


        // Hiding MPV

        public void HideMpvWindow()
        {
            _mpvHidden = true;
            UpdateChildWindowBounds();
        }

        public void ShowMpvWindow()
        {
            _mpvHidden = false;
            UpdateChildWindowBounds();
        }


        // Linear and Cache Toggles

        public void SetLinearTRC(IntPtr mpvHandle)
        {
            MPVInterop.mpv_set_option_string(mpvHandle, "target-trc", "linear");
            _currentTargetTRC = "linear"; // Update the variable
            //Debug.WriteLine("target-trc set to linear");
        }

        public void SetAutoTRC(IntPtr mpvHandle)
        {
            MPVInterop.mpv_set_option_string(mpvHandle, "target-trc", "auto");
            _currentTargetTRC = "auto"; // Update the variable
            //Debug.WriteLine("target-trc set to auto");
        }

        private void ChangeCancelButtonBackground()
        {
            // Find the Button
            var button = this.FindControl<Button>("CancelProcessingButton");
            if (button != null)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 24, 24, 24));

                // Find the Path within the Button
                var cancelIcon = button.FindControl<Avalonia.Controls.Shapes.Path>("CancelProcessingIcon");
                if (cancelIcon != null)
                {
                    cancelIcon.Fill = new SolidColorBrush(Color.FromArgb(255, 45, 45, 45));
                }
                else
                {
                    //Debug.WriteLine("CancelProcessingIcon is not found within CancelProcessingButton.");
                }
            }
            else
            {
                //Debug.WriteLine("CancelProcessingButton is not found.");
            }
        }

        private void ResetCancelButtonBackground()
        {
            var button = this.FindControl<Button>("CancelProcessingButton");
            if (button != null)
            {
                button.Background = new SolidColorBrush(Color.FromArgb(255, 68, 68, 68)); // #FF444444

                var cancelIcon = button.FindControl<Avalonia.Controls.Shapes.Path>("CancelProcessingIcon");
                if (cancelIcon != null)
                {
                    cancelIcon.Fill = new SolidColorBrush(Color.FromArgb(255, 186, 186, 186)); // #FFBABABA
                }
                else
                {
                    //Debug.WriteLine("CancelProcessingIcon is not found within CancelProcessingButton.");
                }
            }
            else
            {
                //Debug.WriteLine("CancelProcessingButton is not found.");
            }
        }



        // Background Window

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            this.Topmost = true; 
            this.Topmost = false;
            EnsureCorrectWindowOrder();
            this.Activate(); // Ensures it's active
            this.Focus();

        }


        private void InitializeBackgroundWindow()
        {
            _backgroundWindow = new BackgroundWindow();
            _backgroundWindow.Initialize(this, videoContainer);
            _backgroundWindow.Show();
            OnVideoModeChanged();
        }

        public void OnVideoModeChanged()
        {
            _backgroundWindow?.TriggerUpdate();
        }

        private void EnsureCorrectWindowOrder()
        {
            if (_backgroundWindow == null || childWindowHandle == IntPtr.Zero) return;

            try
            {
                var bgHandle = _backgroundWindow.TryGetPlatformHandle();
                if (bgHandle != null)
                {
                    WindowManagement.SetWindowPos(
                        childWindowHandle,
                        WindowManagement.HWND_TOP,  
                        0, 0, 0, 0,
                        WindowManagement.SWP_NOMOVE | WindowManagement.SWP_NOSIZE | WindowManagement.SWP_NOACTIVATE
                    );

                    WindowManagement.SetWindowPos(
                        bgHandle.Handle,
                        childWindowHandle,  
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
                Task.Delay(10).ContinueWith(_ =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _backgroundWindow?.UpdatePosition();
                        EnsureCorrectWindowOrder();
                    });
                });
            }
        }

        // Notes View

        private void DisableNotesForImageSequence()
        {
            if (_notesView != null)
            {
                _notesView.DisableForImageSequence();
                if (_notesView.IsVisible)
                {
                    ToggleNotesPanel();
                }
            }
        }

        public IntPtr? GetMpvHandle()
        {
            return mpvHandle != IntPtr.Zero ? mpvHandle : null;
        }

        public string? GetCurrentVideoPath()
        {
            return MPVInterop.GetStringProperty(mpvHandle, "path");
        }

        public double GetCurrentTimecode()
        {
            var timecode = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
            return timecode ?? 0.0;
        }

        public string GetCurrentTimecodeString()
        {
            return CurrentTimeTextBlock.Text;
        }

        public async Task CaptureScreenshot(string outputPath)
        {
            var args = new[] { "screenshot-to-file", outputPath };
            int result = MPVInterop.mpv_command(mpvHandle, args);
            if (result < 0)
            {
                throw new Exception($"Screenshot failed: {MPVInterop.GetError(result)}");
            }

            int attempts = 0;
            while (!File.Exists(outputPath) && attempts < 50)
            {
                await Task.Delay(100);
                attempts++;
            }
        }

        // EXR progress


        public async Task HandleEXRSequence(string firstFrame, string selectedLayer, string frameRate)
        {
            if (_isProcessingSequence)
            {
                Debug.WriteLine("Sequence processing already in progress");
                return;
            }

            _processingCts = new CancellationTokenSource();
            _isProcessingSequence = true;

            try
            {
                _processingPopup = new ProcessingPopup();
                _processingPopup.CancelClicked += (s, e) =>
                {
                    _processingCts?.Cancel();
                    _processingPopup?.Close();
                };
                _processingPopup.Show(this);

                var handler = new EXRSequenceHandler();
                var progress = new Progress<ProgressInfo>(info =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _processingPopup?.UpdateProgress(info.ProgressPercentage);
                    });
                });

                var cachePath = handler.GetCachePath(firstFrame, selectedLayer);
                if (cachePath == null)
                {
                    Debug.WriteLine("cachePath is null");
                    return;
                }

                await handler.ProcessSequence(firstFrame, selectedLayer, cachePath, progress, _processingCts.Token);

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    _processingPopup?.Close();
                    if (firstFrame == null || selectedLayer == null || frameRate == null)
                    {
                        Debug.WriteLine("One of the parameters is null");
                        return;
                    }

                    await PlayCachedSequence(firstFrame, selectedLayer, frameRate);
                });
            }
            catch (OperationCanceledException)
            {
                var toast = new ToastView();
                toast.ShowToast("Info", "EXR sequence processing cancelled", this);
            }
            catch (Exception ex)
            {
                var toast = new ToastView();
                toast.ShowToast("Error", $"Error processing EXR sequence: {ex.Message}", this);
            }
            finally
            {
                _isProcessingSequence = false;
                _processingCts?.Dispose();
                _processingCts = null;
                _processingPopup?.Close();
                _processingPopup = null;
            }
        }

        private async Task PlayCachedSequence(string originalFile, string layerName, string frameRate)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("MPV handle is not initialized");
                    return;
                }

                var handler = new EXRSequenceHandler();
                var cachePattern = handler.GetCachePattern(originalFile, layerName);

                if (!Directory.EnumerateFiles(Path.GetDirectoryName(cachePattern), "*.exr").Any())
                {
                    Debug.WriteLine("No cached files found for playback.");
                    var toast = new ToastView();
                    toast.ShowToast("Error", "No cached files found for playback.", this);
                    return;
                }

                Debug.WriteLine($"Cache pattern for MPV: {cachePattern}");
                MPVInterop.mpv_set_option_string(mpvHandle, "mf-fps", frameRate);
                var mfPath = $"mf://{cachePattern}";
                var args = new[] { "loadfile", mfPath, "replace" };
                int result = MPVInterop.mpv_command(mpvHandle, args);

                if (result < 0)
                {
                    Debug.WriteLine($"Failed to load cached sequence: error code {result}");
                    var toast = new ToastView();
                    toast.ShowToast("Error", "Failed to load cached sequence.", this);
                    return;
                }
                
                if (await WaitForDurationAndInitialize())
                {
                    Debug.WriteLine("Cached sequence loaded successfully, starting playback");
                    _ = UpdateTimecodeAsync();
                    SeekToStart();
                    var playArgs = new[] { "set", "pause", "no" };
                    int playResult = MPVInterop.mpv_command(mpvHandle, playArgs);

                    if (playResult < 0)
                    {
                        Debug.WriteLine($"Failed to start playback: {MPVInterop.GetError(playResult)}");
                    }
                    else
                    {
                        Debug.WriteLine("Successfully started playback of cached sequence");
                        _ = UpdatePlayPauseIcon();
                        _ = UpdatePlayState();
                    }
                }
                else
                {
                    Debug.WriteLine("Failed to initialize cached sequence");
                    var toast = new ToastView();
                    toast.ShowToast("Error", "Failed to initialize cached sequence.", this);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayCachedSequence: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Error", "Failed to play cached sequence.", this);
            }
        }


        private void CancelProcessing_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessingSequence)
            {
                _processingCts?.Cancel(); 
            }

            MPVInterop.mpv_command(mpvHandle, new[] { "loadfile", "" }); 
            Task.Delay(10);
            LoadVideo(null); 
            CacheSettingsPopup.EmptyCache(this);
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
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in TogglePlayPause: {ex.Message}");
            }
        }

        // Toast message

        public void ShowToast(string title, string message)
        {
            try
            {
                // Close the current toast if one is already displayed
                if (_currentToast != null)
                {
                    _currentToast.Close();
                    _currentToast = null;
                }

                var toast = new ToastView();
                toast.SetContent(title, message);

   
                var scalingFactor = VisualRoot?.RenderScaling ?? 1.0;
                const int margin = 16; 
                var xPos = Position.X + (int)(Bounds.Width - (toast.Width * scalingFactor) - (margin * scalingFactor));
                var yPos = Position.Y + (int)(Bounds.Height - (toast.Height * scalingFactor) - (margin * scalingFactor));

                toast.Position = new PixelPoint(xPos, yPos);
                toast.Show();
                _currentToast = toast;

                // Close the toast after 3.5 seconds
                DispatcherTimer timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3.5)
                };
                timer.Tick += (s, e) =>
                {
                    toast.Close();
                    _currentToast = null; 
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error showing toast: {ex.Message}");
            }
        }

        // Drag and drop main window
        private void MainGrid_DragEnter(object? sender, DragEventArgs e)
        {
            var files = e.Data.GetFileNames();
            if (files != null)
            {
                var acceptedExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi", ".jpg", ".tif", ".tiff", ".png", ".dpx", ".tga", ".exr" };
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
                var acceptedExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi", ".jpg", ".tif", ".tiff", ".png", ".dpx", ".tga", ".exr" };
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
                var videoExtensions = new[] { ".mp4", ".mov", ".mxf", ".gif", ".mkv", ".avi" };
                var imageSeqExtensions = new[] { ".jpg", ".tif", ".tiff", ".png", ".dpx", ".tga" };
                var exrExtensions = new[] { ".exr" };

                var validFile = files.FirstOrDefault(file =>
                    videoExtensions.Contains(Path.GetExtension(file).ToLower()) ||
                    imageSeqExtensions.Contains(Path.GetExtension(file).ToLower()) ||
                    exrExtensions.Contains(Path.GetExtension(file).ToLower()));

                if (validFile != null)
                {
                    var fileExtension = Path.GetExtension(validFile).ToLower();

                    try
                    {
                        if (videoExtensions.Contains(fileExtension))
                        {
                            // Handle video files
                            await LoadVideo(validFile, false); // Call your existing video-loading logic
                        }
                        else if (imageSeqExtensions.Contains(fileExtension))
                        {
                            // Handle image sequence files
                            await HandleImageSequence(validFile);
                        }
                        else if (exrExtensions.Contains(fileExtension))
                        {
                            // Check for EXR layers
                            var layers = await _sequenceHandler.AnalyzeEXRFile(validFile);

                            if (layers == null || !layers.Any())
                            {
                                // No layers found, treat as an image sequence
                                await HandleImageSequence(validFile);
                            }
                            else
                            {
                                // Layers found, handle as an EXR sequence
                                await HandleEXRSequenceFromFile(validFile);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling file drop: {ex.Message}");
                    }
                }
            }
        }

        // Image sequence loading

        public async Task HandleImageSequence(string selectedFile)
        {
            var frameRatePopup = new FrameRatePopup
            {
                OnFrameRateSelected = (frameRate) => {
                    DisableNotesForImageSequence();  // Call after user confirms frame rate
                    PlayImageSequence(selectedFile, frameRate);
                },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            await frameRatePopup.ShowDialog(this);
        }

        public async Task HandleImageSequenceFromEXR(string filePath, string frameRate)
        {
            _isBaseColorSequence = true;
            DisableNotesForImageSequence();  // Call just before playing the sequence
            Debug.WriteLine($"Handling EXR Base Color sequence for {filePath} at {frameRate} fps");
            PlayImageSequence(filePath, frameRate);
        }

        private async Task HandleEXRSequenceFromFile(string selectedFile)
        {
            try
            {
                var layerDialog = new EXRLayerSelectionDialog(selectedFile)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                var result = await layerDialog.ShowDialog<bool>(this);

                if (result && layerDialog.SelectedLayer != null)
                {
                    var frameRate = layerDialog.FrameRateInput.Text;
                    DisableNotesForImageSequence();
                    await HandleEXRSequence(selectedFile, layerDialog.SelectedLayer, frameRate);
                }
            }
            catch (Exception ex)
            {
                var toast = new ToastView();
                toast.ShowToast("Error", $"Failed to process sequence: {ex.Message}", this);
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
                if (!Directory.Exists(_lutsDirectory))
                {
                    Directory.CreateDirectory(_lutsDirectory);
                }

                string markerFile = Path.Combine(_lutsDirectory, ".extracted");
                if (File.Exists(markerFile))
                {
                    return; 
                }

                // Get the path to the luts.zip in the Assets folder
                string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                string lutsZipPath = Path.Combine(assetsPath, "luts.zip");

                if (!File.Exists(lutsZipPath))
                {
                    //Debug.WriteLine("LUTs zip file not found in Assets folder");
                    return;
                }

                await Task.Run(() =>
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(lutsZipPath, _lutsDirectory, true);
                });

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

                var currentPath = MPVInterop.GetStringProperty(mpvHandle, "path");
                var currentPosition = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
                var isPaused = GetPropertyAsInt("pause") == 1;
                var currentVolume = MPVInterop.GetDoubleProperty(mpvHandle, "volume");

                if (string.IsNullOrEmpty(currentPath))
                {
                    //Debug.WriteLine("No video is currently loaded");
                    return;
                }

                _currentLutPath = lutPath;
                var result = MPVInterop.mpv_set_option_string(mpvHandle, "lut", lutPath);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to set LUT: {MPVInterop.GetError(result)}");
                    return;
                }

                var args = new[] { "loadfile", currentPath, "replace" };
                result = MPVInterop.mpv_command(mpvHandle, args);

                if (string.IsNullOrEmpty(currentPath))
                {
                    //Debug.WriteLine("No video is currently loaded");
                    return;
                }

                await WaitForFileLoadedAndSeek();

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
                //var toast = new ToastView();
                //toast.ShowToast("Success", "Color transform applied.", this);
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
                //var toast = new ToastView();
                //toast.ShowToast("Success", "Color transforms removed.", this);
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
            SetAutoTRC(mpvHandle);
        }

        private async void sRGB_rec709_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("709_sRGB.cube");
            SetAutoTRC(mpvHandle);
        }

        private async void sRGB_ACES2065_1_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACES2065_1_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_ACEScg_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACEScg_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_AGX_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("AGX_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_Linear_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("Linear_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_ArriLogc3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC3_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_ArriLogc4_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC4_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_CanonLog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("CanonLog3_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_PanasonicVlog_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("PanasonicVlog_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_RedLog3G10_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("RedLog3G10_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_SonySlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void sRGB_SonyVeniceSlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3Venice_to_sRGB.cube");
            SetLinearTRC(mpvHandle);
        }

        // Rec709 conversion handlers
        private async void rec709_ACES2065_1_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACES2065_1_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_ACEScg_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ACEScg_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_AGX_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("AGX_to_bt1886.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_Linear_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("Linear_to_Rec1886.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_ArriLogc3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC3_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_ArriLogc4_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("ArriLogC4_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_CanonLog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("CanonLog3_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_PanasonicVlog_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("PanasonicVlog_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_RedLog3G10_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("RedLog3G10_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_SonySlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3_to_rec709.cube");
            SetLinearTRC(mpvHandle);
        }

        private async void rec709_SonyVeniceSlog3_Click(object? sender, RoutedEventArgs e)
        {
            await ApplyLut("SonySlog3Venice_to_rec709.cube");
            SetLinearTRC(mpvHandle);
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
                return "Unknown";
            }
        }

        public async Task PlayImageSequence(string imagePath, string frameRate)
        {
            try
            {
                Debug.WriteLine("PlayImageSequence started");
                DisableNotesForImageSequence();
                //await Dispatcher.UIThread.InvokeAsync(() => {
                //    ProcessingProgressBar.Value = 0;
                //});
                ChangeCancelButtonBackground();

                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }
                //Debug.WriteLine($"Starting image sequence load process for: {imagePath}");
         
                _lastDuration = 0;
                _lastPosition = 0;

                var directory = Path.GetDirectoryName(imagePath);
                var fileName = Path.GetFileNameWithoutExtension(imagePath);
                var extension = Path.GetExtension(imagePath);
                var fileNameWithoutNumericSequence = System.Text.RegularExpressions.Regex.Replace(fileName, @"[\._]\d+$", "");
                var sequencePath = $"mf://{directory}/{fileNameWithoutNumericSequence}*{extension}";
                var newTitle = $"umpv - {fileNameWithoutNumericSequence}_*{extension}";

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
                    _playlistView?.SetCurrentlyPlaying(sequencePath);
                });

                if (_eventLoopCancellation != null)
                {
                    await _eventLoopCancellation.CancelAsync();
                    _eventLoopCancellation = new CancellationTokenSource();
                }

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
                SeekToStart();
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

        private void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var settingsPopup = new SettingsPopup
            {
                OnSettingsUpdated = InitializeKeyBindings 
            };

            settingsPopup.ShowDialog(this);
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
                    //var toast = new ToastView();
                    //toast.ShowToast("Success", $"Playback speed set to {_currentSpeed}x", this);
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
                    //var toast = new ToastView();
                    //toast.ShowToast("Success", "Subtitles Toggled", this);
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
                        //var toast = new ToastView();
                        //toast.ShowToast("Success", "Subtitles loaded.", this);
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
        private async void ScreenShotDesktop_Click(object? sender, RoutedEventArgs e)
        {
            if (mpvHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string finalFileName = $"screenshot_{DateTime.Now:yyyyMMddHHmmss}.png";
                string finalPath = Path.Combine(desktopPath, finalFileName);

                if (_burnTimecode)
                {
                    // Take a direct approach using CaptureScreenshot which gives us a specific path
                    string tempDir = Path.Combine(Path.GetTempPath(), "umpv");
                    Directory.CreateDirectory(tempDir);
                    string tempPath = Path.Combine(tempDir, $"temp_screenshot_{Guid.NewGuid()}.png");

                    // Use our existing method to capture a screenshot to a specific file
                    await CaptureScreenshot(tempPath);

                    // Check if the file exists
                    var toast = new ToastView();
                    if (!File.Exists(tempPath))
                    {
                        
                        toast.ShowToast("Error", "Failed to capture screenshot.", this);
                        return;
                    }

                    // Now process it with timecode
                    using (var image = DrawingImage.FromFile(tempPath))
                    {
                        var processedImage = BurnTimecodeOnImage(image);
                        processedImage.Save(finalPath);
                    }

                    // Clean up temp file
                    try { File.Delete(tempPath); } catch { /* Ignore errors */ }

                    // Show success toast
                    FlashBorderAsync();
              
                    toast.ShowToast("Success", "Screenshot saved with timecode.", this);
                }
                else
                {
                    // Default screenshot behavior without burn-in
                    int dirResult = MPVInterop.mpv_set_option_string(mpvHandle, "screenshot-directory", desktopPath);
                    if (dirResult < 0)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Error", "Failed to set screenshot directory.", this);
                        return;
                    }

                    int templateResult = MPVInterop.mpv_set_option_string(mpvHandle, "screenshot-template", "%F-%04n");
                    if (templateResult < 0)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Error", "Failed to set screenshot template.", this);
                        return;
                    }

                    var args = new[] { "screenshot" };
                    int commandResult = MPVInterop.mpv_command(mpvHandle, args);
                    if (commandResult < 0)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Warning", "Failed to take screenshot.", this);
                    }
                    else
                    {
                        FlashBorderAsync();
                        //var toast = new ToastView();
                        //toast.ShowToast("Success", "Screenshot saved to desktop.", this);
                    }
                }
            }
            catch (Exception ex)
            {
                var toast = new ToastView();
                toast.ShowToast("Error", $"Screenshot failed: {ex.Message}", this);
            }
        }

        // Playlist

        public void OnPlaylistToggled(object? sender, RoutedEventArgs e)
        {
            PlaylistButton_Click(sender, e);
        }

        public void PlaylistButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_playlistView != null && PlaylistColumn != null)
            {
                bool isVisible = !_playlistView.IsVisible;

                _playlistView.IsVisible = isVisible;

                PlaylistColumn.Width = isVisible
                    ? new GridLength(300) // Set to the desired width when visible
                    : new GridLength(0);  // Collapse the column when hidden

                if (PlaylistSplitter != null)
                {
                    PlaylistSplitter.IsEnabled = isVisible;
                    PlaylistSplitter.IsVisible = isVisible;
                }

                if (PlaylistToggle != null)
                {
                    PlaylistToggle.IsVisible = !isVisible;
                }

                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(0); // Let the layout process updates
                    UpdateChildWindowBounds();
                    OnVideoModeChanged();
                });


                if (_playlistToggleButton?.Content is AvaloniaPath playlistPath)
                {
                    var iconKey = isVisible ? "text_align_right_regular" : "list_regular";

                    if (App.Current?.Resources.TryGetResource(iconKey,
                        ThemeVariant.Default, out object? resource) == true &&
                        resource is StreamGeometry geometry)
                    {
                        playlistPath.Data = geometry;
                    }
                }
            }
        }

        private void PlaylistSplitter_DragDelta(object? sender, VectorEventArgs e)
        {
            // Throttle updates to reduce spamming
            ThrottleDispatcher.Throttle(200, () =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OnVideoModeChanged();
                });
            });
        }

        private void PlaylistSplitter_DragCompleted(object? sender, VectorEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnVideoModeChanged();
            });
        }


        private void ForwardSpotButton_Click(object? sender, RoutedEventArgs e)
        {
            _playlistView?.PlayNext();
        }

        private void BackwardSpotButton_Click(object? sender, RoutedEventArgs e)
        {
            _playlistView?.PlayPrevious();
        }


        // Note Panel Buttons

        public void NotesButton_Click(object? sender, RoutedEventArgs e)
        {
            ToggleNotesPanel();
        }

        private void NotesView_ToggleClicked(object? sender, EventArgs e)
        {
            ToggleNotesPanel();
        }

        private void ToggleNotesPanel()
        {
            if (_notesView != null && NotesColumn != null)
            {
                bool isVisible = !_notesView.IsVisible;

                // Update NotesView
                _notesView.IsVisible = isVisible;
                _notesView.UpdateToggleButtonIcon(isVisible);

                // Update column width
                NotesColumn.Width = isVisible
                    ? new GridLength(400)
                    : new GridLength(0);

                // Update toggle button visibility
                if (_notesToggleButton != null)
                {
                    _notesToggleButton.IsVisible = !isVisible;
                }

                if (NotesSplitter != null)
                {
                    NotesSplitter.IsEnabled = isVisible;
                    NotesSplitter.IsVisible = isVisible;
                }

                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(0);
                    UpdateChildWindowBounds();
                    OnVideoModeChanged();
                });
            }
        }

        private void NotesSplitter_DragDelta(object? sender, VectorEventArgs e)
        {
            ThrottleDispatcher.Throttle(200, () =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    OnVideoModeChanged();
                });
            });
        }

        private void NotesSplitter_DragCompleted(object? sender, VectorEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnVideoModeChanged();
            });
        }

        // Buttons

        private void OpenCacheSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var popup = new CacheSettingsPopup
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            popup.ShowDialog(this);
        }

        private async void OpenEXRSequence_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select First EXR Frame",
                Filters = new List<FileDialogFilter>
        {
            new FileDialogFilter { Name = "EXR Files", Extensions = { "exr" } }
        }
            };

            var filePaths = await openFileDialog.ShowAsync(this);
            if (filePaths is { Length: > 0 })
            {
                await HandleEXRSequenceFromFile(filePaths[0]);
            }
        }


        public void RemovePlaylistButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_playlistView != null && _playlistView.IsVisible) 
            {
                _playlistView.TriggerRemoveFromPlaylist(); 
            }
            else
            {
                var window = TopLevel.GetTopLevel(this) as Window;
                if (window != null)
                {
                    var toast = new ToastView();
                    toast.ShowToast("Error", "PlaylistView is not open or visible.", window);
                }
            }
        }

        private void ResizeToPixelRatio_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("MPV handle is not initialized.");
                    return;
                }

                var videoWidth = MPVInterop.GetIntProperty(mpvHandle, "width");
                var videoHeight = MPVInterop.GetIntProperty(mpvHandle, "height");

                if (!videoWidth.HasValue || !videoHeight.HasValue || videoWidth.Value <= 0 || videoHeight.Value <= 0)
                {
                    Debug.WriteLine("Failed to retrieve video dimensions.");
                    return;
                }

                var topToolbarHeight = Topmenu?.Bounds.Height ?? 0;
                var bottomToolbarHeight = BottomToolbar?.Bounds.Height ?? 0;
                var sideMargins = Bounds.Width - (videoContainer?.Bounds.Width ?? 0);

                var chromeHeight = Height - ClientSize.Height; // Vertical chrome (titlebar + borders)
                var chromeWidth = Width - ClientSize.Width;    // Horizontal chrome (borders)

                Debug.WriteLine($"TopToolbar: {topToolbarHeight}, BottomToolbar: {bottomToolbarHeight}");
                Debug.WriteLine($"ChromeHeight: {chromeHeight}, ChromeWidth: {chromeWidth}");

                // Add fixed padding if necessary
                const int SystemTitlebarHeight = 30; // Adjust for platform-specific titlebars
                const int SystemBorderHeight = 2;    // Optional: borders
                chromeHeight = Math.Max(chromeHeight, SystemTitlebarHeight + SystemBorderHeight);

                // Calculate offsets
                var extraWidth = (int)(sideMargins + chromeWidth);
                var extraHeight = (int)(topToolbarHeight + bottomToolbarHeight + chromeHeight);

                // Calculate new window size
                var newWidth = videoWidth.Value + extraWidth;
                var newHeight = videoHeight.Value + extraHeight;

                // Get screen dimensions
                var screen = Screens.Primary; // Use the primary screen or find the relevant one
                var screenWidth = screen.Bounds.Width;
                var screenHeight = screen.Bounds.Height;

                if (newWidth > screenWidth || newHeight > screenHeight)
                {
                    // Expand to full screen
                    Width = screenWidth / RenderScaling;
                    Height = screenHeight / RenderScaling;
                    Position = new PixelPoint(0, 0); // Top-left corner of the screen
                    Debug.WriteLine($"Window size exceeds screen size, expanding to full screen.");
                    //var toast = new ToastView();
                    //toast.ShowToast("Warning", "Window size exceeds screen size, this is not truly 1:1.", this);

                }
                else
                {
                    // Center the window on the screen
                    var screenCenterX = screen.Bounds.Width / 2;
                    var screenCenterY = screen.Bounds.Height / 2;

                    var newLeft = screenCenterX - (newWidth / 2);
                    var newTop = screenCenterY - (newHeight / 2);

                    // Apply new size and position
                    Width = newWidth / RenderScaling;
                    Height = newHeight / RenderScaling;
                    Position = new PixelPoint((int)newLeft, (int)newTop);

                    Debug.WriteLine($"Resized window to {newWidth}x{newHeight} (video: {videoWidth}x{videoHeight}, UI: {extraWidth}x{extraHeight}).");
                    Debug.WriteLine($"Centered window at ({newLeft}, {newTop}).");
                }

                // Update video container bounds
                UpdateChildWindowBounds();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resizing for 1:1 pixel ratio: {ex.Message}");
            }
        }

        private void ResizeToHalfScreenSize_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Get screen dimensions
                var screen = Screens.Primary; // Use primary screen
                var screenWidth = screen.Bounds.Width;
                var screenHeight = screen.Bounds.Height;

                // Calculate half of the screen size
                var halfWidth = screenWidth / 2;
                var halfHeight = screenHeight / 2;

                // Add padding for UI
                var topToolbarHeight = Topmenu?.Bounds.Height ?? 0;
                var bottomToolbarHeight = BottomToolbar?.Bounds.Height ?? 0;
                const int SystemTitlebarHeight = 30; // Adjust as needed for the platform
                const int SystemBorderHeight = 5;    // Optional: borders

                var extraHeight = topToolbarHeight + bottomToolbarHeight + SystemTitlebarHeight + SystemBorderHeight;

                // Calculate new window dimensions
                var newWidth = halfWidth;
                var newHeight = halfHeight + extraHeight;

                // Calculate center position
                var screenCenterX = screenWidth / 2;
                var screenCenterY = screenHeight / 2;

                var newLeft = screenCenterX - (newWidth / 2);
                var newTop = screenCenterY - (newHeight / 2);

                // Apply new size and position
                Width = newWidth / RenderScaling;
                Height = newHeight / RenderScaling;
                Position = new PixelPoint((int)newLeft, (int)newTop);

                // Update video container bounds
                UpdateChildWindowBounds();

                Debug.WriteLine($"Resized window to 50% of screen size: {Width}x{Height}, Centered at ({newLeft}, {newTop})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resizing to 50% of screen size: {ex.Message}");
            }
        }


        private async void ImageSeq_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select an Image",
                Filters = new List<FileDialogFilter>
        {
            new FileDialogFilter { Name = "Images", Extensions = { "jpg", "tif", "tiff", "png", "tga", "dpx", "exr" } }
        }
            };
            var filePaths = await openFileDialog.ShowAsync(this);
            if (filePaths is { Length: > 0 })
            {
                await HandleImageSequence(filePaths[0]);
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
                    PhotoFilterIcon.Data = Application.Current.FindResource("circle_half_fill_regular") as StreamGeometry;
                    await ApplyLut("709_sRGB.cube");
                    //var toast = new ToastView();
                    //toast.ShowToast("Success", "Color transformed applied.", this);
                }
                else
                {
                    PhotoFilterIcon.Data = Application.Current.FindResource("photo_filter_regular") as StreamGeometry;
                    await RemoveLut();
                    //var toast = new ToastView();
                    //toast.ShowToast("Success", "Color transform removed.", this);
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

        private async void PlayButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (mpvHandle == IntPtr.Zero)
                {
                    //Debug.WriteLine("MPV handle is not initialized");
                    return;
                }

                bool wasPaused = (MPVInterop.GetStringProperty(mpvHandle, "pause") == "yes");
                var currentPosition = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");

                // Cycle mpv's pause state (toggle play/pause)
                var args = new[] { "cycle", "pause" };
                int result = MPVInterop.mpv_command(mpvHandle, args);

                if (result < 0)
                {
                    //Debug.WriteLine($"MPV Error: {MPVInterop.GetError(result)}");
                    EnsureCorrectWindowOrder();
                }
                ShowMpvWindow();
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in PlayButton_Click: {ex.Message}");
            }
        }


        public async Task UpdatePlayPauseIcon()
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

        private void SeekToStart()
        {
            if (mpvHandle != IntPtr.Zero)
            {
                var result = MPVInterop.mpv_command(mpvHandle, new[] { "seek", "0", "absolute" });
                if (result < 0)
                {
                    Debug.WriteLine($"Failed to seek to start: {result}");
                }
            }
            else
            {
                Debug.WriteLine("mpvHandle is not initialized.");
            }
        }

        private void ToStartButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
        {
            SeekToStart();
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

                using (var image = DrawingImage.FromFile(screenshotPath))
                {
                    // Apply timecode if enabled using the current video's data
                    var processedImage = _burnTimecode ? BurnTimecodeOnImage(image) : image;

                    // Copy to clipboard
                    NativeClipboard.SetImage(processedImage);

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
            }
            catch (Exception ex)
            {
                var toastError = new ToastView();
                toastError.ShowToast("Warning", "Screenshot failed.", this);
            }
        }

        private void CameraButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _ = HandleScreenshot();
            FlashBorderAsync();
        }

        // Safety Overlays

        private void ApplyFilter(string filterCommand, string filterIdentifier, bool forceReapply = false)
        {
            if (mpvHandle == IntPtr.Zero)
                return;

            try
            {
                if (!forceReapply)
                {
                    // Normal toggle logic
                    if (currentActiveFilter == filterIdentifier)
                    {
                        filterCommand = "";
                        currentActiveFilter = string.Empty;
                    }
                    else
                    {
                        currentActiveFilter = filterIdentifier;
                    }
                }
                else
                {
                    // On color reapply, don't change currentActiveFilter or clear the filter
                }

                var args = new[] { "vf", "set", filterCommand };
                _ = MPVInterop.mpv_command(mpvHandle, args);
            }
            catch (Exception ex)
            {
                // Log or handle if needed
            }
        }


        private void ToggleOverlay(string key)
        {
            if (_overlays.TryGetValue(key, out var overlay))
            {
                bool isSameOverlay = currentActiveFilter == key;

                if (overlay.IsActive)
                {
                    if (isSameOverlay)
                    {
                        // Reapply filter with updated color (from color change)
                        var filter = overlay.GenerateFilter(_currentOverlayColor);
                        ApplyFilter(filter, key);
                        return;
                    }
                    else
                    {
                        // A different overlay is selected, deactivate this one
                        overlay.IsActive = false;
                    }
                }

                // Apply the new overlay
                foreach (var kv in _overlays)
                    kv.Value.IsActive = false;

                var newFilter = overlay.GenerateFilter(_currentOverlayColor);
                ApplyFilter(newFilter, key);
                overlay.IsActive = true;
                currentActiveFilter = key;
            }
        }


        private void SetOverlayColor(string newColor)
        {
            _currentOverlayColor = newColor;

            if (!string.IsNullOrWhiteSpace(currentActiveFilter) &&
                _overlays.TryGetValue(currentActiveFilter, out var overlay) &&
                overlay.IsActive)
            {
                var filter = overlay.GenerateFilter(_currentOverlayColor);
                ApplyFilter(filter, currentActiveFilter, forceReapply: true);
            }
        }


        private void OnSafetyColorClicked(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                var colorName = item.Name switch
                {
                    "SafetyGold" => "Gold",
                    "SafetyDarkOrange" => "DarkOrange",
                    "SafetyRed" => "Red",
                    "SafetyHotPink" => "HotPink",
                    "SafetyDarkMagenta" => "DarkMagenta",
                    "SafetyPaleGreen" => "PaleGreen",
                    "SafetyDarkGreen" => "DarkGreen",
                    "SafetyDeepSkyBlue" => "DeepSkyBlue",
                    "SafetyMediumBlue" => "MediumBlue",
                    _ => "Gold"
                };

                SetOverlayColor(colorName);
            }
        }

        private void SafetyButton_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Broadcast16x9");
        }

        private void SafetyButton_MetaReel9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("MetaReel9x16");
        }

        private void SafetyButton_MetaStory9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("MetaStory9x16");
        }

        private void SafetyButton_Pinterest9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Pinterest9x16");
        }

        private void SafetyButton_Pinterest1x1PremiumSpotlight_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Pinterest1x1PremiumSpotlight");
        }

        private void SafetyButton_Snapchat9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Snapchat9x16");
        }

        private void SafetyButton_TikTok9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("TikTok9x16");
        }

        private void SafetyButton_Youtube1x1_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Youtube1x1");
        }

        private void SafetyButton_Youtube9x16_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Youtube9x16");
        }

        private void SafetyButton_Youtube16x9_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Youtube16x9");
        }

        private void SafetyButton_Youtube16x9MastHead_Click(object? sender, RoutedEventArgs e)
        {
            ToggleOverlay("Youtube16x9MastHead");
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
                isLooping = !isLooping;
                var loopState = isLooping ? "yes" : "no";
                var args = new[] { "set", "loop-file", loopState };
                int result = MPVInterop.mpv_command(mpvHandle, args);
                if (result >= 0)
                {
                    //Debug.WriteLine($"Looping toggled: {loopState}");
                    UpdateLoopingIcon();
                    FlashBorderAsync();
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

        // Timecode burn-in

        private void ToggleBurnTimecode_Click(object? sender, RoutedEventArgs e)
        {
            _burnTimecode = !_burnTimecode;
    
            // Update the view model binding
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.IsBurnTimecodeEnabled = _burnTimecode;
            }
    
            // Flash border for user feedback
            FlashBorderAsync();
    
            // Optionally show toast message
            var toast = new ToastView();
            toast.ShowToast("Timecode Burn", _burnTimecode ? "Timecode burn enabled" : "Timecode burn disabled", this);
        }

        private System.Drawing.Image BurnTimecodeOnImage(System.Drawing.Image image)
        {
            if (!_burnTimecode)
                return image;

            // Get the CURRENT timecode and frame values directly from MPV at the time of screenshot
            string timecode = CurrentTimeTextBlock.Text;
            string framecode = CurrentFrameTextBlock.Text;

            // If MPV handle is valid, try to get the most accurate current values
            if (mpvHandle != IntPtr.Zero)
            {
                var currentTime = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
                var currentFrame = MPVInterop.GetDoubleProperty(mpvHandle, "estimated-frame-number");
                var fps = MPVInterop.GetDoubleProperty(mpvHandle, "container-fps");

                if (currentTime.HasValue && currentTime >= 0)
                {
                    // Format the timecode using the current values
                    timecode = FormatTimecode(currentTime.Value, currentFrame, fps);
                    if (currentFrame.HasValue)
                    {
                        framecode = currentFrame.Value.ToString("F0");
                    }
                }
            }

            string burnText = $"{timecode} F:{framecode}";

            // Create a bitmap to work with
            using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(image);
            using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);

            // Set graphics quality for better text rendering
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            // Create font and brushes - 50% larger font size
            using System.Drawing.Font font = new System.Drawing.Font("Consolas", 21, System.Drawing.FontStyle.Bold);
            System.Drawing.SizeF textSize = graphics.MeasureString(burnText, font);

            // Improved margins
            int horizontalMargin = 15;    // Left margin
            int verticalMargin = 33;      // Bottom margin
            int padding = 10;             // Padding inside the text box

            // Draw a semi-transparent black background for better readability
            using System.Drawing.SolidBrush bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(180, 0, 0, 0));
            graphics.FillRectangle(
                bgBrush,
                horizontalMargin,
                bitmap.Height - textSize.Height - verticalMargin,
                textSize.Width + padding * 2,
                textSize.Height + padding * 2
            );

            // Draw text
            using System.Drawing.SolidBrush textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            graphics.DrawString(
                burnText,
                font,
                textBrush,
                horizontalMargin + padding,
                bitmap.Height - textSize.Height - verticalMargin + padding
            );

            // Return the modified image
            return new System.Drawing.Bitmap(bitmap);
        }

        // Keyboard commands

        private void InitializeKeyBindings()
        {
            KeyBindings.Clear();

            var settingsPopup = new SettingsPopup();
            settingsPopup.LoadKeyBindings();

            foreach (var binding in settingsPopup.KeyBindings)
            {
                KeyBindings.Add(binding);
            }

            Debug.WriteLine("Keybindings reloaded in MainWindow.");
        }

        private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_notesView != null && _notesView.IsEditingNote())
            {
                return;
            }

            var keyBinding = KeyBindings.FirstOrDefault(kb => kb.Key.Equals(e.Key.ToString(), StringComparison.OrdinalIgnoreCase));
            if (keyBinding != null)
            {
                switch (keyBinding.Bindings)
                {
                    case "Play / Pause":
                        PlayButton_Click(null, null);
                        break;
                    case "Play / Pause Alt":
                        PlayButton_Click(null, null);
                        break;
                    case "Open File":
                        OpenMenuItem_Click(null, null);
                        break;
                    case "Info":
                        InfoButton_Click(null, null);
                        break;
                    case "Previous Frame":
                        PrevFrameButton_Click(null, null);
                        break;
                    case "Next Frame":
                        NextFrameButton_Click(null, null);
                        break;
                    case "Screenshot to Clipboard":
                        CameraButton_Click(null, null);
                        break;
                    case "Screenshot to Desktop":
                        ScreenShotDesktop_Click(null, null);
                        break;
                    case "Seek Backward 1 sec":
                        SeekBackward_Click(null, null);
                        break;
                    case "Seek Forward 1 sec":
                        SeekForward_Click(null, null);
                        break;
                    case "Go to Video Beginning":
                        ToStartButton_Click(null, null);
                        break;
                    case "Go to Video End":
                        ToEndButton_Click(null, null);
                        break;
                    case "Toggle Playlist":
                        PlaylistButton_Click(null, null);
                        break;
                    case "Toggle Looping":
                        LoopingButton_Click(null, null);
                        break;
                    case "Play Video 1:1 Size":
                        ResizeToPixelRatio_Click(null, null);
                        break;
                    case "Play Video 50% Screen Size":
                        ResizeToHalfScreenSize_Click(null, null);
                        break;
                    case "16:9 Title/Action Safety":
                        SafetyButton_Click(null, null);
                        break;
                    case "Toggle Full-screen Mode":
                        FullScreenButton_Click(null, null);
                        break;
                    case "Exit Full-screen Mode":
                        if (isFullScreen)
                        {
                            FullScreenButton_Click(null, null);
                        }
                        break;
                    case "Exit Full-screen Mode Alt":
                        if (isFullScreen)
                        {
                            FullScreenButton_Click(null, null);
                        }
                        break;
                    case "Delete Playlist Item":
                        if (_playlistView != null)
                        {
                            RemovePlaylistButton_Click(null, null);
                        }
                        break;
                    case "Toggle Notes":
                        if (_notesView != null)
                        {
                            NotesButton_Click(null, null);
                        }
                        break;
                    case "Add New Note":
                        if (_notesView != null && _notesView.IsVisible && !_notesView.IsDisabledForImageSequence())
                        {
                            Dispatcher.UIThread.InvokeAsync(() => _notesView.AddNewNote());
                        }
                        break;
                    case "Progressive Backward Speed (Key Hold)":
                        if (!isAdjustingSpeed)
                        {
                            isAdjustingSpeed = true;
                            StartSpeedTimer(isBackward: true);
                        }
                        break;
                    case "Progressive Forward Speed (Key Hold)":
                        if (!isAdjustingSpeed)
                        {
                            isAdjustingSpeed = true;
                            StartSpeedTimer(isBackward: false);
                        }
                        break;
                }
                e.Handled = true;
            }
        }

        private void MainWindow_KeyUp(object? sender, KeyEventArgs e)
        {
            var keyBinding = KeyBindings.FirstOrDefault(kb => kb.Key.Equals(e.Key.ToString(), StringComparison.OrdinalIgnoreCase));
            if (keyBinding != null &&
                (keyBinding.Bindings == "Progressive Backward Speed (Key Hold)" || keyBinding.Bindings == "Progressive Forward Speed (Key Hold)"))
            {
                StopSpeedTimer(); // Stop the progressive speed timer

                if (originalPausedState != null)
                {
                    MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", originalPausedState });
                    Debug.WriteLine($"Restored original paused state: {originalPausedState}");
                    originalPausedState = null; // Clear the saved state
                }

                Debug.WriteLine("Ensured MPV is in a normal playback state.");

                isAdjustingSpeed = false; // Allow new speed adjustments
                e.Handled = true;
            }
        }

        private void StartSpeedTimer(bool isBackward)
        {
            currentSpeedIndex = 1; // Start at the slowest speed (1 frame)

            var currentPausedState = MPVInterop.GetStringProperty(mpvHandle, "pause");
            if (originalPausedState == null) // Only save once
            {
                originalPausedState = currentPausedState;
                Debug.WriteLine($"Saved original paused state: {originalPausedState}");
            }

            _speedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20) // Adjust seek position every 100ms
            };

            _speedTimer.Tick += (s, e) =>
            {
                // Check if MPV is paused, and resume playback if necessary
                var isPaused = MPVInterop.GetStringProperty(mpvHandle, "pause");
                if (isPaused == "no")
                {
                    Debug.WriteLine("MPV is paused. Triggering PlayButton_Click to resume playback.");
                    PlayButton_Click(null, null); // Pause playback
                }

                // Cap speed at the maximum value
                if (currentSpeedIndex < SeekSteps.Length - 1)
                {
                    currentSpeedIndex++;
                }

                var seekStep = SeekSteps[currentSpeedIndex] * (isBackward ? -1 : 1); // Negative for backward
                MPVInterop.mpv_command(mpvHandle, new[] { "seek", seekStep.ToString("F2"), "relative" });

                Debug.WriteLine($"SeekRelative: {(isBackward ? "Backward" : "Forward")} at step {seekStep} seconds.");
            };

            _speedTimer.Start();
        }

        private void StopSpeedTimer()
        {
            _speedTimer?.Stop();
            _speedTimer = null;
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
                            if (!_isLoadingVideo && _currentVideoInitialized &&
                                endFile.reason == MPVInterop.mpv_end_file_reason.MPV_END_FILE_REASON_EOF)
                            {
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    // Only advance if playlist view exists and playlist mode is active
                                    if (_playlistView?.IsPlaylistModeActive == true)
                                    {
                                        _playlistView.PlayNext();
                                    }
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

        // Playback Controls

        private void PlaybackSlider_Tapped(object? sender, TappedEventArgs e)
        {
            if (overlayImage.IsVisible)
            {
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                // Get the clicked position and seek to it
                if (sender is Slider slider)
                {
                    var point = e.GetPosition(slider);
                    var ratio = point.X / slider.Bounds.Width;
                    var position = ratio * slider.Maximum;
                    SeekToPosition(position);
                }
            }
        }

        private void PlaybackSlider_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            isSliderDragging = true;
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
            if (overlayImage.IsVisible)
            {
                overlayImage.IsVisible = false;
                overlayImage.Source = null;  
                ShowMpvWindow();
            }
        }

        private void PlaybackSlider_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            if (!isSliderDragging) return;
            isSliderDragging = false;
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
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
                if (_notesView != null)
                {
                    _notesView.EnableNotes();
                }

                //await Dispatcher.UIThread.InvokeAsync(() => {
                //    ProcessingProgressBar.Value = 0;
                //});
                ChangeCancelButtonBackground();

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
                        //Debug.WriteLine("Successfully started playback of new video");
                        _ = UpdatePlayPauseIcon();
                        _ = UpdatePlayState();
                        EnsureCorrectWindowOrder(); 
                    }
                }
                else
                {
                    //Debug.WriteLine("Failed to initialize duration");
                }
                if (_notesView != null && _notesView.IsVisible)
                {
                    await _notesView.LoadExistingNotes();
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

        public async Task WaitForFileLoadedAndSeek()
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

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);


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

                SetMpvOption("terminal", "yes");
                SetMpvOption("msg-level", "all=v");
                SetMpvOption("alpha", "auto");
                SetMpvOption("background", "tiles");
                SetMpvOption("background-color", "#171717");
                //SetMpvOption("gpu-clear-color", "18/18/18/255");
                SetMpvOption("gpu-api", "d3d11");
                SetMpvOption("opengl-rectangle-textures", "yes");
                SetMpvOption("vid-end-pause", "yes");
                SetMpvOption("demuxer-readahead-secs", "0.1");
                SetMpvOption("screenshot-high-bit-depth", "yes");
                SetMpvOption("screenshot-jpeg-quality", "75");
                SetMpvOption("tone-mapping", "off");
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
                SetMpvOption("volume", "80");
                SetMpvOption("hwdec", "auto");
                SetMpvOption("cache", "yes");
                SetMpvOption("cache-secs", "10");


                //Debug.WriteLine("MPV options set successfully");

                int result = MPVInterop.mpv_initialize(mpvHandle);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to initialize MPV with error code: {result}");
                    throw new Exception($"Failed to initialize MPV: {result}");
                }

                result = MPVInterop.mpv_request_event(mpvHandle, MPVInterop.mpv_event_id.MPV_EVENT_END_FILE, 1);
                if (result < 0)
                {
                    //Debug.WriteLine($"Failed to register for end file events: {MPVInterop.GetError(result)}");
                }
                else
                {
                    //Debug.WriteLine("Successfully registered for end file events");
                }

                result = MPVInterop.mpv_observe_property(mpvHandle, 1, "time-pos", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
                result = MPVInterop.mpv_observe_property(mpvHandle, 2, "duration", MPVInterop.mpv_format.MPV_FORMAT_DOUBLE);
                result = MPVInterop.mpv_observe_property(mpvHandle, 3, "pause", MPVInterop.mpv_format.MPV_FORMAT_FLAG);

                StartEventLoop();

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

                            Dispatcher.UIThread.Post(() =>
                            {
                                var hwnd = GetParentWindowHandle();
                                if (hwnd != IntPtr.Zero)
                                {
                                    SetForegroundWindow(hwnd);
                                    SetFocus(hwnd);
                                }
                            });
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
                    var scaling = window.RenderScaling;

                    if (_mpvHidden)
                    {
                        MoveWindow(childWindowHandle, 0, 0, 1, 1, true);
                        return;
                    }

                    var containerBounds = videoContainer.Bounds;
                    var containerPosition = videoContainer.TranslatePoint(new Avalonia.Point(0, 0), window);

                    if (containerPosition.HasValue)
                    {
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
