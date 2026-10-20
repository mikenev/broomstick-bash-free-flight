using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>Witch model for the boss fairness check, derived from MovementConfig (FF1 free flight).</summary>
  public struct FlightModel {
    public float speed, xMin, xMax, yMin, yMax, playerRadius;
    /// <summary>Fraction of MovementConfig.MaxSpeed the validator assumes. Covers the 0.12 s accel/decel (a full
    /// reversal costs ~0.24 s), 8-way keyboard steering (other angles need alternating keys) and human reaction slack.</summary>
    public const float SpeedFactor = 0.65f;
    public static FlightModel FromConfig() => new FlightModel{
      speed = MovementConfig.MaxSpeed * SpeedFactor,
      xMin = FairnessValidator.XMin, xMax = FairnessValidator.XMax,
      yMin = MovementConfig.BottomY + 0.3f,          // never ask her to skim the street (contact costs a heart)
      yMax = MovementConfig.TopY,                     // ceiling clamp is harmless
      playerRadius = MovementConfig.PlayerRadius };
  }

  /// <summary>Boss fairness for free flight: is there ANY path for the witch (speed <= FlightModel.speed in any direction,
  /// one decision every <see cref="Step"/> s, inside the play box) that avoids every fireball with a safety margin until all
  /// of them have passed? Reachable-set search on a 0.25 m grid in the scroll-anchor (= screen) frame: each step, every
  /// reachable cell expands to the cells within speed*Step that stay clear at every sub-step. Danger is rasterized once per
  /// sub-step, so a check costs a few ms (runtime planner + DevQA autopilot use the same code).</summary>
  public static class FairnessValidator {
    public const float FireballRadius = 0.3f;
    public static float Margin = 0.2f;              // clearance on top of the two radii (the 0.65 speed factor adds slack too)
    /// <summary>Horizontal witch zone the validator may use (anchor frame). Conservative: inside the real clamp for every
    /// landscape aspect from 4:3 to 21:9 (MovementConfig.HorizontalBounds) with LeftMargin up to 1.6 m.</summary>
    public const float XMin = -1.5f, XMax = 3.5f;
    public const float Cell = 0.25f, Step = 0.15f; const int Sub = 3;
    const float Dt = Step / Sub;

    struct Grid { public int nx, ny; public float x0, y0; }
    static Grid G(FlightModel m) => new Grid{ x0 = m.xMin, y0 = m.yMin, nx = Mathf.FloorToInt((m.xMax - m.xMin) / Cell) + 1, ny = Mathf.FloorToInt((m.yMax - m.yMin) / Cell) + 1 };
    static readonly Dictionary<float, (int dx, int dy)[]> moveCache = new();
    static (int dx, int dy)[] Moves(float speed){
      if (moveCache.TryGetValue(speed, out var mv)) return mv;
      var l = new List<(int, int)>(); float r = speed * Step / Cell; int ri = Mathf.FloorToInt(r);
      for (int dx = -ri; dx <= ri; dx++) for (int dy = -ri; dy <= ri; dy++) if (dx * dx + dy * dy <= r * r + 1e-4f) l.Add((dx, dy));
      mv = l.ToArray(); moveCache[speed] = mv; return mv;
    }

    public static Vector2 PosAt(Shot s, float t, float telegraph){
      float tt = t - (s.live ? 0f : telegraph) - s.delay; if (tt < 0) return new Vector2(9999, 0);
      return s.pos + s.vel * tt + 0.5f * s.gravity * tt * tt * Vector2.down;
    }
    static bool Spawned(Shot s, float t, float telegraph) => t - (s.live ? 0f : telegraph) - s.delay >= 0;
    static bool AllPassed(List<Shot> shots, float t, float telegraph, FlightModel m, float r){
      foreach (var s in shots){
        if (!Spawned(s, t, telegraph)) return false;
        var p = PosAt(s, t, telegraph);
        bool gone = (p.x < m.xMin - r && s.vel.x <= 0) || (p.y < m.yMin - r - 0.5f && s.vel.y <= 0 && s.gravity >= 0) || (p.y > m.yMax + r + 0.5f && s.vel.y >= 0 && s.gravity <= 0);
        if (!gone) return false;
      }
      return true;
    }
    static void Rasterize(List<Shot> shots, float t, float telegraph, Grid g, float r, bool[] danger){
      System.Array.Clear(danger, 0, danger.Length);
      float r2 = r * r; int rc = Mathf.CeilToInt(r / Cell);
      foreach (var s in shots){
        var p = PosAt(s, t, telegraph);
        int cx = Mathf.RoundToInt((p.x - g.x0) / Cell), cy = Mathf.RoundToInt((p.y - g.y0) / Cell);
        if (cx < -rc || cx >= g.nx + rc || cy < -rc || cy >= g.ny + rc) continue;
        for (int ix = Mathf.Max(0, cx - rc); ix <= Mathf.Min(g.nx - 1, cx + rc); ix++)
          for (int iy = Mathf.Max(0, cy - rc); iy <= Mathf.Min(g.ny - 1, cy + rc); iy++){
            float dx = g.x0 + ix * Cell - p.x, dy = g.y0 + iy * Cell - p.y;
            if (dx * dx + dy * dy < r2) danger[ix * g.ny + iy] = true;
          }
      }
    }
    static int CellOf(Grid g, Vector2 p) => Mathf.Clamp(Mathf.RoundToInt((p.x - g.x0) / Cell), 0, g.nx - 1) * g.ny + Mathf.Clamp(Mathf.RoundToInt((p.y - g.y0) / Cell), 0, g.ny - 1);
    static Vector2 CenterOf(Grid g, int c) => new Vector2(g.x0 + (c / g.ny) * Cell, g.y0 + (c % g.ny) * Cell);

    /// <summary>Core search. Returns true if some path survives until every shot has passed (or <paramref name="horizon"/>
    /// elapses, if given). <paramref name="firstMove"/> (optional) receives, for the surviving cell closest to
    /// <paramref name="prefer"/>, the first step of its path (used by the dev autopilot).</summary>
    static bool Search(List<Shot> shots, FlightModel m, Vector2 start, float telegraph, float horizon, Vector2? prefer, out Vector2 firstMove){
      firstMove = Vector2.zero;
      var g = G(m); int n = g.nx * g.ny; float r = FireballRadius + m.playerRadius + Margin;
      var moves = Moves(m.speed);
      var reach = new int[n]; var next = new int[n];          // value = 1 + index of the first-step cell (0 = unreachable)
      var danger = new bool[Sub][]; for (int k = 0; k < Sub; k++) danger[k] = new bool[n];
      int sc = CellOf(g, start); reach[sc] = sc + 1;
      float maxDelay = 0; foreach (var s in shots) maxDelay = Mathf.Max(maxDelay, s.delay);
      float maxT = horizon > 0 ? horizon : telegraph + maxDelay + 9f;
      bool first = true;
      for (float t = 0; t < maxT; t += Step){
        if (horizon <= 0 && AllPassed(shots, t, telegraph, m, r)) return true;
        for (int k = 0; k < Sub; k++) Rasterize(shots, t + (k + 1) * Dt, telegraph, g, r, danger[k]);
        System.Array.Clear(next, 0, n); bool any = false;
        for (int c = 0; c < n; c++){
          if (reach[c] == 0) continue;
          int cx = c / g.ny, cy = c % g.ny;
          foreach (var (dx, dy) in moves){
            int tx = cx + dx, ty = cy + dy;
            if (tx < 0 || ty < 0 || tx >= g.nx || ty >= g.ny) continue;
            int dst = tx * g.ny + ty; if (next[dst] != 0) continue;
            bool ok = true;
            for (int k = 0; k < Sub && ok; k++){
              float f = (k + 1) / (float)Sub;
              int ix = Mathf.RoundToInt(cx + dx * f), iy = Mathf.RoundToInt(cy + dy * f);
              if (danger[k][ix * g.ny + iy]) ok = false;
            }
            if (!ok) continue;
            next[dst] = first ? dst + 1 : reach[c]; any = true;
          }
        }
        if (!any) return false;
        (reach, next) = (next, reach); first = false;
      }
      if (prefer.HasValue){
        int best = -1; float bd = float.MaxValue;
        for (int c = 0; c < n; c++){ if (reach[c] == 0) continue; float d = (CenterOf(g, c) - prefer.Value).sqrMagnitude; if (d < bd){ bd = d; best = c; } }
        if (best >= 0) firstMove = CenterOf(g, reach[best] - 1) - CenterOf(g, sc);
      }
      return true;
    }

    public static bool IsFair(List<Shot> shots, FlightModel m, Vector2 start, float telegraph){
      if (shots == null || shots.Count == 0) return true;
      return Search(shots, m, start, telegraph, -1f, null, out _);
    }

    /// <summary>Dev autopilot helper: first move of a path that survives <paramref name="horizon"/> s of the given fireballs
    /// (live ones, plus planned ones that launch after <paramref name="telegraph"/> s) and ends as close as possible to
    /// <paramref name="prefer"/>. null if no surviving path exists.</summary>
    public static Vector2? Steer(List<Shot> shots, FlightModel m, Vector2 pos, Vector2 prefer, float horizon, float telegraph = 0f){
      return Search(shots, m, pos, telegraph, horizon, prefer, out var mv) ? mv : (Vector2?)null;
    }

    /// <summary>Shared planner (runtime + tests): builds the attack, drops shots spawning closer than minDist to the witch,
    /// and checks it TOGETHER with the fireballs already in the air. Tries 4 variants (random gap / drift / jitter), then a
    /// slow 3-shot fan; returns null to skip the attack. <paramref name="kind"/> is updated if the fallback is used.</summary>
    public static List<Shot> PlanFair(ref AttackKind kind, PhaseTuning tun, Vector2 hand, Vector2 witch, float chestY, List<Shot> live,
                                      FlightModel m, System.Random rng, float minDist, out int rejected){
      rejected = 0;
      for (int tries = 0; tries < 4; tries++){
        var shots = FireballPatterns.Build(kind, tun, hand, witch, chestY, rng);
        shots.RemoveAll(s => (s.pos - witch).magnitude < minDist);
        if (shots.Count > 0 && IsFair(Combine(shots, live), m, witch, tun.telegraph)) return shots;
        rejected++;
      }
      var fb = new PhaseTuning{ fanCount = 3, fanSpreadDeg = 25f, fanSpeed = 4f, telegraph = tun.telegraph };
      var one = FireballPatterns.Build(AttackKind.Fan, fb, hand, witch, chestY, rng);
      one.RemoveAll(s => (s.pos - witch).magnitude < minDist);
      if (one.Count > 0 && IsFair(Combine(one, live), m, witch, tun.telegraph)){ kind = AttackKind.Fan; return one; }
      return null;
    }
    static List<Shot> Combine(List<Shot> shots, List<Shot> live){ var all = new List<Shot>(shots); if (live != null) all.AddRange(live); return all; }

    // ---------------- test-time sweeps (BossFairnessTest, run by BuildWeb before every build) ----------------
    /// <summary>Varied witch start positions (anchor frame) used by the sweeps.</summary>
    public static readonly Vector2[] Starts = {
      new(-1f, 1.2f), new(-1f, 5f), new(-1f, 9.2f), new(1.2f, 3f), new(1.2f, 7f), new(3.3f, 1.2f), new(3.3f, 5f), new(3.3f, 9.2f) };
    /// <summary>Werewolf hand + chest for a boss at height bossY (anchor frame; boss x = trackX + 12.5).</summary>
    public static Vector2 HandAt(float bossY) => new Vector2(12.5f - 1.6f, bossY + 0.4f);
    public static float ChestAt(float bossY) => bossY + 0.2f;
    static float BossY(System.Random rng, int phase) => 5f + ((float)rng.NextDouble() * 2f - 1f) * BossTuning.BobAmp(phase);

    /// <summary>Every phase x attack x start position x seed, straight from the pattern builder (no retries).
    /// Returns (checked, unfair) and logs failures.</summary>
    public static (int total, int unfair) Sweep(PhaseTuning[] phases, FlightModel m, int seeds = 4){
      int total = 0, bad = 0;
      for (int p = 0; p < phases.Length; p++)
        foreach (AttackKind k in System.Enum.GetValues(typeof(AttackKind)))
          for (int si = 0; si < Starts.Length; si++)
            for (int s = 0; s < seeds; s++){
              var rng = new System.Random(s * 7919 + p * 31 + (int)k * 7 + si * 104729);
              float by = BossY(rng, p); var start = Starts[si];
              var shots = FireballPatterns.Build(k, phases[p], HandAt(by), start, ChestAt(by), rng);
              shots.RemoveAll(q => (q.pos - start).magnitude < 6f);
              total++;
              if (!IsFair(shots, m, start, phases[p].telegraph)){ bad++; Debug.LogWarning($"[Fairness] UNFAIR phase {p} {k} start={start} bossY={by:0.0} seed={s}"); }
            }
      return (total, bad);
    }

    /// <summary>Overlap sweep: the previous attack (any kind) was fired <c>rest*restScale</c> s ago from a different witch
    /// position and its fireballs are still in the air when the next one is planned. Checks that whatever PlanFair commits
    /// is fair together with the live fireballs. Returns (cases, unfairCommitted, skipped); counts naive plans the live
    /// check caught in OverlapConflictsCaught.</summary>
    public static int OverlapConflictsCaught;
    public static (int total, int unfair, int skipped) OverlapSweep(PhaseTuning[] phases, FlightModel m, int seeds = 2, float restScale = 1f){
      int total = 0, bad = 0, skip = 0; OverlapConflictsCaught = 0;
      for (int p = 0; p < phases.Length; p++)
        foreach (AttackKind prevK in System.Enum.GetValues(typeof(AttackKind)))
          foreach (AttackKind k0 in System.Enum.GetValues(typeof(AttackKind)))
            for (int si = 0; si < Starts.Length; si += 2)
              for (int s = 0; s < seeds; s++){
                var rng = new System.Random(s * 104729 + p * 131 + (int)prevK * 17 + (int)k0 * 3 + si * 7);
                var tun = phases[p]; var start = Starts[si]; var prevStart = Starts[(si + 3) % Starts.Length];
                float by0 = BossY(rng, p), by = BossY(rng, p);
                var prev = FireballPatterns.Build(prevK, tun, HandAt(by0), prevStart, ChestAt(by0), rng);
                var live = new List<Shot>();
                foreach (var sh in prev){
                  var c = sh; float tt = tun.rest * restScale - c.delay;
                  if (tt < 0){ c.delay = -tt; tt = 0; } else c.delay = 0;   // not launched yet (streams / staggered shots)
                  c.pos += c.vel * tt + 0.5f * c.gravity * tt * tt * Vector2.down; c.vel += c.gravity * tt * Vector2.down; c.live = true;
                  if (c.pos.x > m.xMin - 1.5f) live.Add(c);
                }
                total++;
                var naive = FireballPatterns.Build(k0, tun, HandAt(by), start, ChestAt(by), new System.Random(s));
                naive.RemoveAll(q => (q.pos - start).magnitude < 6f);
                if (naive.Count > 0 && IsFair(naive, m, start, tun.telegraph) && !IsFair(Combine(naive, live), m, start, tun.telegraph)) OverlapConflictsCaught++;
                var k = k0;
                var plan = PlanFair(ref k, tun, HandAt(by), start, ChestAt(by), live, m, rng, 6f, out _);
                if (plan == null){ skip++; continue; }
                if (!IsFair(Combine(plan, live), m, start, tun.telegraph)){ bad++; Debug.LogWarning($"[Fairness] UNFAIR overlap phase {p} {prevK}->{k} start={start} seed={s}"); }
              }
      return (total, bad, skip);
    }
  }
}
