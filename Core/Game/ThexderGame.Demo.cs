using System.Buffers.Binary;

namespace Thexder.Core.Game
{
    // Recorded demonstrations THEX01..10.TSC: a game snapshot, the random seed and one input byte per frame (Windows
    // virtual-key codes).
    internal sealed partial class ThexderGame
    {
        /// <summary>Size of the game-state "swap" image that starts every TSC file.</summary>
        public const int SwapSize = 0x185ba;

        private const int ScriptSize = 0x1194; // The script buffer read after the swap image
        private const int ScriptLimit = 0x1193; // Playback stops at this position or at an 0xff byte
        private static readonly byte[] SwapSignature = "SWAPGAME1\0"u8.ToArray();

        // Background and tile sheet for demo n, entries for n = 1..9, then the rest.
        private static readonly (int Background, int TileSheet)[] DemoGraphics =
        {
            (0x16, 3), (2, 1), (7, 4), (0x0c, 2), (0x1b, 0), (0x25, 7), (0x20, 6), (0x11, 5), (0x2a, 8), (0x2f, 9),
        };

        private byte[]? demoSwap;
        private byte[]? demoScript;
        private int demoPosition;

        /// <summary>A demo script drives the input.</summary>
        public bool DemoMode => demoScript != null;

        /// <summary>The next script byte (diagnostics).</summary>
        public int DemoPosition => demoPosition;

        /// <summary>Raised from inside a game frame when the script has ended and the saved state is back.</summary>
        public event Action? DemoFinished;

        /// <summary>Started with "/X"; only the demo loop runs.</summary>
        public bool KioskMode { get; private set; }

        /// <summary>Kiosk start-up: no session, PlayState 0x0f, 250 points per hit, world 3.</summary>
        public void KioskInit()
        {
            KioskMode = true;
            IntroPending = false;
            Player.PlayState = 0x0f;
            Player.Speed = 2;
            Header.PointsPerHit = 250;
            World = 3;
        }

        /// <summary>One step of the demo thread: StartPlayback of THEX&lt;n&gt;.TSC, then SetWorldGraphics(n).</summary>
        public bool StartDemo(int number)
        {
            if (!StartPlayback(Path.Combine(dataDirectory, $"THEX{number:00}.TSC")))
            {
                return false;
            }
            SetWorldGraphics(number);
            return true;
        }

        /// <summary>Saves the current state, loads the file's and starts its script.</summary>
        public bool StartPlayback(string path)
        {
            if (demoScript != null)
            {
                return true;
            }
            if (!File.Exists(path))
            {
                return false;
            }
            // Both buffers are zero-filled (GMEM_ZEROINIT), so a short file reads as zeros.
            byte[] swap = new byte[SwapSize];
            byte[] script = new byte[ScriptSize + 4]; // Padding: the transform look-ahead may pass the end
            using (var file = File.OpenRead(path))
            {
                file.ReadAtLeast(swap, SwapSize, throwOnEndOfStream: false);
                file.ReadAtLeast(script, ScriptSize, throwOnEndOfStream: false);
            }
            if (!swap.AsSpan(0, SwapSignature.Length).SequenceEqual(SwapSignature))
            {
                return false; // "Invalid or old playback script"
            }
            demoSwap = new byte[SwapSize];
            WriteSwap(demoSwap);
            ReadSwap(swap);
            SessionActive = true;
            LevelGfxReady = true;
            EnemyLevelSetup(true);
            RebuildMapFromSeen();
            // So the recorded demos play out a little differently every time (now and then the robot dies); the game's
            // generator is deliberately left alone to keep that behaviour.
            demoScript = script;
            demoPosition = 0;
            return true;
        }

        /// <summary>Ends playback and puts back the state saved at its start.</summary>
        public void StopDemo()
        {
            if (demoScript == null || demoSwap == null)
            {
                return;
            }
            ReadSwap(demoSwap);
            SessionActive = true;
            LevelGfxReady = true;
            EnemyLevelSetup(true);
            RebuildMapFromSeen();
            demoScript = null;
            demoSwap = null;
            demoPosition = 0;
        }

