using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using UnionMpvPlayer.Helpers;
using UnionMpvPlayer.Converters;

namespace UnionMpvPlayer.Views
{
    public partial class VideoInfoPopup : Window
    {
        public ObservableCollection<MetadataItem> Metadata { get; } = new();
        public ICommand LinkCommand { get; }
        public ICommand ProjectCommand { get; }
        public VideoInfoPopup()
        {
            LinkCommand = new RelayCommand<string>(CopyToClipboard);
            ProjectCommand = new RelayCommand<string>(OpenFileInSystem);

            // Set DataContext before InitializeComponent
            DataContext = this;

            InitializeComponent();
        }

        private void OpenFileInSystem(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                    //Debug.WriteLine($"Opened directory: {path}");
                    var toast = new ToastView();
                    toast.ShowToast("Success", "Directory opened.", this);
                }
                else if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                    //Debug.WriteLine($"Opened file: {path}");
                    var toast = new ToastView();
                    toast.ShowToast("Success", "File opened.", this);
                }
                else
                {
                    //Debug.WriteLine($"Path does not exist: {path}");
                    var toast = new ToastView();
                    toast.ShowToast("Warning", "Path does not exist.", this);
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"Error opening path: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Error opening path.", this);
            }
        }

        private void CopyToClipboard(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                clipboard.SetTextAsync(path);
                //Debug.WriteLine($"Copied to clipboard: {path}");
                var toast = new ToastView();
                toast.ShowToast("Success", "Path copied to clipboard.", this);
            }
            else
            {
                //Debug.WriteLine("Clipboard service is not available.");
                var toast = new ToastView();
                toast.ShowToast("Warning", "Failed to copy.", this);
            }
        }

        public async void LoadMetadata(string filePath)
        {
            try
            {
                string exifToolPath = ExifToolManager.GetExifToolPath();
                if (!File.Exists(exifToolPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Metadata.Add(new MetadataItem("Error", "ExifTool is not available."));
                    });
                    return;
                }

                string arguments = $"-s " +
                   "-File:FileName " +
                   "-File:Directory " +
                   "-File:FileSize " +
                   "-File:FileType " +
                   "-QuickTime:Duration " +
                   "-QuickTime:ImageWidth " +
                   "-QuickTime:ImageHeight " +
                   "-QuickTime:CompressorName " +
                   "-QuickTime:BitDepth " +
                   "-QuickTime:PlaybackFrameRate " +
                   "-QuickTime:StartTimecode " +
                   "-XMP:VideoFrameRate " +
                   "-XMP:VideoFieldOrder " +
                   "-XMP:VideoPixelAspectRatio " +
                   "-XMP:AeProjectLinkFullPath " +
                   "-XMP:WindowsAtomUncProjectPath " +
                   "-XMP:MacAtomPosixProjectPath " +
                   "-Composite:ImageSize " +
                   "-Composite:AvgBitrate " +
                   $"\"{filePath}\"";


                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(error))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Metadata.Add(new MetadataItem("Error", "Failed to retrieve metadata."));
                        });
                        return;
                    }

                    // Log raw output
                    //Debug.WriteLine("Raw ExifTool Output:");
                    //Debug.WriteLine(output);

                    // Parse output
                    var metadata = ParseExifToolOutput(output);

                    // Log parsed keys
                    //Debug.WriteLine("Parsed Metadata Keys:");
                    foreach (var key in metadata.Keys)
                    {
                        //Debug.WriteLine($"Key: {key}, Value: {metadata[key]}");
                    }

                    // Update Metadata collection on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Metadata.Clear();
                        Metadata.Add(new MetadataItem("File Name", metadata.TryGetValue("FileName", out var fileName) ? fileName : "N/A"));
                        Metadata.Add(new MetadataItem("Directory", metadata.TryGetValue("Directory", out var directory) ? directory : "N/A"));
                        Metadata.Add(new MetadataItem("File Size", metadata.TryGetValue("FileSize", out var fileSize) ? fileSize : "N/A"));
                        Metadata.Add(new MetadataItem("File Type", metadata.TryGetValue("FileType", out var fileType) ? fileType : "N/A"));
                        Metadata.Add(new MetadataItem("Duration", metadata.TryGetValue("Duration", out var duration) ? duration : "N/A"));
                        Metadata.Add(new MetadataItem("Image Width", metadata.TryGetValue("ImageWidth", out var imageWidth) ? imageWidth : "N/A"));
                        Metadata.Add(new MetadataItem("Image Height", metadata.TryGetValue("ImageHeight", out var imageHeight) ? imageHeight : "N/A"));
                        Metadata.Add(new MetadataItem("Compressor Name", metadata.TryGetValue("CompressorName", out var compressorName) ? compressorName : "N/A"));
                        Metadata.Add(new MetadataItem("Bit Depth", metadata.TryGetValue("BitDepth", out var bitDepth) ? bitDepth : "N/A"));
                        Metadata.Add(new MetadataItem("Frame Rate", metadata.TryGetValue("VideoFrameRate", out var frameRate) ? frameRate : "N/A"));
                        Metadata.Add(new MetadataItem("Start Timecode", metadata.TryGetValue("StartTimecode", out var startTimecode) ? startTimecode : "N/A"));
                        Metadata.Add(new MetadataItem("Video Field Order", metadata.TryGetValue("VideoFieldOrder", out var fieldOrder) ? fieldOrder : "N/A"));
                        Metadata.Add(new MetadataItem("Pixel Aspect Ratio", metadata.TryGetValue("VideoPixelAspectRatio", out var pixelAspectRatio) ? pixelAspectRatio : "N/A"));
                        Metadata.Add(new MetadataItem("AE Project Link", metadata.TryGetValue("AeProjectLinkFullPath", out var aePath) ? aePath : "N/A"));
                        string premierePathWin = metadata.TryGetValue("WindowsAtomUncProjectPath", out var pwPath)
                        ? pwPath.StartsWith(@"\\?\") ? pwPath[4..] : pwPath
                        : "N/A";
                        Metadata.Add(new MetadataItem("Premiere Project (Win)", premierePathWin));
                        Metadata.Add(new MetadataItem("Premiere Project (Mac)", metadata.TryGetValue("MacAtomPosixProjectPath", out var pmPath) ? pmPath : "N/A"));
                        Metadata.Add(new MetadataItem("Image Size", metadata.TryGetValue("ImageSize", out var imageSize) ? imageSize : "N/A"));
                        Metadata.Add(new MetadataItem("Average Bitrate", metadata.TryGetValue("AvgBitrate", out var avgBitrate) ? avgBitrate : "N/A"));
                    });

                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Metadata.Add(new MetadataItem("Error", $"Failed to load metadata: {ex.Message}"));
                });
            }
        }

        private Dictionary<string, string> ParseExifToolOutput(string output)
        {
            var metadata = new Dictionary<string, string>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // ExifTool may add unexpected formatting; ensure key-value parsing
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    metadata[key] = value;
                }
            }

            return metadata;
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }
    }

    public class MetadataItem
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public MetadataItem(string name, string value)
        {
            Name = name ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }
}
