using System.Windows;
using Thexder.Services;

namespace Thexder.UI
{
    internal enum PanelSlot
    {
        Weapons,
        Equipment,
        Enemy,
        Map,
    }

    /// <summary>Docked windows follow the main window.</summary>
    internal sealed class WindowDock
    {
        public const double MinPanelScale = 0.75;
        public const double MaxPanelScale = 4;

        private const double SnapDistance = 40;
        private const double DetachDistance = 6;
        private const double ColumnSurfaceWidth = 144;
        private static readonly PanelSlot[] Column = { PanelSlot.Weapons, PanelSlot.Equipment, PanelSlot.Enemy };

        private readonly Window main;
        private readonly Dictionary<PanelSlot, PanelWindow> panels = new();
        private readonly Dictionary<PanelSlot, bool> docked = new();
        private readonly Dictionary<PanelSlot, Point> dragOrigin = new();
        private double panelScale = 1.5;
        private double mapHeight;
        private int suppress;

        public WindowDock(Window main)
        {
            this.main = main;
            main.LocationChanged += (_, _) => Arrange();
            main.SizeChanged += (_, _) => Arrange();
            main.StateChanged += (_, _) => Arrange();
        }

        /// <summary>Scale of the 144-pixel-wide Weapons / Equipment / Enemy surfaces when docked.</summary>
        public double PanelScale
        {
            get => panelScale;
            set => panelScale = Math.Clamp(value, MinPanelScale, MaxPanelScale);
        }

        public double ColumnWidth => ColumnSurfaceWidth * PanelScale;

        /// <summary>Height of the docked map's picture (its 68 rows); 0 = 68 rows at the column's scale.</summary>
        public double MapHeight
        {
            get => mapHeight > 0 ? mapHeight : Math.Round(68 * PanelScale);
            set => mapHeight = value <= 0 ? 0 : Math.Clamp(value, 34, 68 * MaxPanelScale);
        }

        /// <summary>False while the main window is maximized or full screen: docked panels are hidden then.</summary>
        public bool CanDock => main.WindowState == WindowState.Normal && main.WindowStyle != WindowStyle.None;

        public void Add(PanelSlot slot, PanelWindow panel, bool isDocked)
        {
            panels[slot] = panel;
            docked[slot] = isDocked;
            panel.MoveSizeStarted += () => dragOrigin[slot] = new Point(panel.Left, panel.Top);
            panel.LocationChanged += (_, _) => OnPanelMoved(slot);
            panel.MoveSizeEnded += resized => OnPanelReleased(slot, resized);
        }

        public bool IsDocked(PanelSlot slot) => docked.TryGetValue(slot, out bool d) && d;

        /// <summary>Window > Standard Configuration: docks every panel again.</summary>
        public void DockAll()
        {
            foreach (var slot in panels.Keys)
            {
                docked[slot] = true;
            }
            Arrange();
        }

        /// <summary>
        /// Lays out the main window and the docked windows for a work area: the largest playfield scale for which the
        /// main window, the side column (scaled to the main window's height) and the map below fit, centred.
        /// </summary>
        public void ApplyLayout(Rect work, double designWidth, double designHeight, double chromeHeight)
        {
            const double margin = 8;
            double columnTitles = Column.Length * PanelWindow.TitleHeight;
            double columnPicture = Column.Sum(PictureHeight);
            double scale = 0.5, panel = MinPanelScale;
            for (int step = 160; step >= 10; step--)
            {
                double s = step / 20.0;
                double mainHeight = designHeight * s + chromeHeight;
                double ps = Math.Clamp((mainHeight - columnTitles) / columnPicture, MinPanelScale, MaxPanelScale);
                double width = ColumnSurfaceWidth * ps + designWidth * s;
                double height = Math.Max(mainHeight, columnPicture * ps + columnTitles) + Math.Round(68 * ps) + PanelWindow.TitleHeight;
                if (width <= work.Width - 2 * margin && height <= work.Height - margin)
                {
                    scale = s;
                    panel = ps;
                    break;
                }
            }
            PanelScale = panel;
            MapHeight = 0; // Back to the default height
            double mainWidth = designWidth * scale;
            double mainHeightFinal = designHeight * scale + chromeHeight;
            double totalWidth = ColumnWidth + mainWidth;
            double totalHeight = Math.Max(mainHeightFinal, columnPicture * PanelScale + columnTitles)
                + MapHeight + PanelWindow.TitleHeight;
            suppress++;
            try
            {
                main.WindowState = WindowState.Normal;
                main.Width = mainWidth;
                main.Height = mainHeightFinal;
                main.Left = work.Left + Math.Max(0, (work.Width - totalWidth) / 2) + ColumnWidth;
                main.Top = work.Top + Math.Max(0, (work.Height - totalHeight) / 2);
            }
            finally
            {
                suppress--;
            }
            DockAll();
        }

