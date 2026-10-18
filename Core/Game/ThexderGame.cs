using Thexder.Core.Assets;
using Thexder.Core.Audio;
using Thexder.Core.Engine;

namespace Thexder.Core.Game
{
    internal sealed partial class ThexderGame
    {
        /// <summary>Map size in cells: 320 x 68 for the classic levels, larger for generated ones.</summary>
        public int MapW => Map.Width;

        public int MapH => Map.Height;
        public const int ViewHeightCells = 34;
        public const int HudHeight = 64;
        public const int PlayerMark = 0x7f;

        // Player box per form, speed, damage divisor, first world where the form is available, default weapon.
        public static readonly int[] FormW = { 4, 5, 4 };
        public static readonly int[] FormH = { 6, 5, 3 };
        public static readonly int[] FormSpeed = { 2, 2, 1 };
        public static readonly int[] FormDamageDivisor = { 1, 1, 3 };
        public static readonly int[] FormDefaultWeapon = { 1, 1, 6 };
        public const int ShotArmorDivisor = 3;

        private readonly IGameHost host;
        private readonly string dataDirectory;

        public ThexderGame(GameArt art, SoundBoard sounds, GameRandom random, IGameHost host, string dataDirectory)
        {
            Art = art;
            Sounds = sounds;
            Random = random;
            this.host = host;
            this.dataDirectory = dataDirectory;
            Items = ItemTable.Build();
            for (int i = 0; i < Enemies.Length; i++)
            {
                Enemies[i] = new EnemyRecord();
            }
            for (int i = 0; i < Platforms.Length; i++)
            {
                Platforms[i] = new EnemyRecord();
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                Objects[i] = new LevelObject();
            }
            for (int i = 0; i < Shots.Length; i++)
            {
                Shots[i] = new ShotRecord();
                Shots[i].Reset();
            }
            for (int i = 0; i < Timed.Length; i++)
            {
                Timed[i] = new TimedObject();
            }
            ApplyArt(art);
            InitialPlayerState();
        }

        public GameArt Art { get; private set; }

        public SoundBoard Sounds { get; }

        public GameRandom Random { get; }

        public GameInput Input { get; } = new();

        /// <summary>Wall clock in milliseconds (GetTickCount); replaceable for tests and recordings.</summary>
        public Func<long> Clock { get; set; } = () => Environment.TickCount64;

        // ---- Level state (saved in level and save files) -------------------------------------------------

        public LevelHeader Header { get; } = new();

        public CellMap Map { get; } = new();

        public PlayerState Player { get; } = new();

        public Inventory Inv { get; } = new();

        public EnemyRecord[] Enemies { get; } = new EnemyRecord[100];

        public EnemyRecord[] Platforms { get; } = new EnemyRecord[30];

        public LevelObject[] Objects { get; } = new LevelObject[LevelObject.Count];

        public ShotRecord[] Shots { get; } = new ShotRecord[ShotRecord.Count];

        public TimedObject[] Timed { get; } = new TimedObject[TimedObject.Count];

        public ItemDef[] Items { get; }

        public ShotType[] ShotTypes { get; private set; } = Array.Empty<ShotType>();

        // ---- Flow globals --------------------------------------------------------------------------------

        public int World { get; set; } = 1;

        public int Level { get; set; } = 1;

        public int Difficulty { get; set; }

        public int FrameCounter { get; internal set; }

        public bool SessionActive { get; set; }

        public bool RealGame { get; set; }

        public bool LevelGfxReady { get; set; }

        public int PauseCount { get; set; }

        /// <summary>Options > Performance: scrolling background or fixed (2).</summary>
        public bool ScrollingBackground { get; set; } = true;

        /// <summary>F3 / sol-howfast.</summary>
        public bool ShowFps { get; set; }

        public int ViewWidthCells { get; private set; } = 60;

        /// <summary>The main surface: playfield (34 cells) plus the 64-pixel HUD strip.</summary>
        public FrameBuffer Screen { get; private set; } = new(1, 1);

        public FrameBuffer MapSurface { get; private set; } = new(CellMap.ClassicWidth, CellMap.ClassicHeight);

        /// <summary>Gives the map (and the planet map surface) a new size; the cells are cleared when it changes.</summary>
        public void SetMapSize(int width, int height)
        {
            Map.Resize(width, height);
            if (MapSurface.Width != MapW || MapSurface.Height != MapH)
            {
                MapSurface = new FrameBuffer(MapW, MapH);
            }
        }

