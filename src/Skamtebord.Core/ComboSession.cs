using System;
using System.Collections.Generic;

namespace Skamtebord.Core
{
    public sealed class BankResult
    {
        internal BankResult(int points, int multiplier, int trickCount, string label)
        {
            Points = points;
            Multiplier = multiplier;
            TrickCount = trickCount;
            Label = label;
        }

        public int Points { get; }
        public int Experience => Points;
        public int Multiplier { get; }
        public int TrickCount { get; }
        public string Label { get; }
    }

    /// <summary>
    /// A local player's pending combo. Tick once per physics update, signal each airborne/landing
    /// transition, then call Bank each update. Only a non-null Bank result awards points.
    /// </summary>
    public sealed class ComboSession
    {
        public const float BankGraceSeconds = 2f;
        public const float MinimumAirtimeSeconds = 0.2f;
        public const float MinimumLandingSpeed = 2f;
        public const int MaximumMultiplier = 6;
        public const int MaximumComboPoints = 10000;
        public const int MaximumTricksPerCombo = 32;

        private readonly Dictionary<TrickId, int> _repetitions = new Dictionary<TrickId, int>();
        private readonly List<string> _labels = new List<string>();
        private int _baseScore;
        private float _trickCooldown;
        private float _requiredAirtime;
        private float _groundedSeconds;
        private bool _airborne;
        private bool _safeLanding;

        public int PendingScore => Math.Min(MaximumComboPoints, _baseScore * Multiplier);
        public int Multiplier => Math.Min(MaximumMultiplier, _repetitions.Count);
        public int TrickCount => _labels.Count;
        public bool IsAirborne => _airborne;
        public float TrickCooldownSeconds => _trickCooldown;
        public float BankTimeRemaining => TrickCount == 0 || _airborne || !_safeLanding
            ? BankGraceSeconds : Math.Max(0, BankGraceSeconds - _groundedSeconds);
        public BankResult? LastBank { get; private set; }

        public string ComboLabel
        {
            get
            {
                int first = Math.Max(0, _labels.Count - 4);
                string visible = string.Join(" + ", _labels.GetRange(first, _labels.Count - first));
                return first > 0 ? "… + " + visible : visible;
            }
        }

        /// <summary>Call when a jump or a drop starts, including airtime with no trick.</summary>
        public void BeginAirborne()
        {
            if (_airborne)
                return;
            _airborne = true;
            _safeLanding = false;
            _requiredAirtime = 0;
            _trickCooldown = 0;
            _groundedSeconds = 0;
        }

        /// <summary>
        /// The caller must only request tricks while physically airborne. For convenience, a
        /// first accepted trick also marks the session airborne. Tick enforces animation spacing.
        /// </summary>
        public bool TryAddTrick(TrickId id, int skillLevel)
        {
            if (!Enum.IsDefined(typeof(TrickId), id) || TrickCount >= MaximumTricksPerCombo || _trickCooldown > 0.0001f)
                return false;
            TrickDefinition trick = TrickCatalog.Get(id);
            if (skillLevel < trick.MinimumSkillLevel)
                return false;

            BeginAirborne();
            _repetitions.TryGetValue(id, out int previousCount);
            int award = Math.Max(1, trick.BaseScore >> Math.Min(previousCount, 10));
            _baseScore = Math.Min(MaximumComboPoints, _baseScore + award);
            _repetitions[id] = previousCount + 1;
            _labels.Add(trick.Name);
            _requiredAirtime += trick.DurationSeconds;
            _trickCooldown = trick.DurationSeconds;
            return true;
        }

        /// <summary>
        /// Supply the measured airtime and horizontal landing speed. A crash, unfinished trick,
        /// stationary hop, or invalid measurement loses the entire pending combo.
        /// </summary>
        public bool Land(float airtimeSeconds, float horizontalSpeed, bool safeLanding)
        {
            if (!_airborne)
                return false;
            bool valid = safeLanding && IsFinite(airtimeSeconds) && IsFinite(horizontalSpeed)
                && airtimeSeconds >= MinimumAirtimeSeconds
                && horizontalSpeed >= MinimumLandingSpeed
                && airtimeSeconds + 0.0001f >= _requiredAirtime
                && _trickCooldown <= 0.0001f;
            if (!valid)
            {
                Bail();
                return false;
            }
            _airborne = false;
            _safeLanding = true;
            _groundedSeconds = 0;
            _trickCooldown = 0;
            _requiredAirtime = 0;
            return true;
        }

        /// <summary>Advance time; invalid or nonpositive deltas are ignored and never award XP.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0)
                return;
            _trickCooldown = Math.Max(0, _trickCooldown - deltaTime);
            if (!_airborne && _safeLanding && TrickCount > 0)
                _groundedSeconds = Math.Min(BankGraceSeconds, _groundedSeconds + deltaTime);
        }

        /// <summary>Returns an award once, after the two-second safe, grounded bank window.</summary>
        public BankResult? Bank()
        {
            if (_airborne || !_safeLanding || _groundedSeconds < BankGraceSeconds || PendingScore == 0)
                return null;
            var result = new BankResult(PendingScore, Multiplier, TrickCount, ComboLabel);
            LastBank = result;
            ClearPending();
            return result;
        }

        public void Bail() => ClearPending();

        private void ClearPending()
        {
            _baseScore = 0;
            _repetitions.Clear();
            _labels.Clear();
            _trickCooldown = 0;
            _requiredAirtime = 0;
            _groundedSeconds = 0;
            _airborne = false;
            _safeLanding = false;
        }

        private static bool IsFinite(float number) => !float.IsNaN(number) && !float.IsInfinity(number);
    }
}
