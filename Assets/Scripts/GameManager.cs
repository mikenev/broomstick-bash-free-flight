using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  public enum GameState { Ready, Playing, Boss, GameOver, Win }
  /// <summary>State machine, scoring, high score, boss trigger and IMGUI HUD.</summary>
  public class GameManager : MonoBehaviour {
    public static GameManager I;
    const string HighKey = "BroomstickBash.HighScore";
    public PlayerController player; public Spawner spawner; public Werewolf boss; public Transform cam;
    public GameState State { get; private set; } = GameState.Ready;
    public bool IsActive => State == GameState.Playing || State == GameState.Boss;
    public float Distance { get; private set; }
    public int Kills { get; private set; }
    public int Score => Mathf.FloorToInt(Distance) + KillScore;
    public int KillScore { get; private set; }
    public int HighScore { get; private set; }
    public float ScrollSpeed => GameConfig.I.scrollSpeed * (State == GameState.Boss ? 1f : spawner.SpeedMultiplier);
    bool newHigh; float endTime;

    void Awake(){ I = this; Layers.Validate(); HighScore = PlayerPrefs.GetInt(HighKey, 0); Application.targetFrameRate = 60; }
    void OnEnable(){ GameEvents.EnemyKilled += OnKill; }
    void OnDisable(){ GameEvents.EnemyKilled -= OnKill; }
    void Start(){ ResetRun(); }
    void OnKill(ObstacleBase o){ Kills++; KillScore += o.scoreValue; }

    public void ResetRun(){
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)) Destroy(f.gameObject);
      foreach (var s in FindObjectsByType<Spell>(FindObjectsSortMode.None)) Destroy(s.gameObject);
      foreach (var pf in FindObjectsByType<Puff>(FindObjectsSortMode.None)) Destroy(pf.gameObject); // playtest: clear puffs on reset
      player.ResetPlayer(); spawner.ResetSpawner(); boss.gameObject.SetActive(false);
      Distance = 0; Kills = 0; KillScore = 0; newHigh = false; State = GameState.Ready;
      GameEvents.RunReset?.Invoke();
    }
    public void StartRun(){ if (State == GameState.Ready){ State = GameState.Playing; player.StartGrace(); } }
    public void StartBoss(){
      if (State == GameState.Ready) StartRun();
      if (State != GameState.Playing) return;
      State = GameState.Boss; spawner.spawning = false; spawner.ClearAll(true);
      ClearHazards();                                   // B mid-run: nothing left that can touch the witch
      player.GrantInvulnerability(boss.entryGrace);     // short grace on boss entry
      boss.Begin();
    }
    void ClearHazards(){
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)){ Puff.Burst(f.transform.position, GameConfig.I.sparkleMat, 3); Destroy(f.gameObject); }
      foreach (var o in FindObjectsByType<ObstacleBase>(FindObjectsSortMode.None)) o.Despawn();   // stragglers not tracked by the spawner
    }
    public void GameOver(){ if (!IsActive) return; State = GameState.GameOver; End(); GameEvents.GameOver?.Invoke(); GameEvents.RunEnded?.Invoke(false, Score); }
    public void Win(){ if (State != GameState.Boss) return; KillScore += 500; State = GameState.Win; End(); GameEvents.Won?.Invoke(Score); GameEvents.RunEnded?.Invoke(true, Score); }
    /// <summary>Playtest fix: restarting from game over / win (any key/click/tap or R) goes straight into a new run.</summary>
    void Restart(){ ResetRun(); State = GameState.Playing; player.StartGrace(); }
    void End(){
      endTime = Time.unscaledTime;
      if (Score > HighScore){ HighScore = Score; newHigh = true; PlayerPrefs.SetInt(HighKey, Score); PlayerPrefs.Save(); }
    }
    void Update(){
      var kb = Keyboard.current;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
      if (kb != null && kb.bKey.wasPressedThisFrame) StartBoss();          // dev/QA only: jump to boss
#endif
      bool ended = State == GameState.GameOver || State == GameState.Win;
      if (kb != null && kb.rKey.wasPressedThisFrame && State != GameState.Ready){ if (ended){ if (Time.unscaledTime - endTime > 0.6f) Restart(); } else ResetRun(); return; }
      if (State == GameState.Ready){ if (FlightInput.StartPressed()) StartRun(); return; }
      if (ended){
        if (Time.unscaledTime - endTime > 0.6f && FlightInput.StartPressed()) Restart();
        return;
      }
      float dt = Time.deltaTime, v = ScrollSpeed;
      player.Tick(dt, v);
      if (State == GameState.Playing){
        Distance += v * dt;
        if (Distance >= spawner.table.bossDistance) StartBoss();
      }
    }

    GUIStyle big, small; Texture2D white;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    const string DebugHint = "B: skip to boss (dev build)     ";
