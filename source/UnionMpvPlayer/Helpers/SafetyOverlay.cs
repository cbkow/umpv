using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Helpers
{
    public class SafetyOverlay
    {
        public string Name { get; set; } = string.Empty;
        public Func<string, string> GenerateFilter { get; set; } = _ => string.Empty;
        public bool IsActive { get; set; } = false;
    }

}
