using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using DynamicData;
using Avalonia.Input;
using System.Diagnostics;
using Avalonia.Threading;
using UnionMpvPlayer.Helpers;
using System.Text.Json.Serialization;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using System.Text;

namespace UnionMpvPlayer.Views
{
    public partial class NotesView : UserControl
    {
        private bool _isDisabledForImageSequence = false;
        private EventHandler? _playbackStartedHandler;
        private bool _isOverlayActive = false;
        private double _lastImageTimecode = 0.0;

        public class NoteItem : INotifyPropertyChanged
        {
            [JsonPropertyName("TimecodeString")]
            public string TimecodeString { get; set; }

            [JsonPropertyName("Timecode")]
            public double Timecode { get; set; }

            [JsonPropertyName("ImagePath")]
            public string ImagePath { get; set; }

            [JsonPropertyName("EditedImagePath")]
            public string? EditedImagePath { get; set; }

            private string _notes = "";
            [JsonPropertyName("Notes")]
            public string Notes
            {
                get => _notes;
                set
                {
                    if (_notes != value)
                    {
                        _notes = value;
                        OnPropertyChanged(nameof(Notes));
                        SaveChanges().ConfigureAwait(false);
                    }
                }
            }

            [JsonPropertyName("Created")]
            public DateTime Created { get; set; }

            [JsonIgnore]
            protected string _jsonPath;

            [JsonIgnore]
            private Bitmap? _image;

            [JsonIgnore]
            public Bitmap? Image
            {
                get
                {
                    if (_image == null)
                    {
                        string pathToUse = !string.IsNullOrEmpty(EditedImagePath) && File.Exists(EditedImagePath)
                            ? EditedImagePath
                            : ImagePath;
                        try
                        {
                            _image = new Bitmap(pathToUse);
                        }
                        catch (Exception ex)
                        {
                            //Debug.WriteLine($"Error loading image: {ex.Message}");
                        }
                    }
                    return _image;
                }
            }

            public void RefreshImage()
            {
                _image = null;
                OnPropertyChanged(nameof(Image));
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            public string GetJsonPath()
            {
                return _jsonPath;
            }

            public void SetJsonPath(string path)
            {
                _jsonPath = path;
                //Debug.WriteLine($"Set JSON path to: {path}");
            }

            public async Task SaveChanges()
            {
                if (!string.IsNullOrEmpty(_jsonPath))
                {
                    try
                    {
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true
                        };
                        var saveData = new
                        {
                            TimecodeString,
                            Timecode,
                            ImagePath,
                            EditedImagePath,
                            Notes = _notes,
                            Created
                        };
                        var json = JsonSerializer.Serialize(saveData, options);
                        await File.WriteAllTextAsync(_jsonPath, json);
                        //Debug.WriteLine($"Saved changes to: {_jsonPath}");
                        //Debug.WriteLine($"EditedImagePath: {EditedImagePath}");
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error saving note changes: {ex.Message}");
                    }
                }
            }
        }

        private MainWindow? _mainWindow;
        private ObservableCollection<NoteItem> _notes;
        private string? _currentVideoPath;
        public event EventHandler<EventArgs>? NotesToggleClicked;


        public NotesView()
        {
            InitializeComponent();
            _notes = new ObservableCollection<NoteItem>();
            if (NotesItemsControl != null)
            {
                NotesItemsControl.ItemsSource = _notes;
            }
        }

        public bool IsEditingNote()
        {
            var focusManager = TopLevel.GetTopLevel(this)?.FocusManager;
            if (focusManager != null)
            {
                var focusedElement = focusManager.GetFocusedElement();
                return focusedElement is TextBox && IsVisible;
            }
            return false;
        }

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public async Task LoadExistingNotes()
        {
            if (_isDisabledForImageSequence)
            {
                _notes.Clear();
                return;
            }

            if (_mainWindow == null) return;

            _currentVideoPath = _mainWindow.GetCurrentVideoPath();
            if (_currentVideoPath != null)
            {
                await LoadNotes(_currentVideoPath);
            }
        }