        /// <summary>SetWorldGraphics: World = n and that world's background and tile sheet.</summary>
        private void SetWorldGraphics(int world)
        {
            World = world;
            var (background, tileSheet) = DemoGraphics[world is >= 1 and <= 9 ? world - 1 : 9];
            Header.Background = background;
            Header.TileSheet = tileSheet;
            Art.SetLevelGraphics(background, tileSheet);
        }

        internal byte[] CaptureSwap()
        {
            var image = new byte[SwapSize];
            WriteSwap(image);
            return image;
        }

        /// <summary>The TSC snapshot layout; bytes 0..0x0d are left alone.</summary>
        private void WriteSwap(Span<byte> w)
        {
            EnemyRecord.WriteArray(Enemies, w.Slice(0x0e, Enemies.Length * EnemyRecord.Size));
            Player.Write(w.Slice(0x1396, PlayerState.RecordSize));
            Inv.Write(w.Slice(0x13d8, Inventory.ByteSize));
            for (int i = 0; i < Shots.Length; i++)
            {
                Shots[i].Write(w.Slice(0x14e4 + i * ShotRecord.Size, ShotRecord.Size));
            }
            for (int i = 0; i < Objects.Length; i++)
            {
                Objects[i].Write(w.Slice(0x206a + i * LevelObject.Size, LevelObject.Size));
            }
            EnemyRecord.WriteArray(Platforms, w.Slice(0x2772, Platforms.Length * EnemyRecord.Size));
            for (int i = 0; i < Timed.Length; i++)
            {
                Timed[i].Write(w.Slice(0x2d4e + i * TimedObject.Size, TimedObject.Size));
            }
            WriteSelection(w.Slice(0x319a, 0x20));
            Map.Data.CopyTo(w[0x31ba..]);
        }

        /// <summary>The u32 at 0x14e0 is only EnemySpawn's return scratch.</summary>
        private void ReadSwap(ReadOnlySpan<byte> r)
        {
            for (int i = 0; i < Enemies.Length; i++)
            {
                Enemies[i].Read(r.Slice(0x0e + i * EnemyRecord.Size, EnemyRecord.Size));
            }
            Player.Read(r.Slice(0x1396, PlayerState.RecordSize));
            Inv.Read(r.Slice(0x13d8, Inventory.ByteSize));
            for (int i = 0; i < Shots.Length; i++)
            {
                Shots[i].Read(r.Slice(0x14e4 + i * ShotRecord.Size, ShotRecord.Size));
            }
            SetMapSize(CellMap.ClassicWidth, CellMap.ClassicHeight);
            LevelGenerated = false;
            EnemyReserve.Clear();
            Map.Load(r.Slice(0x31ba, CellMap.ClassicByteSize));
            for (int i = 0; i < Objects.Length; i++)
            {
                Objects[i].Read(r.Slice(0x206a + i * LevelObject.Size, LevelObject.Size));
            }
            for (int i = 0; i < Platforms.Length; i++)
            {
                Platforms[i].Read(r.Slice(0x2772 + i * EnemyRecord.Size, EnemyRecord.Size));
            }
            for (int i = 0; i < Timed.Length; i++)
            {
                Timed[i].Read(r.Slice(0x2d4e + i * TimedObject.Size, TimedObject.Size));
            }
            ReadSelection(r.Slice(0x319a, 0x20));
        }

        private void WriteSelection(Span<byte> w)
        {
            w.Clear();
            BinaryPrimitives.WriteInt16LittleEndian(w, (short)EquipmentListFirst);
            BinaryPrimitives.WriteInt16LittleEndian(w[0x02..], (short)EquipmentCursor);
            BinaryPrimitives.WriteInt16LittleEndian(w[0x10..], (short)WeaponListFirst);
            BinaryPrimitives.WriteInt16LittleEndian(w[0x12..], (short)WeaponCursor);
            BinaryPrimitives.WriteInt16LittleEndian(w[0x14..], (short)AltWeaponCursor);
        }

