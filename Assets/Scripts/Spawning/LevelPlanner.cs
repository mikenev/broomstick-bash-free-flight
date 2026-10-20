using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>One obstacle in a planned column. All geometry and motion is decided here (not randomised in the factory) so
  /// the fairness check sees exactly what gets spawned.</summary>
  public struct PlannedItem {
    public ObstacleKind kind;
    public float y;        // ground kinds: base (groundY); ceiling: top; flyers: SPAWN height
    public float size;     // kid height, lamp-post height, branch length (unused for others)
    public float amp;      // flyer motion amplitude around its target (bat swoop depth, ghost sine amplitude)
    public float speed;    // extra leftward speed (m/s)
    public float dx;       // x offset inside the column (kid groups, bat flock)
    public float targetY;  // FF2 homing: flyers glide from y to targetY (fixed at spawn) over LevelPlanner.HomeTime
    public float Rest => kind == ObstacleKind.Bat || kind == ObstacleKind.Ghost ? targetY : y; // flyer height once homed
    /// <summary>Half width along x (for timing the passage), without dx.</summary>
    public float HalfWidth => kind switch {
      ObstacleKind.Car => 1.6f, ObstacleKind.Kid => 0.55f, ObstacleKind.LampPost => LevelPlanner.LampHeadR,
      ObstacleKind.Branch => 0.4f + 0.21f * size, ObstacleKind.Bat => 0.7f, ObstacleKind.Ghost => 0.45f, _ => 1f };
    /// <summary>Vertical band this item occupies while it can be near the witch (world y), incl. motion, rotation and bob.
    /// Flyers have finished homing by then (checked by the validator), so only the band around targetY counts.</summary>
    public Vector2 Envelope(float groundY, float ceilingY){
      switch (kind){
        case ObstacleKind.Kid: return new Vector2(groundY, groundY + size + 0.15f);
        case ObstacleKind.Car: return new Vector2(groundY, groundY + LevelPlanner.CarHeight);
        case ObstacleKind.LampPost: return new Vector2(groundY, groundY + size + LevelPlanner.LampHeadR);
        case ObstacleKind.Branch: return new Vector2(ceilingY - size - 0.3f, ceilingY + 0.1f);
        case ObstacleKind.Bat: return new Vector2(targetY - amp - LevelPlanner.BatHalf, targetY + LevelPlanner.BatHalf);
        case ObstacleKind.Ghost: return new Vector2(targetY - amp - LevelPlanner.GhostHalf, targetY + amp + LevelPlanner.GhostHalf);
      }
      return new Vector2(y - 1, y + 1);
    }
  }
  public class PlannedColumn {
    public List<PlannedItem> items = new();
    public float difficulty, scroll, interval, clock;
    public Vector2 safe;          // reachable part of the chosen window for the witch's centre (min, max)
    public string template;
    public bool Multi => items.Count > 1;
    public bool HasKid { get { foreach (var i in items) if (i.kind == ObstacleKind.Kid) return true; return false; } }
  }

  /// <summary>FF2 street planner with a FREE-FLIGHT fairness guarantee (MovementConfig is the source of truth).
  /// Each column (group spawned at one x) must leave a vertical gap of at least MinGap(d) (>= 1.8 m; kid columns >= KidGap)
  /// inside the witch's y bounds, and that gap must be reachable from the previous column's gap: the vertical distance between
  /// the two windows must be coverable rest-to-rest with MaxSpeed/AccelRate (see Reach) in the time between the witch leaving
  /// the previous column and entering this one (worst case over witch x offset and obstacle speeds, minus a reaction margin).
  /// Multi-item spawns are at least MinMultiSpacing s apart. Patterns that fail are re-rolled; after MaxTries a single small kid
  /// is used. BuildWeb.ValidateStreet re-checks every column independently and gates the WebGL build.</summary>
  public class LevelPlanner {
    // geometry shared with ObstacleFactory (keep in sync)
    public const float CarHeight = 1.95f, LampHeadR = 0.4f, BatHalf = 0.42f, GhostHalf = 0.62f;
    // witch: sphere r 0.42 centred +0.1 -> occupies [y-0.32, y+0.52]
    public const float PlayerBelow = MovementConfig.PlayerRadius - 0.1f, PlayerAbove = MovementConfig.PlayerRadius + 0.1f;
    public const float PlayerExtent = PlayerBelow + PlayerAbove;               // 0.84 m
    public const float WindowLo = MovementConfig.BottomY + 0.05f;              // street contact costs a heart
    public const float WindowHi = MovementConfig.TopY;                         // ceiling clamp is free
    public const float SpawnAhead = 15f;
    public const float HomeTime = 0.9f;                                        // flyers reach targetY within this
    public const float Reaction = 0.15f;                                       // s subtracted from every transition
    public const float FrameSlack = 1f / 15f;                                  // s lost to per-frame velocity steps at >= 30 fps
    public const float MinMultiSpacing = 0.9f, MinSingleInterval = 0.7f;
    public const float KidGap = 2.6f;                                          // kids always get a clear lane
    // witch x offset range (world x - trackX), conservative over aspect ratios: max is < CameraLead for any aspect
    public const float WitchOffsetMin = -9f, WitchOffsetMax = MovementConfig.CameraLead;
    public const int MaxTries = 30;
    public static float MinGap(float d) => Mathf.Lerp(3.0f, 1.8f, d);
    public static float MinWindow(float d, bool kid) => Mathf.Max(MinGap(d), kid ? KidGap : 0f) - PlayerExtent;

    /// <summary>Max vertical distance the witch surely covers in t seconds, starting and ending at rest (free-flight model:
    /// continuous accel/brake at AccelRate up to MaxSpeed, minus a reaction margin and the frame-step loss at 30 fps).</summary>
    public static float Reach(float t){
      t -= Reaction + FrameSlack; if (t <= 0) return 0;
      float ta = MovementConfig.AccelTime, v = MovementConfig.MaxSpeed;
      return t <= 2 * ta ? MovementConfig.AccelRate * t * t / 4f : v * (t - ta);
    }
    /// <summary>Earliest time after spawn at which any part of the column can touch the witch.</summary>
    public static float ArriveMin(PlannedItem it, float scroll){
      float d = SpawnAhead + MovementConfig.CameraLead - WitchOffsetMax - Mathf.Abs(it.dx) - it.HalfWidth - MovementConfig.PlayerRadius;
      return d / (scroll + it.speed);
    }
    /// <summary>Time between the witch leaving column a and entering column b (b spawned dtSpawn later), worst case.</summary>
    public static float Transition(PlannedColumn a, PlannedColumn b, float dtSpawn, float scroll){
      float worst = float.MaxValue;
      foreach (var D in new[]{ SpawnAhead + MovementConfig.CameraLead - WitchOffsetMax, SpawnAhead + MovementConfig.CameraLead - WitchOffsetMin }){
        float leaveA = float.MinValue, enterB = float.MaxValue;
        foreach (var it in a.items){ float back = it.dx + it.HalfWidth + MovementConfig.PlayerRadius; leaveA = Mathf.Max(leaveA, (D + back) / (a.scroll + it.speed) - dtSpawn); }
        foreach (var it in b.items){ float front = it.dx - it.HalfWidth - MovementConfig.PlayerRadius; enterB = Mathf.Min(enterB, (D + front) / (scroll + it.speed)); }
        worst = Mathf.Min(worst, enterB - leaveA);
      }
      return worst;
    }
    /// <summary>Distance between two intervals (0 if they overlap).</summary>
    public static float Dist(Vector2 a, Vector2 b) => Mathf.Max(0, Mathf.Max(a.x - b.y, b.x - a.y));

    public float groundY = 0, ceilingY = 10;
    public System.Random rng;
    /// <summary>Optional: SpawnTable whose entry weight / minDifficulty gate which kinds the templates use (weight 0 = disabled).</summary>
    public SpawnTable table;
    float W(ObstacleKind k, float d){
      if (!table) return 1f; var e = table.EntryFor(k);
      return e == null || d < e.minDifficulty ? 0f : Mathf.Max(0f, e.weight);
    }
    ObstacleKind PickKind(float d, ObstacleKind fallback, params ObstacleKind[] ks){
      float tot = 0; foreach (var k in ks) tot += W(k, d);
      if (tot <= 0) return fallback;
      float r = R(0, tot); foreach (var k in ks){ r -= W(k, d); if (r <= 0) return k; }
      return ks[ks.Length - 1];
    }
    PlannedColumn prev; float clock, lastMultiClock;
    public LevelPlanner(System.Random r, float ground, float ceiling){ rng = r; groundY = ground; ceilingY = ceiling; Reset(); }
    public void Reset(){ prev = null; clock = 0; lastMultiClock = -99; }
    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    /// <summary>Plan the next column, <paramref name="dt"/> seconds after the previous one, at scroll speed <paramref name="scroll"/>.</summary>
    public PlannedColumn Next(float d, float scroll, float dt, bool allowGhost = true){
      clock += dt;
      bool multiOk = clock - lastMultiClock >= MinMultiSpacing;
      PlannedColumn best = null;
      for (int i = 0; i < MaxTries && best == null; i++){
        var c = Roll(d, allowGhost, multiOk); c.scroll = scroll;
        if (Check(c, d, scroll, dt, out var w)){ c.safe = w; best = c; }
      }
      if (best == null){ // always-safe fallback: one small kid (leaves most of the sky)
        best = new PlannedColumn{ template = "fallback", scroll = scroll };
        best.items.Add(new PlannedItem{ kind = ObstacleKind.Kid, y = groundY, size = 1.0f, speed = 0.5f });
        Check(best, d, scroll, dt, out var w); best.safe = w;
      }
      best.difficulty = d; best.interval = dt; best.clock = clock;
      if (best.Multi) lastMultiClock = clock;
      prev = best;
      return best;
    }

    /// <summary>Largest valid window that is reachable from the previous column's window. Public for the validator.</summary>
    public bool Check(PlannedColumn c, float d, float scroll, float dt, out Vector2 window){
      window = Vector2.zero; c.scroll = scroll;
      float need = MinWindow(d, c.HasKid);
      foreach (var it in c.items)
        if ((it.kind == ObstacleKind.Bat || it.kind == ObstacleKind.Ghost) && ArriveMin(it, scroll) < HomeTime + 0.05f) return false;
      float reach = prev != null ? Reach(Transition(prev, c, dt, scroll)) : 99f;
      float bestLen = -1;
      foreach (var w0 in Windows(c)){
        // only the part of the window reachable from (anywhere in) the previous reachable window counts, so the chain is sound
        var w = prev != null ? new Vector2(Mathf.Max(w0.x, prev.safe.x - reach), Mathf.Min(w0.y, prev.safe.y + reach)) : w0;
        if (w.y - w.x < need + 0.02f) continue;                       // small margin over the validator's sampling
        if (w.y - w.x > bestLen){ bestLen = w.y - w.x; window = w; }
      }
      return bestLen >= 0;
    }

    /// <summary>Free intervals for the witch's centre in this column.</summary>
    public List<Vector2> Windows(PlannedColumn c){
      var occ = new List<Vector2>();
      foreach (var it in c.items){ var e = it.Envelope(groundY, ceilingY); occ.Add(new Vector2(e.x - PlayerAbove, e.y + PlayerBelow)); }
      occ.Sort((a, b) => a.x.CompareTo(b.x));
      var res = new List<Vector2>(); float lo = WindowLo, hi = WindowHi, cur = lo;
      foreach (var o in occ){ if (o.x > cur) res.Add(new Vector2(cur, Mathf.Min(o.x, hi))); cur = Mathf.Max(cur, o.y); }
      if (cur < hi) res.Add(new Vector2(cur, hi));
      res.RemoveAll(w => w.y <= w.x);
      return res;
    }

    // ---- pattern templates ----
    PlannedItem Ground(float d){
      var k = PickKind(d, ObstacleKind.Kid, ObstacleKind.Kid, ObstacleKind.Car, ObstacleKind.LampPost);
      if (k == ObstacleKind.Kid) return Kid(d, 0);
      if (k == ObstacleKind.Car) return new PlannedItem{ kind = ObstacleKind.Car, y = groundY, speed = R(1.2f, 1.5f + 1.5f * d) };
      return new PlannedItem{ kind = ObstacleKind.LampPost, y = groundY, size = R(3.4f, 4.6f + 1.8f * d) };
    }
    PlannedItem Kid(float d, float dx) => new PlannedItem{ kind = ObstacleKind.Kid, y = groundY, size = R(0.9f, 1.8f), speed = R(0.3f, 1.0f), dx = dx };
    PlannedItem Ceiling(float d) => new PlannedItem{ kind = ObstacleKind.Branch, y = ceilingY, size = R(2.2f, 3.2f + 1.8f * d) };
    /// <summary>Homing flyer: spawns at a random height and glides to a target height (lo..hi) fixed now; the target is biased
    /// toward the previous safe window (where the witch probably is) so flyers pressure her, but stays validated.</summary>
    PlannedItem Flyer(float d, bool allowGhost, float lo, float hi){
      var fk = allowGhost ? PickKind(d, ObstacleKind.Bat, ObstacleKind.Bat, ObstacleKind.Ghost) : ObstacleKind.Bat;
      float aim = prev != null ? (prev.safe.x + prev.safe.y) * 0.5f : 5f;
      float spawnY = R(1.5f, 9.3f);
      if (fk == ObstacleKind.Ghost){
        float amp = R(0.4f, 0.6f + 0.5f * d); float ty = Mathf.Clamp(Mathf.Lerp(R(lo, hi), aim, 0.35f * d), lo + amp, Mathf.Max(lo + amp, hi - amp));
        return new PlannedItem{ kind = ObstacleKind.Ghost, y = spawnY, targetY = ty, amp = amp, speed = 1.2f };
      }
      float bamp = R(0.5f, 0.8f + 0.6f * d); float by = Mathf.Clamp(Mathf.Lerp(R(lo, hi), aim, 0.35f * d), Mathf.Min(lo + bamp, hi), hi);
      return new PlannedItem{ kind = ObstacleKind.Bat, y = spawnY, targetY = by, amp = bamp, speed = R(2.0f, 2.5f + 1.0f * d) };
    }
    PlannedColumn Roll(float d, bool allowGhost, bool multiOk){
      var c = new PlannedColumn();
      float w1 = Mathf.Lerp(1f, 0.3f, d), w2 = !multiOk || d < 0.15f ? 0 : Mathf.Lerp(0.4f, 1.0f, d), w3 = !multiOk || d < 0.5f ? 0 : Mathf.Lerp(0.2f, 0.8f, d);
      float r = R(0, w1 + w2 + w3);
      float top = ceilingY - 0.6f, bot = groundY + 1.2f;
      if (r < w1){
        int k = rng.Next(4); c.template = "single";
        if (k == 1 && W(ObstacleKind.Branch, d) <= 0) k = 0;
        c.items.Add(k == 0 ? Ground(d) : k == 1 ? Ceiling(d) : k == 2 ? Flyer(d, allowGhost, bot, top) : Flyer(d, false, 8.2f, 9.3f)); // k==3: high bat (top edge not safe)
        if (k == 3) c.template = "high-bat";
      } else if (r < w1 + w2){
        int k = rng.Next(5);
        if ((k == 0 || k == 2) && W(ObstacleKind.Branch, d) <= 0) k = 1;
        if (k == 3 && W(ObstacleKind.Kid, d) <= 0) k = 1;
        if (k == 0){ c.template = "ground+ceiling"; c.items.Add(Ground(d)); c.items.Add(Ceiling(d)); }
        else if (k == 1){ c.template = "ground+flyer"; c.items.Add(Ground(d)); c.items.Add(Flyer(d, allowGhost, 4.5f, top)); }
        else if (k == 2){ c.template = "ceiling+flyer"; c.items.Add(Ceiling(d)); c.items.Add(Flyer(d, allowGhost, bot, 5.5f)); }
        else if (k == 3){ c.template = "kid-group"; c.items.Add(Kid(d, -1.3f)); c.items.Add(Kid(d, 0)); if (d > 0.5f) c.items.Add(Kid(d, 1.3f)); }
        else { c.template = "high+low"; c.items.Add(Flyer(d, false, 8.2f, 9.3f)); c.items.Add(Flyer(d, allowGhost, bot, 4.0f)); }
      } else {
        int k = rng.Next(2);
        if (k == 0 && W(ObstacleKind.Branch, d) <= 0) k = 1;
        if (k == 0){ c.template = "ground+flyer+ceiling"; c.items.Add(Ground(d)); c.items.Add(Ceiling(d)); c.items.Add(Flyer(d, allowGhost, 3.0f, 7.5f)); }
        else { c.template = "bat-flock"; float y0 = R(3.5f, 6f);
               var a = Flyer(d, false, y0 + 2.6f, 9.3f); var b = Flyer(d, false, bot, y0 - 1.2f); b.dx = 1.2f;
               c.items.Add(a); c.items.Add(b); }
      }
      return c;
    }
  }
}
