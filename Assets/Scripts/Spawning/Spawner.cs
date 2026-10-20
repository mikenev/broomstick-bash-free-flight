using System.Collections.Generic;
using UnityEngine;
namespace BroomstickBash {
  /// <summary>Spawns planned columns (LevelPlanner multi-spawn patterns, fairness-checked) ahead of the camera and despawns
  /// them behind it. Difficulty follows SpawnTable.difficultyByProgress over Distance / rampDistance (non-linear).</summary>
  public class Spawner : MonoBehaviour, IDifficultySource {
    public SpawnTable table;
    public Transform cam, player;
    public float spawnAhead => LevelPlanner.SpawnAhead;   // single source of truth shared with the fairness validator
    public float despawnBehind = 14f;
    public System.Func<float> difficultyOverride;   // hook: custom curve / QA
    public bool spawning = true;
    readonly List<ObstacleBase> live = new();
    float timer, sinceSpawn; LevelPlanner planner;
    public PlannedColumn LastColumn { get; private set; }
    public static float DifficultyAt(SpawnTable t, float distance) =>
      Mathf.Clamp01(t.difficultyByProgress.Evaluate(Mathf.Clamp01(distance / Mathf.Max(1, t.rampDistance))));
    public float Difficulty => difficultyOverride != null ? difficultyOverride() : DifficultyAt(table, GameManager.I ? GameManager.I.Distance : 0);
    public float SpeedMultiplier => table.speedByDifficulty.Evaluate(Difficulty);
    LevelPlanner Planner => planner ??= new LevelPlanner(new System.Random(), GameConfig.I.groundY, GameConfig.I.ceilingY){ table = table };

    public void ResetSpawner(){ ClearAll(); timer = 1.5f; spawning = true; Planner.Reset(); sinceSpawn = 0; }
    public void ClearAll(bool puff = false){
      foreach (var o in live) if (o){ if (puff) Puff.Burst(o.transform.position, GameConfig.I.sparkleMat, 4); o.Despawn(); }
      live.Clear();
    }
    void Update(){
      var gm = GameManager.I; if (!gm || !gm.IsActive) return;
      live.RemoveAll(o => !o);
      for (int i = live.Count - 1; i >= 0; i--) if (live[i].transform.position.x < cam.position.x - despawnBehind){ live[i].Despawn(); live.RemoveAt(i); }
      if (!spawning || gm.State != GameState.Playing) return;
      // stop spawning shortly before the boss so the arena is clear when it arrives
      if (gm.Distance > table.bossDistance - spawnAhead) return;
      timer -= Time.deltaTime; sinceSpawn += Time.deltaTime;
      if (timer <= 0){ SpawnColumn(); timer = Mathf.Max(LevelPlanner.MinSingleInterval, table.intervalByDifficulty.Evaluate(Difficulty) * (0.9f + 0.2f * (float)Planner.rng.NextDouble())); }
    }
    public void SpawnColumn(){
      var gm = GameManager.I; float d = Difficulty, v = gm.ScrollSpeed;
      var col = Planner.Next(d, v, sinceSpawn); LastColumn = col; sinceSpawn = 0;   // planner enforces multi-spawn spacing + reachability
      float x = cam.position.x + spawnAhead;
      foreach (var it in col.items){
        var o = ObstacleFactory.Create(it, x, GameConfig.I, table);
        o.OnSpawned(new SpawnContext{ difficulty = d, spawnX = x, y = o.transform.position.y, player = player });
        live.Add(o);
      }
    }
    /// <summary>M1 API kept: spawns one random table entry (unvalidated; debug only).</summary>
    public ObstacleBase SpawnOne(){
      var e = table.Pick(Difficulty); if (e == null) return null;
      float x = cam.position.x + spawnAhead;
      var o = ObstacleFactory.Create(e, x, GameConfig.I);
      o.OnSpawned(new SpawnContext{ difficulty = Difficulty, spawnX = x, y = o.transform.position.y, player = player });
      live.Add(o); return o;
    }
  }
}
