using System.Text.Json;

namespace Thexder.Core.Game
{
    /// <summary>The enemy types, read from the embedded enemy-types.json.</summary>
    internal static class EnemyTypeTable
    {
        public const int Count = 0x38;

        /// <summary>The flying Ankhra Sphinx: only built together with the statue.</summary>
        public const int FlyingSphinx = 0x1a;

        public const int SphinxStatue = 0x24;

        private static readonly Lazy<JsonElement> Data = new(Load);

        private static readonly Lazy<bool[]> BossTypes = new(() => Enumerable.Range(0, Count).Select(id => Build(id, 8).IsBoss).ToArray());

        /// <summary>Whether a type id is a boss (its death opens the exit).</summary>
        public static bool IsBossType(int id) => id >= 0 && id < Count && BossTypes.Value[id];

        /// <summary>A type at cell size <paramref name="s"/>; unused ids get the placeholder.</summary>
        public static EnemyType Build(int id, int s)
        {
            var types = Data.Value.GetProperty("types");
            return id >= 0 && id < types.GetArrayLength() && types[id].ValueKind == JsonValueKind.Object
                ? Read(types[id], s)
                : Placeholder(id);
        }

        /// <summary>The Sphinx's second form, written into slot <see cref="FlyingSphinx"/> when the statue wakes.</summary>
        public static EnemyType BuildFlyingSphinx(int s) => Read(Data.Value.GetProperty("flyingSphinx"), s);

        /// <summary>The record ("NA", sprite Exit) used for unused ids.</summary>
        public static EnemyType Placeholder(int id)
        {
            var t = Read(Data.Value.GetProperty("placeholder"), 1);
            t.Id = id;
            return t;
        }

        // Some shot origins are given in cells and scale with the art.
        private static EnemyType Read(JsonElement element, int s)
        {
            var t = element.Deserialize<EnemyType>() ?? throw new InvalidDataException("Bad enemy type.");
            if (element.TryGetProperty("ShotInCells", out var cells) && cells.GetBoolean())
            {
                t.ShotDX *= s;
                t.ShotDY *= s;
            }
            return t;
        }

        private static JsonElement Load()
        {
            using var stream = typeof(EnemyTypeTable).Assembly.GetManifestResourceStream("Thexder.enemy-types.json")
                ?? throw new InvalidOperationException("The enemy types are missing from the build.");
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.Clone();
        }
    }
}
