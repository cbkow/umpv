using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using static UnionMpvPlayer.Helpers.OpenEXRCoreInterop;
using static UnionMpvPlayer.Helpers.ProgressInfo;


namespace UnionMpvPlayer.Helpers
{
     public class EXRSequenceHandler
    {
        private string _firstFramePath;
        private string _sequencePattern;
        private readonly ParallelOptions _parallelOptions;
        private readonly ConcurrentDictionary<int, bool> _processedFrames = new();
        private bool _isProcessing = false;
        private readonly object _processingLock = new object();

        private static readonly string CacheConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "umpv", "exrcachepath.json"
        );

        public EXRSequenceHandler()
        {
            _parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2
            };
        }

        public async Task<Dictionary<string, EXRLayer>> AnalyzeEXRFile(string filePath)
        {
            IntPtr fileHandle = IntPtr.Zero;
            var layers = new Dictionary<string, EXRLayer>();
            bool isInitialized = false;

            try
            {
                int rv = exr_start_read(out fileHandle, filePath, IntPtr.Zero);
                if (rv != EXR_ERR_SUCCESS)
                    throw new Exception($"Failed to open EXR: {Marshal.PtrToStringAnsi(exr_get_error_code_as_string(rv))}");

                isInitialized = true;
                Debug.WriteLine($"Initialized file handle: 0x{fileHandle.ToInt64():X}");

                // Get channel list
                IntPtr chlistPtr = IntPtr.Zero;
                rv = exr_get_channels(fileHandle, 0, ref chlistPtr);
                if (rv != EXR_ERR_SUCCESS)
                    throw new Exception("Failed to get channel list");

                // Process channel list
                ProcessChannelList(chlistPtr, layers);
                return layers;
            }
            finally
            {
                if (fileHandle != IntPtr.Zero && isInitialized)
                {
                    try { exr_finish(ref fileHandle); }
                    catch (Exception ex) { Debug.WriteLine($"Cleanup error: {ex.Message}"); }
                    finally { isInitialized = false; }
                }
            }
        }

        public static string GetCacheFolderPath()
        {
            if (File.Exists(CacheConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(CacheConfigPath);
                    var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (config != null && config.TryGetValue("CachePath", out var customPath) && Directory.Exists(customPath))
                    {
                        return customPath;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading cache config: {ex.Message}");
                }
            }

            // Default cache folder
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "umpv", "exrcache"
            );
        }


        private void SaveCachePathToJson(string path)
        {
            var config = new Dictionary<string, string> { { "CachePath", path } };
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(Path.GetDirectoryName(CacheConfigPath));
                File.WriteAllText(CacheConfigPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving cache path: {ex.Message}");
            }
        }


        public bool HasChannel(string exrFilePath, string channel)
        {
            try
            {
                var channels = AnalyzeEXRFile(exrFilePath).Result;
                return channels.Values.SelectMany(layer => layer.Channels).Contains(channel);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking channel: {ex.Message}");
                return false;
            }
        }


        private void ProcessChannelList(IntPtr chlistPtr, Dictionary<string, EXRLayer> layers)
        {
            Debug.WriteLine("\n=== Processing Channel List ===");
            var channelList = Marshal.PtrToStructure<exr_attr_chlist_t>(chlistPtr);
            var entrySize = Marshal.SizeOf<exr_attr_chlist_entry_t>();
            
            // Group channels by layer path
            var channelSets = new Dictionary<string, HashSet<(string path, string component)>>();
            
            for (int i = 0; i < channelList.num_channels; i++)
            {
                var entryPtr = IntPtr.Add(channelList.entries, i * entrySize);
                var entry = Marshal.PtrToStructure<exr_attr_chlist_entry_t>(entryPtr);
                
                if (entry.length > 0 && entry.length < 256 && entry.name != IntPtr.Zero)
                {
                    byte[] nameBytes = new byte[entry.length];
                    Marshal.Copy(entry.name, nameBytes, 0, (int)entry.length);
                    string channelName = Encoding.ASCII.GetString(nameBytes);
                    
                    var parts = channelName.Split('.');
                    if (parts.Length >= 2)
                    {
                        // Build layer path (e.g. "ViewLayer.Combined")
                        string layerPath = string.Join(".", parts.Take(parts.Length - 1));
                        string component = parts.Last();
                        
                        if (!channelSets.ContainsKey(layerPath))
                            channelSets[layerPath] = new HashSet<(string, string)>();
                        
                        channelSets[layerPath].Add((channelName, component));
                    }
                }
            }
        
            // Create layers from channel sets
            foreach (var set in channelSets)
            {
                var components = set.Value.Select(x => x.component).ToHashSet();
                bool hasRGB = components.Contains("R") && components.Contains("G") && components.Contains("B");
                bool hasRGBA = hasRGB && components.Contains("A");
                
                if (hasRGB || hasRGBA)  // Only create layers with complete RGB/RGBA sets
                {
                    var layer = new EXRLayer { Name = set.Key };
                    layer.Channels.AddRange(set.Value.Select(x => x.path));
                    
                    layer.HasR = components.Contains("R");
                    layer.HasG = components.Contains("G");
                    layer.HasB = components.Contains("B");
                    layer.HasA = components.Contains("A");
                    
                    layers[set.Key] = layer;
                    Debug.WriteLine($"Created layer: {set.Key} ({(hasRGBA ? "RGBA" : "RGB")})");
                }
            }
        }

        private int ExtractFrameNumber(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Match frame number surrounded by non-digit characters (e.g., "_####.", ".####-", "-####_")
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"[^\d]*(\d+)[^\d]*$");

            if (match.Success && int.TryParse(match.Groups[1].Value, out int frameNumber))
            {
                return frameNumber;
            }

            throw new ArgumentException($"Could not extract frame number from filename: {fileName}");
        }



        public string GetCachePath(string originalFile, string layerName)
        {
            var cacheDir = GetCacheFolderPath();
            var sequenceDir = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(originalFile));
            var layerDir = Path.Combine(sequenceDir, layerName);

            // Ensure all directories exist
            Directory.CreateDirectory(layerDir);

            return Path.Combine(layerDir, $"{Path.GetFileNameWithoutExtension(originalFile)}_{layerName}.exr");
        }



        private string GetSequencePattern(string firstFrame)
        {
            var fileName = Path.GetFileName(firstFrame);

            // Match frame number with various separators
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"[_.-](\d+)[_.-]");

            if (match.Success)
            {
                var frameNumber = match.Groups[1].Value;
                var separator = fileName[match.Index]; // Get the separator character
                return fileName.Replace($"{separator}{frameNumber}", $"{separator}*");
            }

            throw new ArgumentException($"Invalid sequence filename format: {fileName}");
        }


        public string GetCachePattern(string originalFile, string layerName)
        {
            var cacheDir = GetCacheFolderPath();
            var sequenceDir = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(originalFile));
            var layerDir = Path.Combine(sequenceDir, layerName);

            // Ensure the directory exists (optional, depends on when this is called)
            Directory.CreateDirectory(layerDir);

            return Path.Combine(layerDir, $"{Path.GetFileNameWithoutExtension(originalFile)}_*.exr");
        }



        private string ConvertToNumberedPattern(string pattern, string frameNumber)
        {
            var padding = frameNumber.Length;
            return pattern.Replace("*", $"%0{padding}d");
        }

        private string BuildOIIOSourcePattern(string firstFrame)
        {
            var directory = Path.GetDirectoryName(firstFrame);
            var basePattern = GetSequencePattern(firstFrame);
            var frameNum = ExtractFrameNumber(firstFrame).ToString("D4");
            var numberedPattern = ConvertToNumberedPattern(basePattern, frameNum);
            return Path.Combine(directory, numberedPattern);
        }

        private string GetFramePattern(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"_(\d+).exr$");
            if (!match.Success)
                throw new ArgumentException($"Invalid sequence filename format: {fileName}");
                
            var frameNumber = match.Groups[1].Value;
            var padding = frameNumber.Length;
            return $"_%0{padding}d.exr";
        }

        public async Task ProcessSequence(string firstFrame, string selectedLayer,
            string outputPath, IProgress<ProgressInfo> progress, CancellationToken cancellation)
        {
            lock (_processingLock)
            {
                if (_isProcessing)
                {
                    Debug.WriteLine("Sequence processing already in progress");
                    return;
                }
                _isProcessing = true;
            }

            try
            {
                _processedFrames.Clear();
                Debug.WriteLine("\n=== Starting Sequence Processing ===");

                var directory = Path.GetDirectoryName(firstFrame);
                var basePattern = GetSequencePattern(firstFrame);
                var files = Directory.GetFiles(directory, basePattern)
                                     .OrderBy(f => ExtractFrameNumber(f))
                                     .ToList();

                if (!files.Any())
                    throw new InvalidOperationException("No matching files found in sequence");

                Debug.WriteLine($"Found {files.Count} files to process");
                Debug.WriteLine($"Using {Environment.ProcessorCount / 2} threads");

                var cacheDir = GetCacheFolderPath();
                var sequenceDir = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(firstFrame));
                var layerDir = Path.Combine(sequenceDir, selectedLayer);

                // Ensure cache directories exist
                Directory.CreateDirectory(layerDir);

                var totalFrames = files.Count;
                var processedFrames = 0;
                var semaphore = new SemaphoreSlim(Environment.ProcessorCount / 2);
                var tasks = new List<Task>();

                foreach (var file in files)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellation);
                        try
                        {
                            var frameNum = ExtractFrameNumber(file);
                            if (!_processedFrames.TryAdd(frameNum, true))
                            {
                                Debug.WriteLine($"Skipping duplicate frame {frameNum}");
                                return;
                            }

                            var outFile = Path.Combine(
                                layerDir,
                                $"{Path.GetFileNameWithoutExtension(firstFrame)}_{frameNum:D4}.exr"
                            );

                            var oiioPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenImageIO", "oiiotool.exe");

                            Debug.WriteLine($"Processing frame {frameNum}: {file} -> {outFile}");

                            using var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = oiioPath,
                                    Arguments = new StringBuilder()
                                        .Append("--threads 2 --scanline --native ")
                                        .Append($"\"{file}\" ")
                                        .Append($"--ch {selectedLayer}.R,{selectedLayer}.G,{selectedLayer}.B ")
                                        .Append("--chnames R,G,B ")
                                        .Append($"-o \"{outFile}\"")
                                        .ToString(),
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true
                                }
                            };

                            process.Start();
                            await process.WaitForExitAsync(cancellation);

                            if (process.ExitCode != 0)
                                throw new Exception($"OIIO failed processing frame {frameNum}");

                            var completed = Interlocked.Increment(ref processedFrames);
                            progress?.Report(new ProgressInfo
                            {
                                CurrentFrame = completed,
                                TotalFrames = totalFrames
                            });
                            Debug.WriteLine($"Completed processing frame {frameNum}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellation));
                }

                await Task.WhenAll(tasks);
                Debug.WriteLine("=== Sequence Processing Complete ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ProcessSequence error: {ex.Message}");
                throw;
            }
            finally
            {
                _isProcessing = false;
            }
        }



        public bool IsDisplayableLayer(EXRLayer layer)
        {
            return layer != null && (layer.HasRGBA || layer.HasRGB);
        }


        public void InitializeSequence(string firstFramePath)
        {
            _firstFramePath = firstFramePath;
            
            // Create sequence pattern (e.g., "name.*.exr")
            var fileName = Path.GetFileName(firstFramePath);
            var frameNumber = Path.GetFileNameWithoutExtension(fileName).Split('.').Last();
            _sequencePattern = fileName.Replace(frameNumber, "*");
        }
    
    }
}
