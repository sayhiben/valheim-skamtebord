# Scoring and progression contract

This assembly contains deterministic scoring and progression without Unity or Valheim references.

For each local-player physics update:

1. Call `Tick(deltaTime)` once with elapsed simulation time.
2. Call `BeginAirborne()` on every takeoff or ledge departure, even when no trick was requested.
3. While physically airborne, call `TryAddTrick(id, progression.Level)` for trick input. Animate the trick only when the call returns `true`. `DurationSeconds` supplies its animation duration; the core rejects a new trick while an earlier one is unfinished.
4. On landing, call `Land(measuredAirtimeSeconds, horizontalSpeed, safeLanding)`. Safe landings need at least 0.2 seconds airborne, at least 2 m/s horizontal speed, enough airtime for all performed tricks, and a finished final trick. An invalid landing clears the entire combo.
5. Call `Bank()` after transitions. It returns one award after two continuously grounded seconds following a safe landing. Add `award.Experience` to progression and persist `LifetimePoints`. Repeated calls cannot award twice.

Call `Bail()` when the character crashes, dies, dismounts unsafely, or enters a movement state that invalidates skating. A new airborne interval within the grace period joins the existing combo. The game adapter owns physical state and must never call `TryAddTrick` for grounded input. `LastBank` is informational and must not be consumed repeatedly as an XP event.

Distinct tricks raise the multiplier up to six. Repeated occurrences of each trick halve that trick's base score, down to one point, and do not raise the multiplier. Each combo accepts at most 32 tricks and awards at most 10,000 points. XP equals the exact banked point count; there is no separate XP multiplier. Crashes award zero. Lifetime points are stored separately from Valheim's skill-progress internals, and the adapter mirrors the resulting level into the custom skill.

| Trick | Unlock level | Base points | Duration |
| --- | ---: | ---: | ---: |
| Ollie | 0 | 100 | 0.12 s |
| Shuvit | 3 | 160 | 0.24 s |
| Kickflip | 8 | 250 | 0.32 s |
| Heelflip | 12 | 300 | 0.36 s |
| Grab | 18 | 180 | 0.28 s |
| 360 | 25 | 450 | 0.50 s |

Level thresholds use `100 × level² + 200 × level`, capped at level 100. The first additional trick unlocks at 1,500 lifetime points; the last at 67,500. XP continues accumulating after maximum level. Values loaded below zero normalize to zero, and lifetime-point addition saturates safely at `long.MaxValue`.

Run executable checks with:

```powershell
dotnet run --project tests/Skamtebord.Core.Tests/Skamtebord.Core.Tests.csproj --configuration Release
```
