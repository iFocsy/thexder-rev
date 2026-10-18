using Thexder.Core.Assets;
using Thexder.Core.Engine;

namespace Thexder.Core.Game
{
    /// <summary>
    /// The art of one resolution (THEX640/800/1024.DLL): the game palette, the fixed UI bitmaps, sprites by name and the
    /// level's background and tile sheet.
    /// </summary>
    internal sealed class GameArt : IDisposable
    {
        private IndexedImage? background;
        private IndexedImage? tileSheet;
        private int backgroundId = -1;
        private int tileSheetId = -1;

        public GameArt(ResourceLibrary library)
        {
            Library = library;
            S = library.CellSize;

            BasePalette = library.Bitmap("PALETTE").Palette!.Clone();
            Text = new GlyphText(library.Bitmap("BIGFONT"), library.Bitmap("SMALLFON"), library.Bitmap("NUMBERS"));
            Tints = new TintTables(BasePalette);
        }

        public ResourceLibrary Library { get; }

        /// <summary>Cell size in pixels: 8, 10 or 12.</summary>
        public int S { get; }

        public Palette BasePalette { get; }

        public GlyphText Text { get; }

        public TintTables Tints { get; }

        public IndexedImage UiBitmap => Library.Bitmap("UIBITMAP");

        public IndexedImage UiBitmap2 => Library.Bitmap("UIBITMAP2");

        public IndexedImage Energy => Library.Bitmap("ENERGY");

        public IndexedImage BossEnergy => Library.Bitmap("BOSSENERGY");

        public IndexedImage UnavailableEnergyCell => Library.Bitmap("UNAVAILABLEENERGYCELL");

        public IndexedImage Exit => Library.Bitmap("EXIT");

        public IndexedImage Objects => Library.Bitmap("OBJ1");

        public IndexedImage ObjectsLowRes => Library.Bitmap("OBJLOWRES");

        public IndexedImage Background => background ?? throw new InvalidOperationException("No background loaded.");

        public IndexedImage TileSheet => tileSheet ?? throw new InvalidOperationException("No tile sheet loaded.");

        /// <summary>A sprite strip by resource name; null when the library has no such bitmap.</summary>
        public IndexedImage? Sprite(string? name) =>
            name != null && Library.HasBitmap(name) ? Library.Bitmap(name) : null;

        public void SetLevelGraphics(int backgroundNumber, int tileSheetNumber)
        {
            int bg = Library.HasBitmap(500 + backgroundNumber) ? 500 + backgroundNumber : 500;
            int ts = Library.HasBitmap(600 + tileSheetNumber) ? 600 + tileSheetNumber : 600;
            if (bg != backgroundId)
            {
                if (backgroundId >= 0)
                {
                    Library.Evict(backgroundId);
                }
                background = Library.Bitmap(bg);
                backgroundId = bg;
            }
            if (ts != tileSheetId)
            {
                tileSheet = Library.Bitmap(ts);
                tileSheetId = ts;
            }
        }

        public void Dispose() => Library.Dispose();
    }
}
