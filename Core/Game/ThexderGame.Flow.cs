using Serilog;

namespace Thexder.Core.Game
{
    // The play-state machine around the levels.
    internal sealed partial class ThexderGame
    {
        private int pendingTransition;
        private bool scoreComputed; // The score screen's bonus is applied once
        private uint scoreBasic;
        private int scoreBonus;
        private bool scorePenalty;

        public bool MoviePlaying { get; private set; }

        public bool IntroPending { get; set; } = true;

        /// <summary>The score screen is showing its Replay / Continue buttons.</summary>
        public bool ScoreButtonsShown { get; private set; }

        /// <summary>Raised when the title screen (player name entry) should be shown.</summary>
        public event Action? TitleScreenRequested;

        /// <summary>Start-up (end of Init): the intro movie once, then the title screen.</summary>
        public void Start()
        {
            // Init shows the title over tile sheet 607 and background 537 until a level is loaded.
            Header.TileSheet = 7;
            Header.Background = 0x25;
            Art.SetLevelGraphics(Header.Background, Header.TileSheet);
            PauseCount++;
            ShowIntroOrTitle();
        }

        /// <summary>Command 0x9dd: the 2W0 intro the first time, the title screen afterwards.</summary>
        private void ShowIntroOrTitle()
        {
            if (IntroPending)
            {
                IntroPending = false;
                PlayMovie("2w0.avi");
                return;
            }
            TitleScreen();
        }

        /// <summary>The player-name screen.</summary>
        public void TitleScreen()
        {
            SessionActive = false;
            DrawTitleScreen();
            TitleScreenRequested?.Invoke();
        }

        /// <summary>Login: the name was entered.</summary>
        public void Login(string name)
        {
            var clean = new string(name.Take(51).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : ' ').ToArray()).Trim();
            if (clean.Length == 0)
            {
                return;
            }
            PlayerName = clean;
            SessionActive = true;
            DrawBlack();
            LoadThg();
            LoadThw();
            ChooseMode();
        }

        /// <summary>Pick a difficulty slot, resume it or start at W01L01.</summary>
        private bool GameSelect()
        {
            EndProcedural();
            int? choice = host.ChooseGame(Progress, LastDifficulty);
            if (choice == null)
            {
                RealGame = false;
                TitleScreen();
                return false;
            }
            Difficulty = choice.Value;
            LastDifficulty = Difficulty;
            if (!LoadSnapshot(continueSlot: true))
            {
                World = 1;
                Level = 1;
                LevelGfxReady = true;
                LoadLevel("W01L01.THL", fullReset: true);
                Player.Weapon = 1;
                Player.Energy = 100;
                Player.MaxEnergy = 100;
                Inv.Count[ItemType.Laser] = 100;
                SaveWorldStart();
                SaveSnapshot(continueSlot: false);
            }
            LevelGfxReady = true;
            RealGame = true;
            WeaponCursor = Math.Max(0, Player.Weapon - 1);
            AltWeaponCursor = Math.Max(0, Player.AltWeapon - 1);
            SaveThg();
            return true;
        }

        /// <summary>After a network game (NetDisconnectCleanup): the single-player Game Select.</summary>
        public void NetContinueSinglePlayer()
        {
            PauseCount++;
            if (GameSelect())
            {
                PauseCount--;
            }
        }

        /// <summary>Advance: mouse click, Enter, or the end of a movie.</summary>
        public void Advance()
        {
            Touch();
            if (KioskMode)
            {
                return;
            }
            if (!SessionActive)
            {
                if (MoviePlaying)
                {
                    StopMovie();
                    TitleScreen();
                }
                return;
            }
            if (Player.PlayState == 0)
            {
                return;
            }
            if (MoviePlaying)
            {
                StopMovie();
                if (Player.PlayState == 5)
                {
                    Player.PlayState = 0;
                    return;
                }
                if (Player.PlayState == 4)
                {
                    Player.PlayState = 2;
                    return;
                }
                LevelTransition(revert: false);
                return;
            }
            if (Player.PlayState == 1 && NetActive)
            {
                // Network play: back in the game at a random pad.
                deathBannerFrames = 0;
                Player.Energy = 100;
                NetRespawn(instant: false);
                return;
            }
            if (Player.PlayState == 1)
            {
                deathBannerFrames = 0;
                pendingTransition = 1;
                LevelTransition(revert: false);
                return;
            }
            if (Player.PlayState == 2)
            {
                pendingTransition = 2;
                ScoreScreen();
            }
        }

        // ---- Score screen --------------------------------------------------------------------------------

