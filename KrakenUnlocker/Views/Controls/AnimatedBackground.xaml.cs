using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace KrakenUnlocker.Views.Controls
{
    public partial class AnimatedBackground : System.Windows.Controls.UserControl
    {
        private System.Windows.Threading.DispatcherTimer _animationTimer;
        private List<HexNode> _nodes = new List<HexNode>();
        private Random _random = new Random();
        private WpfColor _themeColor = WpfColor.FromRgb(204, 0, 0); // Default red

        public AnimatedBackground()
        {
            InitializeComponent();
            Loaded += AnimatedBackground_Loaded;
            SizeChanged += AnimatedBackground_SizeChanged;
        }

        public void SetThemeColor(string colorName)
        {
            _themeColor = colorName switch
            {
                "Red" => WpfColor.FromRgb(204, 0, 0),
                "Blue" => WpfColor.FromRgb(0, 120, 215),
                "Purple" => WpfColor.FromRgb(136, 23, 152),
                "Green" => WpfColor.FromRgb(16, 137, 62),
                "Cyan" => WpfColor.FromRgb(0, 183, 195),
                "Orange" => WpfColor.FromRgb(255, 140, 0),
                "Pink" => WpfColor.FromRgb(232, 17, 135),
                "Yellow" => WpfColor.FromRgb(255, 185, 0),
                _ => WpfColor.FromRgb(204, 0, 0)
            };
            
            RefreshGrid();
        }

        private void AnimatedBackground_Loaded(object sender, RoutedEventArgs e)
        {
            GenerateHexGrid();
            StartAnimation();
        }

        private void AnimatedBackground_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded)
            {
                HexCanvas.Children.Clear();
                _nodes.Clear();
                GenerateHexGrid();
            }
        }

        private void GenerateHexGrid()
        {
            double width = ActualWidth > 0 ? ActualWidth : 800;
            double height = ActualHeight > 0 ? ActualHeight : 600;
            
            int nodeSpacing = 80;
            int cols = (int)(width / nodeSpacing) + 2;
            int rows = (int)(height / nodeSpacing) + 2;

            // Create nodes
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    double x = col * nodeSpacing + (row % 2 == 1 ? nodeSpacing / 2 : 0);
                    double y = row * nodeSpacing;

                    var node = new HexNode
                    {
                        X = x,
                        Y = y,
                        OriginalX = x,
                        OriginalY = y
                    };
                    _nodes.Add(node);

                    // Draw node
                    var ellipse = new Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Fill = new SolidColorBrush(_themeColor) { Opacity = 0.3 }
                    };
                    Canvas.SetLeft(ellipse, x - 2);
                    Canvas.SetTop(ellipse, y - 2);
                    HexCanvas.Children.Add(ellipse);
                    node.Visual = ellipse;
                }
            }

            // Draw connecting lines
            for (int i = 0; i < _nodes.Count; i++)
            {
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(_nodes[i].X - _nodes[j].X, 2) +
                        Math.Pow(_nodes[i].Y - _nodes[j].Y, 2)
                    );

                    if (distance < nodeSpacing * 1.5)
                    {
                        var line = new Line
                        {
                            X1 = _nodes[i].X,
                            Y1 = _nodes[i].Y,
                            X2 = _nodes[j].X,
                            Y2 = _nodes[j].Y,
                            Stroke = new SolidColorBrush(_themeColor) { Opacity = 0.15 },
                            StrokeThickness = 1
                        };
                        HexCanvas.Children.Insert(0, line);
                        _nodes[i].Lines.Add(line);
                    }
                }
            }
        }

        private void StartAnimation()
        {
            _animationTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _animationTimer.Tick += AnimationTimer_Tick;
            _animationTimer.Start();
        }

        private double _time = 0;
        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            _time += 0.02;

            foreach (var node in _nodes)
            {
                // Gentle floating animation
                double offsetX = Math.Sin(_time + node.OriginalX * 0.01) * 3;
                double offsetY = Math.Cos(_time + node.OriginalY * 0.01) * 3;
                
                node.X = node.OriginalX + offsetX;
                node.Y = node.OriginalY + offsetY;

                if (node.Visual != null)
                {
                    Canvas.SetLeft(node.Visual, node.X - 2);
                    Canvas.SetTop(node.Visual, node.Y - 2);
                }

                // Update connected lines
                foreach (var line in node.Lines)
                {
                    line.X1 = node.X;
                    line.Y1 = node.Y;
                }
            }
        }

        private void RefreshGrid()
        {
            foreach (var node in _nodes)
            {
                if (node.Visual != null)
                {
                    ((SolidColorBrush)node.Visual.Fill).Color = _themeColor;
                }

                foreach (var line in node.Lines)
                {
                    ((SolidColorBrush)line.Stroke).Color = _themeColor;
                }
            }
        }

        private class HexNode
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double OriginalX { get; set; }
            public double OriginalY { get; set; }
            public Ellipse? Visual { get; set; }
            public List<Line> Lines { get; set; } = new List<Line>();
        }
    }
}