        /// <summary>Moves the docked panels to their places around the main window.</summary>
        public void Arrange()
        {
            if (panels.Count == 0)
            {
                return;
            }
            suppress++;
            try
            {
                double x = main.Left - ColumnWidth;
                double y = main.Top;
                foreach (var slot in Column)
                {
                    if (!panels.TryGetValue(slot, out var p) || !IsDocked(slot))
                    {
                        continue;
                    }
                    Place(p, x, y, ColumnWidth, PictureHeight(slot) * PanelScale + PanelWindow.TitleHeight);
                    if (p.IsVisible)
                    {
                        y += p.Height;
                    }
                }
                if (panels.TryGetValue(PanelSlot.Map, out var map) && IsDocked(PanelSlot.Map))
                {
                    double w = main.ActualWidth > 0 ? main.ActualWidth : main.Width;
                    double h = main.ActualHeight > 0 ? main.ActualHeight : main.Height;
                    Place(map, main.Left, main.Top + h, w, MapHeight + PanelWindow.TitleHeight);
                }
            }
            finally
            {
                suppress--;
            }
        }

        private static double PictureHeight(PanelSlot slot) => slot == PanelSlot.Enemy ? 144 : 116;

        private static void Place(PanelWindow p, double x, double y, double w, double h)
        {
            if (p.InMoveSize)
            {
                return; // Never fight the user's drag
            }
            if (Math.Abs(p.Left - x) > 0.5)
            {
                p.Left = x;
            }
            if (Math.Abs(p.Top - y) > 0.5)
            {
                p.Top = y;
            }
            if (Math.Abs(p.Width - w) > 0.5)
            {
                p.Width = w;
            }
            if (Math.Abs(p.Height - h) > 0.5)
            {
                p.Height = h;
            }
        }

        // While the user drags a docked panel: detach it as soon as it really moves, and let the column close up.
        private void OnPanelMoved(PanelSlot slot)
        {
            if (suppress > 0 || !panels.TryGetValue(slot, out var p) || !p.InMoveSize || p.IsResizing || !IsDocked(slot))
            {
                return;
            }
            var origin = dragOrigin.GetValueOrDefault(slot, new Point(p.Left, p.Top));
            if (Math.Abs(p.Left - origin.X) > DetachDistance || Math.Abs(p.Top - origin.Y) > DetachDistance)
            {
                docked[slot] = false;
                Arrange();
            }
        }

        // The user let go of a panel: dock it when it is near its place; a resized docked column window rescales the
        // column.
        private void OnPanelReleased(PanelSlot slot, bool resized)
        {
            if (!panels.TryGetValue(slot, out var p))
            {
                return;
            }
            if (resized)
            {
                if (IsDocked(slot))
                {
                    if (slot == PanelSlot.Map)
                    {
                        MapHeight = p.Height - PanelWindow.TitleHeight;
                    }
                    else
                    {
                        PanelScale = p.Width / ColumnSurfaceWidth;
                    }
                    Arrange();
                }
                return;
            }
            var (x, y) = DockedPosition(slot);
            docked[slot] = Math.Abs(p.Left - x) < SnapDistance && Math.Abs(p.Top - y) < SnapDistance;
            Arrange();
        }

        private (double X, double Y) DockedPosition(PanelSlot slot)
        {
            if (slot == PanelSlot.Map)
            {
                return (main.Left, main.Top + main.ActualHeight);
            }
            double y = main.Top;
            foreach (var s in Column)
            {
                if (s == slot)
                {
                    break;
                }
                if (panels.TryGetValue(s, out var p) && p.IsVisible && IsDocked(s))
                {
                    y += p.Height;
                }
            }
            return (main.Left - ColumnWidth, y);
        }

        public PanelPlacement Placement(PanelSlot slot)
        {
            var p = panels[slot];
            return new PanelPlacement(IsDocked(slot), p.Left, p.Top, p.Width, p.Height);
        }

        public void Restore(PanelSlot slot, PanelPlacement? placement)
        {
            if (placement == null || placement.Docked || !panels.TryGetValue(slot, out var p))
            {
                return;
            }
            docked[slot] = false;
            suppress++;
            p.Left = placement.Left;
            p.Top = placement.Top;
            p.Width = Math.Max(p.MinWidth, placement.Width);
            p.Height = Math.Max(p.MinHeight, placement.Height);
            suppress--;
        }
    }
}