        private void ReadSelection(ReadOnlySpan<byte> r)
        {
            EquipmentListFirst = BinaryPrimitives.ReadInt16LittleEndian(r);
            EquipmentCursor = BinaryPrimitives.ReadInt16LittleEndian(r[0x02..]);
            WeaponListFirst = BinaryPrimitives.ReadInt16LittleEndian(r[0x10..]);
            WeaponCursor = BinaryPrimitives.ReadInt16LittleEndian(r[0x12..]);
            AltWeaponCursor = BinaryPrimitives.ReadInt16LittleEndian(r[0x14..]);
        }

        /// <summary>This frame's command from the script; false when none is playing.</summary>
        private bool DemoInput()
        {
            if (demoScript == null)
            {
                return false;
            }
            if (demoPosition >= ScriptLimit || demoScript[demoPosition] == 0xff)
            {
                skipRestOfFrame = true;
                StopDemo();
                DemoFinished?.Invoke();
                return false;
            }
            var p = Player;
            bool horizontal = false;
            p.CmdDir = 0;
            byte code = demoScript[demoPosition];
            switch (code)
            {
                case 0x0c: // Clear (keypad 5)
                    RequestTransform(PlayerState.Tank);
                    break;
                case 0x21:
                case 0x22:
                case 0x23:
                case 0x24:
                case 0x25:
                case 0x27:
                    p.CmdDir = code switch { 0x21 => 6, 0x22 => 10, 0x23 => 9, 0x24 => 5, 0x25 => 1, _ => 2 };
                    horizontal = true;
                    p.MoveEnable = 1;
                    break;
                case 0x26:
                    p.MoveEnable = 1;
                    p.CmdDir = 4;
                    break;
                case 0x28:
                    p.MoveEnable = 1;
                    p.CmdDir = 8;
                    break;
                case 0x2d:
                    RequestTransform(PlayerState.Plane);
                    break;
                case 0x2e:
                    RequestTransform(PlayerState.Walker);
                    break;
                case 0x41: // A: previous weapon this form can use
                    for (int s = WeaponCursor - 1; WeaponCursor != 0 && s >= 0; s--)
                    {
                        if (Usable(s + 1, p.Form))
                        {
                            p.Weapon = s + 1;
                            SelectSlot(p.Weapon);
                            break;
                        }
                    }
                    break;
                case 0x53: // S: next weapon this form can use
                    for (int s = WeaponCursor + 1; s < 8; s++)
                    {
                        if (Usable(s + 1, p.Form))
                        {
                            p.Weapon = s + 1;
                            SelectSlot(p.Weapon);
                            break;
                        }
                    }
                    break;
            }
            demoPosition++;

            // Transform keys recorded on the same frame follow the direction byte.
            if (demoScript[demoPosition] == 0x2d)
            {
                RequestTransform(PlayerState.Plane);
                demoPosition++;
            }
            if (demoScript[demoPosition] == 0x0c)
            {
                RequestTransform(PlayerState.Tank);
                demoPosition++;
            }
            if (demoScript[demoPosition] == 0x2e)
            {
                RequestTransform(PlayerState.Walker);
                demoPosition++;
            }
            if (horizontal)
            {
                if ((p.CmdDir & 3) != p.Facing)
                {
                    p.MoveEnable = 0;
                }
                p.Facing = (p.CmdDir & 2) != 0 ? 2 : 1;
            }
            return true;
        }

        /// <summary>A space byte in the script fires on this frame.</summary>
        private bool DemoFire()
        {
            if (demoScript != null && demoScript[demoPosition] == 0x20)
            {
                demoPosition++;
                return true;
            }
            return false;
        }
    }
}
