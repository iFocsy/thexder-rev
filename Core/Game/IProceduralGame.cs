namespace Thexder.Core.Game
{
    /// <summary>What comes after a finished level of a generated game.</summary>
    internal enum ProcStep
    {
        /// <summary>The next level of the same world.</summary>
        NextLevel,

        /// <summary>A new world: its briefing movie, then its first level.</summary>
        NextWorld,

        /// <summary>The campaign is complete, or the single map is done.</summary>
        Finished,
    }

    /// <summary>A game whose levels are generated (a procedural campaign or a single random map).</summary>
    internal interface IProceduralGame
    {
        /// <summary>The name the game's snapshot files carry ("arc", "map").</summary>
        string SavePrefix { get; }

        int World { get; }

        int Level { get; }

        int Stage { get; }

        int Difficulty { get; }

        /// <summary>The game's first level has not been played yet (a new game, not a continued one).</summary>
        bool IsNew { get; }

        bool PlaysBriefings { get; }

        /// <summary>Builds the current level.</summary>
        LevelData BuildLevel();

        /// <summary>The current level was finished with this score: moves on and says what comes next.</summary>
        ProcStep Finish(uint score);

        /// <summary>Back to the first level of the current world (the player died with no energy cell left).</summary>
        void RestartWorld();

        /// <summary>
        /// Writes the game's progress (called at every level start and when the player's stats are recorded).
        /// </summary>
        void Save(ThexderGame game);
    }

    internal enum GameModeKind
    {
        /// <summary>Back to the player-name screen.</summary>
        Cancel,

        /// <summary>The classic campaign (Game Select).</summary>
        Classic,

        /// <summary>A generated game (<see cref="GameModeChoice.Game"/>).</summary>
        Procedural,
    }

    /// <summary>The game mode picked after logging in.</summary>
    internal sealed record GameModeChoice(GameModeKind Kind, IProceduralGame? Game = null, bool Resume = false)
    {
        public static readonly GameModeChoice Classic = new(GameModeKind.Classic);

        public static readonly GameModeChoice Cancel = new(GameModeKind.Cancel);
    }
}
