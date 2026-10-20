#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  /// <summary>Dev-build-only QA harness (compiled out of release): P toggles a hover autopilot, K (in Werewolf) deals
  /// boss damage, and key events are logged with a [QA] prefix for headless smoke tests.</summary>
  public class DevQA : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){ DontDestroyOnLoad(new GameObject("DevQA").AddComponent<DevQA>().gameObject); }
    bool auto; float minFireballDist = 999f; readonly System.Collections.Generic.HashSet<int> seen = new();
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
    void Update(){
      var gm = GameManager.I; if (!gm) return;
      if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame){ auto = !auto; Debug.Log($"[QA] autopilot {auto}"); }
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)){
        if (!seen.Add(f.GetInstanceID())) continue;
        minFireballDist = Mathf.Min(minFireballDist, Vector2.Distance(f.transform.position, gm.player.transform.position));
      }
      // hover at 4.5 m, or at the werewolf's chest height during the boss so QA spells can connect (steers via FlightInput)
      float target = gm.State == GameState.Boss && gm.boss && gm.boss.gameObject.activeSelf ? Mathf.Clamp(gm.boss.transform.position.y - 0.3f, 1.5f, 8.5f) : 4.5f;
      if (auto && gm.IsActive) FlightInput.DebugOverride = new Vector2(0, Mathf.Clamp((target - gm.player.transform.position.y) * 1.5f, -1f, 1f));
      else FlightInput.DebugOverride = null;
    }
  }
}
#endif
