using Skamtebord.Core;

var tests = new (string Name, Action Run)[]
{
    ("Safe landing and grace are required; bank awards exactly once", SafeBank),
    ("Bailing before or after landing loses pending score", Bail),
    ("Stationary and short hops cannot score", NoStationaryFarming),
    ("Trick animation timing prevents rapid input spam", TrickRate),
    ("A late unfinished trick bails even during a long jump", UnfinishedTrick),
    ("Repeated tricks diminish without increasing multiplier", Repetition),
    ("Mixed tricks increase multiplier and remain capped", MultiplierCap),
    ("Combo trick count is bounded", TrickCountCap),
    ("New airborne interval pauses bank and can join combo", ComboContinuation),
    ("Dropping without tricks still blocks banking until landing", EmptyAirborne),
    ("Every trick enforces its exact unlock threshold", Unlocks),
    ("Invalid time and landing values cannot award score", InvalidNumbers),
    ("Only banked points are lifetime experience", AwardPoints),
    ("Progression thresholds are monotonic and exact", ProgressionThresholds),
    ("Progression handles corrupt saves and integer overflow", ProgressionBounds),
    ("Labels and last bank survive safely across combos", LabelAndLastBank),
    ("Radio file URIs preserve spaces, Unicode, hashes and percent signs", RadioPlaylistChecks.FileUris),
    ("Radio scans MP3 extensions case-insensitively and ignores subfolders", RadioPlaylistChecks.FileFiltering),
    ("Radio resolves default and relative folders against the mod directory", RadioPlaylistChecks.FolderResolution),
    ("Radio reports invalid and missing folders to its playback error handler", RadioPlaylistChecks.InvalidFolders),
    ("Radio shuffling preserves every track and avoids immediate repeats", RadioPlaylistChecks.Shuffling)
};
int failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message);
    }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void Require(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

static ComboSession LandedOllie()
{
    var combo = new ComboSession();
    Require(combo.TryAddTrick(TrickId.Ollie, 0));
    combo.Tick(0.5f);
    Require(combo.Land(0.5f, 5f, true));
    return combo;
}

static void SafeBank()
{
    var combo = new ComboSession();
    Require(combo.Bank() is null);
    Require(combo.TryAddTrick(TrickId.Ollie, 0));
    combo.Tick(10);
    Require(combo.Bank() is null, "Cannot bank midair");
    Require(combo.Land(0.5f, 4f, true));
    Require(combo.Bank() is null);
    combo.Tick(1.99f);
    Require(combo.Bank() is null, "Grace not elapsed");
    combo.Tick(0.02f);
    BankResult bank = combo.Bank()!;
    Require(bank != null && bank.Points == 100 && bank.Experience == 100);
    Require(combo.PendingScore == 0 && combo.Bank() is null, "Must not award twice");
}

static void Bail()
{
    var air = new ComboSession();
    air.TryAddTrick(TrickId.Ollie, 0);
    air.Bail();
    air.Tick(10);
    Require(air.PendingScore == 0 && air.Bank() is null);
    var ground = LandedOllie();
    ground.Tick(1);
    ground.Bail();
    ground.Tick(10);
    Require(ground.PendingScore == 0 && ground.Bank() is null);
    var unsafeLanding = new ComboSession();
    unsafeLanding.TryAddTrick(TrickId.Ollie, 0);
    unsafeLanding.Tick(1);
    Require(!unsafeLanding.Land(1, 5, false));
    Require(unsafeLanding.PendingScore == 0);
}

static void NoStationaryFarming()
{
    foreach ((float air, float speed) in new[] { (1f, 0f), (1f, 1.99f), (0.19f, 10f), (1f, -5f) })
    {
        var combo = new ComboSession();
        combo.TryAddTrick(TrickId.Ollie, 0);
        combo.Tick(1);
        Require(!combo.Land(air, speed, true));
        combo.Tick(10);
        Require(combo.Bank() is null && combo.PendingScore == 0);
    }
    var edge = new ComboSession();
    edge.TryAddTrick(TrickId.Ollie, 0);
    edge.Tick(0.2f);
    Require(edge.Land(0.2f, 2f, true), "Inclusive minimums should succeed");
}

static void TrickRate()
{
    var combo = new ComboSession();
    Require(combo.TryAddTrick(TrickId.Ollie, 100));
    for (int i = 0; i < 100; i++) Require(!combo.TryAddTrick(TrickId.Kickflip, 100));
    Require(combo.TrickCount == 1 && combo.PendingScore == 100);
    combo.Tick(0.12f);
    Require(combo.TryAddTrick(TrickId.Kickflip, 100));
    combo.Tick(0.32f);
    Require(combo.Land(0.5f, 4f, true));
    combo.Tick(2);
    Require(combo.Bank()!.Points == 700);
}

static void UnfinishedTrick()
{
    var combo = new ComboSession();
    combo.BeginAirborne();
    combo.Tick(5);
    Require(combo.TryAddTrick(TrickId.ThreeSixty, 100));
    combo.Tick(0.01f);
    Require(!combo.Land(5.01f, 5, true), "Large total airtime cannot finish a last-second trick");
    Require(combo.PendingScore == 0);
}

static void Repetition()
{
    var combo = new ComboSession();
    int[] totals = { 100, 150, 175, 187, 193, 196, 197 };
    foreach (int total in totals)
    {
        Require(combo.TryAddTrick(TrickId.Ollie, 0));
        Require(combo.PendingScore == total && combo.Multiplier == 1);
        combo.Tick(0.5f);
        Require(combo.Land(0.5f, 5, true));
    }
}

static void MultiplierCap()
{
    var combo = new ComboSession();
    for (int cycle = 0; cycle < 4; cycle++)
    {
        foreach (TrickDefinition trick in TrickCatalog.All)
        {
            Require(combo.TryAddTrick(trick.Id, 100));
            combo.Tick(1);
            Require(combo.Land(1, 5, true));
            Require(combo.Multiplier <= ComboSession.MaximumMultiplier);
            Require(combo.PendingScore <= ComboSession.MaximumComboPoints);
        }
    }
    combo.Tick(2);
    var bank = combo.Bank()!;
    Require(bank.Points == ComboSession.MaximumComboPoints && bank.Experience == bank.Points);
}

static void TrickCountCap()
{
    var combo = new ComboSession();
    for (int i = 0; i < ComboSession.MaximumTricksPerCombo; i++)
    {
        Require(combo.TryAddTrick(TrickId.Ollie, 0));
        combo.Tick(0.5f);
        Require(combo.Land(0.5f, 4, true));
    }
    Require(!combo.TryAddTrick(TrickId.Ollie, 0));
    Require(combo.TrickCount == ComboSession.MaximumTricksPerCombo);
    combo.Tick(2);
    Require(combo.Bank() is not null);
}

static void ComboContinuation()
{
    var combo = LandedOllie();
    combo.Tick(1.9f);
    combo.BeginAirborne();
    combo.Tick(2);
    Require(combo.Bank() is null);
    Require(combo.TryAddTrick(TrickId.Shuvit, 3));
    combo.Tick(0.3f);
    Require(combo.Land(2.3f, 5, true));
    combo.Tick(1.9f);
    Require(combo.Bank() is null);
    combo.Tick(0.2f);
    Require(combo.Bank()!.Points == 520);
}

static void EmptyAirborne()
{
    var combo = LandedOllie();
    combo.BeginAirborne();
    combo.Tick(10);
    Require(combo.Bank() is null);
    Require(combo.Land(10, 5, true));
    combo.Tick(2);
    Require(combo.Bank()!.Points == 100);
    var empty = new ComboSession();
    empty.BeginAirborne();
    empty.Tick(1);
    Require(empty.Land(1, 5, true));
    empty.Tick(2);
    Require(empty.Bank() is null);
}

static void Unlocks()
{
    int[] levels = { 0, 0, 8, 12, 0, 25 };
    Require(TrickCatalog.All.Count == levels.Length);
    for (int i = 0; i < levels.Length; i++)
    {
        var trick = TrickCatalog.All[i];
        Require(trick.MinimumSkillLevel == levels[i]);
        var locked = new ComboSession();
        Require(!locked.TryAddTrick(trick.Id, levels[i] - 1));
        Require(locked.PendingScore == 0 && !locked.IsAirborne);
        Require(locked.TryAddTrick(trick.Id, levels[i]));
    }
    Require(!new ComboSession().TryAddTrick((TrickId)500, 100));
}

static void InvalidNumbers()
{
    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        var combo = LandedOllie();
        combo.Tick(invalid);
        combo.Tick(-1);
        Require(combo.Bank() is null);
        combo.BeginAirborne();
        Require(!combo.Land(1, invalid, true));
        Require(combo.PendingScore == 0);
        combo.TryAddTrick(TrickId.Ollie, 0);
        combo.Tick(1);
        Require(!combo.Land(invalid, 5, true));
    }
}

