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
    /// <summary>Restart from game over / win straight into a new run. FF3 (a): needs a DELIBERATE input (Space / R,
    /// or a tap that isn't on the CAST button) after RestartDelay, so mashing cast (X/F/J/Enter/click) can't skip the score screen.</summary>
    public const float RestartDelay = 1.2f;
    bool RestartReady => Time.unscaledTime - endTime > RestartDelay;
    static bool RestartPressed(){
      var kb = Keyboard.current;
      if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame)) return true;
      var ts = Touchscreen.current;
      return ts != null && ts.primaryTouch.press.wasPressedThisFrame && !(TouchControls.I && TouchControls.I.CastPressedThisFrame);
    }
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
      if (kb != null && kb.rKey.wasPressedThisFrame && State != GameState.Ready && !ended){ ResetRun(); return; }
      if (State == GameState.Ready){ if (FlightInput.StartPressed()) StartRun(); return; }
      if (ended){
        if (RestartReady && RestartPressed()) Restart();
        return;
      }
      float dt = Time.deltaTime, v = ScrollSpeed;
      player.Tick(dt, v);
      if (State == GameState.Playing){
        Distance += v * dt;
        if (Distance >= spawner.table.bossDistance) StartBoss();
      }
    }

    GUIStyle big, small, hud; Texture2D white;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
    const string DebugHint = "B: skip to boss (dev build)     ";
#else
    const string DebugHint = "";
#endif
    static bool Touch => TouchControls.I ? TouchControls.I.Visible : Application.isMobilePlatform;
    static string ControlHint => Touch
      ? "Drag on the left half to steer (the stick appears under your thumb)     Tap CAST (bottom right) to cast a spell"
      : "Arrows / WASD: fly (8 directions)     X / F / J / Enter / left-click: cast spell";
    string RestartHint => !RestartReady ? " " : Touch ? "Tap" : "Press Space or R";
    void OnGUI(){
      float s = Screen.height / 720f;
      if (!white){ white = Texture2D.whiteTexture; }
      big ??= new GUIStyle(GUI.skin.label){ alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
      small ??= new GUIStyle(GUI.skin.label){ fontStyle = FontStyle.Bold };
      big.fontSize = Mathf.RoundToInt(46 * s); small.fontSize = Mathf.RoundToInt(24 * s);
      // FF2 HUD: one compact, semi-transparent panel in the top-RIGHT corner. The witch can never be drawn there: she is
      // clamped to the left ScreenFractionX (45%) of the screen (MovementConfig), and the panel is kept right of 52% of the
      // width, so free flight up to TopY can no longer put her behind the hearts/score (it did in the old top-left HUD).
      float ps = Mathf.Min(s, Screen.width * 0.48f / 310f);            // shrink on narrow/portrait screens to stay right of 52%
      float pw = 300 * ps, px = Screen.width - pw - 10 * ps, py = 10 * ps;
      if (State == GameState.Boss && px < Screen.width / 2f + 205 * s) py = 80 * s; // keep clear of the centred boss bar
      hud ??= new GUIStyle(GUI.skin.label){ fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
      hud.fontSize = Mathf.RoundToInt(19 * ps);
      string line1 = $"Score {Score}   Best {HighScore}", line2 = $"{Mathf.FloorToInt(Distance)} m + {Kills} spooks";
      // never clip: shrink the font if the longer line would not fit the panel (big scores / narrow screens)
      float avail = pw - 20 * ps;
      while (hud.fontSize > 9 && Mathf.Max(hud.CalcSize(new GUIContent(line1)).x, hud.CalcSize(new GUIContent(line2)).x) > avail) hud.fontSize--;
      GUI.color = new Color(0, 0, 0, 0.45f); GUI.DrawTexture(new Rect(px, py, pw, 112 * ps), white);
      float ix = px + 10 * ps;
      for (int i = 0; i < player.maxHearts; i++){
        GUI.color = i < player.Hearts ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.25f, 0.25f, 0.25f, 0.7f);
        GUI.DrawTexture(new Rect(ix + i * 30 * ps, py + 8 * ps, 24 * ps, 24 * ps), white);
      }
      GUI.color = Color.white;
      GUI.Label(new Rect(ix, py + 34 * ps, avail, 24 * ps), line1, hud);
      GUI.Label(new Rect(ix, py + 56 * ps, avail, 24 * ps), line2, hud);
      // spell cooldown meter
      float frac = player.castCooldown > 0 ? 1f - Mathf.Clamp01(player.CooldownLeft / player.castCooldown) : 1f;
      bool ready = frac >= 1f;
      GUI.Label(new Rect(ix, py + 82 * ps, 60 * ps, 24 * ps), "Spell", hud);
      var bar = new Rect(ix + 58 * ps, py + 87 * ps, 110 * ps, 14 * ps);
      if (!ready){ // drained: pulsing red outline + red track so it reads at a glance
        GUI.color = Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(1f, 0.6f, 0.2f), Mathf.PingPong(Time.unscaledTime * 6f, 1f));
        GUI.DrawTexture(new Rect(bar.x - 2*ps, bar.y - 2*ps, bar.width + 4*ps, bar.height + 4*ps), white);
      }
      GUI.color = ready ? new Color(0, 0, 0, 0.6f) : new Color(0.35f, 0.05f, 0.05f, 0.95f); GUI.DrawTexture(bar, white);
      GUI.color = ready ? new Color(0.6f, 1f, 0.4f) : new Color(1f, 0.75f, 0.25f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * frac, bar.height), white);
      GUI.color = Color.white;
      GUI.Label(new Rect(bar.xMax + 8 * ps, py + 82 * ps, pw - (bar.xMax - px) - 12 * ps, 24 * ps), ready ? "READY" : "...", hud);
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
        GUI.Label(full, $"GAME OVER\n<size={sz + 6}>Score {Score}{(newHigh ? "  NEW BEST!" : "")}\n{RestartHint} to restart</size>", big);
      else if (State == GameState.Win)
        GUI.Label(full, $"YOU WIN!\n<size={sz + 6}>The Werewolf is vanquished!\nScore {Score}{(newHigh ? "  NEW BEST!" : "")}\n{RestartHint} to play again</size>", big);
    }
  }
}
