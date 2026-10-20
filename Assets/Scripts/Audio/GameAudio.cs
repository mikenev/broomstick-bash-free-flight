using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  /// <summary>Central audio manager (music + SFX), driven by GameEvents and GameManager state.
  /// Music: "Midnight Hop" (main_theme_loop) on the title and during the run, crossfades to "Moonfang" (boss_theme_loop)
  /// when the boss starts, and back to Midnight Hop after a win / game over / restart.
  /// WebGL autoplay: nothing plays until the first user input (key, mouse button or touch); then audio is unlocked and the
  /// current track starts. SFX are rate-limited per clip so frequent sounds (honk, casts) don't stack. Free Flight: no flap SFX.</summary>
  public class GameAudio : MonoBehaviour {
    public static GameAudio I;
    [Header("Music")] public AudioClip mainTheme, bossTheme, titleSting, gameOverSting, victorySting;
    [Header("SFX")] public AudioClip cast, spellHit, sparkle, hurt, honk, bat, ghost, enemyPop, fireballImpact, uiClick;
    [Header("Boss SFX")] public AudioClip roar, bossHurt, fireballThrow, bossDefeat;
    [Header("Mix")] [Range(0, 1)] public float musicVolume = 0.35f, bossMusicVolume = 0.42f, sfxVolume = 0.85f;
    public float crossfade = 1.2f;

    public bool Unlocked { get; private set; }
    public string CurrentTrack => target ? target.name : "none";

    AudioSource musA, musB, sfx, active; AudioClip target; float duck = 1f, duckUntil;
    readonly Dictionary<string, float> lastPlay = new();
    GameState lastState = (GameState)(-1);
    GameManager gm;

    struct Rule { public AudioClip clip; public float vol, minGap; }
    /// <summary>Logical sound id -> clip, volume, minimum gap (s). Keyed by id so two sounds can share a clip.</summary>
    Dictionary<string, Rule> rules;

    void Awake(){
      I = this;
      musA = NewSource(true); musB = NewSource(true); sfx = NewSource(false); sfx.volume = 1f; active = musA;   // one-shots scale by PlayOneShot volume; source must be at 1
      rules = new Dictionary<string, Rule>();
      void R(string id, AudioClip c, float v, float g){ rules[id] = new Rule{ clip = c, vol = v, minGap = g }; }
      R("cast", cast, 0.45f, 0.10f); R("spellHit", spellHit, 0.50f, 0.06f); R("sparkle", sparkle, 0.45f, 0.20f);
      R("immune", sparkle, 0.30f, 0.25f); R("hurt", hurt, 0.90f, 0.30f); R("honk", honk, 0.40f, 2.50f); R("bat", bat, 0.35f, 1.50f);
      R("ghost", ghost, 0.40f, 2.50f); R("pop", enemyPop, 0.55f, 0.06f); R("blocked", fireballImpact, 0.33f, 0.10f);
      R("zap", fireballImpact, 0.55f, 0.10f); R("ui", uiClick, 0.35f, 0.15f);
      R("roar", roar, 0.90f, 1.0f); R("phaseRoar", roar, 0.72f, 1.0f); R("bossHurt", bossHurt, 0.55f, 0.12f); R("throw", fireballThrow, 0.40f, 0.25f);
      R("bossDefeat", bossDefeat, 0.90f, 1f); R("title", titleSting, 0.60f, 1f); R("gameOver", gameOverSting, 0.75f, 1f); R("victory", victorySting, 0.80f, 1f);
    }
    AudioSource NewSource(bool loop){ var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.loop = loop; s.volume = 0; return s; }

    void OnEnable(){
      GameEvents.PlayerHit += OnPlayerHit; GameEvents.EnemyKilled += OnKilled; GameEvents.ObstacleSpawned += OnSpawned;
      GameEvents.SpellCast += OnCast; GameEvents.SpellHit += OnSpellHit; GameEvents.SpellSparkle += OnSparkle;
      GameEvents.SpellBlocked += OnBlocked; GameEvents.FireballZapped += OnZapped; GameEvents.BossImmune += OnImmune;
      GameEvents.BossStarted += OnBossStart; GameEvents.BossPhaseChanged += OnPhase; GameEvents.BossHurt += OnBossHurt;
      GameEvents.BossFired += OnBossFired; GameEvents.BossDefeated += OnBossDefeated;
      GameEvents.GameOver += OnGameOver; GameEvents.Won += OnWon; GameEvents.RunReset += OnReset;
    }
    void OnDisable(){
      GameEvents.PlayerHit -= OnPlayerHit; GameEvents.EnemyKilled -= OnKilled; GameEvents.ObstacleSpawned -= OnSpawned;
      GameEvents.SpellCast -= OnCast; GameEvents.SpellHit -= OnSpellHit; GameEvents.SpellSparkle -= OnSparkle;
      GameEvents.SpellBlocked -= OnBlocked; GameEvents.FireballZapped -= OnZapped; GameEvents.BossImmune -= OnImmune;
      GameEvents.BossStarted -= OnBossStart; GameEvents.BossPhaseChanged -= OnPhase; GameEvents.BossHurt -= OnBossHurt;
      GameEvents.BossFired -= OnBossFired; GameEvents.BossDefeated -= OnBossDefeated;
      GameEvents.GameOver -= OnGameOver; GameEvents.Won -= OnWon; GameEvents.RunReset -= OnReset;
    }

    void Start(){ gm = GameManager.I; Music(mainTheme); }

    // ---------- autoplay unlock ----------
    static bool AnyInput(){
      var kb = Keyboard.current; if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
      var m = Mouse.current; if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) return true;
      var t = Touchscreen.current; return t != null && t.primaryTouch.press.wasPressedThisFrame;
    }
    void Unlock(){
      if (Unlocked) return; Unlocked = true;
      Debug.Log("[Audio] unlocked on first input; music=" + CurrentTrack);
      if (target){ active.clip = target; active.volume = 0; active.Play(); }
      One("title");
    }

    void Update(){
      if (!Unlocked){ if (AnyInput()) Unlock(); else return; }
      if (!gm) gm = GameManager.I;
      if (gm){
        if (gm.State != lastState){
          if (gm.State == GameState.Playing && (lastState == GameState.Ready || (int)lastState == -1)) One("ui");
          lastState = gm.State;
        }
      }
      // music crossfade toward `target`
      if (Time.unscaledTime > duckUntil) duck = Mathf.MoveTowards(duck, 1f, Time.unscaledDeltaTime * 1.5f);
      float want = (target == bossTheme ? bossMusicVolume : musicVolume) * duck;
      float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, crossfade);
      var other = active == musA ? musB : musA;
      active.volume = Mathf.MoveTowards(active.volume, active.clip == target && target ? want : 0f, step * Mathf.Max(want, 0.3f) * 2f);
      other.volume = Mathf.MoveTowards(other.volume, 0f, step * 0.6f);
      if (other.isPlaying && other.volume <= 0.001f) other.Stop();
    }

    /// <summary>Switch music with a crossfade. Same track = no-op (keeps playing, no restart).</summary>
    public void Music(AudioClip clip){
      if (target == clip && (active.isPlaying || !Unlocked)) return;
      target = clip;
      if (!Unlocked || !clip) return;
      if (active.clip == clip && active.isPlaying) return;
      var other = active == musA ? musB : musA;
      active = other; active.clip = clip; active.volume = 0; active.Play();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
      Debug.Log("[Audio] music -> " + clip.name);
#endif
    }
    void Duck(float level, float seconds){ duck = Mathf.Min(duck, level); duckUntil = Time.unscaledTime + seconds; }

    /// <summary>Rate-limited one-shot by sound id. Returns false if suppressed (locked, missing clip or too soon).</summary>
    public bool One(string id){
      if (!Unlocked || !rules.TryGetValue(id, out var r) || !r.clip) return false;
      if (lastPlay.TryGetValue(id, out var t) && Time.unscaledTime - t < r.minGap) return false;
      lastPlay[id] = Time.unscaledTime;
      sfx.PlayOneShot(r.clip, r.vol * sfxVolume);
      return true;
    }

    // ---------- event handlers ----------
    void OnPlayerHit(int hearts){ One("hurt"); }
    void OnKilled(ObstacleBase o){ One("pop"); }
    void OnSpawned(ObstacleBase o){
      if (!o) return;
      switch (o.kind){ case ObstacleKind.Car: One("honk"); break; case ObstacleKind.Bat: One("bat"); break; case ObstacleKind.Ghost: One("ghost"); break; }
    }
    void OnCast(){ One("cast"); }
    void OnSpellHit(){ One("spellHit"); }
    void OnSparkle(){ One("sparkle"); }
    void OnBlocked(){ One("blocked"); }
    void OnZapped(){ One("zap"); }
    void OnImmune(){ One("immune"); }   // soft fizzle: the spell connected but the werewolf is still in its entry grace
    void OnBossStart(){ One("roar"); Music(bossTheme); }
    void OnPhase(int p){ One("phaseRoar"); }
    void OnBossHurt(int hp){ if (hp > 0) One("bossHurt"); }
    void OnBossFired(){ One("throw"); }
    void OnBossDefeated(){ One("bossDefeat"); Music(mainTheme); Duck(0.25f, 4f); }
    void OnGameOver(){ One("gameOver"); Music(mainTheme); Duck(0.3f, 3f); }
    void OnWon(int score){ One("victory"); Duck(0.25f, 4f); }
    void OnReset(){ Music(mainTheme); }
  }
}
