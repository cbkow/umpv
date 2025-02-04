using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;

namespace UnionMpvPlayer.Helpers
{
    public static class ControlExtensions
    {
        public static T? FindDescendantOfType<T>(this Control control) where T : Control
        {
            if (control is T match)
                return match;

            var children = control.GetVisualChildren();
            foreach (var child in children)
            {
                if (child is Control childControl)
                {
                    var result = FindDescendantOfType<T>(childControl);
                    if (result != null)
                        return result;
                }
            }

            return default;
        }
    }
}
