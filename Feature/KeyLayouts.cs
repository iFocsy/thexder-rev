using Thexder.Core.Game;

namespace Thexder.Feature
{
    /// <summary>Ready-made key layouts for Remap Keys (a key table index per action).</summary>
    internal static class KeyLayouts
    {
        /// <summary>The keys of the 1995 game: arrows and the keypad, Space fires.</summary>
        public static int[] OldSchool => (int[])ThexderGame.DefaultKeyBindings.Clone();

        /// <summary>WASD with Q E Z C diagonals and Space to fire; the right hand on J K L and U O for the arsenal.</summary>
        public static int[] Modern => Indices(0x20, 'R', '2', '3', '1', 'F', 'W', 'S', 'A', 'D', 'Q', 'E', 'Z', 'C', 'L', 'J', 'U', 'O', 'K');

        /// <summary>"Old School", "Modern", or null for a layout of the player's own.</summary>
        public static string? NameOf(int[] bindings) =>
            bindings.SequenceEqual(ThexderGame.DefaultKeyBindings) ? "Old School" : bindings.SequenceEqual(Modern) ? "Modern" : null;

        private static int[] Indices(params int[] keys) => keys.Select(k => Array.IndexOf(GameController.KeyTable, k)).ToArray();
    }
}