#else
    const string DebugHint = "";
#endif
    static bool Touch => TouchControls.I ? TouchControls.I.Visible : UnityEngine.InputSystem.Touchscreen.current != null;
    static string ControlHint => Touch
      ? "Drag on the left half to steer (the stick appears under your thumb)     Tap CAST (bottom right) to cast a spell"
      : "Arrows / WASD: fly (8 directions)     X / F / J / Enter / left-click: cast spell";
    void OnGUI(){
      float s = Screen.height / 720f;
      if (!white){ white = Texture2D.whiteTexture; }
      big ??= new GUIStyle(GUI.skin.label){ alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
      small ??= new GUIStyle(GUI.skin.label){ fontStyle = FontStyle.Bold };
      big.fontSize = Mathf.RoundToInt(46 * s); small.fontSize = Mathf.RoundToInt(24 * s);
      // hearts
      for (int i = 0; i < player.maxHearts; i++){
        GUI.color = i < player.Hearts ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.25f, 0.25f, 0.25f, 0.7f);
        GUI.DrawTexture(new Rect((20 + i * 42) * s, 18 * s, 32 * s, 32 * s), white);
      }
      GUI.color = Color.white;
      GUI.Label(new Rect(20*s, 58*s, 700*s, 34*s), $"Score: {Score}   ({Mathf.FloorToInt(Distance)} m + {Kills} spooks)", small);
      GUI.Label(new Rect(20*s, 88*s, 700*s, 34*s), $"Best: {HighScore}", small);
      // spell cooldown meter
      float frac = player.castCooldown > 0 ? 1f - Mathf.Clamp01(player.CooldownLeft / player.castCooldown) : 1f;
      GUI.Label(new Rect(20*s, 118*s, 120*s, 34*s), "Spell", small);
      bool ready = frac >= 1f;
      var bar = new Rect(95*s, 124*s, 180*s, 20*s);
      if (!ready){ // drained: pulsing red outline + red track so it reads at a glance
        GUI.color = Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(1f, 0.6f, 0.2f), Mathf.PingPong(Time.unscaledTime * 6f, 1f));
        GUI.DrawTexture(new Rect(bar.x - 3*s, bar.y - 3*s, bar.width + 6*s, bar.height + 6*s), white);
      }
      GUI.color = ready ? new Color(0, 0, 0, 0.6f) : new Color(0.35f, 0.05f, 0.05f, 0.95f); GUI.DrawTexture(bar, white);
      GUI.color = ready ? new Color(0.6f, 1f, 0.4f) : new Color(1f, 0.75f, 0.25f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * frac, bar.height), white);
      GUI.color = Color.white;
      GUI.Label(new Rect(285*s, 118*s, 420*s, 34*s), ready ? "READY (X / F / right-click)" : "RECHARGING...", small);
      if (State == GameState.Boss){
        float w = 400 * s, x = (Screen.width - w) / 2;
        GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(x, 20*s, w, 22*s), white);
        GUI.color = boss.Phase == 2 ? new Color(1f, 0.2f, 0.15f) : boss.Phase == 1 ? new Color(1f, 0.55f, 0.1f) : new Color(0.95f, 0.8f, 0.2f);
        GUI.DrawTexture(new Rect(x, 20*s, w * Mathf.Max(0, boss.Hp) / boss.maxHp, 22*s), white);
        GUI.color = Color.white; GUI.Label(new Rect(x, 44*s, w, 30*s), $"THE WEREWOLF   {Mathf.Max(0, boss.Hp)}/{boss.maxHp}", small);
      }
      var full = new Rect(0, 0, Screen.width, Screen.height);
      int sz = Mathf.RoundToInt(24 * s);
      if (State == GameState.Ready)
        GUI.Label(full, $"BROOMSTICK BASH: FREE FLIGHT\n<size={sz}>{ControlHint}\nSpells zap bats & ghosts, but sparkle harmlessly past trick-or-treaters. Don't touch anything!\n{DebugHint}{(Touch ? "" : "R: restart")}\n\n{(Touch ? "Tap" : "Press any key or click")} to start</size>", big);
      else if (State == GameState.GameOver)
        GUI.Label(full, $"GAME OVER\n<size={sz + 6}>Score {Score}{(newHigh ? "  NEW BEST!" : "")}\n{(Touch ? "Tap" : "Any key, click or R")} to restart</size>", big);
      else if (State == GameState.Win)
        GUI.Label(full, $"YOU WIN!\n<size={sz + 6}>The Werewolf is vanquished!\nScore {Score}{(newHigh ? "  NEW BEST!" : "")}\n{(Touch ? "Tap" : "Any key, click or R")} to play again</size>", big);
    }
  }
}
