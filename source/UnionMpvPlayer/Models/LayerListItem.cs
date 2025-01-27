using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnionMpvPlayer.Models
{

    public class LayerListItem
    {
        public string Name { get; set; }
        public List<string> Channels { get; set; } = new();
        public bool HasRGB { get; set; }
        public bool HasRGBA { get; set; }
        public string DisplayName => $"{Name} ({(HasRGBA ? "RGBA" : HasRGB ? "RGB" : "Other")})";
    }
}
