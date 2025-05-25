using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
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
using Markdown2Pdf;
using Markdown2Pdf.Options;
using PuppeteerSharp.Media;

namespace UnionMpvPlayer.Views
{
    public partial class NotesView : UserControl
    {
        private bool _isDisabledForImageSequence = false;
        private EventHandler? _playbackStartedHandler;
        private bool _isOverlayActive = false;
        private double _lastImageTimecode = 0.0;
        private NoteItem? _selectedNote;
        public void SetSelectedNote(NoteItem? note)
        {
            // Clear previous selection
            if (_selectedNote != null)
            {
                _selectedNote.IsSelected = false;
            }

            // Set new selection
            _selectedNote = note;
            if (_selectedNote != null)
            {
                _selectedNote.IsSelected = true;
            }
        }

        public class NoteItem : INotifyPropertyChanged
        {
            private bool _isSelected;
            [JsonIgnore]
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }
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

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            // Stop the event from bubbling up - RoutedEventArgs has Handled property
            e.Handled = true;

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

        private async void EditImageButton_Click(object sender, PointerPressedEventArgs e)
        {
            // Stop the event from bubbling up to the parent border
            e.Handled = true;

            if (sender is Border border && border.DataContext is NoteItem noteItem)
            {
                await EditImage(noteItem);
            }
        }

        private async void PlaySelectArea_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isDisabledForImageSequence)
                return;

            if (sender is Border border && border.DataContext is NoteItem noteItem)
            {
                // Update selected note
                SetSelectedNote(noteItem);

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

        // You can also create a separate method to avoid code duplication
        private async Task EditImage(NoteItem noteItem)
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

            string format = ExportFormatComboBox.SelectedItem is ComboBoxItem selected
                ? selected.Content.ToString()
                : "Markdown";

            string extension = format switch
            {
                "HTML" => ".html",
                "PDF" => ".pdf",
                "Markdown" => ".md",
                "Union Notes" => ".md",
                "After Effects (JSON)" => ".json",
                "Premiere Pro (XML)" => ".xml",
                _ => ".md"
            };

            bool isUnionNotes = format == "Union Notes";
            bool isAfterEffects = format == "After Effects (JSON)";
            bool isPremierePro = format == "Premiere Pro (XML)";

            // Get the main window once for use throughout the method
            var mainWindow = (Window)TopLevel.GetTopLevel(this);

            // For non-Union Notes formats, we need to ask for a folder location first
            string exportFolder = "";
            if (!isUnionNotes)
            {
                var dialog = new OpenFolderDialog { Title = "Choose Export Location" };
                exportFolder = await dialog.ShowAsync(mainWindow) ?? "";
                if (string.IsNullOrEmpty(exportFolder)) return; // User canceled
            }

            // Show progress indicator after folder selection
            var progressWindow = new ProgressWindow();
            progressWindow.Show("Exporting Notes", $"Preparing to export to {format} format...", mainWindow);

            try
            {
                // Run the export operation on a background thread to keep UI responsive
                await Task.Run(async () =>
                {
                    string exportPath = "";
                    try
                    {
                        // Update progress
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            progressWindow.UpdateProgress(0.1, "Initializing export...");
                        });

                        string projectRoot = FindProjectRoot(_currentVideoPath) ?? "";

                        if (isUnionNotes)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.2, "Setting up Union Notes export...");
                            });

                            // Network share path for UnionNotes
                            string notesNetworkShare = @"\\192.168.40.100\UnionNotes";

                            // Extract project name from the current video path or use a default
                            string projectFolderName;
                            if (!string.IsNullOrEmpty(projectRoot))
                            {
                                projectFolderName = Path.GetFileName(projectRoot);
                            }
                            else
                            {
                                projectFolderName = Path.GetFileNameWithoutExtension(_currentVideoPath);
                            }

