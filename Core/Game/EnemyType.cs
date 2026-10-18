namespace Thexder.Core.Game
{
    /// <summary>
    /// One of an enemy type's six weapons: shot type id (-1 none), damage given to the shot, switch chance.
    /// </summary>
    internal readonly record struct EnemyWeapon(int Shot, int Dmg, int Chance);

    /// <summary>An attack animation of an enemy type.</summary>
    internal sealed record EnemyAttack(int Chance, int W, int H, int Frames, int Mode, string? SprL, string? SprR, int Snd, int SndMode, int SndArg);

    /// <summary>An enemy type.</summary>
    internal sealed class EnemyType
    {
        public int Id { get; set; }

        public string Name { get; init; } = "";

        public bool IsBoss { get; init; }

        public int Mode { get; init; }

        public int Speed { get; init; }

        public int Range { get; init; }

        public int Hp { get; init; }

        public int TouchDmg { get; init; }

        public int Flags { get; init; }

        public int AnimDelay { get; init; }

        public int HopHeight { get; init; }

        public int W { get; init; }

        public int H { get; init; }

        public int Frames { get; init; }

        public bool HasStand { get; init; }

        public int StandFrame { get; init; }

        public int Weapon { get; init; }

        public EnemyWeapon[] Weapons { get; init; } = Array.Empty<EnemyWeapon>();

        /// <summary>Shot origin offsets in pixels at the current scale.</summary>
        public int ShotDX { get; set; }

        public int ShotDY { get; set; }

        public int AtkRange { get; init; }

        public int AtkCount { get; init; }

        public EnemyAttack[] Attacks { get; init; } = Array.Empty<EnemyAttack>();

        public int FlinchThr { get; init; }

        public int DieW { get; init; }

        public int DieH { get; init; }

        public int DieDX { get; init; }

        public int DieDY { get; init; }

        public int DieFrames { get; init; }

        public int TurnFrames { get; init; }

        public bool HasExpl { get; init; }

        public int BlastDmg { get; init; }

        public int BlastRad { get; init; }

        public int[] Expl { get; init; } = Array.Empty<int>();

        public int ExplCount { get; init; }

        public int ExplDelay0 { get; init; }

        public int[] ExplDelay { get; init; } = Array.Empty<int>();

        public int[] ExplDX { get; init; } = Array.Empty<int>();

        public int[] ExplDY { get; init; } = Array.Empty<int>();

        public int RemW { get; init; }

        public int RemH { get; init; }

        public int RemDX { get; init; }

        public int RemDY { get; init; }

        public int RemFrame { get; init; }

        public int RemFrames { get; init; }

        public int DieMode { get; init; }

        public string? SprL { get; init; }

        public string? SprR { get; init; }

        public string? SprTurn { get; init; }

        public string? SprDieL { get; init; }

        public string? SprDieR { get; init; }

        public string? SprFlinchL { get; init; }

        public string? SprFlinchR { get; init; }

        public int IdleSnd { get; init; }

        public int IdleSndMode { get; init; }

        public int IdleSndArg { get; init; }

        public int HitSnd { get; init; }

        public int DieSnd { get; init; }

        /// <summary>The placeholder record ("NA", sprite Exit) built for unused ids.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsPlaceholder => Name == "NA";
    }
}