        private void NotesToggle_Click(object? sender, RoutedEventArgs e)
        {
            NotesToggleClicked?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateToggleButtonIcon(bool isVisible)
        {
            if (isVisible)
            {
                _ = LoadExistingNotes();
            }

            if (NotesToggle?.Content is Avalonia.Controls.Shapes.Path notesPath)
            {
                var iconKey = isVisible ? "list_regular" : "text_align_right_regular";
                if (Application.Current?.Resources.TryGetResource(iconKey,
                    ThemeVariant.Default, out object? resource) == true &&
                    resource is StreamGeometry geometry)
                {
                    notesPath.Data = geometry;
                }
            }
        }
        private string GetNotesDirectory(string videoPath)
        {
            var videoDir = Path.GetDirectoryName(videoPath);
            var videoName = Path.GetFileNameWithoutExtension(videoPath);
            var umpvDir = Path.Combine(videoDir, ".umpv");
            var videoNotesDir = Path.Combine(umpvDir, videoName);
            var imagesDir = Path.Combine(videoNotesDir, "images");

            // Create directories if they don't exist
            Directory.CreateDirectory(umpvDir);
            File.SetAttributes(umpvDir, File.GetAttributes(umpvDir) | FileAttributes.Hidden);
            Directory.CreateDirectory(videoNotesDir);
            Directory.CreateDirectory(imagesDir);

            return videoNotesDir;
        }

        private async Task AddNote(string videoPath, double currentTimecode, string timecodeString)
        {
            if (_mainWindow == null) return;

            var notesDir = GetNotesDirectory(videoPath);
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var imageFileName = $"frame_{timestamp}.png";
            var imagePath = Path.Combine(notesDir, "images", imageFileName);

            await _mainWindow.CaptureScreenshot(imagePath);

            var jsonFileName = $"note_{timestamp}.json";
            var jsonPath = Path.Combine(notesDir, jsonFileName);

            var noteItem = new NoteItem
            {
                TimecodeString = timecodeString,
                Timecode = currentTimecode,
                ImagePath = imagePath,
                Notes = "",
                Created = DateTime.Now
            };

            noteItem.SetJsonPath(jsonPath);
            var jsonContent = System.Text.Json.JsonSerializer.Serialize(noteItem);
            await File.WriteAllTextAsync(jsonPath, jsonContent);
            await LoadNotes(videoPath);
        }

        private async Task LoadNotes(string videoPath)
        {
            var notesDir = GetNotesDirectory(videoPath);
            var noteFiles = Directory.GetFiles(notesDir, "note_*.json");
            var notes = new List<NoteItem>();

            foreach (var file in noteFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var note = System.Text.Json.JsonSerializer.Deserialize<NoteItem>(json);
                    if (note != null)
                    {
                        note.SetJsonPath(file);
                        notes.Add(note);
                    }
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error loading note {file}: {ex.Message}");
                }
            }

            notes = notes.OrderBy(n => n.Timecode).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateNotesDisplay(notes);
            });
        }

        public async Task AddNewNote()
        {
            if (_isDisabledForImageSequence)
                return;

            if (MainWindow.Current?.GetCurrentVideoPath() is string videoPath)
            {
                var currentTimecode = MainWindow.Current.GetCurrentTimecode();
                var timecodeString = MainWindow.Current.GetCurrentTimecodeString();
                await AddNote(videoPath, currentTimecode, timecodeString);
            }
        }

        private async void AddToNotesButton_Click(object sender, RoutedEventArgs e)
        {
            await AddNewNote();
        }

        private void UpdateNotesDisplay(List<NoteItem> notes)
        {
            _notes.Clear();
            foreach (var note in notes)
            {
                _notes.Add(note);
            }
        }
        private async void TimecodeTextBlock_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isDisabledForImageSequence)
                return;

            if (sender is TextBlock textBlock && textBlock.DataContext is NoteItem noteItem)
            {
                if (_mainWindow?.GetMpvHandle() is IntPtr mpvHandle)
                {
                    var pauseArgs = new[] { "set", "pause", "yes" };
                    MPVInterop.mpv_command(mpvHandle, pauseArgs);

                    var timeStr = noteItem.Timecode.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    var seekArgs = new[] { "seek", timeStr, "absolute" };
                    MPVInterop.mpv_command(mpvHandle, seekArgs);
                    var imagePath = !string.IsNullOrEmpty(noteItem.EditedImagePath) ?
                        noteItem.EditedImagePath : noteItem.ImagePath;
                    ApplyImageOverlay(imagePath, true);

                    await _mainWindow.UpdatePlayPauseIcon();

                    if (_playbackStartedHandler != null)
                    {
                        _mainWindow.PlaybackStarted -= _playbackStartedHandler;
                    }

                    _playbackStartedHandler = (s, args) =>
                    {
                        ApplyImageOverlay(imagePath, false);
                    };
                    _mainWindow.PlaybackStarted += _playbackStartedHandler;


                }
            }
        }
        private async void Image_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isDisabledForImageSequence)
                return;

            if (sender is Border border && border.DataContext is NoteItem noteItem)
            {
                //Debug.WriteLine("Image_PointerPressed triggered");

                if (_mainWindow?.GetMpvHandle() is IntPtr mpvHandle)
                {
                    try
                    {

                        var pauseArgs = new[] { "set", "pause", "yes" };
                        int pauseResult = MPVInterop.mpv_command(mpvHandle, pauseArgs);
                        //Debug.WriteLine($"Pause result: {pauseResult}");

                        var timeStr = noteItem.Timecode.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                        var seekArgs = new[] { "seek", timeStr, "absolute" };
                        int seekResult = MPVInterop.mpv_command(mpvHandle, seekArgs);
                        //Debug.WriteLine($"Seek result: {seekResult}");

                        var imagePath = !string.IsNullOrEmpty(noteItem.EditedImagePath) ?
                            noteItem.EditedImagePath : noteItem.ImagePath;

                        //Debug.WriteLine($"Using image path: {imagePath}");

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            ApplyImageOverlay(imagePath, true);
                        });

                        await _mainWindow.UpdatePlayPauseIcon();

                        if (_playbackStartedHandler != null)
                        {
                            //Debug.WriteLine("Removing existing playback handler");
                            _mainWindow.PlaybackStarted -= _playbackStartedHandler;
                        }

                        _playbackStartedHandler = (s, args) =>
                        {
                            //Debug.WriteLine("PlaybackStarted event triggered");
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                ApplyImageOverlay(imagePath, false);
                            });
                        };

                        _mainWindow.PlaybackStarted += _playbackStartedHandler;
                        //Debug.WriteLine("Added new playback handler");
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error in Image_PointerPressed: {ex.Message}\nStack trace: {ex.StackTrace}");
                    }
                }
            }
        }

        private async void ClearNotesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDisabledForImageSequence)
                return;

            if (_currentVideoPath == null) return;

            var popup = new ClearNotesPopup();
            popup.ConfirmClicked += async (s, args) =>
            {
                try
                {
                    var notesDir = GetNotesDirectory(_currentVideoPath);
                    if (Directory.Exists(notesDir))
                    {

                        var imagesDir = Path.Combine(notesDir, "images");
                        if (Directory.Exists(imagesDir))
                        {
                            Directory.Delete(imagesDir, true);
                        }

                        foreach (var file in Directory.GetFiles(notesDir, "note_*.json"))
                        {
                            File.Delete(file);
                        }

                        Directory.Delete(notesDir, true);
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _notes.Clear();
                    });
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error clearing notes: {ex.Message}");
                }
            };

            await popup.ShowDialog((Window)TopLevel.GetTopLevel(this));
        }

        private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is NoteItem noteItem)
            {
                try
                {

                    string jsonPath = noteItem.GetJsonPath();
                    if (File.Exists(jsonPath))
                    {
                        File.Delete(jsonPath);

                        if (File.Exists(noteItem.ImagePath))
                        {
                            File.Delete(noteItem.ImagePath);
                        }

                        _notes.Remove(noteItem);
                    }
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"Error deleting note: {ex.Message}");
                }
            }
        }
        public void DisableForImageSequence()
        {
            //Debug.WriteLine("DisableForImageSequence called");
            _isDisabledForImageSequence = true;

            _notes.Clear();

            //Debug.WriteLine($"AddToNotesButton is null: {AddToNotesButton == null}");
            //Debug.WriteLine($"ClearNotesButton is null: {ClearNotesButton == null}");
            //Debug.WriteLine($"NotesItemsControl is null: {NotesItemsControl == null}");
            //Debug.WriteLine($"NotesPanel is null: {NotesPanel == null}");

            // Disable UI elements
            if (AddToNotesButton != null)
            {
                AddToNotesButton.IsEnabled = false;
                //Debug.WriteLine("Disabled AddToNotesButton");
            }
            if (ClearNotesButton != null)
            {
                ClearNotesButton.IsEnabled = false;
                //Debug.WriteLine("Disabled ClearNotesButton");
            }
            if (NotesItemsControl != null)
            {
                NotesItemsControl.IsEnabled = false;
                //Debug.WriteLine("Disabled NotesItemsControl");
            }

            if (NotesInstructions != null)
            {
                NotesInstructions.Text = "Notes are not available for image sequences";
                //Debug.WriteLine("Updated NotesInstructions text");
            }
        }

        public void EnableNotes()
        {
            _isDisabledForImageSequence = false;

            if (AddToNotesButton != null)
                AddToNotesButton.IsEnabled = true;
            if (ClearNotesButton != null)
                ClearNotesButton.IsEnabled = true;
            if (NotesItemsControl != null)
                NotesItemsControl.IsEnabled = true;

            if (NotesPanel != null)
            {
                var messageToRemove = NotesPanel.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb => tb.Text == "Notes are not available for image sequences");
                if (messageToRemove != null)
                {
                    NotesPanel.Children.Remove(messageToRemove);
                }
            }

            if (NotesInstructions != null)
            {
                NotesInstructions.Text = "Export Options";
                // Debug.WriteLine("Updated NotesInstructions text");
            }
        }

        public bool IsDisabledForImageSequence()
        {
            return _isDisabledForImageSequence;
        }

        private async void EditImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is NoteItem noteItem)
            {
                var mpvHandle = MainWindow.Current?.GetMpvHandle();
                bool wasPlaying = false;

                if (mpvHandle.HasValue)
                {
                    var isPaused = (MPVInterop.GetStringProperty(mpvHandle.Value, "pause") == "yes");
                    wasPlaying = !isPaused;

                    if (wasPlaying)
                    {
                        MPVInterop.mpv_command(mpvHandle.Value, new[] { "set", "pause", "yes" });
                        await MainWindow.Current.UpdatePlayPauseIcon();
                    }
                }

                var editWindow = new ImageEditWindow(noteItem.ImagePath, noteItem.EditedImagePath)
                {
                    DataContext = noteItem
                };

                editWindow.ImageEdited += (s, args) =>
                {
                    noteItem.RefreshImage();
                };

                await editWindow.ShowDialog((Window)TopLevel.GetTopLevel(this));

                if (string.IsNullOrEmpty(noteItem.EditedImagePath))
                {
                    ApplyImageOverlay(noteItem.ImagePath, true);
                }
                else
                {
                    ApplyImageOverlay(noteItem.EditedImagePath, true);
                }
            }
        }

        private async void EditImageContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is NoteItem noteItem)
            {
                var mpvHandle = MainWindow.Current?.GetMpvHandle();
                bool wasPlaying = false;

                if (mpvHandle.HasValue)
                {
                    var isPaused = (MPVInterop.GetStringProperty(mpvHandle.Value, "pause") == "yes");
                    wasPlaying = !isPaused;

                    if (wasPlaying)
                    {
                        MPVInterop.mpv_command(mpvHandle.Value, new[] { "set", "pause", "yes" });
                        await MainWindow.Current.UpdatePlayPauseIcon();
                    }
                }

                var editWindow = new ImageEditWindow(noteItem.ImagePath, noteItem.EditedImagePath)
                {
                    DataContext = noteItem
                };
                editWindow.ImageEdited += (s, args) =>
                {
                    noteItem.RefreshImage();
                };

                await editWindow.ShowDialog((Window)TopLevel.GetTopLevel(this));

                if (string.IsNullOrEmpty(noteItem.EditedImagePath))
                {
                    ApplyImageOverlay(noteItem.ImagePath, true);
                }
                else
                {
                    ApplyImageOverlay(noteItem.EditedImagePath, true);
                }
            }
        }

        public void ApplyImageOverlay(string? imagePath, bool apply)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (apply && !string.IsNullOrEmpty(imagePath))
                    {
                        MainWindow.Current.HideMpvWindow();
                        MainWindow.Current.overlayImage.Source = new Bitmap(imagePath);
                        MainWindow.Current.overlayImage.IsVisible = true;
                    }
                    else
                    {
                        MainWindow.Current.overlayImage.IsVisible = false;
                        MainWindow.Current.overlayImage.Source = null;  // Clear the source
                        MainWindow.Current.ShowMpvWindow();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyImageOverlay: {ex.Message}\nStack trace: {ex.StackTrace}");
            }
        }

        public bool IsOverlayActive()
        {
            return _isOverlayActive;
        }

        public double GetLastImageTimecode()
        {
            return _lastImageTimecode;
        }

        // Exports

        private string FormatPathForXML(string path)
        {
            // Check if this is a network path
            if (path.StartsWith("\\\\"))
            {
                // Network path - ensure it starts with exactly two backslashes
                string networkPath = path.TrimStart('\\');
                path = "\\\\" + networkPath;
            }

            // Convert backslashes to forward slashes for XML
            path = path.Replace("\\", "/");

            // URL encode the path properly but keep slashes as-is
            path = Uri.EscapeDataString(path).Replace("%2F", "/").Replace("%3A", ":");

            return path;
        }

        private async Task ExportForPremierePro(string exportPath)
        {
            if (string.IsNullOrEmpty(_currentVideoPath) || _notes.Count == 0)
                return;

            var videoName = Path.GetFileNameWithoutExtension(_currentVideoPath);

            // Get video properties from MPV
            double videoFps = GetVideoFrameRate() ?? 30.0;
            // Round FPS to a more standard representation if needed
            videoFps = Math.Round(videoFps * 1000) / 1000;
            bool isNtsc = Math.Abs(videoFps - 23.976) < 0.01 || Math.Abs(videoFps - 29.97) < 0.01;
            // If it's 23.976 or 29.97, make sure we use the exact value Premiere expects
            if (Math.Abs(videoFps - 23.976) < 0.01) videoFps = 24;
            double videoDuration = GetVideoDuration() ?? 60.0;
            (int width, int height) = GetVideoResolution() ?? (1920, 1080);

            // Calculate total frame count
            int totalFrames = (int)Math.Ceiling(videoDuration * videoFps);

            // Create images folder
            var imagesFolder = Path.Combine(
                Path.GetDirectoryName(exportPath),
                $"{Path.GetFileNameWithoutExtension(exportPath)}_images"
            );

            Directory.CreateDirectory(imagesFolder);

            // Copy all images to the new folder
            var imagePathMapping = new Dictionary<string, string>();

            foreach (var note in _notes)
            {
                string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                    ? note.EditedImagePath
                    : note.ImagePath;

                if (File.Exists(sourcePath))
                {
                    var destFileName = $"frame_{note.Timecode.ToString("F3", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "_")}{Path.GetExtension(sourcePath)}";
                    var destPath = Path.Combine(imagesFolder, destFileName);

                    File.Copy(sourcePath, destPath, true);
                    imagePathMapping.Add(sourcePath, destPath);
                }
            }

            // Generate a unique UUID for the sequence
            string uuid = Guid.NewGuid().ToString();

            // Generate XML content
            var xml = new StringBuilder();

            // XML header and project settings
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.AppendLine("<!DOCTYPE xmeml>");
            xml.AppendLine("<xmeml version=\"4\">");
            xml.AppendLine($"\t<sequence id=\"sequence-1\" TL.SQAudioVisibleBase=\"0\" TL.SQVideoVisibleBase=\"0\" TL.SQVisibleBaseTime=\"0\" TL.SQAVDividerPosition=\"0.5\" TL.SQHideShyTracks=\"0\" TL.SQHeaderWidth=\"204\" explodedTracks=\"true\">");
            xml.AppendLine($"\t\t<uuid>{uuid}</uuid>");
            xml.AppendLine($"\t\t<duration>{totalFrames}</duration>");
            xml.AppendLine("\t\t<rate>");
            xml.AppendLine($"\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t</rate>");
            xml.AppendLine($"\t\t<name>{videoName}</name>");

            // Add media
            xml.AppendLine("\t\t<media>");

            // Add video
            xml.AppendLine("\t\t\t<video>");
            xml.AppendLine("\t\t\t\t<format>");
            xml.AppendLine("\t\t\t\t\t<samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t<rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t\t\t\t\t</rate>");
            xml.AppendLine($"\t\t\t\t\t\t<width>{width}</width>");
            xml.AppendLine($"\t\t\t\t\t\t<height>{height}</height>");
            xml.AppendLine("\t\t\t\t\t\t<anamorphic>FALSE</anamorphic>");
            xml.AppendLine("\t\t\t\t\t\t<pixelaspectratio>square</pixelaspectratio>");
            xml.AppendLine("\t\t\t\t\t\t<fielddominance>none</fielddominance>");
            xml.AppendLine("\t\t\t\t\t</samplecharacteristics>");
            xml.AppendLine("\t\t\t\t</format>");

            // Add video track 1 (main video)
            xml.AppendLine("\t\t\t\t<track TL.SQTrackShy=\"0\" TL.SQTrackExpandedHeight=\"41\" TL.SQTrackExpanded=\"0\" MZ.TrackTargeted=\"1\">");

            // For the main video file path
            string safeVideoPath = FormatPathForXML(_currentVideoPath);

            string videoFileName = Path.GetFileName(_currentVideoPath);
            xml.AppendLine("\t\t\t\t\t<clipitem id=\"clipitem-1\">");
            xml.AppendLine("\t\t\t\t\t\t<masterclipid>masterclip-1</masterclipid>");
            xml.AppendLine($"\t\t\t\t\t\t<name>{videoFileName}</name>");
            xml.AppendLine("\t\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine($"\t\t\t\t\t\t<duration>{totalFrames}</duration>");
            xml.AppendLine("\t\t\t\t\t\t<rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t\t\t\t\t</rate>");
            xml.AppendLine("\t\t\t\t\t\t<start>0</start>");
            xml.AppendLine($"\t\t\t\t\t\t<end>{totalFrames}</end>");
            xml.AppendLine("\t\t\t\t\t\t<in>0</in>");
            xml.AppendLine($"\t\t\t\t\t\t<out>{totalFrames}</out>");
            xml.AppendLine("\t\t\t\t\t\t<pproTicksIn>0</pproTicksIn>");
            xml.AppendLine($"\t\t\t\t\t\t<pproTicksOut>{totalFrames * 254016000000}</pproTicksOut>");
            xml.AppendLine("\t\t\t\t\t\t<alphatype>none</alphatype>");
            xml.AppendLine("\t\t\t\t\t\t<pixelaspectratio>square</pixelaspectratio>");
            xml.AppendLine("\t\t\t\t\t\t<anamorphic>FALSE</anamorphic>");

            // Add file reference for the video
            xml.AppendLine("\t\t\t\t\t\t<file id=\"file-1\">");
            xml.AppendLine($"\t\t\t\t\t\t\t<name>{videoFileName}</name>");
            xml.AppendLine($"\t\t\t\t\t\t\t<pathurl>{safeVideoPath}</pathurl>");
            xml.AppendLine("\t\t\t\t\t\t\t<rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t\t\t\t\t\t</rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t<duration>{totalFrames}</duration>");
            xml.AppendLine("\t\t\t\t\t\t\t<media>");
            xml.AppendLine("\t\t\t\t\t\t\t\t<video>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t<samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t</rate>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t<width>{width}</width>");
            xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t<height>{height}</height>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<anamorphic>FALSE</anamorphic>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<pixelaspectratio>square</pixelaspectratio>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<fielddominance>none</fielddominance>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t</samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t\t\t</video>");

            // Basic audio definition is needed even if we don't have audio
            xml.AppendLine("\t\t\t\t\t\t\t\t<audio>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t<samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<depth>16</depth>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<samplerate>48000</samplerate>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t</samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t\t\t\t<channelcount>2</channelcount>");
            xml.AppendLine("\t\t\t\t\t\t\t\t</audio>");
            xml.AppendLine("\t\t\t\t\t\t\t</media>");
            xml.AppendLine("\t\t\t\t\t\t</file>");

            // Add basic link references which Premiere Pro needs
            xml.AppendLine("\t\t\t\t\t\t<link>");
            xml.AppendLine("\t\t\t\t\t\t\t<linkclipref>clipitem-1</linkclipref>");
            xml.AppendLine("\t\t\t\t\t\t\t<mediatype>video</mediatype>");
            xml.AppendLine("\t\t\t\t\t\t\t<trackindex>1</trackindex>");
            xml.AppendLine("\t\t\t\t\t\t\t<clipindex>1</clipindex>");
            xml.AppendLine("\t\t\t\t\t\t</link>");

            // Close main clip
            xml.AppendLine("\t\t\t\t\t</clipitem>");

            // Close main track
            xml.AppendLine("\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine("\t\t\t\t\t<locked>FALSE</locked>");
            xml.AppendLine("\t\t\t\t</track>");

            // Add a second track for still images
            xml.AppendLine("\t\t\t\t<track TL.SQTrackShy=\"0\" TL.SQTrackExpandedHeight=\"41\" TL.SQTrackExpanded=\"0\" MZ.TrackTargeted=\"0\">");

            // Add still images as separate clipitems
            int clipIndex = 2;
            int fileIndex = 2;
            int masterClipIndex = 2;

            foreach (var note in _notes)
            {
                string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                    ? note.EditedImagePath
                    : note.ImagePath;

                if (imagePathMapping.TryGetValue(sourcePath, out string destPath))
                {
                    int frameNumber = (int)(note.Timecode * videoFps);
                    int duration = 10; // 10 frames duration for stills

                    string safeImagePath = destPath.Replace("\\", "/");
                    if (safeImagePath.StartsWith("/"))
                    {
                        safeImagePath = safeImagePath.Substring(1);
                    }
                    // URL encode the path properly
                    safeImagePath = Uri.EscapeDataString(safeImagePath).Replace("%2F", "/").Replace("%3A", ":");

                    string fileName = Path.GetFileName(destPath);

                    xml.AppendLine($"\t\t\t\t\t<clipitem id=\"clipitem-{clipIndex}\">");
                    xml.AppendLine($"\t\t\t\t\t\t<masterclipid>masterclip-{masterClipIndex}</masterclipid>");
                    xml.AppendLine($"\t\t\t\t\t\t<name>{fileName}</name>");
                    xml.AppendLine("\t\t\t\t\t\t<enabled>TRUE</enabled>");
                    xml.AppendLine("\t\t\t\t\t\t<duration>1035764</duration>"); // Use a large duration like in the example
                    xml.AppendLine("\t\t\t\t\t\t<rate>");
                    xml.AppendLine($"\t\t\t\t\t\t\t<timebase>{videoFps}</timebase>");
                    xml.AppendLine($"\t\t\t\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
                    xml.AppendLine("\t\t\t\t\t\t</rate>");
                    xml.AppendLine($"\t\t\t\t\t\t<start>{frameNumber}</start>");
                    xml.AppendLine($"\t\t\t\t\t\t<end>{frameNumber + duration}</end>");
                    xml.AppendLine("\t\t\t\t\t\t<in>86313</in>"); // Use values from example
                    xml.AppendLine("\t\t\t\t\t\t<out>86432</out>"); // Use values from example
                    xml.AppendLine("\t\t\t\t\t\t<pproTicksIn>914450328792000</pproTicksIn>"); // Use values from example
                    xml.AppendLine("\t\t\t\t\t\t<pproTicksOut>915711084288000</pproTicksOut>"); // Use values from example
                    xml.AppendLine("\t\t\t\t\t\t<alphatype>straight</alphatype>");
                    xml.AppendLine("\t\t\t\t\t\t<pixelaspectratio>square</pixelaspectratio>");
                    xml.AppendLine("\t\t\t\t\t\t<anamorphic>FALSE</anamorphic>");

                    // Add file reference
                    xml.AppendLine($"\t\t\t\t\t\t<file id=\"file-{fileIndex}\">");
                    xml.AppendLine($"\t\t\t\t\t\t\t<name>{fileName}</name>");
                    xml.AppendLine($"\t\t\t\t\t\t\t<pathurl>{safeImagePath}</pathurl>");
                    xml.AppendLine("\t\t\t\t\t\t\t<rate>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<timebase>30</timebase>"); // Images often use 30fps in Premiere
                    xml.AppendLine("\t\t\t\t\t\t\t\t<ntsc>TRUE</ntsc>");
                    xml.AppendLine("\t\t\t\t\t\t\t</rate>");
                    xml.AppendLine("\t\t\t\t\t\t\t<timecode>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<rate>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t<timebase>30</timebase>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t<ntsc>TRUE</ntsc>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t</rate>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<string>00;00;00;00</string>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<frame>0</frame>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<displayformat>DF</displayformat>");
                    xml.AppendLine("\t\t\t\t\t\t\t</timecode>");
                    xml.AppendLine("\t\t\t\t\t\t\t<media>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t<video>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t<samplecharacteristics>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<rate>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t\t<timebase>30</timebase>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t\t<ntsc>TRUE</ntsc>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t</rate>");
                    xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t<width>{width}</width>");
                    xml.AppendLine($"\t\t\t\t\t\t\t\t\t\t<height>{height}</height>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<anamorphic>FALSE</anamorphic>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<pixelaspectratio>square</pixelaspectratio>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t\t<fielddominance>none</fielddominance>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t\t</samplecharacteristics>");
                    xml.AppendLine("\t\t\t\t\t\t\t\t</video>");
                    xml.AppendLine("\t\t\t\t\t\t\t</media>");
                    xml.AppendLine("\t\t\t\t\t\t</file>");

                    // Add logginginfo as in the example
                    xml.AppendLine("\t\t\t\t\t\t<logginginfo>");
                    xml.AppendLine("\t\t\t\t\t\t\t<description></description>");
                    xml.AppendLine("\t\t\t\t\t\t\t<scene></scene>");
                    xml.AppendLine("\t\t\t\t\t\t\t<shottake></shottake>");
                    xml.AppendLine("\t\t\t\t\t\t\t<lognote></lognote>");
                    xml.AppendLine("\t\t\t\t\t\t\t<good></good>");
                    xml.AppendLine("\t\t\t\t\t\t\t<originalvideofilename></originalvideofilename>");
                    xml.AppendLine("\t\t\t\t\t\t\t<originalaudiofilename></originalaudiofilename>");
                    xml.AppendLine("\t\t\t\t\t\t</logginginfo>");
                    xml.AppendLine("\t\t\t\t\t\t<colorinfo>");
                    xml.AppendLine("\t\t\t\t\t\t\t<lut></lut>");
                    xml.AppendLine("\t\t\t\t\t\t\t<lut1></lut1>");
                    xml.AppendLine("\t\t\t\t\t\t\t<asc_sop></asc_sop>");
                    xml.AppendLine("\t\t\t\t\t\t\t<asc_sat></asc_sat>");
                    xml.AppendLine("\t\t\t\t\t\t\t<lut2></lut2>");
                    xml.AppendLine("\t\t\t\t\t\t</colorinfo>");
                    xml.AppendLine("\t\t\t\t\t\t<labels>");
                    xml.AppendLine("\t\t\t\t\t\t</labels>");
                    xml.AppendLine("\t\t\t\t\t</clipitem>");

                    clipIndex++;
                    fileIndex++;
                    masterClipIndex++;
                }
            }

            // Close image track
            xml.AppendLine("\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine("\t\t\t\t\t<locked>FALSE</locked>");
            xml.AppendLine("\t\t\t\t</track>");

            // Add an empty track as in the example
            xml.AppendLine("\t\t\t\t<track TL.SQTrackShy=\"0\" TL.SQTrackExpandedHeight=\"41\" TL.SQTrackExpanded=\"0\" MZ.TrackTargeted=\"0\">");
            xml.AppendLine("\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine("\t\t\t\t\t<locked>FALSE</locked>");
            xml.AppendLine("\t\t\t\t</track>");

            // Close video section
            xml.AppendLine("\t\t\t</video>");

            // Add audio section
            xml.AppendLine("\t\t\t<audio>");
            xml.AppendLine("\t\t\t\t<numOutputChannels>2</numOutputChannels>");
            xml.AppendLine("\t\t\t\t<format>");
            xml.AppendLine("\t\t\t\t\t<samplecharacteristics>");
            xml.AppendLine("\t\t\t\t\t\t<depth>16</depth>");
            xml.AppendLine("\t\t\t\t\t\t<samplerate>48000</samplerate>");
            xml.AppendLine("\t\t\t\t\t</samplecharacteristics>");
            xml.AppendLine("\t\t\t\t</format>");

            // Add basic audio outputs
            xml.AppendLine("\t\t\t\t<outputs>");
            xml.AppendLine("\t\t\t\t\t<group>");
            xml.AppendLine("\t\t\t\t\t\t<index>1</index>");
            xml.AppendLine("\t\t\t\t\t\t<numchannels>1</numchannels>");
            xml.AppendLine("\t\t\t\t\t\t<downmix>0</downmix>");
            xml.AppendLine("\t\t\t\t\t\t<channel>");
            xml.AppendLine("\t\t\t\t\t\t\t<index>1</index>");
            xml.AppendLine("\t\t\t\t\t\t</channel>");
            xml.AppendLine("\t\t\t\t\t</group>");
            xml.AppendLine("\t\t\t\t\t<group>");
            xml.AppendLine("\t\t\t\t\t\t<index>2</index>");
            xml.AppendLine("\t\t\t\t\t\t<numchannels>1</numchannels>");
            xml.AppendLine("\t\t\t\t\t\t<downmix>0</downmix>");
            xml.AppendLine("\t\t\t\t\t\t<channel>");
            xml.AppendLine("\t\t\t\t\t\t\t<index>2</index>");
            xml.AppendLine("\t\t\t\t\t\t</channel>");
            xml.AppendLine("\t\t\t\t\t</group>");
            xml.AppendLine("\t\t\t\t</outputs>");

            // Add an empty audio track
            xml.AppendLine("\t\t\t\t<track TL.SQTrackAudioKeyframeStyle=\"0\" TL.SQTrackShy=\"0\" TL.SQTrackExpandedHeight=\"41\" TL.SQTrackExpanded=\"0\" MZ.TrackTargeted=\"1\" PannerCurrentValue=\"0.5\" PannerIsInverted=\"true\" PannerStartKeyframe=\"-91445760000000000,0.5,0,0,0,0,0,0\" PannerName=\"Balance\" currentExplodedTrackIndex=\"0\" totalExplodedTrackCount=\"2\" premiereTrackType=\"Stereo\">");
            xml.AppendLine("\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine("\t\t\t\t\t<locked>FALSE</locked>");
            xml.AppendLine("\t\t\t\t\t<outputchannelindex>1</outputchannelindex>");
            xml.AppendLine("\t\t\t\t</track>");
            xml.AppendLine("\t\t\t\t<track TL.SQTrackAudioKeyframeStyle=\"0\" TL.SQTrackShy=\"0\" TL.SQTrackExpandedHeight=\"41\" TL.SQTrackExpanded=\"0\" MZ.TrackTargeted=\"1\" PannerCurrentValue=\"0.5\" PannerIsInverted=\"true\" PannerStartKeyframe=\"-91445760000000000,0.5,0,0,0,0,0,0\" PannerName=\"Balance\" currentExplodedTrackIndex=\"1\" totalExplodedTrackCount=\"2\" premiereTrackType=\"Stereo\">");
            xml.AppendLine("\t\t\t\t\t<enabled>TRUE</enabled>");
            xml.AppendLine("\t\t\t\t\t<locked>FALSE</locked>");
            xml.AppendLine("\t\t\t\t\t<outputchannelindex>2</outputchannelindex>");
            xml.AppendLine("\t\t\t\t</track>");

            // Close audio section
            xml.AppendLine("\t\t\t</audio>");

            // Close media section
            xml.AppendLine("\t\t</media>");

            // Add sequence timecode
            xml.AppendLine("\t\t<timecode>");
            xml.AppendLine("\t\t\t<rate>");
            xml.AppendLine($"\t\t\t\t<timebase>{videoFps}</timebase>");
            xml.AppendLine($"\t\t\t\t<ntsc>{(isNtsc ? "TRUE" : "FALSE")}</ntsc>");
            xml.AppendLine("\t\t\t</rate>");
            xml.AppendLine("\t\t\t<string>00:00:00:00</string>");
            xml.AppendLine("\t\t\t<frame>0</frame>");
            xml.AppendLine("\t\t\t<displayformat>NDF</displayformat>");
            xml.AppendLine("\t\t</timecode>");

            // Add sequence markers (this is where Premiere actually reads them from)
            xml.AppendLine("\t\t\t<marker>");
            foreach (var note in _notes)
            {
                int frameNumber = (int)(note.Timecode * videoFps);
                xml.AppendLine("\t\t\t\t<marker>");
                xml.AppendLine($"\t\t\t\t\t<comment>{System.Security.SecurityElement.Escape(note.Notes)}</comment>");
                xml.AppendLine("\t\t\t\t\t<name></name>");
                xml.AppendLine($"\t\t\t\t\t<in>{frameNumber}</in>");
                xml.AppendLine("\t\t\t\t\t<out>-1</out>");
                xml.AppendLine("\t\t\t\t\t<out>-1</out>");
                xml.AppendLine("\t\t\t\t</marker>");
            }
            xml.AppendLine("\t\t\t</marker>");

            // Close sequence section with additional metadata
            xml.AppendLine("\t\t<labels>");
            xml.AppendLine("\t\t\t<label2>Forest</label2>");
            xml.AppendLine("\t\t</labels>");
            xml.AppendLine("\t\t<logginginfo>");
            xml.AppendLine("\t\t\t<description></description>");
            xml.AppendLine("\t\t\t<scene></scene>");
            xml.AppendLine("\t\t\t<shottake></shottake>");
            xml.AppendLine("\t\t\t<lognote></lognote>");
            xml.AppendLine("\t\t\t<good></good>");
            xml.AppendLine("\t\t\t<originalvideofilename></originalvideofilename>");
            xml.AppendLine("\t\t\t<originalaudiofilename></originalaudiofilename>");
            xml.AppendLine("\t\t</logginginfo>");
            xml.AppendLine("\t</sequence>");

            // Close project and xmeml tags
            xml.AppendLine("</children>");
            xml.AppendLine("</project>");
            xml.AppendLine("</xmeml>");

            // Write XML file
            await File.WriteAllTextAsync(exportPath, xml.ToString());
        }

        private async Task ExportForAfterEffects(string exportPath)
        {
            if (string.IsNullOrEmpty(_currentVideoPath) || _notes.Count == 0)
                return;

            var videoName = Path.GetFileNameWithoutExtension(_currentVideoPath);

            // Get video properties from MPV
            double videoFps = GetVideoFrameRate() ?? 30.0;
            double videoDuration = GetVideoDuration() ?? 60.0;
            (int width, int height) = GetVideoResolution() ?? (1920, 1080);

            // Create images folder
            var imagesFolder = Path.Combine(
                Path.GetDirectoryName(exportPath),
                $"{Path.GetFileNameWithoutExtension(exportPath)}_images"
            );

            Directory.CreateDirectory(imagesFolder);

            // Copy all images to the new folder
            var imagePathMapping = new Dictionary<string, string>();

            foreach (var note in _notes)
            {
                string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                    ? note.EditedImagePath
                    : note.ImagePath;

                if (File.Exists(sourcePath))
                {
                    var destFileName = $"frame_{note.Timecode.ToString("F3", System.Globalization.CultureInfo.InvariantCulture).Replace(".", "_")}{Path.GetExtension(sourcePath)}";
                    var destPath = Path.Combine(imagesFolder, destFileName);

                    File.Copy(sourcePath, destPath, true);
                    imagePathMapping.Add(sourcePath, destPath);
                }
            }

            // Create data structure for AE with image mapping included
            var exportData = new
            {
                ProjectInfo = new
                {
                    VideoPath = _currentVideoPath,
                    VideoName = videoName,
                    Fps = videoFps,
                    Duration = videoDuration,
                    Width = width,
                    Height = height,
                    ExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                },
                Notes = _notes.Select(note =>
                {
                    string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath
                        : note.ImagePath;

                    string exportedImagePath = imagePathMapping.ContainsKey(sourcePath)
                        ? imagePathMapping[sourcePath]
                        : sourcePath;

                    return new
                    {
                        Timecode = note.Timecode,
                        TimecodeString = note.TimecodeString,
                        FrameNumber = (int)(note.Timecode * videoFps),
                        Notes = note.Notes,
                        OriginalImagePath = sourcePath,
                        ExportedImagePath = exportedImagePath,
                        Created = note.Created.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                }).OrderBy(n => n.Timecode).ToList(),
                ImageMappings = imagePathMapping
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(exportData, options);
            await File.WriteAllTextAsync(exportPath, json);

            // Create the JSX file
            var jsxContent = GenerateAfterEffectsJSX();
            var jsxPath = Path.Combine(
                Path.GetDirectoryName(exportPath),
                $"Import_{Path.GetFileNameWithoutExtension(exportPath)}.jsx"
            );
            await File.WriteAllTextAsync(jsxPath, jsxContent);
        }

        private (int width, int height)? GetVideoResolution()
        {
            if (_mainWindow?.GetMpvHandle() is IntPtr mpvHandle && mpvHandle != IntPtr.Zero)
            {
                try
                {
                    // Try to get width and height from MPV player
                    var widthStr = MPVInterop.GetStringProperty(mpvHandle, "width");
                    var heightStr = MPVInterop.GetStringProperty(mpvHandle, "height");

                    if (!string.IsNullOrEmpty(widthStr) && !string.IsNullOrEmpty(heightStr) &&
                        int.TryParse(widthStr, out int width) && int.TryParse(heightStr, out int height))
                    {
                        return (width, height);
                    }

                    // Alternative approach if the above fails
                    var videoParamsStr = MPVInterop.GetStringProperty(mpvHandle, "video-params");
                    if (!string.IsNullOrEmpty(videoParamsStr))
                    {
                        // Parse the video-params string which might look like "w=1920,h=1080,..."
                        var wMatch = System.Text.RegularExpressions.Regex.Match(videoParamsStr, @"w=(\d+)");
                        var hMatch = System.Text.RegularExpressions.Regex.Match(videoParamsStr, @"h=(\d+)");

                        if (wMatch.Success && hMatch.Success &&
                            int.TryParse(wMatch.Groups[1].Value, out width) &&
                            int.TryParse(hMatch.Groups[1].Value, out height))
                        {
                            return (width, height);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting video resolution: {ex.Message}");
                }
            }

            return null;
        }

        private double? GetVideoDuration()
        {
            if (_mainWindow?.GetMpvHandle() is IntPtr mpvHandle && mpvHandle != IntPtr.Zero)
            {
                try
                {
                    // Try to get duration from MPV player
                    var durationStr = MPVInterop.GetStringProperty(mpvHandle, "duration");
                    if (!string.IsNullOrEmpty(durationStr) && double.TryParse(durationStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double duration))
                    {
                        return duration;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting video duration: {ex.Message}");
                }
            }

            return null;
        }

        private double? GetVideoFrameRate()
        {
            if (_mainWindow?.GetMpvHandle() is IntPtr mpvHandle && mpvHandle != IntPtr.Zero)
            {
                try
                {
                    // Try to get fps from MPV player
                    var fpsStr = MPVInterop.GetStringProperty(mpvHandle, "fps");
                    if (!string.IsNullOrEmpty(fpsStr) && double.TryParse(fpsStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double fps))
                    {
                        return fps;
                    }

                    // Alternative: try container-fps if normal fps isn't available
                    fpsStr = MPVInterop.GetStringProperty(mpvHandle, "container-fps");
                    if (!string.IsNullOrEmpty(fpsStr) && double.TryParse(fpsStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out fps))
                    {
                        return fps;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting video frame rate: {ex.Message}");
                }
            }

            return null;
        }

        private string GenerateAfterEffectsJSX()
        {
            return @"// After Effects Import Script for UnionMpvPlayer Notes
// Place this script and the corresponding JSON file in the same folder
// Run via File > Scripts > Import_UnionMpvPlayer_Notes.jsx

(function(thisObj) {
    var myPanel = (thisObj instanceof Panel) ? thisObj : new Window(""palette"", ""Import UnionMpvPlayer Notes"", undefined, { resizeable: true });

    function buildUI(thisPanel) {
        thisPanel.orientation = ""column"";
        thisPanel.alignChildren = [""fill"", ""top""];
        thisPanel.spacing = 10;
        thisPanel.margins = 10;

        // Auto-detect JSON file
        var scriptFile = new File($.fileName);
        var scriptFolder = scriptFile.parent;
        var jsonFileName = scriptFile.name.replace("".jsx"", "".json"").replace(""Import_"", """");
        var jsonFile = new File(scriptFolder.fsName + ""/"" + jsonFileName);
        
        // === File Path Display ===
        var fileGroup = thisPanel.add(""panel"", undefined, ""JSON File"");
        fileGroup.orientation = ""column"";
        fileGroup.alignChildren = [""fill"", ""top""];
        fileGroup.margins = 10;

        var fileStatusText = fileGroup.add(""statictext"", undefined, jsonFile.exists ? ""Found: "" + jsonFileName : ""JSON file not found in script directory"");
        fileStatusText.preferredSize.width = 300;

        // Only show browse button if auto-detection failed
        var browseButton;
        if (!jsonFile.exists) {
            browseButton = fileGroup.add(""button"", undefined, ""Browse for JSON file..."");
        }

        // === Options Panel ===
        var optionsGroup = thisPanel.add(""panel"", undefined, ""Import Options"");
        optionsGroup.orientation = ""column"";
        optionsGroup.alignChildren = [""left"", ""top""];
        optionsGroup.margins = 10;

        var importMarkers = optionsGroup.add(""checkbox"", undefined, ""Import Markers"");
        importMarkers.value = true;

        var importStills = optionsGroup.add(""checkbox"", undefined, ""Import Still Images"");
        importStills.value = true;

        var stillDuration = optionsGroup.add(""group"");
        stillDuration.orientation = ""row"";
        stillDuration.alignChildren = [""left"", ""center""];
        stillDuration.add(""statictext"", undefined, ""Still Duration (frames):"");
        var stillDurationInput = stillDuration.add(""edittext"", undefined, ""10"");
        stillDurationInput.preferredSize.width = 50;

        // === Button Group ===
        var buttonGroup = thisPanel.add(""group"");
        buttonGroup.orientation = ""row"";
        buttonGroup.alignment = ""right"";

        var cancelButton = buttonGroup.add(""button"", undefined, ""Cancel"");
        var okButton = buttonGroup.add(""button"", undefined, ""Import"");
        okButton.enabled = jsonFile.exists;

        // === Browse logic (only if needed) ===
        if (browseButton) {
            browseButton.onClick = function() {
                var file = File.openDialog(""Select Notes JSON File"", ""JSON Files:*.json"");
                if (file) {
                    jsonFile = file;
                    fileStatusText.text = ""Selected: "" + jsonFile.name;
                    okButton.enabled = true;
                }
            };
        }

        // === Button logic ===
        cancelButton.onClick = function() {
            if (thisPanel instanceof Window) {
                thisPanel.close();
            }
        };

        okButton.onClick = function() {
            try {
                importNotesFromJson(jsonFile.fsName, {
                    importMarkers: importMarkers.value,
                    importStills: importStills.value,
                    stillDuration: parseInt(stillDurationInput.text, 10) || 10
                });
                
                if (thisPanel instanceof Window) {
                    thisPanel.close();
                }
            } catch (e) {
                alert(""Error importing notes: "" + e.toString());
            }
        };

        return thisPanel;
    }

    var builtPanel = buildUI(myPanel);

    if (builtPanel instanceof Window) {
        builtPanel.center();
        builtPanel.show();
    } else {
        builtPanel.layout.layout(true);
        builtPanel.layout.resize();
    }

    // === Main Import Logic ===
    function importNotesFromJson(jsonPath, options) {
        var file = new File(jsonPath);
        file.open(""r"");
        var jsonContent = file.read();
        file.close();

        var data = JSON.parse(jsonContent);
        var projectInfo = data.ProjectInfo;
        var notesData = data.Notes;

        var comp = app.project.activeItem;
        if (!(comp instanceof CompItem)) {
            comp = app.project.items.addComp(
                projectInfo.VideoName,
                projectInfo.Width,
                projectInfo.Height,
                1,
                projectInfo.Duration,
                projectInfo.Fps
            );
        } else {
            if (comp.duration < projectInfo.Duration) {
                comp.duration = projectInfo.Duration;
            }
        }

        var videoItem = null;
        try {
            var videoFile = new File(projectInfo.VideoPath);
            if (videoFile.exists) {
                var importOptions = new ImportOptions(videoFile);
                videoItem = app.project.importFile(importOptions);
                comp.layers.add(videoItem);
            }
        } catch (e) {
            alert(""Could not import video: "" + e.toString());
        }

        for (var i = 0; i < notesData.length; i++) {
            var noteItem = notesData[i];
            var frameTime = noteItem.FrameNumber / projectInfo.Fps;

            if (options.importMarkers) {
                var markerObj = new MarkerValue(noteItem.Notes);
                comp.markerProperty.setValueAtTime(frameTime, markerObj);
            }

            if (options.importStills) {
                try {
                    // Use the exported image path directly from the JSON
                    var imgPath = noteItem.ExportedImagePath;
                    
                    var imgFile = new File(imgPath);
                    if (imgFile.exists) {
                        var imgOptions = new ImportOptions(imgFile);
                        var img = app.project.importFile(imgOptions);
                        var imgLayer = comp.layers.add(img);

                        imgLayer.startTime = frameTime;
                        imgLayer.outPoint = frameTime + (options.stillDuration / projectInfo.Fps);
                    }
                } catch (e) {
                    alert(""Error importing image for note "" + (i+1) + "": "" + e.toString());
                }
            }
        }

        alert(""Import complete! Imported "" + notesData.length + "" notes."");
    }
})(this);";
        }

        private string ConvertImageToBase64(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath)) return imagePath;

                byte[] imgBytes = File.ReadAllBytes(imagePath);
                string base64Data = Convert.ToBase64String(imgBytes);
                string ext = Path.GetExtension(imagePath).ToLower();
                string mimeType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".gif" => "image/gif",
                    _ => "image/jpeg"
                };

                return $"data:{mimeType};base64,{base64Data}";
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error converting image to base64: {ex.Message}");
                return imagePath;
            }
        }

        private async Task ExportToHtml(string exportPath)
        {
            var sb = new StringBuilder();

            // Build initial markdown
            sb.AppendLine($"# {Path.GetFileNameWithoutExtension(_currentVideoPath)}");
            sb.AppendLine($"`{_currentVideoPath}`");
            sb.AppendLine("\n\\");
            sb.AppendLine("&nbsp;\n");

            foreach (var note in _notes)
            {
                string imagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                    ? note.EditedImagePath
                    : note.ImagePath;

                sb.AppendLine($"### {note.TimecodeString}");
                sb.AppendLine($"![Frame at {note.TimecodeString}]({imagePath})");
                sb.AppendLine(note.Notes);
                sb.AppendLine("\n\\");
                sb.AppendLine("&nbsp;\n");
            }

            string htmlContent = HtmlGeneratorHelper.GenerateHtmlFromMarkdownContent(sb.ToString(), false);
            await File.WriteAllTextAsync(exportPath, htmlContent);
        }


        private string GetExportFolder(string projectRoot)
        {
            var exportFolder = Path.Combine(projectRoot, "docs", "notes", "video_notes");
            Directory.CreateDirectory(exportFolder);
            return exportFolder;
        }

        private string? FindProjectRoot(string path)
        {
            var dir = new DirectoryInfo(path);
            while (dir != null)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(dir.Name, @"^\d{6,7}[a-zA-Z]?_.*"))
                {
                    var requiredFolders = new[] { "docs", "assets", "3d", "ae" };
                    if (requiredFolders.All(folder => Directory.Exists(Path.Combine(dir.FullName, folder))))
                    {
                        return dir.FullName;
                    }
                }
                dir = dir.Parent;
            }
            return null;
        }

        private string LoadEmbeddedCss(string resourceName)
        {
            var assembly = typeof(NotesView).Assembly;
            var resourcePath = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceName));

            if (resourcePath != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourcePath);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
            }
            return string.Empty;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_notes.Count == 0 || string.IsNullOrEmpty(_currentVideoPath)) return;

            bool isUnionNotes = ExportFormatComboBox.SelectedIndex == 3;
            bool isHtml = ExportFormatComboBox.SelectedIndex == 1;
            bool isMarkdown = ExportFormatComboBox.SelectedIndex == 0;
            bool isPdf = ExportFormatComboBox.SelectedIndex == 2;
            bool isAfterEffects = ExportFormatComboBox.SelectedIndex == 4;
            bool isPremierePro = ExportFormatComboBox.SelectedIndex == 5;

            string extension = isHtml ? ".html" :
                      isMarkdown ? ".md" :
                      isPdf ? ".pdf" :
                      isAfterEffects ? ".json" :
                      isPremierePro ? ".xml" : ".md";

            string exportFolder;
            string projectRoot = FindProjectRoot(_currentVideoPath) ?? "";

            if (!string.IsNullOrEmpty(projectRoot) && isUnionNotes)
            {
                exportFolder = GetExportFolder(projectRoot);
            }
            else
            {
                var dialog = new OpenFolderDialog { Title = "Choose Export Location" };
                exportFolder = await dialog.ShowAsync((Window)TopLevel.GetTopLevel(this)) ?? "";
                if (string.IsNullOrEmpty(exportFolder)) return;
            }

            var videoName = Path.GetFileNameWithoutExtension(_currentVideoPath);
            var exportPath = Path.Combine(exportFolder, $"{videoName}_notes{extension}");

            if (isAfterEffects)
            {
                await ExportForAfterEffects(exportPath);
            }
            else if (isPremierePro)
            {
                await ExportForPremierePro(exportPath);
            }
            else if (isUnionNotes)
            {
                // Union Notes now using Markdown format with .md extension
                var sb = new StringBuilder();

                // File reference as markdown
                sb.AppendLine($"# {videoName}");
                sb.AppendLine($"`{_currentVideoPath}`");
                sb.AppendLine();

                // Network share path for notesViewer app
                string notesNetworkShare = @"\\192.168.40.100\UnionNotes";

                // Extract project name from the current video path or use a default
                string projectFolderName;
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    // Use existing project folder name extraction logic
                    projectFolderName = Path.GetFileName(projectRoot);
                }
                else
                {
                    // If not in a standard project structure, create a folder based on video name
                    projectFolderName = videoName;
                }

                // Create the target directory in the network share
                string targetDirectory = Path.Combine(notesNetworkShare, projectFolderName);
                Directory.CreateDirectory(targetDirectory);

                // Generate filename using the pattern yyMMdda_videoName.md
                string todayDate = DateTime.Now.ToString("yyMMdd");
                char versionLetter = 'a';
                string baseFileName = $"{todayDate}{versionLetter}";
                string newFileName = $"{baseFileName}_{videoName}.md";
                string fullPath = Path.Combine(targetDirectory, newFileName);

                // Check if a file with this pattern already exists and increment the version letter if needed
                while (Directory.EnumerateFiles(targetDirectory, $"{todayDate}*")
                                .Any(f => Path.GetFileName(f).StartsWith(baseFileName)))
                {
                    versionLetter++;
                    if (versionLetter > 'z')
                    {
                        // This is extremely unlikely to happen, but handle it just in case
                        baseFileName = $"{todayDate}_extra";
                        break;
                    }
                    baseFileName = $"{todayDate}{versionLetter}";
                    newFileName = $"{baseFileName}_{videoName}.md";
                    fullPath = Path.Combine(targetDirectory, newFileName);
                }

                // Process each note, copying images to the same flat directory
                int imageCounter = 1;
                foreach (var note in _notes)
                {
                    string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath : note.ImagePath;

                    // Create a new image name in the target location
                    string imageExt = Path.GetExtension(sourcePath);
                    string imageFileName = $"{baseFileName}_img_{imageCounter++:D3}{imageExt}";
                    string targetImagePath = Path.Combine(targetDirectory, imageFileName);

                    // Copy the image
                    File.Copy(sourcePath, targetImagePath, true);

                    // Add markdown for this note with screenshot reference
                    sb.AppendLine($"## {note.TimecodeString}");
                    sb.AppendLine($"![Frame at {note.TimecodeString}]({imageFileName})");
                    sb.AppendLine(note.Notes);
                    sb.AppendLine();
                }

                // Update the exportPath for use by the popup dialog
                exportPath = fullPath;
                await File.WriteAllTextAsync(exportPath, sb.ToString());
            }

            else if (isMarkdown)
            {
                // Standard Markdown format
                var sb = new StringBuilder();
                sb.AppendLine($"# {videoName}");
                sb.AppendLine($"`{_currentVideoPath}`");
                sb.AppendLine();
                foreach (var note in _notes)
                {
                    string imagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath : note.ImagePath;
                    sb.AppendLine("---");
                    sb.AppendLine($"### {note.TimecodeString}");
                    sb.AppendLine($"![Frame at {note.TimecodeString}]({imagePath})");
                    sb.AppendLine(note.Notes);
                    sb.AppendLine();
                }
                await File.WriteAllTextAsync(exportPath, sb.ToString());
            }
            else if (isHtml)
            {
                var htmlSb = new StringBuilder();
                htmlSb.AppendLine("<!DOCTYPE html>");
                htmlSb.AppendLine("<html><head>");
                htmlSb.AppendLine("<style>");
                htmlSb.AppendLine(LoadEmbeddedCss("convertMarkdownToHTML.css"));
                htmlSb.AppendLine("</style></head><body class='markdown-body'>");

                htmlSb.AppendLine($"<h1>{videoName}</h1>");
                htmlSb.AppendLine($"<code>{_currentVideoPath}</code><br><br>");

                foreach (var note in _notes)
                {
                    string imagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath
                        : note.ImagePath;

                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64 = Convert.ToBase64String(imageBytes);
                    string ext = Path.GetExtension(imagePath).ToLower();
                    string mimeType = ext == ".png" ? "image/png" : "image/jpeg";

                    htmlSb.AppendLine($"<h3>{note.TimecodeString}</h3>");
                    htmlSb.AppendLine($"<img src=\"data:{mimeType};base64,{base64}\" alt=\"Frame at {note.TimecodeString}\"><br>");
                    htmlSb.AppendLine($"<p>{note.Notes}</p><br>");
                }

                htmlSb.AppendLine("</body></html>");
                await File.WriteAllTextAsync(exportPath, htmlSb.ToString());
            }
            else if (isPdf) // PDF
            {
                var htmlSb = new StringBuilder();
                htmlSb.AppendLine("<!DOCTYPE html>");
                htmlSb.AppendLine("<html><head><style>");
                htmlSb.AppendLine(LoadEmbeddedCss("convertMarkdownToHTML.css"));
                htmlSb.AppendLine("</style></head><body class='markdown-body'>");
                htmlSb.AppendLine($"<h1>{videoName}</h1>");
                htmlSb.AppendLine($"<code>{_currentVideoPath}</code><br>");

                foreach (var note in _notes)
                {
                    string imagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath : note.ImagePath;
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64 = Convert.ToBase64String(imageBytes);
                    string ext = Path.GetExtension(imagePath).ToLower();
                    string mimeType = ext == ".png" ? "image/png" : "image/jpeg";

                    htmlSb.AppendLine($"<h3>{note.TimecodeString}</h3>");
                    htmlSb.AppendLine($"<img src=\"data:{mimeType};base64,{base64}\" alt=\"Frame at {note.TimecodeString}\">");
                    htmlSb.AppendLine($"<p>{note.Notes}</p><br>");
                }

                htmlSb.AppendLine("</body></html>");

                var tempHtmlPath = Path.GetTempFileName() + ".html";
                await File.WriteAllTextAsync(tempHtmlPath, htmlSb.ToString());

                try
                {
                    var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "umpv");
                    var wkhtmltopdfPath = Path.Combine(settingsPath, "wkhtmltopdf");
                    exportPandoc.ExtractWkHtmlToPdfIfNeeded(wkhtmltopdfPath);

                    var pdfPath = Path.ChangeExtension(exportPath, ".pdf");
                    await exportPandoc.ConvertHtmlToPdfAsync(
                        tempHtmlPath,
                        pdfPath,
                        Path.Combine(wkhtmltopdfPath, "wkhtmltopdf.exe"),
                        settingsPath
                    );

                    exportPath = pdfPath;
                }
                finally
                {
                    if (File.Exists(tempHtmlPath))
                    {
                        File.Delete(tempHtmlPath);
                    }
                }
            }

            if (isAfterEffects || isPremierePro)
            {
                var popup = new ExportCompletePopup(hideOpenButton: true) { Tag = exportPath };

                popup.OpenFolderRequested += (s, path) =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = Path.GetDirectoryName(path),
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error opening folder: {ex.Message}");
                    }
                };

                await popup.ShowDialog((Window)TopLevel.GetTopLevel(this));
            }
            else
            {
                var popup = new ExportCompletePopup() { Tag = exportPath };

                popup.OpenFileRequested += (s, path) =>
                {
                    if (isUnionNotes)
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = @"C:\UnionApps\unionProjects\notesViewer.exe",
                                Arguments = $"\"{exportPath}\"", // Make sure to quote the path
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error opening UnionNotes viewer: {ex.Message}");
                        }
                    }
                    else
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = path,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch (Exception ex)
                        {
                            //Debug.WriteLine($"Error opening file: {ex.Message}");
                        }
                    }
                };

                popup.OpenFolderRequested += (s, path) =>
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = Path.GetDirectoryName(path),
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error opening folder: {ex.Message}");
                    }
                };

                await popup.ShowDialog((Window)TopLevel.GetTopLevel(this));
            }
        }
    }
}