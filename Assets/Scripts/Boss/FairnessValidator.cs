using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>Witch flap physics, mirrored from PlayerController.</summary>
  public struct FlapModel {
    public float gravity, flapVelocity, maxFall, groundY, ceilingY, playerRadius;
    /// <summary>TODO(FF3): Free Flight removed flap/gravity from the witch. Until FF3 re-models the boss validator with
    /// MovementConfig (free 2D kinematics), the runtime check keeps using the legacy M3 flap model so patterns stay as tuned.</summary>
    public static FlapModel From(PlayerController p, GameConfig c){ var m = Default; m.groundY = c.groundY; m.ceilingY = c.ceilingY; m.playerRadius = MovementConfig.PlayerRadius; return m; }
    public static FlapModel Default => new FlapModel{ gravity = 24f, flapVelocity = 8.2f, maxFall = -12f, groundY = 0f, ceilingY = 10f, playerRadius = 0.42f };
  }

  /// <summary>Fairness check: is there ANY flap-only input sequence (one decision every decisionStep seconds, i.e. a
  /// human-ish flap rate) that keeps the witch off the street and clear of every fireball, with a safety margin?
  /// Dynamic programming over discretized (y, vy) states; the witch's x is fixed (fireballs are simulated in her frame).</summary>
  public static class FairnessValidator {
    public const float FireballRadius = 0.3f;
    public static float Margin = 0.35f;          // extra clearance for casual players
    public static float DecisionStep = 0.12f;    // at most one flap per 0.12 s
    const float Dt = 1f / 30f, YBin = 0.1f, VBin = 0.5f;

    public static bool IsFair(List<Shot> shots, FlapModel m, float startY, float startVy, float telegraph){
      if (shots == null || shots.Count == 0) return true;
      float top = m.ceilingY - 0.4f, bottom = m.groundY + 0.45f;
      float hitR = FireballRadius + m.playerRadius + Margin;
      var states = new Dictionary<int, Vector2>(); states[Key(startY, startVy)] = new Vector2(startY, startVy);
      float t = 0, maxT = telegraph + 12f;
      int sub = Mathf.Max(1, Mathf.RoundToInt(DecisionStep / Dt));
      var next = new Dictionary<int, Vector2>();
      while (t < maxT){
        if (AllPassed(shots, t, telegraph)) return true;
        next.Clear();
        foreach (var s in states.Values){
          for (int a = 0; a < 2; a++){
            float y = s.x, vy = a == 1 ? m.flapVelocity : s.y; bool dead = false;
            for (int k = 0; k < sub && !dead; k++){
              vy = Mathf.Max(vy - m.gravity * Dt, m.maxFall); y += vy * Dt;
              if (y > top){ y = top; vy = Mathf.Min(vy, 0); }
              if (y < bottom){ dead = true; break; }
              if (Collides(shots, t + (k + 1) * Dt, telegraph, y, hitR)) dead = true;
            }
            if (!dead) next[Key(y, vy)] = new Vector2(y, vy);
          }
        }
        if (next.Count == 0) return false;
        (states, next) = (next, states);
        t += sub * Dt;
      }
      return true;
    }
    static int Key(float y, float vy) => Mathf.RoundToInt(y / YBin) * 1000 + Mathf.RoundToInt(vy / VBin) + 500;
    static Vector2 PosAt(Shot s, float t, float telegraph){
      float tt = t - (s.live ? 0f : telegraph) - s.delay; if (tt < 0) return new Vector2(9999, 0);
      return s.pos + s.vel * tt + 0.5f * s.gravity * tt * tt * Vector2.down;
    }
    // witch-frame: the witch sits at x = 0; shot y is absolute (see ToAbsolute)
    static bool Collides(List<Shot> shots, float t, float telegraph, float y, float r){
      foreach (var s in shots){ var p = PosAt(s, t, telegraph); float dx = p.x, dy = p.y - y; if (dx * dx + dy * dy < r * r) return true; }
      return false;
    }
    static bool AllPassed(List<Shot> shots, float t, float telegraph){
      if (t < telegraph) return false;
      foreach (var s in shots){ var p = PosAt(s, t, telegraph); if (p.x > -1.5f && p.y > -1f) return false; }
      return true;
    }
    /// <summary>Converts shots built relative to the witch (pos.y relative) into absolute-y shots for IsFair.</summary>
    public static List<Shot> ToAbsolute(List<Shot> rel, float witchY){
      var o = new List<Shot>(rel.Count);
      foreach (var s in rel){ var c = s; if (!c.live) c.pos.y += witchY; o.Add(c); }
      return o;
    }

    /// <summary>Shared planner (runtime + tests): builds the attack, drops shots closer than minDist, and checks it TOGETHER with
    /// the fireballs already in the air (<paramref name="live"/>, absolute y, witch-frame x/vel). Tries 5 variants, then a single
    /// slow aimed shot; returns null to skip the attack. <paramref name="kind"/> is updated if the fallback is used.</summary>
    public static List<Shot> PlanFair(ref AttackKind kind, PhaseTuning tun, Vector2 bossRel, float y, float vy, List<Shot> live,
                                      FlapModel m, System.Random rng, float minDist, out int rejected){
      rejected = 0;
      for (int tries = 0; tries < 5; tries++){
        var rel = FireballPatterns.Build(kind, tun, bossRel, y, m.groundY, m.ceilingY, rng);
        rel.RemoveAll(s => s.pos.magnitude < minDist);
        if (rel.Count > 0 && IsFair(Combine(rel, live, y), m, y, vy, tun.telegraph)) return rel;
        rejected++;
      }
      var fb = new PhaseTuning{ fireballSpeed = 3.2f, aimedCount = 1, telegraph = tun.telegraph };
      var one = FireballPatterns.Build(AttackKind.Aimed, fb, bossRel, y, m.groundY, m.ceilingY, rng);
      one.RemoveAll(s => s.pos.magnitude < minDist);
      if (one.Count > 0 && IsFair(Combine(one, live, y), m, y, vy, tun.telegraph)){ kind = AttackKind.Aimed; return one; }
      return null;
    }
    static List<Shot> Combine(List<Shot> rel, List<Shot> live, float y){
      var all = ToAbsolute(rel, y); if (live != null) all.AddRange(live); return all;
    }

    /// <summary>Overlap sweep: a previous attack was fired <c>rest</c> seconds ago and its fireballs are still in the air when the
    /// next one is planned. Checks (a) whatever PlanFair returns is fair with the live fireballs, and counts how often it had to
    /// adjust or skip. Returns (cases, unfairCommitted, skipped).</summary>
    public static int OverlapConflictsCaught;
    public static (int total, int unfair, int skipped) OverlapSweep(PhaseTuning[] phases, FlapModel m, int seeds = 8, float restScale = 1f){
      int total = 0, bad = 0, skip = 0; OverlapConflictsCaught = 0;
      for (int p = 0; p < phases.Length; p++)
        foreach (AttackKind prevK in System.Enum.GetValues(typeof(AttackKind)))
          foreach (AttackKind k0 in System.Enum.GetValues(typeof(AttackKind)))
            for (float y0 = 2f; y0 <= 8f; y0 += 2f)
              for (int s = 0; s < seeds; s++){
                var rng = new System.Random(s * 104729 + p * 131 + (int)prevK * 17 + (int)k0);
                var tun = phases[p];
                float bossY = 2.5f + (float)rng.NextDouble() * 5f;
                var bossRel = new Vector2(12.5f - 1.6f, bossY + 0.4f - y0);
                // previous attack, advanced by its telegraph + rest (it has been flying for 'rest' seconds)
                var prev = ToAbsolute(FireballPatterns.Build(prevK, tun, bossRel, y0, m.groundY, m.ceilingY, rng), y0);
                var live = new List<Shot>();
                foreach (var sh in prev){
                  var c = sh; float tt = tun.rest * restScale - c.delay;
                  if (tt < 0){ c.delay = -tt; tt = 0; } else c.delay = 0;   // not yet launched (staggered arcs)
                  c.pos += c.vel * tt + 0.5f * c.gravity * tt * tt * Vector2.down; c.vel += c.gravity * tt * Vector2.down; c.live = true;
                  if (c.pos.x > -1.5f && c.pos.y > -1f) live.Add(c);
                }
                total++;
                // would a plan that ignored the live fireballs have been unfair? (shows the overlap check matters)
                var naive = FireballPatterns.Build(k0, tun, bossRel, y0, m.groundY, m.ceilingY, new System.Random(s));
                naive.RemoveAll(q => q.pos.magnitude < 6f);
                if (naive.Count > 0 && IsFair(ToAbsolute(naive, y0), m, y0, 0f, tun.telegraph) && !IsFair(Combine(naive, live, y0), m, y0, 0f, tun.telegraph)) OverlapConflictsCaught++;
                var k = k0;
                var plan = PlanFair(ref k, tun, bossRel, y0, 0f, live, m, rng, 6f, out _);
                if (plan == null){ skip++; continue; }
                var all = ToAbsolute(plan, y0); all.AddRange(live);
                if (!IsFair(all, m, y0, 0f, tun.telegraph)){ bad++; Debug.LogWarning($"[Fairness] UNFAIR overlap phase {p} {prevK}->{k} y0={y0} seed={s}"); }
              }
      return (total, bad, skip);
    }

    /// <summary>Test-time sweep used by the editor build (BossFairnessTest): every phase x attack x start height x seed.
    /// Returns (checked, unfair) and logs failures.</summary>
    public static (int total, int unfair) Sweep(PhaseTuning[] phases, FlapModel m, int seeds = 12){
      int total = 0, bad = 0;
      for (int p = 0; p < phases.Length; p++)
        foreach (AttackKind k in System.Enum.GetValues(typeof(AttackKind)))
          for (float y0 = 1.5f; y0 <= 9f; y0 += 1.5f)
            for (int s = 0; s < seeds; s++){
              var rng = new System.Random(s * 7919 + p * 31 + (int)k);
              float bossY = 2.5f + (float)rng.NextDouble() * 5f;
              var rel = FireballPatterns.Build(k, phases[p], new Vector2(12.5f - 1.6f, bossY + 0.4f - y0), y0, m.groundY, m.ceilingY, rng);
              total++;
              if (!IsFair(ToAbsolute(rel, y0), m, y0, 0f, phases[p].telegraph)){ bad++; Debug.LogWarning($"[Fairness] UNFAIR phase {p} {k} y0={y0} seed={s}"); }
            }
      return (total, bad);
    }
  }
}
