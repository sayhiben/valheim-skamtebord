using System;
using System.Collections.Generic;

namespace Skamtebord.Core
{
    public enum TrickId
    {
        Ollie,
        Shuvit,
        Kickflip,
        Heelflip,
        Grab,
        ThreeSixty
    }

    public sealed class TrickDefinition
    {
        internal TrickDefinition(TrickId id, string name, int baseScore, int minimumSkillLevel, float durationSeconds)
        {
            Id = id;
            Name = name;
            BaseScore = baseScore;
            MinimumSkillLevel = minimumSkillLevel;
            DurationSeconds = durationSeconds;
        }

        public TrickId Id { get; }
        public string Name { get; }
        public int BaseScore { get; }
        public int MinimumSkillLevel { get; }
        public float DurationSeconds { get; }
    }

    public static class TrickCatalog
    {
        private static readonly IReadOnlyList<TrickDefinition> Definitions = Array.AsReadOnly(new[]
        {
            new TrickDefinition(TrickId.Ollie, "Ollie", 100, 0, 0.12f),
            new TrickDefinition(TrickId.Shuvit, "Shuvit", 160, 3, 0.24f),
            new TrickDefinition(TrickId.Kickflip, "Kickflip", 250, 8, 0.32f),
            new TrickDefinition(TrickId.Heelflip, "Heelflip", 300, 12, 0.36f),
            new TrickDefinition(TrickId.Grab, "Grab", 180, 18, 0.28f),
            new TrickDefinition(TrickId.ThreeSixty, "360", 450, 25, 0.50f)
        });

        public static IReadOnlyList<TrickDefinition> All => Definitions;

        public static TrickDefinition Get(TrickId id)
        {
            int index = (int)id;
            if (index < 0 || index >= Definitions.Count)
                throw new ArgumentOutOfRangeException(nameof(id));
            return Definitions[index];
        }

        public static bool IsUnlocked(TrickId id, int skillLevel)
        {
            return skillLevel >= Get(id).MinimumSkillLevel;
        }
    }
}
