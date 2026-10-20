using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  public enum ObstacleKind { Kid, Car, Branch, Bat, Ghost, LampPost } // append only (serialized ints)
  [System.Serializable] public class SpawnEntry {
    public ObstacleKind kind;
    [Tooltip("UNUSED: art is applied by BroomArtSkin (name-based) to avoid double art. Kept for serialization compatibility.")] public GameObject prefab;
    public float weight = 1f;
    [Range(0, 1)] public float minDifficulty = 0f;
    [Tooltip("M1 legacy (SpawnOne only). M2 LevelPlanner picks heights itself so the fairness validator can guarantee a gap; weight/minDifficulty DO gate M2 kinds (Branch weight only enables/disables ceiling templates).")]
    public float minY = 0f, maxY = 0f;
  }
  /// <summary>Data-driven spawn table + difficulty ramp. M2: tune weights/curves or add entries in the asset.</summary>
  [CreateAssetMenu(menuName = "Broomstick Bash/Spawn Table")]
  public class SpawnTable : ScriptableObject {
    public List<SpawnEntry> entries = new();
    [Tooltip("Seconds between spawns vs difficulty 0..1")] public AnimationCurve intervalByDifficulty = AnimationCurve.Linear(0, 1.8f, 1, 0.9f);
    [Tooltip("Scroll speed multiplier vs difficulty 0..1")] public AnimationCurve speedByDifficulty = AnimationCurve.Linear(0, 1f, 1, 1.4f);
    [Tooltip("Distance (m) to reach difficulty 1")] public float rampDistance = 600f;
    [Tooltip("Distance (m) at which the boss appears")] public float bossDistance = 450f;
    [Tooltip("M2: difficulty 0..1 vs ramp progress (Distance / rampDistance), non-linear with breathers")]
    public AnimationCurve difficultyByProgress = AnimationCurve.Linear(0, 0, 1, 1);
    [Tooltip("M2 tuning version applied by BuildWeb (0 = M1 asset). Bump only to re-apply defaults.")] public int m2TuningVersion;
    public SpawnEntry EntryFor(ObstacleKind k){ foreach (var e in entries) if (e.kind == k) return e; return null; }

    public SpawnEntry Pick(float difficulty){
      float total = 0; foreach (var e in entries) if (difficulty >= e.minDifficulty) total += e.weight;
      if (total <= 0) return null;
      float r = Random.value * total; SpawnEntry last = null;
      foreach (var e in entries){ if (difficulty < e.minDifficulty) continue; last = e; r -= e.weight; if (r <= 0) return e; }
      return last;   // float rounding fallback: last *eligible* entry
    }
    public static SpawnTable CreateDefault(){
      var t = CreateInstance<SpawnTable>();
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.Kid, weight = 3 });
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.Car, weight = 2 });
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.Branch, weight = 2 });
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.Bat, weight = 2.5f, minY = 3.5f, maxY = 7.5f });
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.Ghost, weight = 1.5f, minDifficulty = 0.15f, minY = 3f, maxY = 8f });
      t.entries.Add(new SpawnEntry{ kind = ObstacleKind.LampPost, weight = 1.5f });
      ApplyM2Curve(t);
      return t;
    }
    /// <summary>M2 tuning: ~2.5 min level. Difficulty eases in, has two breathers, and hits 1.0 at 85% of the level,
    /// so the last ~20 s before the boss run at full difficulty. Speed and spawn rate follow difficulty.</summary>
    public static void ApplyM2Curve(SpawnTable t){
      t.difficultyByProgress = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(0.25f, 0.18f), new Keyframe(0.40f, 0.36f),
        new Keyframe(0.48f, 0.30f),            // breather
        new Keyframe(0.68f, 0.64f), new Keyframe(0.76f, 0.58f), // breather
        new Keyframe(1f, 1f, 2.2f, 0f));
      t.speedByDifficulty = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(0.5f, 1.22f), new Keyframe(1, 1.6f));
      t.intervalByDifficulty = new AnimationCurve(new Keyframe(0, 1.9f), new Keyframe(0.5f, 1.25f), new Keyframe(1, 0.9f));
      t.bossDistance = 1150f; t.rampDistance = t.bossDistance * 0.85f; t.m2TuningVersion = 1;
    }
    /// <summary>FF2 free-flight tuning (GameConfig.scrollSpeed base stays 6 m/s, which the boss uses unscaled):
    /// street scroll 7 -> 9.5 m/s cap (multiplier 7/6 -> 9.5/6), spawn interval 1.6 -> 0.75 s (singles; multi-item
    /// spawns are additionally kept >= 0.9 s apart by LevelPlanner). Same non-linear difficulty curve as M2.</summary>
    public const float FF2ScrollMin = 7f, FF2ScrollMax = 9.5f, FF2BaseScroll = 6f;
    public static void ApplyFF2(SpawnTable t){
      ApplyM2Curve(t);
      t.speedByDifficulty = new AnimationCurve(new Keyframe(0, FF2ScrollMin / FF2BaseScroll), new Keyframe(0.5f, 8.1f / FF2BaseScroll), new Keyframe(1, FF2ScrollMax / FF2BaseScroll));
      t.intervalByDifficulty = new AnimationCurve(new Keyframe(0, 1.6f), new Keyframe(0.5f, 1.05f), new Keyframe(1, 0.75f));
      t.m2TuningVersion = 2;
    }
  }
  /// <summary>Difficulty hook: 0..1. Override via Spawner.difficultyOverride for testing or a custom curve.</summary>
  public interface IDifficultySource { float Difficulty { get; } }
}
