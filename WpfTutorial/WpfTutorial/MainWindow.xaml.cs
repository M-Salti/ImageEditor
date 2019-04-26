using Microsoft.Win32;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZoomAndPan;
using Point = System.Windows.Point;

namespace ImageEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        EditableBitmap bmp = null;
        DrawingTools tools = new DrawingTools();
        byte[] orig = null;

        enum Tool
        {
            PanAndZoom,
            Pen,
            Line,
            Rectangle,
            ColorPicker,
            Circle,
            Ellipse
        }
        Tool curTool = Tool.PanAndZoom;

        BitmapScalingMode scalingMode = BitmapScalingMode.Fant;

        uint[] thicknessList = { 1, 2, 3, 4 };

        public MainWindow()
        {
            InitializeComponent();

            primary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0));
            secondary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
        }

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject([In] IntPtr hObject);
        public ImageSource ImageSourceForBitmap(Bitmap bmp)
        {
            var handle = bmp.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally { DeleteObject(handle); }
        }

        private void ShowImage(string path)
        {
            try
            {
                bmp = new EditableBitmap(path);
                orig = new byte[bmp.underlyingBytes.ByteArray.Length];
                Array.Copy(bmp.underlyingBytes.ByteArray, orig, orig.Length);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }

            RenderOptions.SetBitmapScalingMode(content, scalingMode);
            RenderOptions.SetEdgeMode(content, EdgeMode.Aliased);

            //content.Source = bmp.writeableBitmap;
            content.Source = ImageSourceForBitmap(bmp.underlyingBitmap);

            tools.Image = bmp;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.PanAndZoom;
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Bitmaps|*.bmp"
            };
            openFileDialog.ShowDialog();
            string path = openFileDialog.FileName;
            if (path != String.Empty)
                ShowImage(path);
        }

        private void LineToolButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.Line;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        private void PenButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.Pen;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        private void RectangleButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.Rectangle;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        private void ColorPickerButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.ColorPicker;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        private void CircleButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.Circle;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        #region ZoomAndPan

        /// <summary>
        /// Specifies the current state of the mouse handling logic.
        /// </summary>
        private MouseHandlingMode mouseHandlingMode = MouseHandlingMode.None;

        /// <summary>
        /// The point that was clicked relative to the ZoomAndPanControl.
        /// </summary>
        private Point origZoomAndPanControlMouseDownPoint;

        /// <summary>
        /// The point that was clicked relative to the content that is contained within the ZoomAndPanControl.
        /// </summary>
        private Point origContentMouseDownPoint;

        /// <summary>
        /// Records which mouse button clicked during mouse dragging.
        /// </summary>
        private MouseButton mouseButtonDown;

        /// <summary>
        /// Event raised on mouse down in the ZoomAndPanControl.
        /// </summary>
        private void zoomAndPanControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            content.Focus();
            Keyboard.Focus(content);

            mouseButtonDown = e.ChangedButton;
            origZoomAndPanControlMouseDownPoint = e.GetPosition(zoomAndPanControl);
            origContentMouseDownPoint = e.GetPosition(content);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
                (e.ChangedButton == MouseButton.Left ||
                 e.ChangedButton == MouseButton.Right))
            {
                // Shift + left- or right-down initiates zooming mode.
                mouseHandlingMode = MouseHandlingMode.Zooming;
            }
            else if (mouseButtonDown == MouseButton.Left)
            {
                // Just a plain old left-down initiates panning mode.
                mouseHandlingMode = MouseHandlingMode.Panning;
            }

            if (mouseHandlingMode != MouseHandlingMode.None)
            {
                // Capture the mouse so that we eventually receive the mouse up event.
                zoomAndPanControl.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised on mouse up in the ZoomAndPanControl.
        /// </summary>
        private void zoomAndPanControl_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (mouseHandlingMode != MouseHandlingMode.None)
            {
                if (mouseHandlingMode == MouseHandlingMode.Zooming)
                {
                    if (mouseButtonDown == MouseButton.Left)
                    {
                        // Shift + left-click zooms in on the content.
                        ZoomIn(origContentMouseDownPoint);
                    }
                    else if (mouseButtonDown == MouseButton.Right)
                    {
                        // Shift + left-click zooms out from the content.
                        ZoomOut(origContentMouseDownPoint);
                    }
                }
                else if (mouseHandlingMode == MouseHandlingMode.DragZooming)
                {
                    // When drag-zooming has finished we zoom in on the rectangle that was highlighted by the user.
                    ApplyDragZoomRect();
                }

                zoomAndPanControl.ReleaseMouseCapture();
                mouseHandlingMode = MouseHandlingMode.None;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised on mouse move in the ZoomAndPanControl.
        /// </summary>
        private void zoomAndPanControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseHandlingMode == MouseHandlingMode.Panning)
            {
                //
                // The user is left-dragging the mouse.
                // Pan the viewport by the appropriate amount.
                //
                Point curContentMousePoint = e.GetPosition(content);
                Vector dragOffset = curContentMousePoint - origContentMouseDownPoint;

                zoomAndPanControl.ContentOffsetX -= dragOffset.X;
                zoomAndPanControl.ContentOffsetY -= dragOffset.Y;

                e.Handled = true;
            }
            else if (mouseHandlingMode == MouseHandlingMode.Zooming)
            {
                Point curZoomAndPanControlMousePoint = e.GetPosition(zoomAndPanControl);
                Vector dragOffset = curZoomAndPanControlMousePoint - origZoomAndPanControlMouseDownPoint;
                double dragThreshold = 10;
                if (mouseButtonDown == MouseButton.Left &&
                    (Math.Abs(dragOffset.X) > dragThreshold ||
                     Math.Abs(dragOffset.Y) > dragThreshold))
                {
                    //
                    // When Shift + left-down zooming mode and the user drags beyond the drag threshold,
                    // initiate drag zooming mode where the user can drag out a rectangle to select the area
                    // to zoom in on.
                    //
                    mouseHandlingMode = MouseHandlingMode.DragZooming;
                    Point curContentMousePoint = e.GetPosition(content);
                    InitDragZoomRect(origContentMouseDownPoint, curContentMousePoint);
                }

                e.Handled = true;
            }
            else if (mouseHandlingMode == MouseHandlingMode.DragZooming)
            {
                //
                // When in drag zooming mode continously update the Point of the rectangle
                // that the user is dragging out.
                //
                Point curContentMousePoint = e.GetPosition(content);
                SetDragZoomRect(origContentMouseDownPoint, curContentMousePoint);

                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised by rotating the mouse wheel
        /// </summary>
        private void zoomAndPanControl_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;

            if (e.Delta > 0)
            {
                Point curContentMousePoint = e.GetPosition(content);
                ZoomIn(curContentMousePoint);
            }
            else if (e.Delta < 0)
            {
                Point curContentMousePoint = e.GetPosition(content);
                ZoomOut(curContentMousePoint);
            }
        }

        /// <summary>
        /// Zoom the viewport out, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomOut(Point contentZoomCenter)
        {
            zoomAndPanControl.ZoomAboutPoint(zoomAndPanControl.ContentScale * 0.9, contentZoomCenter);
            AdjustRenderMode();
        }

        /// <summary>
        /// Zoom the viewport in, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomIn(Point contentZoomCenter)
        {
            zoomAndPanControl.ZoomAboutPoint(zoomAndPanControl.ContentScale * 1.1, contentZoomCenter);
            AdjustRenderMode();
        }

        /// <summary>
        /// When the user has finished dragging out the rectangle the zoom operation is applied.
        /// </summary>
        private void ApplyDragZoomRect()
        {
            //
            // Retreive the rectangle that the user draggged out and zoom in on it.
            //
            double contentX = Canvas.GetLeft(dragZoomBorder);
            double contentY = Canvas.GetTop(dragZoomBorder);
            double contentWidth = dragZoomBorder.Width;
            double contentHeight = dragZoomBorder.Height;
            zoomAndPanControl.AnimatedZoomTo(new Rect(contentX, contentY, contentWidth, contentHeight));

            FadeOutDragZoomRect();

            AdjustRenderMode();
        }

        //
        // Fade out the drag zoom rectangle.
        //
        private void FadeOutDragZoomRect()
        {
            AnimationHelper.StartAnimation(dragZoomBorder, Border.OpacityProperty, 0.0, 0.1,
                delegate (object sender, EventArgs e)
                {
                    dragZoomCanvas.Visibility = Visibility.Collapsed;
                });
        }

        /// <summary>
        /// Initialise the rectangle that the use is dragging out.
        /// </summary>
        private void InitDragZoomRect(Point pt1, Point pt2)
        {
            SetDragZoomRect(pt1, pt2);

            dragZoomCanvas.Visibility = Visibility.Visible;
            dragZoomBorder.Opacity = 0.5;
        }

        /// <summary>
        /// Update the Point and size of the rectangle that user is dragging out.
        /// </summary>
        private void SetDragZoomRect(Point pt1, Point pt2)
        {
            double x, y, width, height;

            //
            // Deterine x,y,width and height of the rect inverting the points if necessary.
            // 

            if (pt2.X < pt1.X)
            {
                x = pt2.X;
                width = pt1.X - pt2.X;
            }
            else
            {
                x = pt1.X;
                width = pt2.X - pt1.X;
            }

            if (pt2.Y < pt1.Y)
            {
                y = pt2.Y;
                height = pt1.Y - pt2.Y;
            }
            else
            {
                y = pt1.Y;
                height = pt2.Y - pt1.Y;
            }

            //
            // Update the coordinates of the rectangle that is being dragged out by the user.
            // The we offset and rescale to convert from content coordinates.
            //
            Canvas.SetLeft(dragZoomBorder, x);
            Canvas.SetTop(dragZoomBorder, y);
            dragZoomBorder.Width = width;
            dragZoomBorder.Height = height;
        }

        private void zoomAndPanControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 && curTool == Tool.PanAndZoom)
            {
                Point doubleClickPoint = e.GetPosition(content);
                zoomAndPanControl.AnimatedSnapTo(doubleClickPoint);
            }
        }

        /// <summary>
        /// The 'ZoomIn' command (bound to the plus key) was executed.
        /// </summary>
        private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ZoomIn(new Point(zoomAndPanControl.ContentZoomFocusX, zoomAndPanControl.ContentZoomFocusY));
        }

        /// <summary>
        /// The 'ZoomOut' command (bound to the minus key) was executed.
        /// </summary>
        private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ZoomOut(new Point(zoomAndPanControl.ContentZoomFocusX, zoomAndPanControl.ContentZoomFocusY));
        }

        /// <summary>
        /// The 'Fill' command was executed.
        /// </summary>
        private void Fill_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            zoomAndPanControl.AnimatedScaleToFit();
            RenderOptions.SetBitmapScalingMode(content, scalingMode);
        }

        /// <summary>
        /// The 'OneHundredPercent' command was executed.
        /// </summary>
        private void OneHundredPercent_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            zoomAndPanControl.AnimatedZoomTo(1.0);
            RenderOptions.SetBitmapScalingMode(content, scalingMode);
        }

        #endregion

        private void UpdateContent()
        {
            content.Source = ImageSourceForBitmap(bmp.underlyingBitmap);
        }

        private void content_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (curTool == Tool.PanAndZoom)
                return;

            Position position = GetCorrectPosition(e);

            if (curTool == Tool.Pen)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    tools.usedColor = 0;
                }
                else if (e.RightButton == MouseButtonState.Pressed)
                {
                    tools.usedColor = 1;
                }
                tools.p0 = position;
                tools.PlotPenDot(position);
            }
            else if (curTool == Tool.Line || curTool == Tool.Rectangle || curTool == Tool.Circle || curTool == Tool.Ellipse)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (tools.p0.X > 0)
                    {
                        if (tools.usedColor == 0)
                        {
                            if (curTool == Tool.Line)
                                tools.AliasedLine(tools.p0, position);
                            else if (curTool == Tool.Rectangle)
                                tools.DrawRectangle(tools.p0, position);
                            else if (curTool == Tool.Circle)
                                tools.DrawCircle(tools.p0, tools.p0.Distance(position));
                            else if (curTool == Tool.Ellipse)
                                tools.DrawEllipse(tools.p0, position);
                        }
                        tools.p0.X = -1;
                    }
                    else
                    {
                        tools.usedColor = 0;
                        tools.p0 = position;
                        //tools.PlotPenDot(position);
                    }
                }
                else if (e.RightButton == MouseButtonState.Pressed)
                {
                    if (tools.p0.X > 0)
                    {
                        if (tools.usedColor == 0)
                        {
                            if (curTool == Tool.Line)
                                tools.AliasedLine(tools.p0, position);
                            else if (curTool == Tool.Rectangle)
                                tools.DrawRectangle(tools.p0, position);
                            else if (curTool == Tool.Circle)
                                tools.DrawCircle(tools.p0, tools.p0.Distance(position));
                            else if (curTool == Tool.Ellipse)
                                tools.DrawEllipse(tools.p0, position);
                        }
                        tools.p0.X = -1;
                    }
                    else
                    {
                        tools.usedColor = 1;
                        tools.p0 = position;
                        //tools.PlotPenDot(position);
                    }
                }
            }
            else if (curTool == Tool.ColorPicker)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    tools.colors[0] = bmp.GetPixelXY(position.X, position.Y);
                }
                else if (e.RightButton == MouseButtonState.Pressed)
                {
                    tools.colors[1] = bmp.GetPixelXY(position.X, position.Y);
                }
            }

            UpdateContent();
            e.Handled = true;
        }

        private void content_MouseMove(object sender, MouseEventArgs e)
        {
            if (curTool == Tool.PanAndZoom)
                return;

            Position position = GetCorrectPosition(e);

            if (curTool == Tool.Pen)
            {
                if ((e.LeftButton == MouseButtonState.Pressed && tools.usedColor == 0)
                    || (e.RightButton == MouseButtonState.Pressed && tools.usedColor == 1))
                {
                    tools.p1 = position;
                    tools.AliasedLine(tools.p0, tools.p1);
                    tools.p0 = position;
                }
                else
                {
                    tools.p0.X = -1;
                }
            }

            UpdateContent();
            e.Handled = true;
        }

        private Position GetCorrectPosition(MouseEventArgs e)
        {
            Point position = e.GetPosition(content);
            return new Position((int)(position.X * bmp.Width / content.ActualWidth),
                (int)(position.Y * bmp.Height / content.ActualHeight));
        }

        private void AdjustRenderMode()
        {
            if (zoomAndPanControl.ContentScale > 1.5)
                RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.NearestNeighbor);
            else
                RenderOptions.SetBitmapScalingMode(content, scalingMode);
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<System.Windows.Media.Color?> e)
        {
            var color = ColorPicker.SelectedColor;
            byte r = color.Value.R;
            byte g = color.Value.G;
            byte b = color.Value.B;
            byte a = color.Value.A;

            tools.colors[tools.usedColor] = Color.FromArgb(a, r, g, b);

            if (tools.usedColor == 0)
                primary.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            else
                secondary.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        }

        private void primary_Click(object sender, RoutedEventArgs e)
        {
            tools.usedColor = 0;
        }

        private void secondary_Click(object sender, RoutedEventArgs e)
        {
            tools.usedColor = 1;
        }

        private void thicknessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            tools.thickness = thicknessList[thicknessComboBox.SelectedIndex];
        }

        private void Effects_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (bmp == null)
                return;

            const string prefix = "System.Windows.Controls.ComboBoxItem: ";
            if (Effects.SelectedIndex == -1)
                return;
            string effect = Effects.SelectedItem.ToString().Substring(prefix.Length);
            Effects.SelectedIndex = -1;

            switch (effect)
            {
                case "Black And White":
                    bmp.BlackAndWhite();
                    break;
                case "Sepia Tone":
                    bmp.SepiaTone();
                    break;
                case "Blur":
                    bmp.globafun(EditableBitmap.filters.blur);
                    break;
                case "Sharpen":
                    bmp.globafun(EditableBitmap.filters.sharp);
                    break;
                case "Vertical Edge":
                    bmp.globafun(EditableBitmap.filters.ver);
                    break;
                case "Horizontal Edge":
                    bmp.globafun(EditableBitmap.filters.hor);
                    break;
                case "Diagonal Edge":
                    bmp.globafun(EditableBitmap.filters.diag);
                    break;
                case "Edge Detection":
                    bmp.globafun(EditableBitmap.filters.all);
                    break;
                case "East Emboss":
                    bmp.globafun(EditableBitmap.filters.east);
                    break;
                case "South Emboss":
                    bmp.globafun(EditableBitmap.filters.south);
                    break;
                case "SouthEast Emboss":
                    bmp.globafun(EditableBitmap.filters.southeast);
                    break;
                case "Flip vertical":
                    bmp.FlipVertical();
                    break;
                case "Flip horizontal":
                    bmp.FlipHorizontal();
                    break;
                case "Rotate 180":
                    bmp.Rotate180();
                    break;
                case "Rotate right 90":
                    bmp.RotateRight90();
                    break;
                case "Rotate left 90":
                    bmp.RotateLeft90();
                    break;
                case "Invert colors":
                    bmp.InvertColors();
                    break;
                default:
                    break;
            }
            UpdateContent();
            //content.Source = bmp.writeableBitmap;
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (bmp == null)
                return;
            bmp.SaveImage(bmp.FileName);
        }

        private void SaveImageAS_Click(object sender, RoutedEventArgs e)
        {
            if (bmp == null)
                return;
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.ShowDialog();
            string path = saveFileDialog.FileName;
            if (path != String.Empty)
                bmp.SaveImage(saveFileDialog.FileName);
        }

        private void EllipseButton_Click(object sender, RoutedEventArgs e)
        {
            curTool = Tool.Ellipse;
            mouseHandlingMode = MouseHandlingMode.None;
        }

        private void Mirror_Click(object sender, RoutedEventArgs e)
        {
            tools.mirrorStrokes = !tools.mirrorStrokes;
        }

        private void SpiralCount_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            tools.spiralCount = SpiralCount.SelectedIndex + 1;
        }

        private void SpiralCount_Loaded(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            for (int i = 1; i <= 50; i++)
            {
                comboBox.Items.Add(i);
            }
            comboBox.SelectedIndex = 0;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            Array.Copy(orig, bmp.underlyingBytes.ByteArray, orig.Length);
            UpdateContent();
        }
    }
}
