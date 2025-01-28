using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Runtime.InteropServices;
using UnionMpvPlayer.Helpers;

namespace UnionMpvPlayer.Views
{
    public partial class BackgroundWindow : Window
    {
        private Window? _parentWindow;
        private Panel? _videoContainer;

        public BackgroundWindow()
        {
            InitializeComponent();
            
            IsHitTestVisible = false; // Make it non-interactive
        }

        public void Initialize(Window parent, Panel videoContainer)
        {
            _parentWindow = parent;
            _videoContainer = videoContainer;

            // Set the background window as owned by the parent window
            Owner = _parentWindow;

            // Hide from Alt-Tab
            var handle = TryGetPlatformHandle();
            if (handle != null)
            {
                int exStyle = WindowManagement.GetWindowLong(handle.Handle, WindowManagement.GWL_EXSTYLE);
                exStyle |= WindowManagement.WS_EX_TOOLWINDOW;  // Add TOOLWINDOW style
                exStyle &= ~WindowManagement.WS_EX_APPWINDOW;  // Remove APPWINDOW style
                WindowManagement.SetWindowLong(handle.Handle, WindowManagement.GWL_EXSTYLE, exStyle);
            }

            // Subscribe to parent window events
            _parentWindow.PositionChanged += ParentWindow_PositionChanged;
            _parentWindow.PropertyChanged += ParentWindow_PropertyChanged;
            _parentWindow.GotFocus += ParentWindow_GotFocus; // Subscribe to focus event
            _videoContainer.PropertyChanged += VideoContainer_PropertyChanged;

            UpdatePosition();
        }

        private void ParentWindow_GotFocus(object? sender, EventArgs e)
        {
            EnsureZOrder();
        }

        private void VideoContainer_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == nameof(Bounds) ||
                e.Property.Name == nameof(Width) ||
                e.Property.Name == nameof(Height))
            {
                UpdatePosition();
            }
        }

        private void ParentWindow_PositionChanged(object? sender, PixelPointEventArgs e)
        {
            UpdatePosition();
        }

        private void ParentWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property.Name == nameof(Window.WindowState) || e.Property.Name == nameof(Window.IsFocused))
            {
                UpdatePosition();
                EnsureZOrder(); // Ensure Z-Order is updated
                this.Topmost = false; // Ensure it's below the main window

            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        public void EnsureZOrder()
        {
            var handle = TryGetPlatformHandle();
            var parentHandle = _parentWindow?.TryGetPlatformHandle();

            if (handle != null && parentHandle != null)
            {
                // Position this window just behind the parent window
                SetWindowPos(handle.Handle, parentHandle.Handle, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        public void UpdatePosition()
        {
            if (_parentWindow == null || _videoContainer == null) return;

            // Apply the tool window style on the first position update
            var handle = TryGetPlatformHandle();
            if (handle != null)
            {
                // Hide from Alt-Tab
                int exStyle = WindowManagement.GetWindowLong(handle.Handle, WindowManagement.GWL_EXSTYLE);
                exStyle |= WindowManagement.WS_EX_TOOLWINDOW;
                exStyle &= ~WindowManagement.WS_EX_APPWINDOW;
                WindowManagement.SetWindowLong(handle.Handle, WindowManagement.GWL_EXSTYLE, exStyle);
            }

            var containerBounds = _videoContainer.Bounds;
            var containerPosition = _videoContainer.TranslatePoint(new Point(0, 0), _parentWindow);

            if (_parentWindow == null || _videoContainer == null) return;


            if (containerPosition.HasValue)
            {
                var screenPoint = _parentWindow.PointToScreen(containerPosition.Value);

                Position = new PixelPoint(
                    (int)screenPoint.X,
                    (int)screenPoint.Y
                );

                Width = containerBounds.Width;
                Height = containerBounds.Height;

                EnsureZOrder();  // Add this call
            }
        }

        public void TriggerUpdate()
        {
            UpdatePosition();
        }


        protected override void OnClosed(EventArgs e)
        {
            if (_parentWindow != null)
            {
                _parentWindow.PositionChanged -= ParentWindow_PositionChanged;
                _parentWindow.PropertyChanged -= ParentWindow_PropertyChanged;
                _parentWindow.GotFocus -= ParentWindow_GotFocus;
            }

            // Add this cleanup
            if (_videoContainer != null)
            {
                _videoContainer.PropertyChanged -= VideoContainer_PropertyChanged;
            }

            base.OnClosed(e);
        }

    }
}