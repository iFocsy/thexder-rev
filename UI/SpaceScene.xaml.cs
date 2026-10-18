using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.UI
{
    // Thexder, drawn from the game's own sprites, runs on a grid floor, turns into the jet and lands again while the
    // stars stream past. Everything scales with the control's size; with Windows animation effects off it is one still
    // frame. It only animates while visible.
    internal partial class SpaceScene
    {
        private const double Cycle = 11;                     // seconds: run, transform, fly, land, run
        private const double FramesPerSecond = 15;
        private static readonly double[] StarLayerSpeed = { 0.18, 0.45, 1.0 };

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private BitmapSource[] run = [], transform = [], plane = [];
        private double spriteScale;
        private (double X, double Y, int Layer)[] stars = [];
        private double distance, lastTime;
        private bool rendering, paused;

        public SpaceScene()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Layout();
            IsVisibleChanged += (_, _) => UpdateRendering();
            Loaded += (_, _) => UpdateRendering();
            Unloaded += (_, _) => StopRendering();
        }

        /// <summary>Stops the animation where it is (and its clock, so it carries on from there).</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                if (paused)
                {
                    clock.Stop();
                }
                else
                {
                    clock.Start();
                }
                UpdateRendering();
            }
        }

        /// <summary>The robot's height as a share of the scene's.</summary>
        public double RobotSize { get; set; } = 0.53;

        /// <summary>Where the robot stands, as a share of the width.</summary>
        public double RobotPlace { get; set; } = 0.67;

        // Sizes in scene pixels, from the current size.
        private double W => Math.Max(1, ActualWidth);

        private double H => Math.Max(1, ActualHeight);

        private double Horizon => H * 0.744;

        private double FloorY => H * 0.92;

        private double RobotHeight => H * RobotSize;

        private double RunSpeed => H * 0.28;

        private double FlySpeed => H * 3.0;

        /// <summary>Cuts the robot's frames out of the game art.</summary>
        public void Start(GameArt art, Palette palette)
        {
            int s = art.S;
            run = Frames(art.Sprite("ThexderR"), 4 * s, 6 * s, 12, palette);
            transform = Frames(art.Sprite("W2PRight"), 5 * s, 6 * s, 12, palette);
            plane = Frames(art.Sprite("Plane"), 5 * s, 5 * s, 1, palette);
            spriteScale = 1.0 / (6 * s);
            Draw(1.0, 0);
        }

        private void UpdateRendering()
        {
            bool want = IsLoaded && IsVisible && !paused && SystemParameters.ClientAreaAnimation;
            if (want && !rendering)
            {
                CompositionTarget.Rendering += OnRendering;
                rendering = true;
                lastTime = clock.Elapsed.TotalSeconds;
            }
            else if (!want)
            {
                StopRendering();
                if (!SystemParameters.ClientAreaAnimation)
                {
                    Draw(1.0, 0);
                }
            }
        }

        private void StopRendering()
        {
            if (rendering)
            {
                CompositionTarget.Rendering -= OnRendering;
                rendering = false;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(t - lastTime, 0, 0.1);
            lastTime = t;
            Draw(t, dt);
        }

        // Planet, floor and stars for the current size.
        private void Layout()
        {
            double size = H * 1.84;
            Planet.Width = Planet.Height = size;
            Canvas.SetLeft(Planet, W - size * 0.587);
            Canvas.SetTop(Planet, -size * 0.717);
            FloorTint.Width = HorizonLine.Width = W;
            FloorTint.Height = H - Horizon;
            Canvas.SetTop(FloorTint, Horizon);
            Canvas.SetTop(HorizonLine, Horizon - 1);
            var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, Horizon), EndPoint = new Point(0, H) };
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xb0, 0, 0, 0), 1));
            mask.Freeze();
            GridLines.OpacityMask = mask;
            var random = new Random(1985);
            int count = (int)Math.Clamp(W * H / 1170, 60, 600);
            stars = Enumerable.Range(0, count).Select(i => (random.NextDouble() * (W + 40), random.NextDouble() * (Horizon - 4), i % 3)).ToArray();
            Draw(clock.Elapsed.TotalSeconds, 0);
        }

        // Where the loop stands: sprite frame, ground speed, height above the floor, and whether the laser is out.
        private (BitmapSource? Frame, double Speed, double Lift, bool Firing) Pose(double t)
        {
            t %= Cycle;
            double lift = RobotHeight * 0.27;
            int tick = (int)(t * FramesPerSecond);
            if (t < 3.5 || t >= 8.8)
            {
                bool firing = t is (> 1.2 and < 1.32) or (> 1.9 and < 2.02) or (> 9.9 and < 10.02);
                return (Frame(run, tick % Math.Max(1, run.Length)), RunSpeed, 0, firing);
            }
            if (t < 4.3)
            {
                double k = Smooth((t - 3.5) / 0.8);
                return (Frame(transform, (int)((t - 3.5) * FramesPerSecond)), RunSpeed + (FlySpeed - RunSpeed) * k, lift * k, false);
            }
            if (t < 8.0)
            {
                double bob = RobotHeight * 0.05 * Math.Sin((t - 4.3) * 2.6) * Math.Sin(Math.PI * (t - 4.3) / 3.7);
                return (Frame(plane, 0), FlySpeed, lift + bob, false);
            }
            double land = Smooth((t - 8.0) / 0.8);
            return (Frame(transform, transform.Length - 1 - (int)((t - 8.0) * FramesPerSecond)), FlySpeed + (RunSpeed - FlySpeed) * land, lift * (1 - land), false);
        }

        private static BitmapSource? Frame(BitmapSource[] frames, int index) =>
            frames.Length == 0 ? null : frames[Math.Clamp(index, 0, frames.Length - 1)];

        private static double Smooth(double k) => k <= 0 ? 0 : k >= 1 ? 1 : k * k * (3 - 2 * k);

        private void Draw(double t, double dt)
        {
            if (ActualWidth < 1 || ActualHeight < 1)
            {
                return;
            }
            var pose = Pose(t);
            distance += pose.Speed * dt;
            DrawStars(pose.Speed, dt);
            DrawGrid();

            if (pose.Frame == null)
            {
                Robot.Source = null;
                Shadow.Visibility = Visibility.Hidden;
                return;
            }
            double scale = RobotHeight * spriteScale;
            double w = pose.Frame.PixelWidth * scale, h = pose.Frame.PixelHeight * scale, x = W * RobotPlace;
            // The jet's frame is shorter than the walker's: it stays centred where the body was.
            double top = h >= RobotHeight - 0.5 ? FloorY - pose.Lift - h : FloorY - pose.Lift - (RobotHeight + h) / 2;
            Robot.Source = pose.Frame;
            Robot.Width = w;
            Robot.Height = h;
            Canvas.SetLeft(Robot, x);
            Canvas.SetTop(Robot, top);

            double shadow = Math.Max(0.35, 1 - pose.Lift / (RobotHeight * 0.53));
            Shadow.Visibility = Visibility.Visible;
            Shadow.Width = RobotHeight * 0.73 * shadow;
            Shadow.Height = RobotHeight * 0.1;
            Shadow.Opacity = shadow;
            Canvas.SetLeft(Shadow, x + w * 0.47 - Shadow.Width / 2);
            Canvas.SetTop(Shadow, FloorY - Shadow.Height / 2);

            Laser.Visibility = pose.Firing ? Visibility.Visible : Visibility.Hidden;
            if (pose.Firing)
            {
                Laser.X1 = x + w * 0.82;
                Laser.Y1 = top + h * 0.34;
                Laser.X2 = W;
                Laser.Y2 = top + h * 0.3;
            }
        }

        // Three layers of stars streaming left; the faster the robot goes, the longer their streaks.
        private void DrawStars(double speed, double dt)
        {
            var layers = new[] { new StreamGeometry(), new StreamGeometry(), new StreamGeometry() };
            var contexts = layers.Select(g => g.Open()).ToArray();
            const double streak = 0.04;   // seconds of travel each streak shows
            for (int i = 0; i < stars.Length; i++)
            {
                var (x, y, layer) = stars[i];
                double v = speed * StarLayerSpeed[layer];
                x -= v * dt;
                if (x < -40)
                {
                    x += W + 80;
                }
                stars[i] = (x, y, layer);
                contexts[layer].BeginFigure(new Point(x, y), false, false);
                contexts[layer].LineTo(new Point(x + Math.Max(0.01, v * streak), y), true, false);
            }
            foreach (var context in contexts)
            {
                context.Close();
            }
            foreach (var layer in layers)
            {
                layer.Freeze();
            }
            StarsFar.Data = layers[0];
            StarsMid.Data = layers[1];
            StarsNear.Data = layers[2];
        }

        // A perspective floor scrolling under the robot: lines to a vanishing point, rows closer together far away.
        private void DrawGrid()
        {
            double spacing = H * 0.184, vanishX = W / 2;
            var grid = new StreamGeometry();
            using (var c = grid.Open())
            {
                double phase = distance % spacing;
                for (double x = -W * 2.4 - phase; x < W * 3.4; x += spacing)
                {
                    c.BeginFigure(new Point(vanishX + (x - vanishX) * 0.12, Horizon), false, false);
                    c.LineTo(new Point(x, H), true, false);
                }
                for (int i = 1; i <= 7; i++)
                {
                    double y = Horizon + (H - Horizon) * Math.Pow(i / 7.0, 2.2);
                    c.BeginFigure(new Point(0, y), false, false);
                    c.LineTo(new Point(W, y), true, false);
                }
            }
            grid.Freeze();
            GridLines.Data = grid;
        }

        /// <summary>Cuts frames out of a sprite strip into bitmaps (index 0 is transparent).</summary>
        public static BitmapSource[] Frames(IndexedImage? strip, int frameWidth, int frameHeight, int count, Palette palette, int first = 0)
        {
            if (strip == null || strip.Width < frameWidth || strip.Height < frameHeight)
            {
                return [];
            }
            int perRow = strip.Width / frameWidth;
            count = Math.Min(count, perRow * (strip.Height / frameHeight) - first);
            var frames = new BitmapSource[Math.Max(0, count)];
            var pixels = new uint[frameWidth * frameHeight];
            for (int f = 0; f < frames.Length; f++)
            {
                int ox = (first + f) % perRow * frameWidth, oy = (first + f) / perRow * frameHeight;
                for (int y = 0; y < frameHeight; y++)
                {
                    for (int x = 0; x < frameWidth; x++)
                    {
                        byte index = strip.Pixels[(oy + y) * strip.Width + ox + x];
                        pixels[y * frameWidth + x] = index == 0 ? 0 : palette.Colors[index] | 0xff000000u;
                    }
                }
                var bitmap = BitmapSource.Create(frameWidth, frameHeight, 96, 96, PixelFormats.Bgra32, null, pixels, frameWidth * 4);
                bitmap.Freeze();
                frames[f] = bitmap;
            }
            return frames;
        }
    }
}