        /// <summary>
        /// The part of <see cref="MapSurface"/> the Map of Planet window shows: all of it for the classic size, a 320
        /// x 68 window around the player on larger maps.
        /// </summary>
        public Rect MapViewRect
        {
            get
            {
                int w = Math.Min(MapW, CellMap.ClassicWidth), h = Math.Min(MapH, CellMap.ClassicHeight);
                int x = Math.Clamp(Player.X + 2 - w / 2, 0, MapW - w), y = Math.Clamp(Player.Y + 3 - h / 2, 0, MapH - h);
                return new Rect(x, y, w, h);
            }
        }

        /// <summary>Screen shake offset for the frame just presented.</summary>
        public (int X, int Y) ShakeOffset { get; private set; }

        public int S => Art.S;

        public int ViewWidthPx => ViewWidthCells * S;

        public int ViewHeightPx => ViewHeightCells * S;

        /// <summary>Raised after a frame has been drawn into <see cref="Screen"/> (Present).</summary>
        public event Action? FramePresented;

        // ---- Camera: a pure function of the player position ----------------------------------------------

        public int CamX
        {
            get
            {
                int x = LevelGfxReady ? Player.X - (ViewWidthCells - 4) / 2 : Player.X;
                return Math.Clamp(x, 0, MapW - ViewWidthCells);
            }
        }

        public int CamY
        {
            get
            {
                int y = LevelGfxReady ? Player.Y - (ViewHeightCells - 6) / 2 : Player.Y;
                return Math.Clamp(y, 0, MapH - ViewHeightCells);
            }
        }

        public bool OnScreen(int x, int y, int w, int h)
        {
            int cx = CamX, cy = CamY;
            return x + w > cx && x < cx + ViewWidthCells && y + h > cy && y < cy + ViewHeightCells;
        }

        // ---- Art / view ----------------------------------------------------------------------------------

        /// <summary>Changes resolution: rebuilds every table that holds pixel sizes (LoadResources).</summary>
        public void ApplyArt(GameArt art)
        {
            Art = art;
            ShotTypes = Game.ShotTypes.Build(art.S);
            Palette.CopyFrom(art.BasePalette);
            art.SetLevelGraphics(Header.Background, Header.TileSheet);
            ReloadEnemyTypes();
            ResizeView(ViewWidthCells);
        }

        /// <summary>Options > Playfield Width: 40 or 60 cells.</summary>
        public void ResizeView(int widthCells)
        {
            ViewWidthCells = widthCells == 40 ? 40 : 60;
            Screen = new FrameBuffer(ViewWidthCells * S, ViewHeightCells * S + HudHeight);
            HudFullRedraw = true;
        }

        /// <summary>The live palette (with colour cycling applied by the host).</summary>
        public Palette Palette { get; } = new();

        // ---- One tick ------------------------------------------------------------------------------------

        /// <summary>One logic step and one drawn frame.</summary>
        public void GameFrame()
        {
            if (PauseCount >= 1)
            {
                return;
            }
            IdleTaunt();
            FrameCounter++;
            if (!LevelGfxReady)
            {
                return;
            }

            if (Player.PlayState == 0)
            {
                Player.LevelFrames++;
                ReadInput();
                if (!skipRestOfFrame)
                {
                    MovePlayer(Player.X, Player.Y, Player.CmdDir, Player.Speed, FormW[Player.Form], FormH[Player.Form], isPlayer: true, checkEnemy: true, raw: false);
                    if (!NetActive)
                    {
                        UpdatePlatforms(); // Platforms do not move in network play
                    }
                    FloorEffects();
                    DrawTiles();
                    UpdateEnemyReserve();
                    EnemyThinkAll();
                    ObjectsFall();
                    PlayerAnimate();
                    PlayerFire();
                    PlayerPickups();
                    NetTick();
                    PlayerHazards();
                    DrawObjects();
                    EnemyAnimateDrawAll();
                    PlayerDraw();
                    if (Player.PlayState != 4)
                    {
                        if (NetActive)
                        {
                            DrawRemotePlayers();
                        }
                        InventoryTimersAndBombs();
                        UpdateShots();
                        DrawForeground();
                        if (ShowFps)
                        {
                            DrawFps();
                        }
                        Present();
                        EraseLasers();
                    }
                }
                keyboardAllowed = true;
                Player.MoveEnable = 0;
            }
            else
            {
                DrawStateBanner();
            }
        }