                            // Create the target directory in the network share
                            string targetDirectory = Path.Combine(notesNetworkShare, projectFolderName);
                            Directory.CreateDirectory(targetDirectory);

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.3, "Generating filename...");
                            });

                            // Generate filename
                            string todayDate = DateTime.Now.ToString("yyMMdd");
                            char versionLetter = 'a';
                            string baseFileName = $"{todayDate}{versionLetter}";
                            var videoName = Path.GetFileNameWithoutExtension(_currentVideoPath);
                            string newFileName = $"{baseFileName}_{videoName}.md";
                            exportPath = Path.Combine(targetDirectory, newFileName);

                            // Check if a file with this pattern already exists
                            while (Directory.EnumerateFiles(targetDirectory, $"{todayDate}*")
                                            .Any(f => Path.GetFileName(f).StartsWith(baseFileName)))
                            {
                                versionLetter++;
                                if (versionLetter > 'z')
                                {
                                    baseFileName = $"{todayDate}_extra";
                                    break;
                                }
                                baseFileName = $"{todayDate}{versionLetter}";
                                newFileName = $"{baseFileName}_{videoName}.md";
                                exportPath = Path.Combine(targetDirectory, newFileName);
                            }

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.4, "Creating content...");
                            });

                            // Create Union Notes content
                            var sb = new StringBuilder();
                            sb.AppendLine($"# {videoName}");
                            sb.AppendLine($"`{_currentVideoPath}`");
                            sb.AppendLine();

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.5, "Processing images...");
                            });

                            // Start the table
                            sb.AppendLine("| Timecode | Image & Notes |");
                            sb.AppendLine("|----------|---------------|");

                            // Process each note, copying images and creating HTTP URLs
                            int imageCounter = 1;
                            int totalNotes = _notes.Count;
                            double baseProgress = 0.5;

                            foreach (var note in _notes.OrderBy(n => n.Timecode))
                            {
                                // Update progress per note
                                double noteProgress = baseProgress + ((double)imageCounter / totalNotes) * 0.4;
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(noteProgress,
                                        $"Processing note {imageCounter} of {totalNotes}...");
                                });

                                string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                                    ? note.EditedImagePath
                                    : note.ImagePath;

                                try
                                {
                                    // Create a new image name
                                    string imageFileName = $"{baseFileName}_img_{imageCounter:D3}.jpg";
                                    string targetImagePath = Path.Combine(targetDirectory, imageFileName);

                                    // Transcode the image to a smaller JPG directly to the target location
                                    TranscodeToJpeg(sourcePath, targetDirectory, imageFileName, 1920);

                                    // Create HTTP URL for the image
                                    string httpImageUrl = $"http://192.168.40.100:8723/{projectFolderName}/{imageFileName}";

                                    // Format the note content to work in a table cell
                                    // Replace newlines with <br> for HTML rendering
                                    string formattedNotes = note.Notes.Replace("\n", "<br>");

                                    // Create table row
                                    sb.Append($"| {note.TimecodeString} | ");
                                    sb.Append($"![Frame at {note.TimecodeString}]({httpImageUrl})<br>");
                                    sb.Append($"{formattedNotes} |");
                                    sb.AppendLine();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error processing note {imageCounter}: {ex.Message}");

                                    // Fallback: copy the original image
                                    string imageExt = Path.GetExtension(sourcePath);
                                    string imageFileName = $"{baseFileName}_img_{imageCounter:D3}{imageExt}";
                                    string targetImagePath = Path.Combine(targetDirectory, imageFileName);
                                    File.Copy(sourcePath, targetImagePath, true);

                                    // Create HTTP URL
                                    string httpImageUrl = $"http://192.168.40.100:8723/{projectFolderName}/{imageFileName}";

                                    // Format the note content to work in a table cell
                                    string formattedNotes = note.Notes.Replace("\n", "<br>");

                                    // Create table row
                                    sb.Append($"| {note.TimecodeString} | ");
                                    sb.Append($"![Frame at {note.TimecodeString}]({httpImageUrl})<br>");
                                    sb.Append($"{formattedNotes} |");
                                    sb.AppendLine();
                                }

                                imageCounter++;
                            }

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.9, "Writing file...");
                            });

                            // Write the file
                            await File.WriteAllTextAsync(exportPath, sb.ToString());
                        }
                        else
                        {
                            // For non-Union Notes formats
                            var videoName = Path.GetFileNameWithoutExtension(_currentVideoPath);
                            exportPath = Path.Combine(exportFolder, $"{videoName}_notes{extension}");

                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                progressWindow.UpdateProgress(0.3, $"Creating {format} export...");
                            });

                            // Handle different export formats
                            if (isAfterEffects)
                            {
                                await ExportForAfterEffects(exportPath);
                            }
                            else if (isPremierePro)
                            {
                                await ExportForPremierePro(exportPath);
                            }
                            else if (format == "HTML")
                            {
                                // HTML export with table layout
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.3, "Creating HTML export...");
                                });

                                var sb = new StringBuilder();

                                sb.AppendLine("<!DOCTYPE html>");
                                sb.AppendLine("<html>");
                                sb.AppendLine("<head>");
                                sb.AppendLine("  <meta charset='utf-8'>");
                                sb.AppendLine("  <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
                                sb.AppendLine($"  <title>{videoName} - Video Notes</title>");
                                sb.AppendLine("  <style>");
                                sb.AppendLine(@"
        /* Base styles matching your viewer */
        body {
            font-family: system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', 'Noto Sans', 'Liberation Sans', Arial, sans-serif;
            line-height: 1.4;
            color: #000000;
            margin: 0;
            padding: 20px 90px;
            background-color: white;
            max-width: 1280px;
        }

        /* Headings */
        h1, h2, h3, h4, h5, h6 {
            font-family: system-ui, -apple-system, 'Segoe UI', Roboto, 'Helvetica Neue', 'Noto Sans', 'Liberation Sans', Arial, sans-serif;
            color: black;
            margin-top: 2rem;
            margin-bottom: 1rem;
            font-weight: 400;
            line-height: 1.2;
        }

        h1 { font-size: 3rem; }

        /* Code styling */
        code {
            background: #f5f5f5;
            color: #d73a49;
            padding: 0.2em 0.4em;
            font-family: 'JetBrains Mono', Menlo, Monaco, 'Courier New', Courier, monospace;
            font-size: 0.875em;
            border-radius: 3px;
        }

        /* Table styling */
        table {
            width: 100%;
            border-collapse: collapse;
            margin: 2rem 0;
        }

        th, td {
            border: 1px solid #e1dfdd;
            padding: 1rem;
            vertical-align: top;
        }

        th {
            background-color: #f8f9fa;
            font-weight: 600;
            text-align: left;
        }

        /* Timecode column */
        td:first-child {
            width: 120px;
            white-space: nowrap;
            font-family: 'JetBrains Mono', Menlo, Monaco, 'Courier New', Courier, monospace;
            font-size: 0.9em;
        }

        /* Images */
        img {
            max-width: 100%;
            height: auto;
            border-radius: 6px;
            margin-bottom: 1rem;
            display: block;
        }

        /* Note content */
        .note-content {
            line-height: 1.5;
        }

        /* Print optimizations */
        @media print {
            body {
                font-size: 12pt;
                line-height: 1.4;
                padding: 20px;
            }
            
            h1 { font-size: 24pt; }
            
            table {
                page-break-inside: avoid;
            }
            
            tr {
                page-break-inside: avoid;
            }
        }
");
                                sb.AppendLine("  </style>");
                                sb.AppendLine("</head>");
                                sb.AppendLine("<body>");

                                // Header
                                sb.AppendLine($"  <h1>{videoName}</h1>");
                                sb.AppendLine($"  <code class=\"video-path\">{_currentVideoPath}</code>");

                                // Start table
                                sb.AppendLine("  <table>");
                                sb.AppendLine("    <thead>");
                                sb.AppendLine("      <tr>");
                                sb.AppendLine("        <th>Timecode</th>");
                                sb.AppendLine("        <th>Image &amp; Notes</th>");
                                sb.AppendLine("      </tr>");
                                sb.AppendLine("    </thead>");
                                sb.AppendLine("    <tbody>");

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.4, "Processing images...");
                                });

                                int imageCounter = 1;
                                int totalNotes = _notes.Count;
                                foreach (var note in _notes.OrderBy(n => n.Timecode))
                                {
                                    // Update progress per note
                                    double noteProgress = 0.4 + ((double)imageCounter / totalNotes) * 0.5;
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        progressWindow.UpdateProgress(noteProgress, $"Processing note {imageCounter} of {totalNotes}...");
                                    });

                                    string originalImagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                                        ? note.EditedImagePath
                                        : note.ImagePath;

                                    try
                                    {
                                        // Convert image to base64
                                        byte[] imageBytes = await File.ReadAllBytesAsync(originalImagePath);
                                        string base64String = Convert.ToBase64String(imageBytes);

                                        // Determine MIME type
                                        string extension = Path.GetExtension(originalImagePath).ToLower();
                                        string mimeType = extension switch
                                        {
                                            ".png" => "image/png",
                                            ".jpg" => "image/jpeg",
                                            ".jpeg" => "image/jpeg",
                                            ".gif" => "image/gif",
                                            ".svg" => "image/svg+xml",
                                            ".webp" => "image/webp",
                                            ".bmp" => "image/bmp",
                                            _ => "image/jpeg"
                                        };

                                        string dataUrl = $"data:{mimeType};base64,{base64String}";

                                        // Process note text - convert newlines to <br> tags
                                        string noteContent = note.Notes
                                            .Replace("&", "&amp;")
                                            .Replace("<", "&lt;")
                                            .Replace(">", "&gt;")
                                            .Replace("\n", "<br>");

                                        // Create table row
                                        sb.AppendLine("      <tr>");
                                        sb.AppendLine($"        <td>{note.TimecodeString}</td>");
                                        sb.AppendLine("        <td>");
                                        sb.AppendLine($"          <img src=\"{dataUrl}\" alt=\"Frame at {note.TimecodeString}\">");
                                        sb.AppendLine($"          <div class=\"note-content\">{noteContent}</div>");
                                        sb.AppendLine("        </td>");
                                        sb.AppendLine("      </tr>");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error processing note {imageCounter}: {ex.Message}");

                                        // Fallback: try to use the original image
                                        try
                                        {
                                            byte[] imageBytes = await File.ReadAllBytesAsync(originalImagePath);
                                            string base64String = Convert.ToBase64String(imageBytes);
                                            string mimeType = "image/jpeg";
                                            string dataUrl = $"data:{mimeType};base64,{base64String}";

                                            string noteContent = note.Notes
                                                .Replace("&", "&amp;")
                                                .Replace("<", "&lt;")
                                                .Replace(">", "&gt;")
                                                .Replace("\n", "<br>");

                                            sb.AppendLine("      <tr>");
                                            sb.AppendLine($"        <td>{note.TimecodeString}</td>");
                                            sb.AppendLine("        <td>");
                                            sb.AppendLine($"          <img src=\"{dataUrl}\" alt=\"Frame at {note.TimecodeString}\">");
                                            sb.AppendLine($"          <div class=\"note-content\">{noteContent}</div>");
                                            sb.AppendLine("        </td>");
                                            sb.AppendLine("      </tr>");
                                        }
                                        catch
                                        {
                                            // Final fallback: just show the note without image
                                            string noteContent = note.Notes
                                                .Replace("&", "&amp;")
                                                .Replace("<", "&lt;")
                                                .Replace(">", "&gt;")
                                                .Replace("\n", "<br>");

                                            sb.AppendLine("      <tr>");
                                            sb.AppendLine($"        <td>{note.TimecodeString}</td>");
                                            sb.AppendLine("        <td>");
                                            sb.AppendLine($"          <div class=\"note-content\">{noteContent}</div>");
                                            sb.AppendLine("        </td>");
                                            sb.AppendLine("      </tr>");
                                        }
                                    }

                                    imageCounter++;
                                }

                                // Close table
                                sb.AppendLine("    </tbody>");
                                sb.AppendLine("  </table>");

                                // Footer
                                sb.AppendLine("  <footer>");
                                sb.AppendLine($"    <p>Generated by Union MPV Player on {DateTime.Now.ToString("yyyy-MM-dd HH:mm")}</p>");
                                sb.AppendLine("  </footer>");

                                sb.AppendLine("</body>");
                                sb.AppendLine("</html>");

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.9, "Writing file...");
                                });

                                await File.WriteAllTextAsync(exportPath, sb.ToString());
                            }
                            else if (format == "PDF")
                            {
                                // PDF export using Markdown2Pdf with table layout
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.4, "Generating PDF content...");
                                });

                                // Create markdown content with table format
                                var mdSb = new StringBuilder();
                                mdSb.AppendLine($"# {videoName}");
                                mdSb.AppendLine($"`{_currentVideoPath}`");
                                mdSb.AppendLine();

                                // Start the table
                                mdSb.AppendLine("| Timecode | Image & Notes |");
                                mdSb.AppendLine("|----------|---------------|");

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.5, "Processing images...");
                                });

                                int imageCounter = 1;
                                int totalNotes = _notes.Count;
                                foreach (var note in _notes.OrderBy(n => n.Timecode))
                                {
                                    // Update progress per note
                                    double noteProgress = 0.5 + ((double)imageCounter / totalNotes) * 0.3;
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        progressWindow.UpdateProgress(noteProgress, $"Processing note {imageCounter} of {totalNotes}...");
                                    });

                                    string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                                        ? note.EditedImagePath
                                        : note.ImagePath;

                                    // Convert image to base64 for embedding in markdown
                                    byte[] imageBytes = await File.ReadAllBytesAsync(sourcePath);
                                    string base64String = Convert.ToBase64String(imageBytes);

                                    // Determine MIME type
                                    string extension = Path.GetExtension(sourcePath).ToLower();
                                    string mimeType = extension switch
                                    {
                                        ".png" => "image/png",
                                        ".jpg" => "image/jpeg",
                                        ".jpeg" => "image/jpeg",
                                        ".gif" => "image/gif",
                                        ".svg" => "image/svg+xml",
                                        ".webp" => "image/webp",
                                        ".bmp" => "image/bmp",
                                        _ => "image/jpeg"
                                    };

                                    string dataUrl = $"data:{mimeType};base64,{base64String}";

                                    // Format the note content to work in a table cell
                                    // Replace newlines with <br> for HTML rendering
                                    string formattedNotes = note.Notes.Replace("\n", "<br>");

                                    // Create table row
                                    mdSb.Append($"| {note.TimecodeString} | ");
                                    mdSb.Append($"![Frame at {note.TimecodeString}]({dataUrl})<br>");
                                    mdSb.Append($"{formattedNotes} |");
                                    mdSb.AppendLine();

                                    imageCounter++;
                                }

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.8, "Converting to PDF...");
                                });

                                // Create temporary markdown file
                                string tempMdPath = Path.GetTempFileName();
                                tempMdPath = Path.ChangeExtension(tempMdPath, ".md");

                                try
                                {
                                    await File.WriteAllTextAsync(tempMdPath, mdSb.ToString());

                                    // Extract project name for the PDF converter
                                    string projectFolderName = !string.IsNullOrEmpty(projectRoot)
                                        ? Path.GetFileName(projectRoot)
                                        : Path.GetFileNameWithoutExtension(_currentVideoPath);

                                    // Use Markdown2Pdf for conversion
                                    await PdfExportHelper.ConvertMarkdownToPdfDirect(tempMdPath, exportPath, projectFolderName);

                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        progressWindow.UpdateProgress(0.9, "PDF generation complete...");
                                    });
                                }
                                finally
                                {
                                    // Clean up temp file
                                    if (File.Exists(tempMdPath))
                                    {
                                        File.Delete(tempMdPath);
                                    }
                                }
                            }
                            else // Markdown
                            {
                                // Standard Markdown format with table layout
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.4, "Generating Markdown content...");
                                });

                                var sb = new StringBuilder();
                                sb.AppendLine($"# {videoName}");
                                sb.AppendLine($"`{_currentVideoPath}`");
                                sb.AppendLine();

                                // Create images folder for the exported notes
                                string imagesFolder = Path.Combine(exportFolder, "images");
                                Directory.CreateDirectory(imagesFolder);

                                // Start the table
                                sb.AppendLine("| Timecode | Image & Notes |");
                                sb.AppendLine("|----------|---------------|");

                                // Process each note
                                int imageCounter = 1;
                                int totalNotes = _notes.Count;

                                foreach (var note in _notes.OrderBy(n => n.Timecode))
                                {
                                    // Update progress per note
                                    double noteProgress = 0.4 + ((double)imageCounter / totalNotes) * 0.5;
                                    await Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        progressWindow.UpdateProgress(noteProgress,
                                            $"Processing note {imageCounter} of {totalNotes}...");
                                    });

                                    string sourcePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                                        ? note.EditedImagePath
                                        : note.ImagePath;

                                    try
                                    {
                                        // Transcode the image to a smaller JPG
                                        string imageName = $"frame_{imageCounter:D3}.jpg";
                                        string transcodedPath = TranscodeToJpeg(
                                            sourcePath,
                                            imagesFolder,
                                            imageName,
                                            1920);

                                        // Get just the filename for the link
                                        string imageFileName = Path.GetFileName(transcodedPath);

                                        // Format the note content to work in a table cell
                                        // Replace newlines with <br> for HTML rendering
                                        string formattedNotes = note.Notes.Replace("\n", "<br>");

                                        // Create table row
                                        sb.Append($"| {note.TimecodeString} | ");
                                        sb.Append($"![Frame at {note.TimecodeString}](images/{imageFileName})<br>");
                                        sb.Append($"{formattedNotes} |");
                                        sb.AppendLine();
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error processing note {imageCounter}: {ex.Message}");

                                        // Fallback: just copy the original image
                                        string imageName = $"frame_{imageCounter:D3}{Path.GetExtension(sourcePath)}";
                                        string exportedImagePath = Path.Combine(imagesFolder, imageName);
                                        File.Copy(sourcePath, exportedImagePath, true);

                                        // Format the note content to work in a table cell
                                        string formattedNotes = note.Notes.Replace("\n", "<br>");

                                        // Create table row
                                        sb.Append($"| {note.TimecodeString} | ");
                                        sb.Append($"![Frame at {note.TimecodeString}](images/{imageName})<br>");
                                        sb.Append($"{formattedNotes} |");
                                        sb.AppendLine();
                                    }

                                    imageCounter++;
                                }

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    progressWindow.UpdateProgress(0.9, "Writing file...");
                                });

                                await File.WriteAllTextAsync(exportPath, sb.ToString());
                            }
                        }

                        // Final result path for UI
                        string finalExportPath = exportPath;

                        // Complete progress and prepare for completion popup
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            progressWindow.UpdateProgress(1.0, "Export completed!");
                        });

                        // Short delay to show completion
                        await Task.Delay(500);

                        // Show the appropriate popup on the UI thread
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            // Close the progress window
                            progressWindow.Close();

                            if (isAfterEffects || isPremierePro)
                            {
                                var popup = new ExportCompletePopup(hideOpenButton: true) { Tag = finalExportPath };

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
                                        Debug.WriteLine($"Error opening folder: {ex.Message}");
                                    }
                                };

                                await popup.ShowDialog(mainWindow);
                            }
                            else
                            {
                                var popup = new ExportCompletePopup() { Tag = finalExportPath };

                                popup.OpenFileRequested += (s, path) =>
                                {
                                    if (isUnionNotes)
                                    {
                                        try
                                        {
                                            // Use the notesViewer.exe to open the Union Notes file
                                            var psi = new System.Diagnostics.ProcessStartInfo
                                            {
                                                FileName = @"C:\UnionApps\unionProjects\notesViewer.exe",
                                                Arguments = $"\"{path}\"", // Make sure to quote the path
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
                                            Debug.WriteLine($"Error opening file: {ex.Message}");
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
                                        Debug.WriteLine($"Error opening folder: {ex.Message}");
                                    }
                                };

                                await popup.ShowDialog(mainWindow);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Handle any exceptions on the UI thread
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            progressWindow.Close();

                            // Show error message
                            var errorPopup = new MessageBoxTempView();
                            errorPopup.ShowMessage("Export Error",
                                $"An error occurred while exporting: {ex.Message}",
                                null, mainWindow);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                // Handle any outer exceptions
                progressWindow.Close();

                // Show error message
                var errorPopup = new MessageBoxTempView();
                errorPopup.ShowMessage("Export Error",
                    $"An error occurred while exporting: {ex.Message}",
                    null, mainWindow);
            }
        }

        private string TranscodeToJpeg(string sourcePath, string destFolder, string filename, int maxWidth = 1920)
        {
            try
            {
                Debug.WriteLine($"Transcoding image: {sourcePath}");

                // Ensure the destination has a jpg extension
                string destFilename = Path.GetFileNameWithoutExtension(filename) + ".jpg";
                string destPath = Path.Combine(destFolder, destFilename);

                // Make sure the destination directory exists
                Directory.CreateDirectory(destFolder);

                using (var input = File.OpenRead(sourcePath))
                {
                    // Load the source image
                    using (var bitmap = SkiaSharp.SKBitmap.Decode(input))
                    {
                        // Check if we need to resize
                        SkiaSharp.SKBitmap resizedBitmap;
                        if (bitmap.Width <= maxWidth)
                        {
                            // No resizing needed
                            resizedBitmap = bitmap;
                        }
                        else
                        {
                            // Calculate new height to maintain aspect ratio
                            float ratio = (float)maxWidth / bitmap.Width;
                            int newHeight = (int)(bitmap.Height * ratio);

                            // Create a new bitmap with the desired size
                            var imageInfo = new SkiaSharp.SKImageInfo(maxWidth, newHeight);
                            resizedBitmap = bitmap.Resize(imageInfo, SkiaSharp.SKFilterQuality.High);
                        }

                        // Encode as JPEG
                        using (var image = SkiaSharp.SKImage.FromBitmap(resizedBitmap))
                        using (var output = File.Create(destPath))
                        {
                            image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 85).SaveTo(output);
                        }

                        // Clean up the resized bitmap if it's different from the original
                        if (resizedBitmap != bitmap)
                        {
                            resizedBitmap.Dispose();
                        }
                    }
                }

                return destPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error transcoding image: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return sourcePath; // Return the original path if transcoding fails
            }
        }
    }
}