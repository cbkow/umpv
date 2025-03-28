using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DynamicData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using UnionMpvPlayer.ViewModels;

namespace UnionMpvPlayer.Views
{
    public partial class ImageEditWindow : Window
    {
        private class DrawingOperation
        {
            public List<Line> Lines { get; set; } = new List<Line>();
            public TextBox? TextBox { get; set; }
            public Border? ResizeGrip { get; set; }
            public Border? DragHandle { get; set; }
            public bool IsErase { get; set; }

            public bool IsTextBox { get; set; }
        }

        private bool _isDrawing;
        private Point _lastPoint;
        private readonly string _imagePath;
        private readonly string? _editedImagePath;
        private readonly Bitmap _originalBitmap;
        private IBrush _currentBrush = new SolidColorBrush(Color.Parse("#ff2d55"));
        private double _currentThickness = 6;
        private readonly double _scale;
        private Stack<DrawingOperation> _undoStack = new Stack<DrawingOperation>();
        private Stack<DrawingOperation> _redoStack = new Stack<DrawingOperation>();
        private List<Line> _currentStroke = new List<Line>();
        private List<Line> _currentErase = new List<Line>();
        private Button? _selectedColorButton;
        private bool _isErasing;
        private const double ERASER_SIZE = 20;
        public event EventHandler<ImageEditedEventArgs>? ImageEdited;
        private bool _isArrowMode;
        private Point _arrowStart;
        private List<Line> _arrowPreview = new List<Line>();
        private bool _isTextMode = false;
        private bool _isDraggingTextBox = false;
        private Point _textBoxOffset;
        private bool _isResizingTextBox = false;
        private TextBox? _activeTextBox = null;
        private Border? _dragHandle = null;
        private Border? _resizeGrip = null;


        public ImageEditWindow(string imagePath, string? editedImagePath = null)
        {
            InitializeComponent();
            Activated += (_, _) => SetActiveBorder(true);
            Deactivated += (_, _) => SetActiveBorder(false);
            _imagePath = imagePath;
            _originalBitmap = new Bitmap(imagePath);
            string pathToLoad = !string.IsNullOrEmpty(editedImagePath) && File.Exists(editedImagePath)
                ? editedImagePath
                : imagePath;

            _originalBitmap = new Bitmap(pathToLoad);
            BackgroundImage.Source = _originalBitmap;

            // Calculate a reasonable window size (e.g., 70% of screen size)
            var screenWidth = Screens.Primary.Bounds.Width * 0.55;
            var screenHeight = Screens.Primary.Bounds.Height * 0.55;

            // Calculate scale to fit within these bounds while maintaining aspect ratio
            var widthScale = screenWidth / _originalBitmap.PixelSize.Width;
            var heightScale = screenHeight / _originalBitmap.PixelSize.Height;
            _scale = Math.Min(widthScale, heightScale);

            // Set fixed sizes for both image and canvas
            var scaledWidth = _originalBitmap.PixelSize.Width * _scale;
            var scaledHeight = _originalBitmap.PixelSize.Height * _scale;

            // Set window size with some padding
            Width = scaledWidth + 40;  // 20px padding on each side
            Height = scaledHeight + 100;  // Extra space for controls

            BackgroundImage.Width = scaledWidth;
            BackgroundImage.Height = scaledHeight;

            DrawingCanvas.Width = scaledWidth;
            DrawingCanvas.Height = scaledHeight;

            DrawingCanvas.PointerPressed += Canvas_PointerPressed;
            DrawingCanvas.PointerMoved += Canvas_PointerMoved;
            DrawingCanvas.PointerReleased += Canvas_PointerReleased;
            BrushSize.SelectionChanged += BrushSize_SelectionChanged;

            this.KeyDown += ImageEditWindow_KeyDown;
        }

        public class ImageEditedEventArgs : EventArgs
        {
            public string EditedPath { get; }
            public ImageEditedEventArgs(string editedPath)
            {
                EditedPath = editedPath;
            }
        }

