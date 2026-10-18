using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.UI
{
    // A calm view of space for the main menu: stars drifting and twinkling over a slow nebula, a gas giant turning with a
    // moon on its orbit, a small ringed planet far away, a shooting star now and then and, once in a while, the Thexder
    // jet crossing in the distance. The planets are shaded spheres over procedural surfaces. It is drawn to be cheap:
    // star layers are cached tiles that only slide, the soft shapes are cached bitmaps that move by transform, and
    // the planets are shaded 20 times a second on a worker thread; the UI thread only copies the result in, and never
    // waits for the render thread to do it, so moving the window stays smooth. It animates only while visible and not paused; with Windows
    // animation effects off it is one still picture.
    internal partial class CosmosScene
    {
        private const int TexW = 512, TexH = 256;
        private const double JetEvery = 42, JetCrossing = 26;   // seconds
        private static readonly Lazy<uint[]> GasGiantSurface = new(() => GasGiant(7));
        private static readonly Lazy<uint[]> MoonSurface = new(() => Rocky(11));
        private static readonly Lazy<uint[]> DesertSurface = new(() => Desert(5));
        private static Task? surfaces;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new();
        private readonly List<(Rectangle Layer, TranslateTransform Slide, double Speed, double Alpha, double Phase)> starLayers = new();
        private Sphere? giant, moon, far;
        private Task<(Sphere Giant, Sphere Moon, Sphere Far)>? shading;
        private BitmapSource? jetFrame;
        private double giantRadius, moonRadius, lastPlanetFrame = -1, shootingStart = -10, nextShootingStar = 4;
        private Point giantCentre, shootingFrom, shootingDirection;
        private bool rendering, paused, waitingForSurfaces;

        public CosmosScene()
        {
            InitializeComponent();
            SizeChanged += (_, _) => Layout();
            IsVisibleChanged += (_, _) => UpdateRendering();
            Loaded += (_, _) => UpdateRendering();
            Unloaded += (_, _) => StopRendering();
        }

        /// <summary>
        /// Starts making the planet surfaces in the background: they take a moment, so the app calls this at start-up
        /// and they are ready by the time the menu shows.
        /// </summary>
        public static Task Prepare() => surfaces ??= Task.Run(() =>
        {
            _ = GasGiantSurface.Value;
            _ = MoonSurface.Value;
            _ = DesertSurface.Value;
        });

        /// <summary>Holds the picture still, e.g. while a dialog is open over it.</summary>
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

        private double W => Math.Max(1, ActualWidth);

        private double H => Math.Max(1, ActualHeight);

        /// <summary>Takes the jet's picture from the game art.</summary>
        public void Start(GameArt art, Palette palette)
        {
            int s = art.S;
            jetFrame = SpaceScene.Frames(art.Sprite("Plane"), 5 * s, 5 * s, 1, palette).FirstOrDefault();
            Jet.Source = jetFrame;
        }

        private void UpdateRendering()
        {
            bool want = IsLoaded && IsVisible && !paused && SystemParameters.ClientAreaAnimation;
            if (want && !rendering)
            {
                CompositionTarget.Rendering += OnRendering;
                rendering = true;
            }
            else if (!want && rendering)
            {
                StopRendering();
            }
            else if (!want && !SystemParameters.ClientAreaAnimation)
            {
                Draw(20, force: true);
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

        private void OnRendering(object? sender, EventArgs e) => Draw(clock.Elapsed.TotalSeconds, force: false);

        // ---- Layout ----------------------------------------------------------------------------------------------

        private void Layout()
        {
            if (ActualWidth < 2 || ActualHeight < 2)
            {
                return;
            }
            Nebula(NebulaA, 0.18, 0.28, 0.55, Color.FromArgb(0x5C, 0x3A, 0x2E, 0x7A));
            Nebula(NebulaB, 0.55, 0.14, 0.42, Color.FromArgb(0x40, 0x1E, 0x5A, 0x6E));
            Nebula(NebulaC, 0.36, 0.72, 0.5, Color.FromArgb(0x3A, 0x5A, 0x2A, 0x5E));
            BuildStars();
            JetTrail.Height = 2;
            JetTrail.Width = W * 0.16;
            JetTrail.Fill = new LinearGradientBrush(Color.FromArgb(0, 0x9F, 0xD8, 0xFF), Color.FromArgb(0x70, 0x9F, 0xD8, 0xFF), 0);
            Jet.Width = Jet.Height = H * 0.05;
            if (Prepare().IsCompletedSuccessfully)
            {
                LayoutPlanets();
            }
            else if (!waitingForSurfaces)
            {
                // The menu came up before the surfaces were made: the sky shows now, the planets fade in after.
                waitingForSurfaces = true;
                Prepare().ContinueWith(_ => Dispatcher.BeginInvoke(() =>
                {
                    waitingForSurfaces = false;
                    LayoutPlanets();
                    foreach (UIElement planet in new UIElement[] { Glow, BigPlanet, MoonBehind, MoonFront, FarPlanet, RingBack, RingFront })
                    {
                        planet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2)));
                    }
                }), TaskScheduler.Default);
            }
            Draw(clock.Elapsed.TotalSeconds, force: true);
        }

        private void LayoutPlanets()
        {
            if (ActualWidth < 2 || ActualHeight < 2)
            {
                return;
            }

            // One sun, behind the planets and up to the left: they show lit crescents towards the menu, night sides away.
            var light = (-0.62, -0.5, -0.6);
            giantRadius = H * 0.4;
            giantCentre = new Point(W * 0.8, H * 0.88);
            giant = new Sphere(GasGiantSurface.Value, (int)Math.Min(2 * giantRadius, 340), 0.32, light, Color.FromRgb(0x6F, 0x9C, 0xFF), 0.08);
            Place(BigPlanet, giant.Bitmap, giantCentre, giantRadius);
            Glow.Width = Glow.Height = giantRadius * 2.3;
            Canvas.SetLeft(Glow, giantCentre.X - Glow.Width / 2);
            Canvas.SetTop(Glow, giantCentre.Y - Glow.Height / 2);
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x5F, 0x86, 0xE0), 0.8));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0x5F, 0x86, 0xE0), 0.875));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x5F, 0x86, 0xE0), 1));
            Glow.Fill = glow;
            Glow.CacheMode = new BitmapCache();

            moonRadius = giantRadius * 0.15;
            moon = new Sphere(MoonSurface.Value, (int)Math.Min(2 * moonRadius, 150), 0.1, light, Color.FromRgb(0x50, 0x58, 0x70), 0.06);
            MoonBehind.Source = MoonFront.Source = moon.Bitmap;
            MoonBehind.Width = MoonBehind.Height = MoonFront.Width = MoonFront.Height = moonRadius * 2;

            double farRadius = H * 0.045;
            var farCentre = new Point(W * 0.57, H * 0.2);
            far = new Sphere(DesertSurface.Value, (int)Math.Min(2 * farRadius, 96), -0.2, light, Color.FromRgb(0xFF, 0x9A, 0x70), 0.1);
            Place(FarPlanet, far.Bitmap, farCentre, farRadius);
            Rings(farCentre, farRadius);
            Draw(clock.Elapsed.TotalSeconds, force: true);
        }

        private void Nebula(Ellipse cloud, double x, double y, double size, Color colour)
        {
            cloud.Width = H * size * 2.2;
            cloud.Height = H * size * 1.4;
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(colour, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(colour.A / 3), colour.R, colour.G, colour.B), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, colour.R, colour.G, colour.B), 1));
            cloud.Fill = brush;
            Canvas.SetLeft(cloud, W * x - cloud.Width / 2);
            Canvas.SetTop(cloud, H * y - cloud.Height / 2);
            cloud.RenderTransform = new TranslateTransform();
            cloud.CacheMode = new BitmapCache();
        }

        private static void Place(Image image, ImageSource source, Point centre, double radius)
        {
            image.Source = source;
            image.Width = image.Height = radius * 2;
            Canvas.SetLeft(image, centre.X - radius);
            Canvas.SetTop(image, centre.Y - radius);
        }

        // The ring is drawn whole behind the planet and its near half again in front of it.
        private void Rings(Point centre, double radius)
        {
            foreach (var (ring, front) in new[] { (RingBack, false), (RingFront, true) })
            {
                ring.Width = radius * 3.4;
                ring.Height = radius * 0.8;
                ring.StrokeThickness = radius * 0.16;
                ring.Stroke = new LinearGradientBrush(Color.FromArgb(0xA0, 0xE8, 0xC8, 0xA8), Color.FromArgb(0x40, 0xB8, 0x90, 0x78), 0);
                Canvas.SetLeft(ring, centre.X - ring.Width / 2);
                Canvas.SetTop(ring, centre.Y - ring.Height / 2);
                ring.RenderTransformOrigin = new Point(0.5, 0.5);
                ring.RenderTransform = new RotateTransform(-16);
                ring.Clip = front ? new RectangleGeometry(new Rect(0, ring.Height / 2, ring.Width, ring.Height / 2)) : null;
                ring.CacheMode = new BitmapCache();
            }
        }

        // Three depths of stars, each in two groups that twinkle out of step. Each group is a tile brush over the whole
        // scene: the stars are drawn into it once and the tile only slides (it repeats, so the sky wraps).
        private void BuildStars()
        {
            Stars.Children.Clear();
            starLayers.Clear();
            var depths = new[] { (Count: 0.00038, Size: 1.0, Speed: 0.004, Colour: Color.FromRgb(0xB8, 0xC4, 0xFF), Alpha: 0.5),
                                 (Count: 0.00018, Size: 1.5, Speed: 0.009, Colour: Color.FromRgb(0xE4, 0xE9, 0xFF), Alpha: 0.75),
                                 (Count: 0.00005, Size: 2.4, Speed: 0.016, Colour: Colors.White, Alpha: 1.0) };
            var rng = new Random(1985);
            foreach (var depth in depths)
            {
                for (int group = 0; group < 2; group++)
                {
                    var geometry = new StreamGeometry();
                    using (var c = geometry.Open())
                    {
                        int count = (int)(W * H * depth.Count / 2);
                        for (int i = 0; i < count; i++)
                        {
                            var at = new Point(2 + rng.NextDouble() * (W - 4), rng.NextDouble() * H);
                            c.BeginFigure(at, false, false);
                            c.LineTo(new Point(at.X + 0.01, at.Y), true, false);
                        }
                    }
                    geometry.Freeze();
                    var pen = new Pen(new SolidColorBrush(depth.Colour), depth.Size) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    var slide = new TranslateTransform();
                    var tile = new DrawingBrush(new GeometryDrawing(null, pen, geometry))
                    {
                        TileMode = TileMode.Tile,
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                        Viewbox = new Rect(0, 0, W, H),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, W, H),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Transform = slide,
                    };
                    RenderOptions.SetCachingHint(tile, CachingHint.Cache);
                    var layer = new Rectangle { Width = W, Height = H, Fill = tile, Opacity = depth.Alpha };
                    Stars.Children.Add(layer);
                    starLayers.Add((layer, slide, depth.Speed * H, depth.Alpha, rng.NextDouble() * Math.PI * 2));
                }
            }
        }

        // ---- Animation -------------------------------------------------------------------------------------------

        private void Draw(double t, bool force)
        {
            foreach (var (layer, slide, speed, alpha, phase) in starLayers)
            {
                slide.X = -(t * speed % W);
                layer.Opacity = alpha * (0.72 + 0.28 * Math.Sin(t * 0.9 + phase));
            }
            int n = 0;
            foreach (var cloud in new[] { NebulaA, NebulaB, NebulaC })
            {
                if (cloud.RenderTransform is TranslateTransform drift)
                {
                    drift.X = Math.Sin(t * 0.021 + n * 2.1) * W * 0.02;
                    drift.Y = Math.Cos(t * 0.017 + n * 1.3) * H * 0.015;
                    cloud.Opacity = 0.8 + 0.2 * Math.Sin(t * 0.11 + n * 1.7);
                }
                n++;
            }

            ShootingStars(t);
            FlyBy(t);
            if (giant == null || moon == null || far == null)
            {
                return;
            }

            // The moon goes round the giant: behind it on the far half of the orbit.
            double angle = t * Math.PI * 2 / 80 + 2.2, tilt = -0.2;
            double ox = Math.Cos(angle) * giantRadius * 1.6, oy = Math.Sin(angle) * giantRadius * 0.3;
            var moonCentre = new Point(giantCentre.X + ox * Math.Cos(tilt) - oy * Math.Sin(tilt), giantCentre.Y + ox * Math.Sin(tilt) + oy * Math.Cos(tilt));
            bool behind = Math.Sin(angle) < 0;
            MoonBehind.Visibility = behind ? Visibility.Visible : Visibility.Hidden;
            MoonFront.Visibility = behind ? Visibility.Hidden : Visibility.Visible;
            foreach (var image in new[] { MoonBehind, MoonFront })
            {
                Canvas.SetLeft(image, moonCentre.X - moonRadius);
                Canvas.SetTop(image, moonCentre.Y - moonRadius);
            }

            if (force)
            {
                giant.Shade(t / 170 * TexW);
                moon.Shade(t / 95 * TexW);
                far.Shade(t / 60 * TexW);
                giant.Show(wait: true);
                moon.Show(wait: true);
                far.Show(wait: true);
                return;
            }
            // Pictures shaded on the worker go in when the bitmaps are free; then the next ones are started.
            if (shading is { IsCompleted: true } done)
            {
                var (g, m, f) = done.Result;
                if (g != giant || m != moon || f != far || (g.Show(wait: false) & m.Show(wait: false) & f.Show(wait: false)))
                {
                    shading = null;
                }
            }
            if (shading == null && t - lastPlanetFrame >= 1 / 20.0)
            {
                lastPlanetFrame = t;
                var (sg, sm, sf) = (giant, moon, far);
                shading = Task.Run(() =>
                {
                    sg.Shade(t / 170 * TexW);
                    sm.Shade(t / 95 * TexW);
                    sf.Shade(t / 60 * TexW);
                    return (sg, sm, sf);
                });
            }
        }

        private void ShootingStars(double t)
        {
            if (t >= nextShootingStar)
            {
                shootingStart = t;
                nextShootingStar = t + 7 + random.NextDouble() * 9;
                shootingFrom = new Point(W * (0.2 + random.NextDouble() * 0.7), H * (0.04 + random.NextDouble() * 0.35));
                double a = Math.PI * (0.78 + random.NextDouble() * 0.12);
                shootingDirection = new Point(Math.Cos(a), Math.Sin(a));
                ShootingStar.Stroke = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Colors.White, new Point(1, 0), new Point(0, 1));
            }
            double p = (t - shootingStart) / 0.9;
            if (p is < 0 or > 1)
            {
                ShootingStar.Opacity = 0;
                return;
            }
            double travel = H * 0.35 * p, length = H * 0.12 * Math.Min(1, p * 2.5);
            var head = new Point(shootingFrom.X + shootingDirection.X * travel, shootingFrom.Y + shootingDirection.Y * travel);
            ShootingStar.X1 = head.X - shootingDirection.X * length;
            ShootingStar.Y1 = head.Y - shootingDirection.Y * length;
            ShootingStar.X2 = head.X;
            ShootingStar.Y2 = head.Y;
            ShootingStar.Opacity = Math.Sin(Math.PI * p) * 0.9;
        }

        // The jet crosses far off every so often, its trail fading behind it.
        private void FlyBy(double t)
        {
            double since = (t - 6) % JetEvery;
            if (jetFrame == null || t < 6 || since > JetCrossing)
            {
                Jet.Opacity = JetTrail.Opacity = 0;
                return;
            }
            double p = since / JetCrossing;
            double x = -Jet.Width + (W + Jet.Width * 2) * p, y = H * 0.1 + Math.Sin(p * Math.PI * 2) * H * 0.012;
            Canvas.SetLeft(Jet, x);
            Canvas.SetTop(Jet, y - Jet.Height / 2);
            Canvas.SetLeft(JetTrail, x - JetTrail.Width + Jet.Width * 0.2);
            Canvas.SetTop(JetTrail, y);
            double fade = Math.Min(1, Math.Min(p, 1 - p) * 8);
            Jet.Opacity = 0.85 * fade;
            JetTrail.Opacity = fade;
        }

        // ---- Planets ---------------------------------------------------------------------------------------------

        // A lit sphere over a wrapping surface texture. Each pixel's texture position, light and rim are worked out once;
        // a frame only shifts the texture round the axis. Shade (any thread) makes the picture in a buffer, Show (UI
        // thread) copies it into the bitmap.
        private sealed class Sphere
        {
            private readonly uint[] texture;
            private readonly int[] pixel;
            private readonly float[] u, v, light, rim, cover;
            private readonly uint[] shaded;
            private bool fresh;
            private readonly Color rimColour;

            public Sphere(uint[] texture, int size, double tilt, (double X, double Y, double Z) towardsLight, Color rimColour, double ambient)
            {
                this.texture = texture;
                this.rimColour = rimColour;
                size = Math.Max(8, size);
                Bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
                double len = Math.Sqrt(towardsLight.X * towardsLight.X + towardsLight.Y * towardsLight.Y + towardsLight.Z * towardsLight.Z);
                var (lx, ly, lz) = (towardsLight.X / len, towardsLight.Y / len, towardsLight.Z / len);
                double r = size / 2.0, cos = Math.Cos(tilt), sin = Math.Sin(tilt);
                var list = new List<(int, float, float, float, float, float)>();
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        double dx = x + 0.5 - r, dy = y + 0.5 - r, dist = Math.Sqrt(dx * dx + dy * dy);
                        double coverage = Math.Clamp(r - dist + 0.5, 0, 1);
                        if (coverage <= 0)
                        {
                            continue;
                        }
                        double nx = dx / r, ny = dy / r, d2 = Math.Min(1, nx * nx + ny * ny), nz = Math.Sqrt(1 - d2);
                        double tx = nx * cos - ny * sin, ty = nx * sin + ny * cos;
                        double lon = Math.Atan2(tx, nz), lat = Math.Asin(Math.Clamp(ty, -1, 1));
                        double lambert = Math.Max(0, nx * lx + ny * ly + nz * lz);
                        list.Add((y * size + x, (float)((lon / (2 * Math.PI) + 0.5) * TexW), (float)((lat / Math.PI + 0.5) * (TexH - 1)),
                            (float)(ambient + (1 - ambient) * Math.Pow(lambert, 0.85)), (float)(Math.Pow(1 - nz, 3) * (0.2 + 0.8 * lambert)), (float)coverage));
                    }
                }
                shaded = new uint[size * size];
                pixel = list.Select(e => e.Item1).ToArray();
                u = list.Select(e => e.Item2).ToArray();
                v = list.Select(e => e.Item3).ToArray();
                light = list.Select(e => e.Item4).ToArray();
                rim = list.Select(e => e.Item5).ToArray();
                cover = list.Select(e => e.Item6).ToArray();
            }

            public WriteableBitmap Bitmap { get; }

            /// <summary>Makes the picture with the surface turned by <paramref name="turn"/> texels; safe off the UI thread.</summary>
            public void Shade(double turn)
            {
                for (int i = 0; i < pixel.Length; i++)
                {
                    double uu = u[i] + turn;
                    int u0 = (int)Math.Floor(uu);
                    float f = (float)(uu - u0);
                    u0 = ((u0 % TexW) + TexW) % TexW;
                    int u1 = (u0 + 1) % TexW, row = (int)v[i] * TexW;
                    uint a = texture[row + u0], b = texture[row + u1];
                    float s = light[i], k = rim[i], c = cover[i];
                    float red = (((a >> 16) & 0xff) * (1 - f) + ((b >> 16) & 0xff) * f) * s + rimColour.R * k;
                    float green = (((a >> 8) & 0xff) * (1 - f) + ((b >> 8) & 0xff) * f) * s + rimColour.G * k;
                    float blue = ((a & 0xff) * (1 - f) + (b & 0xff) * f) * s + rimColour.B * k;
                    uint ri = (uint)(Math.Min(255f, red) * c), gi = (uint)(Math.Min(255f, green) * c), bi = (uint)(Math.Min(255f, blue) * c);
                    shaded[pixel[i]] = ((uint)(c * 255) << 24) | (ri << 16) | (gi << 8) | bi;
                }
                fresh = true;
            }

            /// <summary>
            /// Copies the last picture into the bitmap. Without <paramref name="wait"/> it does not wait for the render
            /// thread: false when the bitmap is busy (try again next frame).
            /// </summary>
            public unsafe bool Show(bool wait)
            {
                if (!fresh)
                {
                    return true;
                }
                if (wait)
                {
                    Bitmap.Lock();
                }
                else if (!Bitmap.TryLock(new Duration(TimeSpan.Zero)))
                {
                    return false;
                }
                try
                {
                    int size = Bitmap.PixelWidth, stride = Bitmap.BackBufferStride;
                    fixed (uint* src = shaded)
                    {
                        for (int y = 0; y < size; y++)
                        {
                            Buffer.MemoryCopy(src + y * size, (byte*)Bitmap.BackBuffer + y * stride, stride, size * 4);
                        }
                    }
                    Bitmap.AddDirtyRect(new Int32Rect(0, 0, size, size));
                    fresh = false;
                }
                finally
                {
                    Bitmap.Unlock();
                }
                return true;
            }
        }

        // ---- Surfaces --------------------------------------------------------------------------------------------

        // Calm blue-violet bands with cream belts and a pale storm.
        private static uint[] GasGiant(int seed)
        {
            var noise = new ValueNoise(seed);
            Color[] palette = { Color.FromRgb(0x1C, 0x26, 0x46), Color.FromRgb(0x2C, 0x3E, 0x68), Color.FromRgb(0x44, 0x5A, 0x8C), Color.FromRgb(0x68, 0x7C, 0xAA), Color.FromRgb(0x8E, 0x96, 0xB4) };
            return Surface((u, v) =>
            {
                double turb = noise.Fbm(u, v, 6, 3, 4);
                double t = v * 5.5 + (turb - 0.5) * 0.7 + Math.Sin(u * Math.PI * 4 + v * 10) * 0.03;
                double band = 0.5 + 0.32 * Math.Sin(t * Math.PI * 2) + 0.18 * Math.Sin(t * Math.PI * 5.3 + 1.1);
                double hue = noise.Fbm(u + 0.37, v * 0.5, 4, 2, 2);
                var colour = Lerp(Ramp(palette, band * 0.85 + hue * 0.3), Color.FromRgb(0, 0, 0), 0);
                double fine = (noise.Fbm(u, v, 24, 40, 3) - 0.5) * 0.1;
                double du = Math.Min(Math.Abs(u - 0.3), 1 - Math.Abs(u - 0.3)) / 0.06, dv = (v - 0.62) / 0.032;
                double storm = Math.Max(0, 1 - (du * du + dv * dv));
                colour = Lerp(colour, Color.FromRgb(0xD8, 0xD4, 0xDA), storm * 0.7);
                double pole = Math.Min(1, Math.Min(v, 1 - v) / 0.1);
                return Scale(colour, (1 + fine) * (0.55 + 0.45 * pole));
            });
        }

        private static uint[] Rocky(int seed)
        {
            var noise = new ValueNoise(seed);
            var rng = new Random(seed);
            var craters = Enumerable.Range(0, 26).Select(_ => (U: rng.NextDouble(), V: 0.15 + rng.NextDouble() * 0.7, R: 0.015 + rng.NextDouble() * 0.05)).ToArray();
            return Surface((u, v) =>
            {
                double shade = 0.52 + (noise.Fbm(u, v, 8, 4, 4) - 0.5) * 0.5 + (noise.Fbm(u, v, 32, 16, 2) - 0.5) * 0.2;
                foreach (var (cu, cv, cr) in craters)
                {
                    double du = Math.Min(Math.Abs(u - cu), 1 - Math.Abs(u - cu)) * 2, d = Math.Sqrt(du * du + (v - cv) * (v - cv)) / cr;
                    if (d < 1)
                    {
                        shade *= 0.78 + 0.22 * d * d;
                    }
                    else if (d < 1.25)
                    {
                        shade *= 1.08;
                    }
                }
                return Color.FromRgb(Byte(shade * 172), Byte(shade * 174), Byte(shade * 184));
            });
        }

        private static uint[] Desert(int seed)
        {
            var noise = new ValueNoise(seed);
            return Surface((u, v) =>
            {
                double n = noise.Fbm(u, v, 8, 4, 4) + Math.Sin(v * 18 + noise.Fbm(u, v, 4, 2, 2) * 4) * 0.08;
                return Lerp(Color.FromRgb(0x7A, 0x34, 0x26), Color.FromRgb(0xDE, 0x92, 0x60), Math.Clamp(n, 0, 1));
            });
        }

        private static uint[] Surface(Func<double, double, Color> at)
        {
            var pixels = new uint[TexW * TexH];
            Parallel.For(0, TexH, y =>
            {
                for (int x = 0; x < TexW; x++)
                {
                    var c = at((double)x / TexW, (double)y / TexH);
                    pixels[y * TexW + x] = ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
                }
            });
            return pixels;
        }

        private static Color Ramp(Color[] palette, double t)
        {
            t = Math.Clamp(t, 0, 0.9999) * (palette.Length - 1);
            int i = (int)t;
            return Lerp(palette[i], palette[i + 1], t - i);
        }

        private static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb(Byte(a.R + (b.R - a.R) * t), Byte(a.G + (b.G - a.G) * t), Byte(a.B + (b.B - a.B) * t));

        private static Color Scale(Color c, double k) => Color.FromRgb(Byte(c.R * k), Byte(c.G * k), Byte(c.B * k));

        private static byte Byte(double value) => (byte)Math.Clamp(value, 0, 255);

        // Smooth value noise that wraps round the planet (in u) at every octave.
        private sealed class ValueNoise
        {
            private readonly float[] grid = new float[256 * 256];

            public ValueNoise(int seed)
            {
                var rng = new Random(seed);
                for (int i = 0; i < grid.Length; i++)
                {
                    grid[i] = (float)rng.NextDouble();
                }
            }

            public double Fbm(double u, double v, int cellsU, double cellsV, int octaves)
            {
                double sum = 0, amplitude = 0.5, total = 0;
                for (int o = 0, f = 1; o < octaves; o++, f *= 2)
                {
                    sum += amplitude * Sample(u * cellsU * f, v * cellsV * f, cellsU * f);
                    total += amplitude;
                    amplitude *= 0.5;
                }
                return sum / total;
            }

            private double Sample(double x, double y, int wrap)
            {
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                double fx = x - x0, fy = y - y0;
                fx = fx * fx * (3 - 2 * fx);
                fy = fy * fy * (3 - 2 * fy);
                int xa = ((x0 % wrap) + wrap) % wrap, xb = (xa + 1) % wrap;
                double top = At(xa, y0) + (At(xb, y0) - At(xa, y0)) * fx;
                double bottom = At(xa, y0 + 1) + (At(xb, y0 + 1) - At(xa, y0 + 1)) * fx;
                return top + (bottom - top) * fy;
            }

            private float At(int x, int y) => grid[((y & 255) << 8) | (x & 255)];
        }
    }
}
