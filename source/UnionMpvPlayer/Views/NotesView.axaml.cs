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
                if (apply && !string.IsNullOrEmpty(imagePath))
                {
                    MainWindow.Current.HideMpvWindow();

                    MainWindow.Current.overlayImage.Source = new Bitmap(imagePath);
                    MainWindow.Current.overlayImage.IsVisible = true;
                }
                else
                {
                    MainWindow.Current.overlayImage.IsVisible = false;

                    MainWindow.Current.ShowMpvWindow();
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error in ApplyImageOverlay: {ex.Message}\nStack trace: {ex.StackTrace}");
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
                if (System.Text.RegularExpressions.Regex.IsMatch(dir.Name, @"^\d{6,7}_.*"))
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
            string extension = isHtml ? ".html" : ".md";

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
            var sb = new StringBuilder();

            if (isUnionNotes)
            {
                // Union Notes format
                sb.AppendLine($"# {videoName}");
                sb.AppendLine("```File Path");
                sb.AppendLine($"{_currentVideoPath}");
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("\\");
                sb.AppendLine("&nbsp;");
                sb.AppendLine();
                foreach (var note in _notes)
                {
                    string imagePath = !string.IsNullOrEmpty(note.EditedImagePath) && File.Exists(note.EditedImagePath)
                        ? note.EditedImagePath : note.ImagePath;
                    sb.AppendLine($"### {note.TimecodeString}");
                    sb.AppendLine($"![Frame at {note.TimecodeString}]({imagePath})");
                    sb.AppendLine();
                    sb.AppendLine(note.Notes);
                    sb.AppendLine();
                    sb.AppendLine("\\");
                    sb.AppendLine("&nbsp;");
                    sb.AppendLine();
                }
                await File.WriteAllTextAsync(exportPath, sb.ToString());
            }
            else if (isMarkdown)
            {
                // Standard Markdown format
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

            if (isHtml)
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
            else if (ExportFormatComboBox.SelectedIndex == 2) // PDF
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
            else
            {
                await File.WriteAllTextAsync(exportPath, sb.ToString());
            }


            var popup = new ExportCompletePopup { Tag = exportPath };

            popup.OpenFileRequested += (s, path) =>
            {
                if (isUnionNotes)
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = @"C:\UnionApps\unionProjects\notesViewer.exe",
                            Arguments = path,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        //Debug.WriteLine($"Error opening UnionNotes viewer: {ex.Message}");
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