        private void SetActiveBorder(bool isActive)
        {
            if (this.FindControl<Border>("MainBorder") is Border border)
            {
                border.BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromRgb(85, 64, 2)) // Mica blue highlight
                    : new SolidColorBrush(Colors.Transparent);
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeRestoreButton_Click(object? sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

        // Tools

        private void BrushSize_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (BrushSize.SelectedIndex >= 0)
            {
                _currentThickness = BrushSize.SelectedIndex switch
                {
                    0 => 1, // Thin
                    1 => 3, // Normal
                    2 => 6, // Thick
                    3 => 9, // Very Thick
                    _ => 6  // Default to Normal
                };
            }
        }

        private void EraseAtPoint(Point point)
        {
            var itemsToRemove = new List<Control>();

            foreach (var child in DrawingCanvas.Children)
            {
                if (child is TextBox textBox)
                {
                    var textBoxBounds = new Rect(
                        Canvas.GetLeft(textBox),
                        Canvas.GetTop(textBox),
                        textBox.Bounds.Width,
                        textBox.Bounds.Height
                    );

                    if (textBoxBounds.Contains(point))
                    {
                        itemsToRemove.Add(textBox);
                        continue; 
                    }
                }
            }

            if (!itemsToRemove.Any())
            {
                foreach (var child in DrawingCanvas.Children.OfType<Line>())
                {
                    var distance = DistanceToLine(point, child.StartPoint, child.EndPoint);
                    if (distance <= ERASER_SIZE)
                    {
                        itemsToRemove.Add(child);
                    }
                }
            }

            foreach (var item in itemsToRemove)
            {
                DrawingCanvas.Children.Remove(item);
            }
        }

        private void ImageEditWindow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_activeTextBox != null)
                return;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) // If Ctrl is held
            {
                switch (e.Key)
                {
                    case Key.Z:
                        UndoButton_Click(this, null);
                        break;
                    case Key.Y:
                        RedoButton_Click(this, null);
                        break;
                    case Key.R:
                        RevertButton_Click(this, null);
                        break;
                    case Key.S:
                        SaveButton_Click(this, null);
                        break;
                    case Key.Q:
                        CancelButton_Click(this, null);
                        break;
                }
            }
            else // No modifier keys
            {
                switch (e.Key)
                {
                    case Key.A:
                        ArrowButton_Click(this, null);
                        break;
                    case Key.P:
                        PenButton_Click(this, null);
                        break;
                    case Key.B:
                        TextButton_Click(this, null);
                        break;
                }
            }
        }

        private double DistanceToLine(Point point, Point lineStart, Point lineEnd)
        {
            double lengthSquared = Math.Pow(lineEnd.X - lineStart.X, 2) + Math.Pow(lineEnd.Y - lineStart.Y, 2);
            if (lengthSquared == 0) return Math.Sqrt(Math.Pow(point.X - lineStart.X, 2) + Math.Pow(point.Y - lineStart.Y, 2));

            double t = Math.Max(0, Math.Min(1,
                ((point.X - lineStart.X) * (lineEnd.X - lineStart.X) +
                 (point.Y - lineStart.Y) * (lineEnd.Y - lineStart.Y)) / lengthSquared));

            double projX = lineStart.X + t * (lineEnd.X - lineStart.X);
            double projY = lineStart.Y + t * (lineEnd.Y - lineStart.Y);

            return Math.Sqrt(Math.Pow(point.X - projX, 2) + Math.Pow(point.Y - projY, 2));
        }

        private void EraserButton_Click(object? sender, RoutedEventArgs e)
        {
            ExitTextEditMode();  // Make sure to exit text edit mode
            _isArrowMode = false;
            _isErasing = true;
            _isTextMode = false;

            PenButton.Classes.Set("Selected", false);
            ArrowButton.Classes.Set("Selected", false);
            EraserButton.Classes.Set("Selected", true);
            TextButton.Classes.Set("Selected", false);
        }

        private void ExitTextEditMode()
        {
            if (_activeTextBox != null)
            {
                // Find and remove the corresponding resize grip and drag handle
                var resizeGrip = DrawingCanvas.Children
                    .OfType<Border>()
                    .FirstOrDefault(b => Canvas.GetLeft(b) == Canvas.GetLeft(_activeTextBox) + _activeTextBox.Width - 5
                                      && Canvas.GetTop(b) == Canvas.GetTop(_activeTextBox) + _activeTextBox.Height - 5);

                var dragHandle = DrawingCanvas.Children
                    .OfType<Border>()
                    .FirstOrDefault(b => Canvas.GetLeft(b) == Canvas.GetLeft(_activeTextBox) - 10
                                      && Canvas.GetTop(b) == Canvas.GetTop(_activeTextBox) - 10);

                if (resizeGrip != null)
                    DrawingCanvas.Children.Remove(resizeGrip);

                if (dragHandle != null)
                    DrawingCanvas.Children.Remove(dragHandle);

                // Finalize text box
                _activeTextBox.IsReadOnly = true;
                _activeTextBox.Cursor = new Cursor(StandardCursorType.Arrow); // Remove text cursor
                _activeTextBox.Classes.Add("DisabledTextBox"); // Apply a style if needed
                _activeTextBox.LostFocus -= OnTextBoxDeselected;
                _activeTextBox = null;
                _isTextMode = false; // Exit text mode
            }
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                var operation = _undoStack.Pop();
                _redoStack.Push(operation);

                if (operation.IsTextBox && operation.TextBox != null)
                {
                    // Remove the text box when undoing
                    DrawingCanvas.Children.Remove(operation.TextBox);

                    if (_resizeGrip != null)
                    {
                        DrawingCanvas.Children.Remove(_resizeGrip);
                        _resizeGrip = null;
                    }

                    if (_dragHandle != null)
                    {
                        DrawingCanvas.Children.Remove(_dragHandle);
                        _dragHandle = null;
                    }
                }
                else if (operation.IsErase)
                {
                    // Restore erased lines
                    foreach (var line in operation.Lines!)
                    {
                        DrawingCanvas.Children.Add(line);
                    }
                }
                else
                {
                    // Remove drawn lines
                    foreach (var line in operation.Lines!)
                    {
                        DrawingCanvas.Children.Remove(line);
                    }
                }

                UndoButton.IsEnabled = _undoStack.Count > 0;
                RedoButton.IsEnabled = true;
            }
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count > 0)
            {
                var operation = _redoStack.Pop();
                _undoStack.Push(operation);

                if (operation.IsTextBox && operation.TextBox != null)
                {
                    // Restore the text box
                    DrawingCanvas.Children.Add(operation.TextBox);

                    if (_resizeGrip != null)
                    {
                        DrawingCanvas.Children.Add(_resizeGrip);
                    }

                    if (_dragHandle != null)
                    {
                        DrawingCanvas.Children.Add(_dragHandle);
                    }
                }
                else if (operation.IsErase)
                {
                    // Re-erase lines
                    foreach (var line in operation.Lines!)
                    {
                        DrawingCanvas.Children.Remove(line);
                    }
                }
                else
                {
                    // Restore drawn lines
                    foreach (var line in operation.Lines!)
                    {
                        DrawingCanvas.Children.Add(line);
                    }
                }

                RedoButton.IsEnabled = _redoStack.Count > 0;
                UndoButton.IsEnabled = true;
            }
        }

        private void ColorButton_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Background is IBrush brush)
            {
                _currentBrush = brush;
                _isErasing = false;

                if (_selectedColorButton != null)
                {
                    _selectedColorButton.Classes.Remove("Selected");
                }
                button.Classes.Add("Selected");
                _selectedColorButton = button;

                EraserButton.Classes.Remove("Selected");
            }
        }

        private Bitmap LoadImage(string path)
        {
            var bitmap = new Bitmap(path);
            BackgroundImage.Source = bitmap;

            DrawingCanvas.Children.Clear();
            DrawingCanvas.Width = bitmap.PixelSize.Width;
            DrawingCanvas.Height = bitmap.PixelSize.Height;

            return bitmap;
        }

        private async void RevertButton_Click(object? sender, RoutedEventArgs e)
        {
            var confirmPopup = new RevertImagePopup();

            confirmPopup.ConfirmClicked += async (s, args) =>
            {
                if (DataContext is NotesView.NoteItem noteItem)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(noteItem.EditedImagePath) &&
                            File.Exists(noteItem.EditedImagePath))
                        {
                            File.Delete(noteItem.EditedImagePath);
                        }

                        noteItem.EditedImagePath = null;
                        await noteItem.SaveChanges();

                        LoadImage(_imagePath);

                        ImageEdited?.Invoke(this, new ImageEditedEventArgs(noteItem.ImagePath));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error reverting to original: {ex.Message}");
                    }
                    Close();
                }
            };

            confirmPopup.ShowDialog(this); // Show as modal dialog
        }


        // page

        private void ImageEditWindow_LayoutUpdated(object? sender, EventArgs e)
        {
            if (BackgroundImage.Bounds.Width > 0)
            {
                var scale = BackgroundImage.Bounds.Width / _originalBitmap.PixelSize.Width;
                DrawingCanvas.Width = BackgroundImage.Bounds.Width;
                DrawingCanvas.Height = BackgroundImage.Bounds.Height;
                DrawingCanvas.RenderTransform = new ScaleTransform(scale, scale);
            }
        }

        private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _isDrawing = true;
            _lastPoint = e.GetPosition(DrawingCanvas);

            _redoStack.Clear();
            RedoButton.IsEnabled = false;


            if (_isTextMode)
            {
                CreateTextBox(_lastPoint);
                _isDrawing = false; // Don't continue dragging for text
                return;
            }
            if (_isArrowMode)
            {
                _arrowStart = _lastPoint;

                // Clear any existing arrow preview
                foreach (var line in _arrowPreview)
                    DrawingCanvas.Children.Remove(line);
                _arrowPreview.Clear();
            }
            else if (_isErasing)
            {
                _currentErase.Clear();
                EraseAtPoint(_lastPoint); // Start erasing immediately
            }
            else
            {
                _currentStroke.Clear();
            }
        }


        private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isDrawing) return;
            var currentPoint = e.GetPosition(DrawingCanvas);

            if (_isArrowMode)
            {
                // Remove old preview lines
                foreach (var line in _arrowPreview)
                    DrawingCanvas.Children.Remove(line);
                _arrowPreview.Clear();

                // Create new preview lines
                var arrowLines = CreateArrowLines(_arrowStart, currentPoint, _currentBrush, _currentThickness);
                _arrowPreview.AddRange(arrowLines);

                foreach (var line in arrowLines)
                    DrawingCanvas.Children.Add(line);
            }
            else if (_isErasing)
            {
                EraseAtPoint(currentPoint);
            }
            else
            {
                var smoothedPoint = new Point(
                    (_lastPoint.X + currentPoint.X) / 2,
                    (_lastPoint.Y + currentPoint.Y) / 2
                );

                var line1 = new Line
                {
                    StartPoint = _lastPoint,
                    EndPoint = smoothedPoint,
                    Stroke = _currentBrush,
                    StrokeThickness = _currentThickness,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round
                };

                var line2 = new Line
                {
                    StartPoint = smoothedPoint,
                    EndPoint = currentPoint,
                    Stroke = _currentBrush,
                    StrokeThickness = _currentThickness,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round
                };

                DrawingCanvas.Children.Add(line1);
                DrawingCanvas.Children.Add(line2);
                _currentStroke.Add(line1);
                _currentStroke.Add(line2);
            }

            _lastPoint = currentPoint;
        }

        private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isDrawing) return;
            if (_isTextMode)
            {
                _undoStack.Push(new DrawingOperation
                {
                    Lines = new List<Line>(),  // No lines, just text
                    IsErase = false
                });

                UndoButton.IsEnabled = _undoStack.Count > 0;
            }
            if (_isArrowMode)
            {
                // Finalize the arrow
                foreach (var line in _arrowPreview)
                    DrawingCanvas.Children.Remove(line);

                var releasePoint = e.GetPosition(DrawingCanvas);
                var finalArrowLines = CreateArrowLines(_arrowStart, releasePoint, _currentBrush, _currentThickness);

                foreach (var line in finalArrowLines)
                    DrawingCanvas.Children.Add(line);

                _undoStack.Push(new DrawingOperation
                {
                    Lines = new List<Line>(finalArrowLines),
                    IsErase = false
                });

                UndoButton.IsEnabled = _undoStack.Count > 0;
                _arrowPreview.Clear();
            }
            else if (_isErasing)
            {
                if (_currentErase.Count > 0)
                {
                    _undoStack.Push(new DrawingOperation
                    {
                        Lines = new List<Line>(_currentErase),
                        IsErase = true
                    });
                    _currentErase.Clear();
                }
            }
            else
            {
                if (_currentStroke.Count > 0)
                {
                    _undoStack.Push(new DrawingOperation
                    {
                        Lines = new List<Line>(_currentStroke),
                        IsErase = false
                    });
                    _currentStroke.Clear();
                }
            }

            UndoButton.IsEnabled = _undoStack.Count > 0;
            _isDrawing = false;
        }

        private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var baseDir = System.IO.Path.GetDirectoryName(_imagePath);
                var editedImagesDir = System.IO.Path.Combine(baseDir!, "imagesEdited");
                Directory.CreateDirectory(editedImagesDir);

                var fileName = System.IO.Path.GetFileName(_imagePath);
                var editedPath = System.IO.Path.Combine(editedImagesDir, fileName);

                var originalWidth = _originalBitmap.PixelSize.Width;
                var originalHeight = _originalBitmap.PixelSize.Height;

                var currentWidth = DrawingCanvas.Bounds.Width;
                var currentHeight = DrawingCanvas.Bounds.Height;

                var scaleX = originalWidth / currentWidth;
                var scaleY = originalHeight / currentHeight;

                // ✅ Step 1: Render the image and lines manually
                var panel = new Panel
                {
                    Width = originalWidth,
                    Height = originalHeight
                };

                var background = new Image
                {
                    Source = _originalBitmap,
                    Width = originalWidth,
                    Height = originalHeight
                };
                panel.Children.Add(background);

                var tempCanvas = new Canvas
                {
                    Width = originalWidth,
                    Height = originalHeight
                };

                foreach (var child in DrawingCanvas.Children)
                {
                    if (child is Line originalLine)
                    {
                        var newLine = new Line
                        {
                            StartPoint = new Point(
                                originalLine.StartPoint.X * scaleX,
                                originalLine.StartPoint.Y * scaleY
                            ),
                            EndPoint = new Point(
                                originalLine.EndPoint.X * scaleX,
                                originalLine.EndPoint.Y * scaleY
                            ),
                            Stroke = originalLine.Stroke,
                            StrokeThickness = originalLine.StrokeThickness * scaleX,
                            StrokeLineCap = originalLine.StrokeLineCap,
                            StrokeJoin = originalLine.StrokeJoin
                        };
                        tempCanvas.Children.Add(newLine);
                    }
                }

                panel.Children.Add(tempCanvas);

                var size = new Size(originalWidth, originalHeight);
                panel.Measure(size);
                panel.Arrange(new Rect(size));

                var pixelSize = new PixelSize(originalWidth, originalHeight);
                using var renderBitmap = new RenderTargetBitmap(pixelSize);
                renderBitmap.Render(panel);

                // ✅ Step 2: Render text boxes separately
                var textCanvas = new Canvas
                {
                    Width = originalWidth,
                    Height = originalHeight
                };

                foreach (var child in DrawingCanvas.Children)
                {
                    if (child is TextBox textBox)
                    {
                        var textBlock = new Border
                        {
                            Background = textBox.Background,
                            Width = textBox.Width * scaleX,
                            Height = textBox.Height * scaleY,
                            Child = new TextBlock
                            {
                                Text = textBox.Text,
                                FontSize = textBox.FontSize,
                                Foreground = textBox.Foreground,
                                TextWrapping = TextWrapping.WrapWithOverflow,
                                Padding = new Thickness(
                                    (textBox.Padding.Left - 2) * scaleX,
                                    (textBox.Padding.Top - 2) * scaleY,
                                    (textBox.Padding.Right - 2) * scaleX,
                                    (textBox.Padding.Bottom - 2) * scaleY
                                ),
                                LineHeight = textBox.FontSize * 1.05,
                            }
                        };


                        Canvas.SetLeft(textBlock, Canvas.GetLeft(textBox) * scaleX);
                        Canvas.SetTop(textBlock, Canvas.GetTop(textBox) * scaleY);
                        textCanvas.Children.Add(textBlock);
                    }
                }

                textCanvas.Measure(size);
                textCanvas.Arrange(new Rect(size));

                var textRenderBitmap = new RenderTargetBitmap(pixelSize);
                textRenderBitmap.Render(textCanvas);

                // ✅ Step 3: Merge both layers into one final image
                var finalCanvas = new Canvas
                {
                    Width = originalWidth,
                    Height = originalHeight
                };

                var imageLayer = new Image
                {
                    Source = renderBitmap,
                    Width = originalWidth,
                    Height = originalHeight
                };
                finalCanvas.Children.Add(imageLayer);

                var textLayer = new Image
                {
                    Source = textRenderBitmap,
                    Width = originalWidth,
                    Height = originalHeight
                };
                finalCanvas.Children.Add(textLayer);

                finalCanvas.Measure(size);
                finalCanvas.Arrange(new Rect(size));

                var finalRender = new RenderTargetBitmap(pixelSize);
                finalRender.Render(finalCanvas);

                // ✅ Step 4: Save the final image
                using (var fileStream = File.OpenWrite(editedPath))
                {
                    finalRender.Save(fileStream);
                    await fileStream.FlushAsync();
                }

                if (DataContext is NotesView.NoteItem noteItem)
                {
                    noteItem.EditedImagePath = editedPath;
                    await noteItem.SaveChanges();

                    noteItem.RefreshImage();
                    ImageEdited?.Invoke(this, new ImageEditedEventArgs(noteItem.EditedImagePath ?? noteItem.ImagePath));

                    await Task.Delay(100);
                    Close();
                }
                else
                {
                    Debug.WriteLine("DataContext is not a NoteItem");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving edited image: {ex.Message}");
            }
        }


        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (DataContext is NotesView.NoteItem noteItem)
            {
                noteItem.RefreshImage();
                ImageEdited?.Invoke(this, new ImageEditedEventArgs(noteItem.EditedImagePath ?? noteItem.ImagePath));
            }
        }

        // Draggable Menu Bar
        private void Menu_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
            }
        }

        // Arrows

        private void ArrowButton_Click(object? sender, RoutedEventArgs e)
        {
            ExitTextEditMode();  // Make sure to exit text edit mode
            _isArrowMode = true;
            _isErasing = false;
            _isTextMode = false;

            PenButton.Classes.Set("Selected", false);
            ArrowButton.Classes.Set("Selected", true);
            EraserButton.Classes.Set("Selected", false);
            TextButton.Classes.Set("Selected", false);
        }

        private List<Line> CreateArrowLines(Point start, Point end, IBrush stroke, double thickness)
        {
            var lines = new List<Line>();

            // 1) Main shaft
            var mainLine = new Line
            {
                StartPoint = start,
                EndPoint = end,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };
            lines.Add(mainLine);

            // 2) Arrowhead lines
            // Calculate direction
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angle = Math.Atan2(dy, dx); // angle in radians

            // Some length for the arrowhead lines
            double arrowHeadLength = 20.0 * (thickness / 4.0);
            double arrowAngle = Math.PI / 6; // 30 degrees

            // Left side
            double leftAngle = angle + Math.PI - arrowAngle;
            double leftX = end.X + arrowHeadLength * Math.Cos(leftAngle);
            double leftY = end.Y + arrowHeadLength * Math.Sin(leftAngle);

            // Right side
            double rightAngle = angle + Math.PI + arrowAngle;
            double rightX = end.X + arrowHeadLength * Math.Cos(rightAngle);
            double rightY = end.Y + arrowHeadLength * Math.Sin(rightAngle);

            var leftLine = new Line
            {
                StartPoint = new Point(leftX, leftY),
                EndPoint = end,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };

            var rightLine = new Line
            {
                StartPoint = new Point(rightX, rightY),
                EndPoint = end,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round
            };

            lines.Add(leftLine);
            lines.Add(rightLine);

            return lines;
        }

        // Pen Mode

        private void PenButton_Click(object? sender, RoutedEventArgs e)
        {
            ExitTextEditMode(); 
            _isArrowMode = false;
            _isErasing = false;
            _isTextMode = false;

            PenButton.Classes.Set("Selected", true);
            ArrowButton.Classes.Set("Selected", false);
            EraserButton.Classes.Set("Selected", false);
            TextButton.Classes.Set("Selected", false);
        }



        // Text Box
        private void TextButton_Click(object? sender, RoutedEventArgs e)
        {
            ExitTextEditMode();  
            _activeTextBox = null; 
            _isTextMode = true;
            _isArrowMode = false;
            _isErasing = false;

            PenButton.Classes.Set("Selected", false);
            ArrowButton.Classes.Set("Selected", false);
            EraserButton.Classes.Set("Selected", false);
            TextButton.Classes.Set("Selected", true);
        }

        private void CreateTextBox(Point position)
        {
            if (_activeTextBox != null)
                return;

            var textBoxViewModel = new TextBoxViewModel
            {
                X = position.X,
                Y = position.Y,
            };

            var textBox = new TextBox
            {
                Background = _currentBrush,
                Foreground = Brushes.White,
                FontSize = 28,
                TextWrapping = TextWrapping.WrapWithOverflow,
                Padding = new Thickness(4),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0),
            };

            textBox.Bind(Canvas.LeftProperty, textBoxViewModel.WhenAnyValue(x => x.X));
            textBox.Bind(Canvas.TopProperty, textBoxViewModel.WhenAnyValue(x => x.Y));
            textBox.Bind(TextBox.WidthProperty, textBoxViewModel.WhenAnyValue(x => x.Width));
            textBox.Bind(TextBox.HeightProperty, textBoxViewModel.WhenAnyValue(x => x.Height));
            textBox.Bind(TextBox.TextProperty, textBoxViewModel.WhenAnyValue(x => x.Text));

            _resizeGrip = new Border
            {
                Width = 10,
                Height = 10,
                Background = Brushes.Gray,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
            };

            _resizeGrip.Bind(Canvas.LeftProperty, textBoxViewModel.WhenAnyValue(x => x.X, x => x.Width, (x, w) => x + w - 5));
            _resizeGrip.Bind(Canvas.TopProperty, textBoxViewModel.WhenAnyValue(x => x.Y, x => x.Height, (y, h) => y + h - 5));

            _resizeGrip.PointerPressed += (s, e) => StartResizeTextBox(s, e, textBoxViewModel);
            _resizeGrip.PointerReleased += (s, e) => _isResizingTextBox = false;

            _dragHandle = new Border
            {
                Width = 12,
                Height = 12,
                Background = Brushes.DarkGray,
                CornerRadius = new CornerRadius(6),
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };

            Canvas.SetLeft(_dragHandle, position.X - 10);
            Canvas.SetTop(_dragHandle, position.Y - 10);

            _dragHandle.PointerPressed += (s, e) => StartDragTextBox(s, e, textBox);
            _dragHandle.PointerReleased += (s, e) => _isDraggingTextBox = false;

            DrawingCanvas.Children.Add(textBox);
            DrawingCanvas.Children.Add(_dragHandle);
            DrawingCanvas.Children.Add(_resizeGrip);

            _activeTextBox = textBox;

            textBox.LostFocus += (s, e) => OnTextBoxDeselected(s, e);

            _undoStack.Push(new DrawingOperation
            {
                TextBox = textBox,
                IsTextBox = true,
                IsErase = false
            });

            UndoButton.IsEnabled = _undoStack.Count > 0;

            textBox.Focus();
        }

        private void StartDragTextBox(object? sender, PointerPressedEventArgs e, TextBox textBox)
        {
            //Debug.WriteLine("🟢 Drag Started!");

            _isDraggingTextBox = true;
            _textBoxOffset = e.GetPosition(textBox);

            DrawingCanvas.PointerMoved += DragTextBox;
            DrawingCanvas.PointerReleased += StopDragTextBox;

            //Debug.WriteLine("🔵 Dragging Events attached to DrawingCanvas.");
        }


        private void DragTextBox(object? sender, PointerEventArgs e)
        {
            if (!_isDraggingTextBox || _activeTextBox == null) return;

            var newPosition = e.GetPosition(DrawingCanvas);

            double newX = newPosition.X - _textBoxOffset.X;
            double newY = newPosition.Y - _textBoxOffset.Y;

            Canvas.SetLeft(_activeTextBox, newX);
            Canvas.SetTop(_activeTextBox, newY);

            if (_dragHandle != null)
            {
                Canvas.SetLeft(_dragHandle, newX - 5);
                Canvas.SetTop(_dragHandle, newY - 5);
            }

            if (_resizeGrip != null)
            {
                Canvas.SetLeft(_resizeGrip, newX + _activeTextBox.Width - 5);
                Canvas.SetTop(_resizeGrip, newY + _activeTextBox.Height - 5);
            }
        }


        private void StopDragTextBox(object? sender, PointerReleasedEventArgs e)
        {
            _isDraggingTextBox = false;

            //Debug.WriteLine("🔴 Drag Stopped!");

            DrawingCanvas.PointerMoved -= DragTextBox;
            DrawingCanvas.PointerReleased -= StopDragTextBox;
        }



        private void StartResizeTextBox(object? sender, PointerPressedEventArgs e, TextBoxViewModel textBoxViewModel)
        {
            _isResizingTextBox = true;

            var initialX = Canvas.GetLeft(_activeTextBox);
            var initialY = Canvas.GetTop(_activeTextBox);
            var initialWidth = _activeTextBox.Width;
            var initialHeight = _activeTextBox.Height;

            void OnPointerMoved(object? s, PointerEventArgs ev)
            {
                if (!_isResizingTextBox) return;

                var newPosition = ev.GetPosition(DrawingCanvas);

                _activeTextBox.Width = Math.Max(40, newPosition.X - initialX);
                _activeTextBox.Height = Math.Max(20, newPosition.Y - initialY);

                if (_resizeGrip != null)
                {
                    Canvas.SetLeft(_resizeGrip, initialX + _activeTextBox.Width - 5);
                    Canvas.SetTop(_resizeGrip, initialY + _activeTextBox.Height - 5);
                }

                if (_dragHandle != null)
                {
                    Canvas.SetLeft(_dragHandle, initialX - 5);
                    Canvas.SetTop(_dragHandle, initialY - 5);
                }
            }

            void OnPointerReleased(object? s, PointerReleasedEventArgs ev)
            {
                _isResizingTextBox = false;
                ((IInputElement)sender!).PointerMoved -= OnPointerMoved;
                ((IInputElement)sender!).PointerReleased -= OnPointerReleased;
            }

            ((IInputElement)sender!).PointerMoved += OnPointerMoved;
            ((IInputElement)sender!).PointerReleased += OnPointerReleased;
        }


        private void OnTextBoxDeselected(object? sender, RoutedEventArgs e)
        {
            if (_activeTextBox != null)
            {
                _activeTextBox.IsReadOnly = true;
                _activeTextBox.Focusable = false;
                _activeTextBox.IsHitTestVisible = false;
                //_activeTextBox.IsEnabled = false;
                _activeTextBox.Cursor = new Cursor(StandardCursorType.Arrow);
                _activeTextBox.Classes.Add("DisabledTextBox");
                _activeTextBox.LostFocus -= OnTextBoxDeselected;

                if (_resizeGrip != null)
                {
                    DrawingCanvas.Children.Remove(_resizeGrip);
                    _resizeGrip = null;
                }

                if (_dragHandle != null)
                {
                    DrawingCanvas.Children.Remove(_dragHandle);
                    _dragHandle = null;
                }

                _activeTextBox = null;
                _isTextMode = false;
            }
        }

    }
}