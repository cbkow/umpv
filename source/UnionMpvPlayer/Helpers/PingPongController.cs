using Avalonia.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System;
using UnionMpvPlayer.Helpers;
using System.Threading;

public class PingPongController
{
    private readonly IntPtr mpvHandle;
    private string? currentVideoPath;
    private double? currentPosition;
    private bool isForwardPlaying = true;
    private readonly DispatcherTimer monitorTimer;

    public bool IsActive { get; private set; }

    public PingPongController(IntPtr mpvHandle)
    {
        this.mpvHandle = mpvHandle;
        this.monitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        this.monitorTimer.Tick += MonitorPlayback;
    }

    public async Task StartPingPong(string videoPath, double position)
    {
        if (IsActive) return;

        IsActive = true;
        currentVideoPath = videoPath;
        currentPosition = position;

        // Set keep-open to no for continuous playback
        MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", "no");

        // Initialize with forward playback
        isForwardPlaying = true;
        MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "yes" });
        MPVInterop.mpv_command(mpvHandle, new[] { "set", "play-dir", "+" });
        MPVInterop.mpv_command(mpvHandle, new[] { "seek", "0", "absolute-percent" });
        MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "no" });

        monitorTimer.Start();
    }

    private void MonitorPlayback(object? sender, EventArgs e)
    {
        try
        {
            var position = MPVInterop.GetDoubleProperty(mpvHandle, "time-pos");
            var duration = MPVInterop.GetDoubleProperty(mpvHandle, "duration");
            var currentDir = MPVInterop.GetStringProperty(mpvHandle, "play-dir");
            var isPaused = MPVInterop.GetStringProperty(mpvHandle, "pause");

            Debug.WriteLine($"Position: {position}, Duration: {duration}, Direction: {currentDir}, Paused: {isPaused}");

            if (!position.HasValue || !duration.HasValue) return;

            currentPosition = position.Value;

            if (isForwardPlaying && position.Value >= duration.Value - 0.5)
            {
                Debug.WriteLine($"Triggering backward switch at position {position.Value}");
                SwitchToBackward();
                Debug.WriteLine("After switch - Direction: " + MPVInterop.GetStringProperty(mpvHandle, "play-dir"));
                Debug.WriteLine("After switch - Position: " + MPVInterop.GetDoubleProperty(mpvHandle, "time-pos"));
                Debug.WriteLine("After switch - Paused: " + MPVInterop.GetStringProperty(mpvHandle, "pause"));
            }
            else if (!isForwardPlaying && position.Value <= 0.5)
            {
                Debug.WriteLine($"Triggering forward switch at position {position.Value}");
                SwitchToForward();
                Debug.WriteLine("After switch - Direction: " + MPVInterop.GetStringProperty(mpvHandle, "play-dir"));
                Debug.WriteLine("After switch - Position: " + MPVInterop.GetDoubleProperty(mpvHandle, "time-pos"));
                Debug.WriteLine("After switch - Paused: " + MPVInterop.GetStringProperty(mpvHandle, "pause"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in monitoring: {ex.Message}");
        }
    }

    private void SwitchToBackward()
    {
        try
        {
            isForwardPlaying = false;
            Debug.WriteLine("Starting backward switch sequence");

            // Make sure we're at the end before switching direction
            MPVInterop.mpv_command(mpvHandle, new[] { "seek", "100", "absolute-percent" });
            Debug.WriteLine("Sought to end");

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "yes" });
            Debug.WriteLine("Set pause");

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "play-dir", "-" });
            Debug.WriteLine("Set direction backward");

            // Small delay to let commands process
            Thread.Sleep(50);

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "no" });
            Debug.WriteLine("Unset pause");

            Debug.WriteLine("Completed backward switch sequence");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in SwitchToBackward: {ex.Message}");
        }
    }

    private void SwitchToForward()
    {
        try
        {
            isForwardPlaying = true;
            Debug.WriteLine("Starting forward switch sequence");

            // Make sure we're at the start before switching direction
            MPVInterop.mpv_command(mpvHandle, new[] { "seek", "0", "absolute-percent" });
            Debug.WriteLine("Sought to start");

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "yes" });
            Debug.WriteLine("Set pause");

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "play-dir", "+" });
            Debug.WriteLine("Set direction forward");

            // Small delay to let commands process
            Thread.Sleep(50);

            MPVInterop.mpv_command(mpvHandle, new[] { "set", "pause", "no" });
            Debug.WriteLine("Unset pause");

            Debug.WriteLine("Completed forward switch sequence");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in SwitchToForward: {ex.Message}");
        }
    }

    public async Task StopPingPong()
    {
        if (!IsActive) return;

        monitorTimer.Stop();
        IsActive = false;

        // Reset keep-open to always
        MPVInterop.mpv_set_option_string(mpvHandle, "keep-open", "always");

        // Restore forward playback at current position
        if (currentPosition.HasValue)
        {
            var positionStr = currentPosition.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            MPVInterop.mpv_command(mpvHandle, new[] { "set", "play-dir", "+" });
            MPVInterop.mpv_command(mpvHandle, new[] { "seek", positionStr, "absolute" });
        }
    }

    public void Dispose()
    {
        monitorTimer.Stop();
        IsActive = false;
    }
}