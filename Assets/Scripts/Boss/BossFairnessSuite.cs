using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>The boss fairness gate (pure code, no scene): run by the editor BossFairnessTest, i.e. by BuildWeb.Build before
  /// every build, which fails the build if this returns false.</summary>
  public static class BossFairnessSuite {
    public static bool Run(){
      var phases = BossTuning.DefaultPhases(); var m = FlightModel.FromConfig();
      var sw = System.Diagnostics.Stopwatch.StartNew();
      var (total, bad) = FairnessValidator.Sweep(phases, m);
      Debug.Log($"[Fairness] {total - bad}/{total} werewolf patterns fair (free flight: {m.speed:0.00} m/s = {FlightModel.SpeedFactor:0.00} x MaxSpeed, " +
                $"box x {m.xMin}..{m.xMax} y {m.yMin:0.00}..{m.yMax:0.00}, margin {FairnessValidator.Margin} m, {FairnessValidator.Starts.Length} start positions) in {sw.ElapsedMilliseconds} ms");

      // negative control 1: a wall whose gap is too small to pass must be rejected
      var tight = new PhaseTuning{ wallGap = 0.6f, wallSpeed = 4.5f, telegraph = 1f };
      var wall = FireballPatterns.Build(AttackKind.Wall, tight, FairnessValidator.HandAt(5f), new Vector2(1f, 5f), 5.2f, new System.Random(1));
      bool detects = !FairnessValidator.IsFair(wall, m, new Vector2(1f, 5f), 1f);
      Debug.Log($"[Fairness] negative control (0.6 m gap wall) rejected: {detects}");

      // negative control 2: a live, gapless wall already in the air in front of her must be rejected
      var liveWall = new List<Shot>();
      for (float y = -0.5f; y <= 10.5f; y += 0.8f) liveWall.Add(new Shot{ pos = new Vector2(6f, y), vel = new Vector2(-5f, 0), live = true });
      var fan = FireballPatterns.Build(AttackKind.Fan, phases[0], FairnessValidator.HandAt(5f), new Vector2(1f, 5f), 5.2f, new System.Random(2));
      var all = new List<Shot>(fan); all.AddRange(liveWall);
      bool liveDetects = !FairnessValidator.IsFair(all, m, new Vector2(1f, 5f), 1f);
      Debug.Log($"[Fairness] negative control (live solid wall in the air) rejected: {liveDetects}");

      var (ot, obad, oskip) = FairnessValidator.OverlapSweep(phases, m);
      Debug.Log($"[Fairness] overlap: {ot} cases (live fireballs from the previous attack, fired 1x rest ago), unfair committed={obad}, skipped={oskip} ({100f * oskip / Mathf.Max(1, ot):0.0}%), naive plans the live check caught={FairnessValidator.OverlapConflictsCaught}");
      var (st, sbad, sskip) = FairnessValidator.OverlapSweep(phases, m, 2, 0.3f);
      Debug.Log($"[Fairness] overlap stress (0.3x rest): {st} cases, unfair committed={sbad}, skipped={sskip} ({100f * sskip / Mathf.Max(1, st):0.0}%), naive plans the live check caught={FairnessValidator.OverlapConflictsCaught}");

      // deterrent check: a witch that camps level with the werewolf's chest is hit at least once per attack cycle
      int cycles = 0, cyclesHit = 0, attacksHit = 0, attacks = 0;
      for (int p = 0; p < phases.Length; p++)
        for (int s = 0; s < 6; s++){
          var rng = new System.Random(900 + s * 13 + p); float by = 5f + ((float)rng.NextDouble() * 2f - 1f) * BossTuning.BobAmp(p);
          var camp = new Vector2(1.2f, FairnessValidator.ChestAt(by)); bool hitCycle = false;
          foreach (var k in phases[p].attacks){
            var shots = FireballPatterns.Build(k, phases[p], FairnessValidator.HandAt(by), camp, FairnessValidator.ChestAt(by), rng);
            attacks++; if (HitsStationary(shots, camp, phases[p].telegraph)){ attacksHit++; hitCycle = true; }
          }
          cycles++; if (hitCycle) cyclesHit++;
        }
      bool deterrent = cyclesHit == cycles;
      Debug.Log($"[Fairness] chest camping: hit in {cyclesHit}/{cycles} attack cycles ({attacksHit}/{attacks} attacks would hit a stationary witch at chest height)");
      return bad == 0 && detects && liveDetects && obad == 0 && sbad == 0 && oskip <= ot * 0.25f && deterrent;
    }
    static bool HitsStationary(List<Shot> shots, Vector2 at, float telegraph){
      float r = FairnessValidator.FireballRadius + MovementConfig.PlayerRadius;
      for (float t = 0; t < telegraph + 8f; t += 1f / 60f)
        foreach (var s in shots){ var p = FairnessValidator.PosAt(s, t, telegraph); if ((p - at).sqrMagnitude < r * r) return true; }
      return false;
    }
  }
}
