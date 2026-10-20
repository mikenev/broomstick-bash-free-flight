using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>FF3 werewolf attacks (free flight). Every attack covers a big part of the vertical play range and forces the
  /// witch to move; Beam is the chest-height deterrent so hovering level with the werewolf and casting nonstop fails.</summary>
  public enum AttackKind {
    Fan,     // spread of fireballs aimed at the witch's position when the attack is planned (covers her band)
    Wall,    // full-height column with a gap; in later phases the whole wall drifts up/down so the gap moves
    Burst,   // staggered fast shots, each aimed at a point around her plan-time position (fixed targets, deterministic)
    Beam,    // dense stream along the werewolf's chest height (1-2 rows): camping level with him gets hit
    Sweep    // streams skimming the street (and in phase 3 the ceiling) plus a couple of aimed shots
  }

  /// <summary>One planned fireball in the SCROLL-ANCHOR frame: pos.x is relative to PlayerController.TrackX (the camera
  /// follows the anchor, so this is a screen-fixed frame), pos.y is absolute world y; vel is relative to the anchor.</summary>
  public struct Shot { public Vector2 pos; public Vector2 vel; public float gravity; public float delay;
    /// <summary>true = a fireball already in the air (moves from t=0, not after the telegraph).</summary>
    public bool live; }

  /// <summary>Per-phase tuning (phase 0 = full HP ... 2 = low HP). Not serialized: code is the single source of truth.</summary>
  public class PhaseTuning {
    public float telegraph = 1.2f, rest = 1.2f;
    public int fanCount = 3; public float fanSpreadDeg = 22f, fanSpeed = 5f;
    public float wallGap = 3.0f, wallSpeed = 4.5f, wallDrift = 0f;
    public int burstCount = 2; public float burstInterval = 0.4f, burstSpeed = 6f, burstOffset = 1.4f;
    public int beamRows = 1; public float beamDuration = 0.8f, beamSpeed = 10f;
    public bool sweepCeiling = false; public float sweepDuration = 1.4f, sweepSpeed = 7f; public int sweepAimed = 1;
    public AttackKind[] attacks = { AttackKind.Fan, AttackKind.Wall };
  }

  /// <summary>FF3 boss tuning: 3 phases (HP 12-9, 8-5, 4-1), each faster, tighter and with more attacks per cycle.
  /// Every attack forces movement; Beam (chest-height deterrent) appears in every cycle, twice in phase 3.</summary>
  public static class BossTuning {
    public static PhaseTuning[] DefaultPhases() => new[]{
      new PhaseTuning{ telegraph = 1.2f, rest = 1.25f,
        fanCount = 3, fanSpreadDeg = 22f, fanSpeed = 5f, wallGap = 3.0f, wallSpeed = 4.5f, wallDrift = 0f,
        burstCount = 2, burstInterval = 0.45f, burstSpeed = 6f, burstOffset = 1.4f, beamRows = 1, beamDuration = 0.8f, beamSpeed = 10f,
        sweepCeiling = false, sweepDuration = 1.2f, sweepSpeed = 6.5f, sweepAimed = 1,
        attacks = new[]{ AttackKind.Fan, AttackKind.Wall, AttackKind.Beam, AttackKind.Burst } },
      new PhaseTuning{ telegraph = 1.0f, rest = 1.05f,
        fanCount = 5, fanSpreadDeg = 15f, fanSpeed = 5.6f, wallGap = 2.7f, wallSpeed = 5f, wallDrift = 0.8f,
        burstCount = 3, burstInterval = 0.38f, burstSpeed = 6.5f, burstOffset = 1.5f, beamRows = 2, beamDuration = 1.0f, beamSpeed = 10f,
        sweepCeiling = false, sweepDuration = 1.5f, sweepSpeed = 7f, sweepAimed = 1,
        attacks = new[]{ AttackKind.Wall, AttackKind.Fan, AttackKind.Beam, AttackKind.Sweep, AttackKind.Burst } },
      new PhaseTuning{ telegraph = 0.85f, rest = 0.85f,
        fanCount = 5, fanSpreadDeg = 18f, fanSpeed = 6.2f, wallGap = 2.5f, wallSpeed = 5.5f, wallDrift = 1.3f,
        burstCount = 4, burstInterval = 0.32f, burstSpeed = 7f, burstOffset = 1.6f, beamRows = 2, beamDuration = 1.3f, beamSpeed = 10.5f,
        sweepCeiling = true, sweepDuration = 1.8f, sweepSpeed = 7.5f, sweepAimed = 2,
        attacks = new[]{ AttackKind.Wall, AttackKind.Burst, AttackKind.Beam, AttackKind.Fan, AttackKind.Sweep, AttackKind.Beam } },
    };
    /// <summary>Vertical bob amplitude around y 5 (bigger in later phases, so the chest band moves over most of the range).</summary>
    public static float BobAmp(int phase) => phase <= 0 ? 2.6f : phase == 1 ? 3.0f : 3.4f;
  }

  /// <summary>Builds attack patterns. Pure (no scene access) so the editor fairness sweep uses exactly the runtime code.</summary>
  public static class FireballPatterns {
    public const float SpawnX = 11.4f;                 // anchor-frame x where streams/walls enter (just off the right edge)
    public const float WallSpacing = 0.85f, StreamInterval = 0.1f, SweepInterval = 0.28f;
    public const float PlayBottom = MovementConfig.BottomY, PlayTop = MovementConfig.TopY;

    static Vector2 Rot(Vector2 v, float deg){ float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }
    static Vector2 Aim(Vector2 from, Vector2 to, float speed){ var d = to - from; return d.sqrMagnitude < 1e-4f ? new Vector2(-speed, 0) : d.normalized * speed; }

    /// <param name="hand">werewolf hand (spawn point of thrown shots), anchor frame</param>
    /// <param name="witch">witch position when the attack is planned, anchor frame (aim targets are fixed to this)</param>
    /// <param name="chestY">werewolf chest height when planned (Beam)</param>
    public static List<Shot> Build(AttackKind k, PhaseTuning t, Vector2 hand, Vector2 witch, float chestY, System.Random rng){
      var list = new List<Shot>();
      switch (k){
        case AttackKind.Fan: {
          var dir = Aim(hand, witch, t.fanSpeed);
          float jitter = ((float)rng.NextDouble() - 0.5f) * t.fanSpreadDeg * 0.5f;   // so the gaps aren't always in the same place
          for (int i = 0; i < t.fanCount; i++){
            float a = (i - (t.fanCount - 1) * 0.5f) * t.fanSpreadDeg + jitter;
            list.Add(new Shot{ pos = hand, vel = Rot(dir, a) });
          }
          break;
        }
        case AttackKind.Wall: {
          float drift = t.wallDrift * (rng.NextDouble() < 0.5 ? -1f : 1f);
          // times at which the wall enters / leaves the witch zone; the moving gap must stay inside the play range meanwhile
          float tIn = (SpawnX - FairnessValidator.XMax - 1f) / t.wallSpeed, tOut = (SpawnX - FairnessValidator.XMin + 1f) / t.wallSpeed;
          float half = t.wallGap * 0.5f, lo = PlayBottom + 0.6f + half, hi = PlayTop - 0.2f - half;
          float sIn = drift * tIn, sOut = drift * tOut;
          float cLo = lo - Mathf.Min(sIn, sOut), cHi = hi - Mathf.Max(sIn, sOut);
          float c0 = cHi > cLo ? Mathf.Lerp(cLo, cHi, (float)rng.NextDouble()) : (cLo + cHi) * 0.5f;
          // extend the column beyond the screen opposite to the drift so no lane opens at the top/bottom
          float yLo = PlayBottom - 0.3f - Mathf.Max(0f, drift) * tOut, yHi = PlayTop + 0.4f + Mathf.Max(0f, -drift) * tOut;
          for (float y = yLo; y <= yHi + 0.01f; y += WallSpacing){
            if (Mathf.Abs(y - c0) < half) continue;
            list.Add(new Shot{ pos = new Vector2(SpawnX, y), vel = new Vector2(-t.wallSpeed, drift) });
          }
          break;
        }
        case AttackKind.Burst: {
          for (int i = 0; i < t.burstCount; i++){
            // targets fixed at plan time: on her, then alternately above/below so standing still OR drifting one way gets hit
            float off = i == 0 ? 0f : ((i + 1) / 2) * t.burstOffset * ((i % 2 == 1) == (rng.NextDouble() < 0.5) ? 1f : -1f);
            var target = new Vector2(witch.x, Mathf.Clamp(witch.y + off, PlayBottom, PlayTop));
            list.Add(new Shot{ pos = hand, vel = Aim(hand, target, t.burstSpeed), delay = i * t.burstInterval });
          }
          break;
        }
        case AttackKind.Beam: {
          int n = Mathf.Max(1, Mathf.RoundToInt(t.beamDuration / StreamInterval));
          for (int r = 0; r < t.beamRows; r++){
            float y = Mathf.Clamp(chestY + (t.beamRows == 1 ? 0f : (r == 0 ? -0.45f : 0.45f)), PlayBottom + 0.3f, PlayTop - 0.3f);
            for (int i = 0; i < n; i++) list.Add(new Shot{ pos = new Vector2(hand.x, y), vel = new Vector2(-t.beamSpeed, 0), delay = i * StreamInterval });
          }
          break;
        }
        case AttackKind.Sweep: {
          var rows = new List<float>{ PlayBottom + 0.3f, PlayBottom + 1.1f };
          if (t.sweepCeiling){ rows.Add(PlayTop - 0.75f); rows.Add(PlayTop); }
          int n = Mathf.Max(1, Mathf.RoundToInt(t.sweepDuration / SweepInterval));
          foreach (var y in rows)
            for (int i = 0; i < n; i++) list.Add(new Shot{ pos = new Vector2(SpawnX, y), vel = new Vector2(-t.sweepSpeed, 0), delay = i * SweepInterval });
          for (int i = 0; i < t.sweepAimed; i++)
            list.Add(new Shot{ pos = hand, vel = Aim(hand, witch, t.fanSpeed), delay = 0.3f + i * 0.5f });
          break;
        }
      }
      return list;
    }
  }
}
