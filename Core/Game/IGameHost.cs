namespace Thexder.Core.Game
{
    internal interface IGameHost
    {
        /// <summary>
        /// Starts a movie (e.g. "2w1.avi"); the host calls <see cref="ThexderGame.Advance"/> when it ends or is skipped.
        /// </summary>
        void PlayMovie(string name);

        void StopMovie();

        /// <summary>(Re)starts the music of a level from the beginning; world 0 or > 10 picks a random track.</summary>
        void StartLevelMusic(int world, int level);

        void StopMusic();

        /// <summary>Shows or removes the score screen's Replay / Continue buttons.</summary>
        void ShowScoreButtons(bool show);

        /// <summary>The "Encoded Features" dialog (T + X); returns the codeword or null.</summary>
        string? AskCodeword();

        /// <summary>A yes/no question in a message box.</summary>
        bool AskYesNo(string text, string caption);

        void ShowMessage(string text, string caption);

        /// <summary>The Game Select dialog: returns the difficulty 0..4, or null for Cancel.</summary>
        int? ChooseGame(IReadOnlyList<(int World, int Level)> progress, int lastDifficulty);

        /// <summary>The Difficulty dialog; returns 0..4.</summary>
        int ChooseDifficulty(int current);

        /// <summary>The Hall of Fame dialog, opened on a level.</summary>
        void ShowHallOfFame(int world, int level);

        /// <summary>Quits the program (WM_CLOSE without the confirmation).</summary>
        void Quit();

        /// <summary>After logging in: the classic campaign, a generated game, or back to the name screen.</summary>
        GameModeChoice ChooseGameMode() => GameModeChoice.Classic;

        /// <summary>
        /// A generated game is over (campaign complete or single map done); the host shows the result and calls <see
        /// cref="ThexderGame.ChooseMode"/> to go on.
        /// </summary>
        void ProceduralFinished()
        {
        }
    }
}
