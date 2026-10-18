using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Thexder.Core.Assets;
using Thexder.Core.Game;

namespace Thexder.UI
{
    // The About box: Thexder running through space over the wordmark, the credits rolling underneath and the build's
    // details. The button at the banner's top right pauses all of it; the credits always scroll with the wheel. With
    // Windows animation effects off the scene is still.
    internal partial class AboutWindow
    {
        private const double RollSpeed = 22, RollDelay = 2.5;   // pixels per second; seconds before it starts

        private static readonly Brush Brand = Frozen(0xE8, 0x46, 0x3A);
        private static readonly Brush LaserBlue = Frozen(0x4F, 0xD8, 0xFF);
        private static readonly Brush Gold = Frozen(0xFF, 0xB3, 0x47);
        private static readonly Brush Light = Frozen(0xEE, 0xF1, 0xF8);
        private static readonly Brush Dim = Frozen(0x8F, 0x98, 0xAE);

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Action<bool>? animatedChanged;
        private double lastTime, rollOffset = 34;
        private bool still;

        /// <summary><paramref name="animatedChanged"/> hears the pause button (the choice is shared with the main menu).</summary>
        public AboutWindow(GameArt art, Palette palette, bool animated = true, Action<bool>? animatedChanged = null)
        {
            InitializeComponent();
            this.animatedChanged = animatedChanged;
            still = !animated;
            Scene.Start(art, palette);
            ApplyAnimated();
            ShowDetails();
            BuildCredits();
            RollViewport.SizeChanged += (_, e) => RollViewport.OpacityMask = EdgeFade(e.NewSize.Height, 40);
            Loaded += (_, _) =>
            {
                if (SystemParameters.ClientAreaAnimation)
                {
                    Animate();
                }
                else
                {
                    PlaceRoll();
                }
            };
            Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
        }

        // ---- Details ---------------------------------------------------------------------------------------------

        private void ShowDetails()
        {
            var assembly = typeof(AboutWindow).Assembly;
            string[] version = (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+', 2);
            VersionText.Text = $"Version {version[0]}" + (version.Length > 1 ? $" r{version[1]}" : "");
            string built = DateTimeOffset.TryParse(Metadata(assembly, "BuildTime"), out var time) ? $"Built {time.LocalDateTime:g} · " : "";
            BuiltText.Text = built + RuntimeInformation.FrameworkDescription;
            CopyrightText.Text = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
            if (Uri.TryCreate(Metadata(assembly, "Website"), UriKind.Absolute, out var uri))
            {
                WebsiteLink.NavigateUri = uri;
                WebsiteText.Text = uri.Host + uri.AbsolutePath.TrimEnd('/');
            }
        }

        private static string? Metadata(Assembly assembly, string key) =>
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

        // ---- Credits ---------------------------------------------------------------------------------------------

        // credits.txt: "# " a section, "## " a role, "> " a note, other lines names ("nicknames" highlighted).
        private void BuildCredits()
        {
            using var stream = typeof(AboutWindow).Assembly.GetManifestResourceStream("Thexder.credits.txt");
            if (stream == null)
            {
                return;
            }
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    Roll.Children.Add(CreditLine(Spaced(line[2..]), 14, Brand, FontWeights.Bold, Roll.Children.Count == 0 ? 0 : 24));
                }
                else if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    Roll.Children.Add(CreditLine(line[3..].ToUpperInvariant(), 10, LaserBlue, FontWeights.SemiBold, 10));
                }
                else if (line.StartsWith("> ", StringComparison.Ordinal))
                {
                    var note = CreditLine(line[2..], 11.5, Dim, FontWeights.Normal, 2);
                    note.FontStyle = FontStyles.Italic;
                    Roll.Children.Add(note);
                }
                else if (line.Length > 0)
                {
                    Roll.Children.Add(NameLine(line));
                }
            }
        }

        private static TextBlock CreditLine(string text, double size, Brush brush, FontWeight weight, double top) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            Foreground = brush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, 0),
        };

        // Michael "Mega Gun" Branham: the nickname (an enemy of the game) in gold.
        private static TextBlock NameLine(string line)
        {
            var text = CreditLine("", 12.5, Light, FontWeights.Normal, 1);
            string[] parts = line.Split('"');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    text.Inlines.Add(i % 2 == 1 ? new Run($"“{parts[i]}”") { Foreground = Gold } : new Run(parts[i]));
                }
            }
            return text;
        }

        // Fades the roll in and out over its top and bottom edges. Absolute, so the long list inside does not stretch it.
        private static LinearGradientBrush EdgeFade(double height, double edge)
        {
            double k = Math.Min(0.5, edge / Math.Max(1, height));
            var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, height) };
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            brush.GradientStops.Add(new GradientStop(Colors.Black, k));
            brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - k));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            brush.Freeze();
            return brush;
        }

        private static string Spaced(string text) =>
            string.Join("  ", text.ToUpperInvariant().Split(' ').Select(word => string.Join(" ", word.ToCharArray())));

        private void OnRollWheel(object sender, MouseWheelEventArgs e)
        {
            rollOffset += e.Delta / 3.0;
            PlaceRoll();
            e.Handled = true;
        }

        // Keeps the roll in range; when it has gone up out of view it comes in again from the bottom.
        private void PlaceRoll()
        {
            double height = Roll.ActualHeight, view = RollViewport.ActualHeight;
            if (rollOffset < -height)
            {
                rollOffset = view;
            }
            rollOffset = Math.Min(rollOffset, view);
            RollShift.Y = rollOffset;
        }

        // ---- Animation -------------------------------------------------------------------------------------------

        private void Animate()
        {
            Reveal(AuthorMark, 0, 500, 0);
            Reveal(Hero, 120, 700, 16);
            Reveal(Info, 420, 600, 8);
            Reveal(RollViewport, 520, 900, 0);
            ApplyAnimated();
        }

        // Plays or holds everything that moves: the scene, the wordmark's light and the credits roll.
        private void ApplyAnimated()
        {
            Scene.Paused = still;
            Hero.Paused = still;
            AnimationIcon.Symbol = still ? Wpf.Ui.Controls.SymbolRegular.Play24 : Wpf.Ui.Controls.SymbolRegular.Pause24;
            AnimationButton.ToolTip = still ? "Play the animation" : "Pause the animation";
            CompositionTarget.Rendering -= OnRendering;
            if (still)
            {
                clock.Stop();
                PlaceRoll();
            }
            else
            {
                clock.Start();
                if (IsLoaded && SystemParameters.ClientAreaAnimation)
                {
                    lastTime = clock.Elapsed.TotalSeconds;
                    CompositionTarget.Rendering += OnRendering;
                }
            }
        }

        private void OnToggleAnimation(object sender, RoutedEventArgs e)
        {
            still = !still;
            ApplyAnimated();
            animatedChanged?.Invoke(!still);
        }

        // Fades an element in after a delay, rising into place.
        private static void Reveal(FrameworkElement element, int delay, int duration, double rise)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            TimeSpan begin = TimeSpan.FromMilliseconds(delay);
            element.Opacity = 0;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(duration)) { BeginTime = begin, EasingFunction = ease });
            if (rise > 0)
            {
                var shift = new TranslateTransform(0, rise);
                element.RenderTransform = shift;
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(rise, 0, TimeSpan.FromMilliseconds(duration)) { BeginTime = begin, EasingFunction = ease });
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(t - lastTime, 0, 0.1);
            lastTime = t;
            if (t > RollDelay && !RollViewport.IsMouseOver)
            {
                rollOffset -= RollSpeed * dt;
            }
            PlaceRoll();
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private void OnNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
