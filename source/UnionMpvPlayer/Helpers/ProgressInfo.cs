using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Helpers
{
    public class ProgressInfo
    {
        private readonly DateTime _startTime = DateTime.Now;

        public int CurrentFrame { get; set; }
        public int TotalFrames { get; set; }
        public string CurrentFile { get; set; }
        public string Status { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
        public double ProgressPercentage => (CurrentFrame / (double)TotalFrames) * 100;
        public TimeSpan ElapsedTime => DateTime.Now - _startTime;
        public double FramesPerSecond => CurrentFrame / ElapsedTime.TotalSeconds;
        public TimeSpan EstimatedTimeRemaining => TimeSpan.FromSeconds(
            (TotalFrames - CurrentFrame) / Math.Max(FramesPerSecond, 0.1)
        );
    }
}