        // ---- Level loading -------------------------------------------------------------------------------

        /// <summary>
        /// <paramref name="fullReset"/>: take the whole player record from the file and clear the inventory (new game);
        /// otherwise only form, facing and last direction are taken.
        /// </summary>
        public void LoadLevel(string fileName, bool fullReset)
        {
            CurrentLevelFile = fileName;
            LevelGenerated = false;
            EnemyReserve.Clear();
            var file = LevelFile.Load(Path.IsPathRooted(fileName) ? fileName : Path.Combine(dataDirectory, fileName));
            Header.Load(file.Header);
            SetMapSize(CellMap.ClassicWidth, CellMap.ClassicHeight);
            Map.Load(file.Cells);
            for (int i = 0; i < Enemies.Length; i++)
            {
                Enemies[i].Read(file.Enemies.AsSpan(i * EnemyRecord.Size, EnemyRecord.Size));
            }

            var record = new PlayerState();
            record.Read(file.Thexder);
            if (fullReset)
            {
                Player.Read(file.Thexder);
                Inv.Clear();
            }
            else
            {
                int oldForm = Player.Form;
                Player.Facing = record.Facing;
                Player.Form = record.Form;
                Player.LastDir = record.LastDir;
                if (Player.Form != oldForm)
                {
                    SetFormWeapons(Player.Form);
                }
            }

            for (int i = 0; i < Platforms.Length; i++)
            {
                Platforms[i].Read(file.Platforms.AsSpan(i * EnemyRecord.Size, EnemyRecord.Size));
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                Objects[i].Read(file.Objects.AsSpan(i * LevelObject.Size, LevelObject.Size));
            }
            objectsMissing = file.Objects.All(b => b == 0);

            PostLoad(fresh: true);
            if (LevelGfxReady && Header.SavedFlag == 0)
            {
                ApplyDifficulty();
            }
            host.StopMusic();
            host.StartLevelMusic(World, Level);
        }

        /// <summary>Derive cell flags, objects, graphics and per-level runtime state.</summary>
        private void PostLoad(bool fresh)
        {
            sceneGeneration++; // Smooth motion: no gliding across a level (re)start
            if (Header.Flipped is not (0 or 1))
            {
                Header.Flipped = 0;
            }
            for (int i = 0; i < Map.Count; i++)
            {
                Map.SetB2(i, (byte)(Map.B2(i) & 0x80));
                if (Header.SavedFlag == 0)
                {
                    Map.SetB1(i, (byte)(Map.B1(i) & 0x7f));
                }
                if (Map.Tile(i) == 0)
                {
                    Map.SetB2(i, (byte)(Map.B2(i) & 0x7f));
                    Map.SetB3(i, (byte)(Map.B3(i) & 0x3f));
                }
            }
            MapSurface.Clear();

            if (!fresh || !objectsMissing)
            {
                foreach (var o in Objects)
                {
                    if (o.Kind == LevelObject.KindPickup)
                    {
                        int idx = Map.Index(o.X, o.Y);
                        Map.SetB3(idx, (byte)((Map.B3(idx) & 0xc0) | (o.Type & 0x3f)));
                        Map.SetB1(idx, (byte)((Map.B1(idx) & 0x80) | (o.Amount & 0x7f)));
                    }
                }
            }
            else
            {
                // A level without an object block: rebuild the table from the cells (row-major, max 100).
                int n = 0;
                foreach (var o in Objects)
                {
                    o.Kind = 0;
                }
                for (int i = 0; i < Map.Count && n < Objects.Length; i++)
                {
                    if ((Map.B3(i) & 0x3f) != 0)
                    {
                        var o = Objects[n++];
                        o.Kind = LevelObject.KindPickup;
                        o.X = i % MapW;
                        o.Y = i / MapW;
                        o.Type = Map.B3(i) & 0x3f;
                        o.Amount = Map.B1(i) & 0x7f;
                    }
                }
            }

            Art.SetLevelGraphics(Header.Background, Header.TileSheet);
            if (fresh)
            {
                ClearShots();
                foreach (var t in Timed)
                {
                    t.Clear();
                }
                if (Header.StartX == 0)
                {
                    Player.X = 0x11;
                    Player.Y = 0x0e;
                }
                else
                {
                    Player.X = Header.StartX;
                    Player.Y = Header.StartY;
                }
                Player.StateFlags = 0;
            }
            Player.PhysicsOn = LevelGfxReady ? 1 : 0;
            Player.Speed = FormSpeed[Player.Form];
            Player.SpriteStride = 0;
            Player.PlayState = 0;
            Player.FramePointer = 0;
            if (Player.MaxEnergy == 0 || Player.MaxEnergy > 220)
            {
                Player.MaxEnergy = 100;
            }
            foreach (var p in Platforms)
            {
                p.AnimTick = 0;
            }
            EnemyLevelSetup(fresh);
            WeaponCursor = Math.Max(0, Player.Weapon - 1);
            AltWeaponCursor = Math.Max(0, Player.AltWeapon - 1);
            HudFullRedraw = true;
        }

