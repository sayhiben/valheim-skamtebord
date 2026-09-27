using System;

namespace Skamtebord.Core
{
    /// <summary>
    /// Banked score is lifetime Skamtebord XP, independent of Valheim's skill XP calculation.
    /// Persist LifetimePoints per character and mirror Level into the game's custom skill.
    /// </summary>
    public sealed class SkamtebordProgression
    {
        public const int MaximumLevel = 100;

        public SkamtebordProgression(long lifetimePoints = 0)
        {
            LifetimePoints = Math.Max(0, lifetimePoints);
        }

        public long LifetimePoints { get; private set; }
        public int Level => LevelFromPoints(LifetimePoints);
        public float LevelProgress
        {
            get
            {
                int level = Level;
                if (level == MaximumLevel)
                    return 1f;
                long current = PointsForLevel(level);
                long next = PointsForLevel(level + 1);
                return (float)(LifetimePoints - current) / (next - current);
            }
        }

        public void AddPoints(int points)
        {
            if (points < 0)
                throw new ArgumentOutOfRangeException(nameof(points));
            LifetimePoints = LifetimePoints > long.MaxValue - points ? long.MaxValue : LifetimePoints + points;
        }

        public static long PointsForLevel(int level)
        {
            int bounded = Math.Max(0, Math.Min(MaximumLevel, level));
            return 100L * bounded * bounded + 200L * bounded;
        }

        public static int LevelFromPoints(long points)
        {
            int low = 0;
            int high = MaximumLevel;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (points >= PointsForLevel(middle))
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }
    }
}
