using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Helpers
{
    public class EXRLayer
    {
        public string Name { get; set; }
        public List<string> Channels { get; set; } = new List<string>();
        public bool HasR { get; set; }
        public bool HasG { get; set; }
        public bool HasB { get; set; }
        public bool HasA { get; set; }

        public bool HasRGB => HasR && HasG && HasB;
        public bool HasRGBA => HasRGB && HasA;
    }
}