        /// <summary>ScoreScreen: Basic Score, Time Bonus or Penalty, Total and Par.</summary>
        public void ScoreScreen()
        {
            Player.PlayState = 3;
            if (!scoreComputed)
            {
                scoreBasic = Player.Score;
                uint frames = Player.LevelFrames;
                if (frames <= 4000)
                {
                    scorePenalty = false;
                    scoreBonus = (int)((4000 - frames) / 10);
                    Player.Score += (uint)scoreBonus;
                }
                else
                {
                    scorePenalty = true;
                    int penalty = (int)((frames - 4000) / 10);
                    if ((int)Player.Score < penalty)
                    {
                        penalty = (int)Player.Score;
                    }
                    scoreBonus = penalty;
                    Player.Score -= (uint)penalty;
                }
                scoreComputed = true;
            }
            DrawScoreScreen(scoreBasic, scoreBonus, scorePenalty);
            ScoreButtonsShown = true;
            host.ShowScoreButtons(true);
        }

        /// <summary>Redraws the score screen (window resized while it shows).</summary>
        public void RedrawScoreScreen()
        {
            if (Player.PlayState == 3 && scoreComputed)
            {
                DrawScoreScreen(scoreBasic, scoreBonus, scorePenalty);
            }
        }

        /// <summary>The Replay button: the level again from its start, the score is discarded.</summary>
        public void ScoreReplay()
        {
            HideScoreButtons();
            pendingTransition = 0;
            Player.PlayState = 0;
            LevelTransition(revert: false);
        }

        /// <summary>The Continue button.</summary>
        public void ScoreContinue()
        {
            HideScoreButtons();
            if (Procedural != null)
            {
                var step = Procedural.Finish(Player.Score);
                if (step == ProcStep.Finished)
                {
                    host.ProceduralFinished();
                    return;
                }
                if (step == ProcStep.NextWorld && Procedural.PlaysBriefings)
                {
                    PlayMovie($"2w{Procedural.World}.avi");
                    return;
                }
                LevelTransition(revert: false);
                return;
            }
            if (RealGame)
            {
                SaveLevelScore();
                host.ShowHallOfFame(World, Level);
            }
            if (Level == 5)
            {
                if (World <= 9)
                {
                    PlayMovie($"2w{World + 1}.avi");
                    return;
                }
                EndOfGame();
                return;
            }
            LevelTransition(revert: false);
        }

        private void HideScoreButtons()
        {
            scoreComputed = false;
            ScoreButtonsShown = false;
            host.ShowScoreButtons(false);
        }

        /// <summary>After the last level.</summary>
        private void EndOfGame()
        {
            if (!host.AskYesNo("You have completed your mission.\nWould you like to try again at a different difficulty level?", "Thexder"))
            {
                host.Quit();
                return;
            }
            if (host.AskYesNo("Would you like to start a previous saved game?", "Thexder"))
            {
                PauseCount++;
                if (GameSelect())
                {
                    PauseCount--;
                }
                return;
            }
            Difficulty = host.ChooseDifficulty(Difficulty);
            World = 1;
            Level = 1;
            LoadLevel("W01L01.THL", fullReset: true);
            ResetLevelStart(full: true);
            Player.Energy = 100;
            Player.MaxEnergy = 100;
            Inv.Count[ItemType.Laser] = 100;
            SaveWorldStart();
            SaveSnapshot(continueSlot: false);
            Player.PlayState = 5;
            PlayMovie("2w1.avi");
        }

        // ---- Level transitions ---------------------------------------------------------------------------

        /// <summary>Next level, death retry, replay, revert.</summary>
        public void LevelTransition(bool revert)
        {
            bool loadFresh = true, resetEnergy = false, saveSnap = false;
            PauseCount++;
            if (!revert && RealGame)
            {
                if (pendingTransition == 2)
                {
                    saveSnap = true;
                    NextLevel();
                }
                else if (Player.PlayState == 0)
                {
                    if (LoadSnapshot(continueSlot: false))
                    {
                        loadFresh = false;
                    }
                    else
                    {
                        resetEnergy = true;
                    }
                }
                else if (Inv.Count[ItemType.EnergyCell] == 0)
                {
                    saveSnap = true; // Dead with no energy cell left: back to the start of the world
                    RestartWorld();
                }
                else if (!LoadSnapshot(continueSlot: false))
                {
                    resetEnergy = true;
                }
                else
                {
                    loadFresh = false;
                    if (Inv.Count[ItemType.EnergyCell] != 0)
                    {
                        Inv.Count[ItemType.EnergyCell]--; // A spare life is used
                    }
                    if (Player.Energy < 100)
                    {
                        Player.Energy = 100;
                    }
                    if (Inv.Count[ItemType.Laser] < 100)
                    {
                        Inv.Count[ItemType.Laser] = 100;
                    }
                    saveSnap = true;
                }
            }
            else if (revert)
            {
                RestartWorld();
            }
            int oldState = Player.PlayState;
            if (loadFresh)
            {
                LoadCurrentLevel();
                ResetLevelStart(full: true);
                if ((!RealGame && oldState != 0) || resetEnergy)
                {
                    Player.Energy = 100;
                    Player.MaxEnergy = 100;
                    Inv.Count[ItemType.Laser] = 100;
                }
            }
            else
            {
                ResetLevelStart(full: false);
            }
            if (RealGame && saveSnap)
            {
                SaveSnapshot(continueSlot: false);
                if (Level == 1)
                {
                    SaveWorldStart();
                }
            }
            pendingTransition = 0;
            Player.PlayState = 0;
            PauseCount--;
            MapSurface.Clear();
            for (int i = 0; i < Map.Count; i++)
            {
                Map.SetB1(i, (byte)(Map.B1(i) & 0x7f));
            }
        }

