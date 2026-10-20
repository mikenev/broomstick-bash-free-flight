#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  /// <summary>Dev-build-only QA harness (compiled out of release): P toggles a hover autopilot, K (in Werewolf) deals
  /// boss damage, and key events are logged with a [QA] prefix for headless smoke tests.</summary>
  public class DevQA : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){ DontDestroyOnLoad(new GameObject("DevQA").AddComponent<DevQA>().gameObject); }
    bool auto, camp; float logT; GameState lastState = (GameState)(-1); float minFireballDist = 999f; readonly System.Collections.Generic.HashSet<int> seen = new();
    void OnEnable(){
      GameEvents.PlayerHit += h => Debug.Log($"[QA] hit hearts={h} t={Time.time:0.00}");
      GameEvents.BossStarted += () => { minFireballDist = 999f; Debug.Log($"[QA] boss start t={Time.time:0.00} hazards={FindObjectsByType<Fireball>(FindObjectsSortMode.None).Length} obstacles={FindObjectsByType<ObstacleBase>(FindObjectsSortMode.None).Length}"); };
      GameEvents.BossPhaseChanged += p => Debug.Log($"[QA] boss phase {p}");
      GameEvents.BossImmune += () => Debug.Log($"[QA] boss IMMUNE fizzle t={Time.time:0.00}");
      GameEvents.BossDefeated += () => Debug.Log("[QA] boss defeated");
      GameEvents.Won += _ => Debug.Log($"[QA] WIN minFireballSpawnDist={minFireballDist:0.00} fired={Werewolf.FiredPatterns} rejected={Werewolf.RejectedPatterns}");
      GameEvents.GameOver += () => Debug.Log($"[QA] GAME OVER minFireballSpawnDist={minFireballDist:0.00}");
    }
    void OnDisable(){ FlightInput.DebugOverride = null; }
    float pilotT; Vector2 pilotDir;
    void BossPilot(GameManager gm, Werewolf b){
      var p = gm.player;
      if ((pilotT -= Time.deltaTime) <= 0f){
        pilotT = 0.05f;
        var shots = b.LiveShots();   // in-air + not-yet-launched stream shots
        if (b.PlannedShots != null) shots.AddRange(b.PlannedShots);
        var prefer = new Vector2(0.5f, Mathf.Clamp(b.ChestY, MovementConfig.BottomY + 0.6f, MovementConfig.TopY - 0.3f));
        var mv = FairnessValidator.Steer(shots, FlightModel.FromConfig(), p.Offset, prefer, 1.5f, b.TelegraphLeft);
        pilotDir = mv.HasValue ? Vector2.ClampMagnitude(mv.Value / (MovementConfig.MaxSpeed * FairnessValidator.Step), 1f) : Vector2.ClampMagnitude(prefer - p.Offset, 1f);
      }
      FlightInput.DebugOverride = pilotDir;
      if (Mathf.Abs(p.transform.position.y - b.ChestY) < 1.5f && p.CooldownLeft <= 0f) p.CastSpell();
    }
    void Update(){
      var gm = GameManager.I; if (!gm) return;
      if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame){ auto = !auto; camp = false; Debug.Log($"[QA] autopilot {auto}"); }
      // C: "camper" - holds level with the werewolf's chest and casts nonstop without dodging (FF3 deterrent check)
      if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame){ camp = !camp; auto = false; Debug.Log($"[QA] camp {camp}"); }
      if (gm.State != lastState){ Debug.Log($"[QA] state {gm.State} t={Time.unscaledTime:0.00}"); lastState = gm.State; }
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)){
        if (!seen.Add(f.GetInstanceID())) continue;
        minFireballDist = Mathf.Min(minFireballDist, Vector2.Distance(f.transform.position, gm.player.transform.position));
      }
      if (gm.IsActive && (logT -= Time.unscaledDeltaTime) <= 0){   // movement telemetry for headless smoke tests
        logT = 0.1f; var p = gm.player; var hb = MovementConfig.HorizontalBounds(Camera.main);
        Debug.Log($"[QA] pos t={Time.time:0.00} off=({p.Offset.x:0.00},{p.Offset.y:0.00}) vel=({p.Velocity.x:0.00},{p.Velocity.y:0.00}) speed={p.Velocity.magnitude:0.00} bx=({hb.x:0.00},{hb.y:0.00}) spells={FindObjectsByType<Spell>(FindObjectsSortMode.None).Length} cool={p.CooldownLeft:0.00} hearts={p.Hearts} chest={(gm.State == GameState.Boss && gm.boss ? gm.boss.ChestY : 0f):0.00} bossHp={(gm.boss ? gm.boss.Hp : 0)}");
      }
      // P autopilot: hover at 4.5 m on the street; in the boss fight dodge with the fairness search (same model as the
      // gate, sees live + telegraphed fireballs) while preferring the werewolf's chest height, and cast when level with him
      if (camp && gm.IsActive && gm.State == GameState.Boss && gm.boss && gm.boss.gameObject.activeSelf){
        var p = gm.player; FlightInput.DebugOverride = new Vector2(Mathf.Clamp((1f - p.Offset.x) * 1.5f, -1f, 1f), Mathf.Clamp((gm.boss.ChestY - p.Offset.y) * 2f, -1f, 1f));
        if (p.CooldownLeft <= 0f) p.CastSpell();
      }
      else if (auto && gm.IsActive){
        var b = gm.boss;
        if (gm.State == GameState.Boss && b && b.gameObject.activeSelf && !b.Defeated) BossPilot(gm, b);
        else FlightInput.DebugOverride = new Vector2(0, Mathf.Clamp((4.5f - gm.player.transform.position.y) * 1.5f, -1f, 1f));
      }
      else FlightInput.DebugOverride = null;
    }
  }
}
#endif
