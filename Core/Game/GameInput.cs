namespace Thexder.Core.Game
{
    /// <summary>The 19 remappable game actions.</summary>
    internal enum GameAction
    {
        Fire,
        SwapWeapons,
        ToPlane,
        ToTank,
        ToWalker,
        Detonate,
        Up,
        Down,
        Left,
        Right,
        UpLeft,
        UpRight,
        DownLeft,
        DownRight,
        NextWeapon,
        PreviousWeapon,
        PreviousEquipment,
        NextEquipment,
        UseEquipment,
    }

    /// <summary>The host feeds key transitions; the game polls once per frame.</summary>
    internal sealed class GameInput
    {
        public const int ActionCount = 19;

        private readonly bool[] held = new bool[ActionCount];
        private readonly bool[] pressedSincePoll = new bool[ActionCount];
        private readonly HashSet<int> heldKeys = new();

        /// <summary>The game window has focus; keys are ignored otherwise.</summary>
        public bool Active { get; set; } = true;

        /// <summary>The codeword dialog combination (T + X held).</summary>
        public bool CheatCombo { get; set; }

        public void SetAction(GameAction action, bool down)
        {
            int i = (int)action;
            if (down && !held[i])
            {
                pressedSincePoll[i] = true;
            }
            held[i] = down;
        }

        public void SetKey(int key, bool down)
        {
            if (down)
            {
                heldKeys.Add(key);
            }
            else
            {
                heldKeys.Remove(key);
            }
        }

        public bool IsKeyHeld(int key) => heldKeys.Contains(key);

        /// <summary>GetAsyncKeyState(...) &amp; 0x8000.</summary>
        public bool Held(GameAction action) => Active && held[(int)action];

        /// <summary>GetAsyncKeyState(...) &amp; 1: true once after the key went down; reading clears it.</summary>
        public bool PressedSincePoll(GameAction action)
        {
            int i = (int)action;
            bool result = pressedSincePoll[i];
            pressedSincePoll[i] = false;
            return Active && result;
        }

        public void ReleaseAll()
        {
            Array.Clear(held);
            Array.Clear(pressedSincePoll);
            heldKeys.Clear();
            CheatCombo = false;
        }
    }
}