        /// <summary>The next level file that exists.</summary>
        private void NextLevel()
        {
            if (Procedural != null)
            {
                World = Procedural.World;
                Level = Procedural.Level;
                UpdateFormsForStage();
                return;
            }
            for (int guard = 0; guard < 60; guard++)
            {
                if (CurrentLevelFile.Length > 0)
                {
                    char c = char.ToUpperInvariant(Path.GetFileName(CurrentLevelFile)[0]);
                    if (c != 'W')
                    {
                        break; // A custom level file: stop
                    }
                    ParseLevelName(Path.GetFileName(CurrentLevelFile));
                }
                if (++Level > 5)
                {
                    Level = 1;
                    World++;
                }
                if (World > 10)
                {
                    World = 1;
                    Level = 1;
                }
                CurrentLevelFile = $"W{World:00}L{Level:00}.THL";
                if (File.Exists(Path.Combine(dataDirectory, CurrentLevelFile)))
                {
                    break;
                }
            }
        }

        /// <summary>ParseLevelName: "W03L02.THL" -> world 3, level 2; other names -> world 11.</summary>
        private void ParseLevelName(string name)
        {
            if (name.Length == 0)
            {
                World = 1;
                Level = 1;
                return;
            }
            if (char.ToUpperInvariant(name[0]) != 'W' || name.Length < 6 || !int.TryParse(name.AsSpan(1, 2), out int w) || !int.TryParse(name.AsSpan(4, 2), out int l))
            {
                World = 11;
                Level = 1;
                return;
            }
            World = w;
            Level = l;
        }

        /// <summary>Level 1 of this world with the stats recorded when it was entered.</summary>
        private void RestartWorld()
        {
            if (Procedural != null)
            {
                Procedural.RestartWorld();
                Level = Procedural.Level;
                LoadCurrentLevel();
                RestoreProcWorldStart();
                return;
            }
            Level = 1;
            CurrentLevelFile = $"W{World:00}L01.THL";
            LoadLevel(CurrentLevelFile, fullReset: false);
            int i = World * 5 + Difficulty - 5;
            if (i < 0 || i >= WorldStarts.Length)
            {
                return;
            }
            var ws = WorldStarts[i];
            Player.Weapon = ws.Weapon;
            Player.Energy = ws.Energy;
            Player.MaxEnergy = ws.MaxEnergy;
            Player.AltWeapon = ws.Weapon;
            Inv.CopyFrom(ws.Inventory);
        }

        private void ResetLevelStart(bool full)
        {
            if (full)
            {
                Player.Score = 0;
                Player.LevelFrames = 0;
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
                for (int i = 0; i < Map.Count; i++)
                {
                    Map.SetB1(i, (byte)(Map.B1(i) & 0x7f));
                }
                MapSurface.Clear();
            }
            HudFullRedraw = true;
        }

        // ---- Menu commands -------------------------------------------------------------------------------

        /// <summary>File > Replay (0x7534).</summary>
        public void MenuReplay()
        {
            LevelTransition(revert: false);
            if (!RealGame)
            {
                Player.Energy = 100;
                Player.MaxEnergy = 100;
                Inv.Count[ItemType.Laser] = 100;
            }
        }

        /// <summary>File > Revert (0x7532): restart a world that was played before, from its recorded start.</summary>
        public void MenuRevert(int world, int difficulty)
        {
            World = world;
            Difficulty = difficulty;
            RealGame = true;
            LevelTransition(revert: true);
        }

        /// <summary>File > Load (0x7531): plays one level file outside the campaign (no saving).</summary>
        public void MenuLoadLevel(string path, int difficulty)
        {
            EndProcedural();
            Difficulty = difficulty;
            ParseLevelName(Path.GetFileName(path));
            LevelGfxReady = true;
            LoadLevel(path, fullReset: false);
            ResetLevelStart(full: true);
            Player.Energy = 100;
            RealGame = false;
            Player.MaxEnergy = 100;
            Inv.Count[ItemType.Laser] = 100;
        }

