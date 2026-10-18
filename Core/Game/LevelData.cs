namespace Thexder.Core.Game
{
    /// <summary>A level built in memory instead of read from a .THL file (procedural levels).</summary>
    internal sealed class LevelData
    {
        public LevelData(int width, int height)
        {
            Width = width;
            Height = height;
            Cells = new byte[width * height * 4];
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Row-major cells, 4 bytes each, laid out like <see cref="CellMap"/>.</summary>
        public byte[] Cells { get; }

        public int StartX { get; set; }

        public int StartY { get; set; }

        /// <summary>Top-left cell of the 6 x 6 exit.</summary>
        public int ExitX { get; set; }

        public int ExitY { get; set; }

        /// <summary>Tile sheet 600 + n and background 500 + n.</summary>
        public int TileSheet { get; set; }

        public int Background { get; set; }

        /// <summary>The player record (0x42 bytes) a new game starts with: form, facing and so on.</summary>
        public byte[] PlayerRecord { get; set; } = new byte[PlayerState.RecordSize];

        public List<EnemyRecord> Enemies { get; } = new();

        /// <summary>Moving platforms (at most 30).</summary>
        public List<EnemyRecord> Platforms { get; } = new();

        /// <summary>Object records (at most 100): teleporters and the pickups that can fall.</summary>
        public List<LevelObject> Objects { get; } = new();

        /// <summary>Shown in the Map of Planet title, e.g. "Terra Nova - Sector 3".</summary>
        public string Title { get; set; } = "";

        public int Index(int x, int y) => y * Width + x;
    }
}