static void AwardPoints()
{
    var progression = new SkamtebordProgression();
    var combo = LandedOllie();
    Require(progression.LifetimePoints == 0);
    combo.Tick(2);
    var bank = combo.Bank()!;
    progression.AddPoints(bank.Experience);
    Require(progression.LifetimePoints == bank.Points);
    Require(progression.Level == 0);
    progression.AddPoints(200);
    Require(progression.Level == 1 && progression.LevelProgress == 0);
}

static void ProgressionThresholds()
{
    for (int level = 1; level <= 100; level++)
    {
        long threshold = SkamtebordProgression.PointsForLevel(level);
        Require(threshold > SkamtebordProgression.PointsForLevel(level - 1));
        Require(SkamtebordProgression.LevelFromPoints(threshold) == level);
        Require(SkamtebordProgression.LevelFromPoints(threshold - 1) == level - 1);
        var progression = new SkamtebordProgression(threshold - 1);
        Require(progression.LevelProgress >= 0 && progression.LevelProgress < 1);
    }
    Require(SkamtebordProgression.PointsForLevel(3) == 1500);
    Require(SkamtebordProgression.PointsForLevel(25) == 67500);
}

static void ProgressionBounds()
{
    var negative = new SkamtebordProgression(-500);
    Require(negative.LifetimePoints == 0 && negative.Level == 0);
    Require(SkamtebordProgression.LevelFromPoints(long.MinValue) == 0);
    var huge = new SkamtebordProgression(long.MaxValue - 50);
    huge.AddPoints(100);
    Require(huge.LifetimePoints == long.MaxValue && huge.Level == 100 && huge.LevelProgress == 1);
    bool threw = false;
    try { huge.AddPoints(-1); } catch (ArgumentOutOfRangeException) { threw = true; }
    Require(threw && huge.LifetimePoints == long.MaxValue);
}

static void LabelAndLastBank()
{
    var combo = LandedOllie();
    Require(combo.ComboLabel == "Ollie");
    combo.Tick(2);
    var bank = combo.Bank();
    Require(bank == combo.LastBank && bank!.Label == "Ollie");
    Require(combo.ComboLabel == string.Empty && combo.Multiplier == 0);
    combo.TryAddTrick(TrickId.Ollie, 0);
    combo.Bail();
    Require(combo.LastBank == bank, "Last award is informational and survives a later bail");
}
