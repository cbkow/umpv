using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Helpers
{
    public static class ThrottleDispatcher
    {
        private static DateTime _lastExecution = DateTime.MinValue;
        private static Timer? _timer;

        public static void Throttle(int intervalMilliseconds, Action action)
        {
            var now = DateTime.UtcNow;
            if (now.Subtract(_lastExecution).TotalMilliseconds >= intervalMilliseconds)
            {
                _lastExecution = now;
                action();
            }
            else
            {
                _timer?.Dispose();
                _timer = new Timer(_ => action(), null, intervalMilliseconds, Timeout.Infinite);
            }
        }
    }
}