        /// <summary>Enemies and pickups the level removes at this difficulty, par score.</summary>
        private void ApplyDifficulty()
        {
            Header.BossCount = 0;
            Header.Par = 0;
            Header.ExitOpen = 1;
            HudFullRedraw = true;
            if (Difficulty > 4)
            {
                Difficulty = 0;
            }
            BossShown = 0;
            Header.PointsPerHit = (Difficulty + 1) * 50;
            int mask = 1 << Difficulty;
            int par = 0;
            for (int i = 1; i < 100; i++)
            {
                var t = Enemies[i];
                if (t.State == 0)
                {
                    continue;
                }
                if ((mask & t.DiffMask) != 0)
                {
                    t.State = 0;
                    continue;
                }
                if (Difficulty == 0)
                {
                    int q = t.Hp / 3;
                    t.Hp = q < 2 ? 1 : q;
                }
                if (t.Type != 1 && t.Type != 6)
                {
                    par += t.Hp * Header.PointsPerHit;
                }
                if (TypeOf(t)?.IsBoss == true)
                {
                    Header.BossCount++;
                    Header.ExitOpen = 0;
                    BossShown = i;
                    if (World == 10 && Level == 5)
                    {
                        if (t.Type == 0x29)
                        {
                            PrimusFlyerSlot = i;
                            t.State = 0;
                        }
                        if (t.Type == 0x2a)
                        {
                            PrimusWalkerSlot = i;
                            t.State = 0;
                        }
                    }
                }
            }
            if (EnemyReserve.Count > 0)
            {
                ApplyDifficultyToReserve(mask, ref par);
            }
            Header.Par = par * 3 / 4;
            foreach (var o in Objects)
            {
                if (o.Kind != 0 && (mask & o.DiffMask) != 0)
                {
                    o.Kind = 0;
                    int idx = Map.Index(o.X, o.Y);
                    Map.SetB3(idx, (byte)(Map.B3(idx) & 0xc0));
                }
            }
        }

        /// <summary>WM_CREATE player defaults.</summary>
        private void InitialPlayerState()
        {
            Span<byte> zero = stackalloc byte[PlayerState.RecordSize];
            Player.Read(zero);
            Player.Weapon = 1;
            Player.AltWeapon = 1;
            Player.Facing = 2;
            Player.Form = 0;
            Player.PlaneDir = 2;
            Player.Energy = 100;
            Player.MaxEnergy = 100;
            Inv.Clear();
            Inv.Count[ItemType.Laser] = 100;
            foreach (var t in Timed)
            {
                t.Clear();
            }
        }

        // ---- Misc globals used across subsystems ---------------------------------------------------------

        public string CurrentLevelFile { get; set; } = "";

        public int BossShown { get; set; }

        public int PrimusFlyerSlot { get; set; }

        public int PrimusWalkerSlot { get; set; }

        public bool HudFullRedraw { get; set; } = true;

        public int WeaponCursor { get; set; }

        public int AltWeaponCursor { get; set; }

        public int WeaponListFirst { get; set; }

        public int EquipmentCursor { get; set; }

        public int EquipmentListFirst { get; set; }

        public int ShakeRequest { get; set; }

        private bool objectsMissing;

        private bool skipRestOfFrame;

        private bool keyboardAllowed = true;

        public string DataDirectory => dataDirectory;

        public IGameHost Host => host;
    }
}