        /// <summary>File > New Game (0x7535): back to the player-name screen.</summary>
        public void MenuNewGame()
        {
            if (RealGame && SessionActive && host.AskYesNo("Do you wish to save this game?", "Thexder"))
            {
                SaveSnapshot(continueSlot: true);
            }
            PauseCount++;
            RealGame = true;
            SessionActive = false;
            EndProcedural();
            for (int i = 0; i < Map.Count; i++)
            {
                Map.SetTile(i, 0);
            }
            ShowIntroOrTitle();
        }

        /// <summary>
        /// File > Game Mode: saves the game being played (if the player wants), then the Game Mode dialog.
        /// </summary>
        public void MenuChangeMode()
        {
            if (RealGame && SessionActive && Player.PlayState == 0 && host.AskYesNo("Do you wish to save this game?", "Thexder"))
            {
                SaveSnapshot(continueSlot: true);
            }
            PauseCount++;
            HideScoreButtons();
            ChooseMode();
        }

        /// <summary>Whether File > Exit is allowed now ("Cannot exit the game between levels.").</summary>
        public bool CanExit => KioskMode || Player.PlayState == 0 || !SessionActive;

        /// <summary>WM_CLOSE: the continue slot keeps the exact state of a campaign game.</summary>
        public void SaveOnExit()
        {
            if (RealGame && !KioskMode && SessionActive && Player.PlayState == 0)
            {
                SaveSnapshot(continueSlot: true);
            }
        }

        /// <summary>
        /// The host calls <see cref="Advance"/> when the movie ends or is skipped; a missing movie counts as finished at
        /// once.
        /// </summary>
        private void PlayMovie(string name)
        {
            MoviePlaying = true;
            if (!File.Exists(Path.Combine(dataDirectory, name)))
            {
                Log.Information("Movie {Name} is missing; skipped", name);
                Advance();
                return;
            }
            DrawBlack();
            Sounds.StopAll();
            host.PlayMovie(name);
        }

        private void StopMovie()
        {
            host.StopMovie();
            MoviePlaying = false;
        }

        // ---- Codewords and idle taunts -------------------------------------------------------------------

        /// <summary>Cheat: the "Encoded Features" codewords (case-sensitive).</summary>
        public bool ApplyCodeword(string word)
        {
            var p = Player;
            switch (word)
            {
                case "sol-pumpmeup":
                    p.Energy = Math.Min(p.Energy + 25, 220);
                    break;
                case "sol-moreammo":
                case "sol-muchoammo":
                {
                    int t = WeaponCursor + 1;
                    Inv.Owned[t] = 1;
                    Inv.Count[t] = Math.Min(Inv.Count[t] + (word == "sol-moreammo" ? 10 : 100), 999);
                    break;
                }
                case "sol-morestuff":
                {
                    int item = ItemType.EquipmentList[Math.Clamp(EquipmentCursor, 0, ItemType.EquipmentList.Length - 1)];
                    Inv.Owned[item] = 1;
                    Inv.Count[item] = item == ItemType.EnergyCell ? Math.Min(Inv.Count[item] + 1, 6) : Math.Min(Inv.Count[item] + 2, 999);
                    break;
                }
                case "sol-diehard":
                    Inv.Count[ItemType.EnergyCell] = Math.Min(Inv.Count[ItemType.EnergyCell] + 1, 6);
                    break;
                case "sol-switcheroo":
                    ActivatePlatforms();
                    break;
                case "sol-warp1":
                    p.Speed = 1;
                    break;
                case "sol-warp2":
                    p.Speed = 2;
                    break;
                case "sol-offwego":
                    FormMinWorld[PlayerState.Plane] = 1;
                    break;
                case "sol-tanksforthememories":
                    FormMinWorld[PlayerState.Tank] = 1;
                    break;
                case "k9c7m5":
                    p.PhysicsOn = p.PhysicsOn != 0 ? 0 : 1;
                    break;
                case "sol-howfast":
                    ShowFps = true;
                    break;
                case "sol-nofps":
                    ShowFps = false;
                    break;
                case "sol-instantexit":
                    p.PlayState = 2;
                    break;
                default:
                    return false;
            }
            HudFullRedraw = true;
            return true;
        }

        /// <summary>A minute without input makes the announcer nag.</summary>
        private void IdleTaunt()
        {
            long now = Clock();
            if (now - idleSince <= 60000)
            {
                return;
            }
            if (SessionActive && Player.PlayState == 0)
            {
                Sounds.Play((Random.Next() % 11) switch
                {
                    0 or 1 => 0x21,
                    2 => 0x1e,
                    3 => 0x1f,
                    4 => 0x96,
                    5 => 0x23,
                    6 => 0x24,
                    7 => 0x97,
                    8 => 0x26,
                    9 => 0x25,
                    _ => 0x1d,
                });
            }
            idleSince = now;
        }
    }
}
