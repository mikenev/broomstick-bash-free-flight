using System.Collections.Generic;
using UnityEngine;
using BroomstickBash.Art;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif
namespace BroomstickBash {
  /// <summary>Werewolf boss: hovers/bobs at the right of the screen, throws telegraphed fireball patterns that ramp
  /// through 3 phases as HP drops (FF3: Fan / Wall / Burst / Beam / Sweep, see FireballPatterns + BossTuning). Every
  /// pattern is checked against the free-flight model (FairnessValidator, MovementConfig) together with the fireballs
  /// still in the air before it is committed. Planning works in the scroll-anchor frame (x relative to the witch's TrackX).
  /// Art swap point: assign <see cref="visualPrefab"/> (Art Dev's werewolf) and the gray-box child "GrayBox" is hidden.</summary>
  public class Werewolf : MonoBehaviour {
    enum Mode { Entering, Idle, Telegraph, Defeated }
    [Header("Art")] public GameObject visualPrefab; public Transform grayBox;
    public Vector3 artOffset = new Vector3(-0.2f, -1.75f, 0); public float artScale = 1.25f;
    GameObject art; Animator anim; PhaseTuning committed;
    [Header("Refs")] public Transform cam, player;
    [Header("Stats")] public int maxHp = 12;
    public float entryGrace = 2.5f;               // witch invulnerable + no attacks after boss entry
    public float minSpawnDistance = 6f;           // fireballs never spawn closer than this to the witch
    public float phase2Hp = 8, phase3Hp = 4;      // HP thresholds (<=)
    [System.NonSerialized] public PhaseTuning[] phases = BossTuning.DefaultPhases();   // code is the source of truth
    public int Hp { get; private set; }
    public int Phase => Hp > phase2Hp ? 0 : Hp > phase3Hp ? 1 : 2;
    public bool Defeated => mode == Mode.Defeated;
    public static int RejectedPatterns, FiredPatterns;   // telemetry for QA

    Mode mode; float t, timer, flash, defeatT; bool poofed; AttackKind pending; List<Shot> planned; int attackIdx;
    readonly List<GameObject> tells = new(); Renderer[] rends; MaterialPropertyBlock mpb; System.Random rng = new();
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    PlayerController pc;

    public static PhaseTuning[] DefaultPhases() => BossTuning.DefaultPhases();
    /// <summary>Dev autopilot: the committed attack and seconds until it fires (null when not telegraphing).</summary>
    public List<Shot> PlannedShots => mode == Mode.Telegraph ? planned : null;
    public float TelegraphLeft => mode == Mode.Telegraph ? Mathf.Max(0f, timer) : 0f;
    public float ChestY => transform.position.y + 0.2f;

    void Awake(){
      mpb = new MaterialPropertyBlock();
      if (!visualPrefab) visualPrefab = BroomArt.Load("Werewolf");   // fallback: Art Dev's Resources prefab
      if (visualPrefab){
        art = Instantiate(visualPrefab, transform, false); art.name = "Art_Werewolf";
        foreach (var c in art.GetComponentsInChildren<Collider>()) Destroy(c);
        // art faces +X with its front to the camera; mirror on X so it faces the witch (-X) and still shows its front
        art.transform.localPosition = artOffset; art.transform.localScale = new Vector3(-artScale, artScale, artScale);
        if (grayBox) grayBox.gameObject.SetActive(false);
        anim = art.GetComponentInChildren<Animator>();
      }
      rends = GetComponentsInChildren<Renderer>(false);
      pc = player ? player.GetComponent<PlayerController>() : null;
    }
    void OnDisable(){ ClearTells(); }

    public void Begin(){
      gameObject.SetActive(true);
      Hp = maxHp; t = 0; mode = Mode.Entering; timer = entryGrace; attackIdx = 0; flash = 0; defeatT = 0;
      transform.position = new Vector3(cam.position.x + 16f, 5f, 0); transform.rotation = Quaternion.identity; transform.localScale = Vector3.one;
      SetTint(null); foreach (var r in rends) if (r) r.enabled = true;
      if (anim){ anim.Rebind(); anim.Update(0f); }
      GameEvents.BossStarted?.Invoke();
    }

    float TargetX => cam.position.x + 7.5f;
    void Update(){
      var gm = GameManager.I; if (!gm || !gm.IsActive) return;
      float dt = Time.deltaTime; t += dt; if (immuneCue > 0) immuneCue -= dt;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) Damage(1);   // dev-only QA: auto-hit
#endif
      if (mode == Mode.Defeated){ DefeatTick(dt); return; }
      var p = transform.position;
      p.x = Mathf.Max(Mathf.MoveTowards(p.x, TargetX, (gm.ScrollSpeed + 6f) * dt), TargetX);
      float bobAmp = mode == Mode.Telegraph ? 0.3f : BossTuning.BobAmp(Phase);   // holds (mostly) still while winding up
      p.y = Mathf.Lerp(p.y, 5f + Mathf.Sin(t * 0.9f) * bobAmp, 3f * dt);
      transform.position = p;

      if (flash > 0){ flash -= dt; if (flash <= 0){ if (mode == Mode.Telegraph) SetTint(TellColor, TellBlend); else SetTint(null); } }
      timer -= dt;
      switch (mode){
        case Mode.Entering: if (timer <= 0){ mode = Mode.Idle; timer = 0.6f; } break;
        case Mode.Idle: if (timer <= 0) PlanAttack(); break;
        case Mode.Telegraph: TelegraphTick(); if (timer <= 0) Fire(); break;
      }
    }

    PhaseTuning Tuning => phases[Mathf.Clamp(Phase, 0, phases.Length - 1)];
    Color TellColor => pending switch {
      AttackKind.Wall => new Color(1f, 0.85f, 0.2f), AttackKind.Beam => new Color(1f, 0.15f, 0.1f), AttackKind.Sweep => new Color(0.4f, 0.8f, 1f),
      AttackKind.Burst => new Color(1f, 0.4f, 0.9f), _ => new Color(1f, 0.5f, 0.2f) };
    /// <summary>Hand (thrown shots spawn here), scroll-anchor frame.</summary>
    Vector2 Hand => new Vector2(transform.position.x - 1.6f - pc.TrackX, transform.position.y + 0.4f);

    void PlanAttack(){
      var tun = Tuning; var m = FlightModel.FromConfig();
      var order = tun.attacks; pending = order[attackIdx++ % order.Length];
      // runtime fairness check (free-flight model), including fireballs still in the air (see FairnessValidator.PlanFair)
      planned = FairnessValidator.PlanFair(ref pending, tun, Hand, pc.Offset, ChestY, LiveShots(), m, rng, minSpawnDistance, out int rej);
      RejectedPatterns += rej;
      if (planned == null){ timer = tun.rest; return; }   // nothing fair right now: skip a beat
      mode = Mode.Telegraph; timer = tun.telegraph; committed = tun;   // phase can't change a committed attack
      SetTint(TellColor, TellBlend);
      SpawnTells();
      GameEvents.BossTelegraph?.Invoke(pending);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Debug.Log($"[Werewolf] plan {pending} phase {Phase} shots={planned.Count} rejectedVariants={rej} witch={pc.Offset} chest={ChestY:0.0}");
#endif
    }
    /// <summary>Fireballs already in the air, in the scroll-anchor frame (x and velocity relative to TrackX, y absolute).</summary>
    List<Shot> LiveShots(){
      var live = new List<Shot>(); float scroll = GameManager.I.ScrollSpeed;
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)){
        var p = f.transform.position;
        live.Add(new Shot{ pos = new Vector2(p.x - pc.TrackX, p.y), vel = new Vector2(f.vel.x - scroll, f.vel.y), gravity = f.gravity, live = true });
      }
      return live;
    }

    // Tell: glowing "ghost" orbs where the fireballs will appear (one per distinct spawn point), growing through the wind-up;
    // streams (Beam / Sweep) also mark their whole lane across the screen. Boss tints + crouches.
    readonly List<Vector2> tellPos = new();
    void SpawnTells(){
      ClearTells(); var cfg = GameConfig.I;
      foreach (var s in planned){
        bool dup = false; foreach (var q in tellPos) if ((q - s.pos).sqrMagnitude < 0.09f){ dup = true; break; }
        if (!dup) tellPos.Add(s.pos);
      }
      if (pending == AttackKind.Beam || pending == AttackKind.Sweep){
        var rows = new List<float>();
        foreach (var s in planned) if (Mathf.Abs(s.vel.y) < 0.01f && s.pos.x > 10f && !rows.Exists(r => Mathf.Abs(r - s.pos.y) < 0.2f)) rows.Add(s.pos.y);
        foreach (var y in rows) for (float x = 9.5f; x > FairnessValidator.XMin - 2.5f; x -= 1.3f) tellPos.Add(new Vector2(x, y));
      }
      foreach (var _ in tellPos){
        var go = cfg.MakeBlock("Tell", cfg.sphere, cfg.sparkleMat, Vector3.zero, Vector3.one * 0.15f);
        go.transform.SetParent(transform.parent, true);
        tells.Add(go);
      }
    }
    void TelegraphTick(){
      float k = 1f - Mathf.Clamp01(timer / Mathf.Max(0.01f, committed.telegraph));
      transform.localScale = new Vector3(1f + 0.08f * k, 1f - 0.12f * k, 1f);
      // blink faster as it gets closer to firing
      bool on = Mathf.Repeat(t * (4f + 10f * k), 1f) < 0.6f;
      for (int i = 0; i < tells.Count && i < tellPos.Count; i++){
        if (!tells[i]) continue;
        // planned in the scroll-anchor frame, so the tells move with the screen and show exactly where shots start
        var q = tellPos[i]; tells[i].transform.position = new Vector3(pc.TrackX + q.x, Mathf.Clamp(q.y, -1, 11), -0.6f);
        tells[i].transform.localScale = Vector3.one * Mathf.Lerp(0.15f, q.x < 10f ? 0.3f : 0.55f, k);
        tells[i].SetActive(on || k > 0.85f);
      }
    }
    void ClearTells(){ foreach (var g in tells) if (g) Destroy(g); tells.Clear(); tellPos.Clear(); }

    void Fire(){
      foreach (var s in planned){
        var at = new Vector3(pc.TrackX + s.pos.x, s.pos.y, 0);
        if (Vector2.Distance(at, player.position) < minSpawnDistance) continue;   // hard guarantee, even if the witch moved
        if (s.delay > 0){ StartCoroutine(Delayed(s, at)); continue; }
        Spawn(s, at);
      }
      ClearTells(); SetTint(null); transform.localScale = Vector3.one;
      if (art) BroomArt.Trigger(art, "Throw");
      mode = Mode.Idle; timer = committed.rest; FiredPatterns++;
      GameEvents.BossFired?.Invoke();
    }
    System.Collections.IEnumerator Delayed(Shot s, Vector3 at){
      float d = s.delay; while (d > 0){ if (GameManager.I.IsActive) d -= Time.deltaTime; yield return null; }
      if (mode == Mode.Defeated) yield break;
      at = new Vector3(pc.TrackX + s.pos.x, s.pos.y, 0);
      if (Vector2.Distance(at, player.position) >= minSpawnDistance) Spawn(s, at);
    }
    void Spawn(Shot s, Vector3 at){
      var cfg = GameConfig.I;
      var go = cfg.MakeBlock("Fireball", cfg.sphere, cfg.fireMat, at, Vector3.one * 0.6f);
      go.layer = Layers.Hazard;
      go.AddComponent<SphereCollider>().isTrigger = true;
      var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
      var f = go.AddComponent<Fireball>(); f.vel = (Vector3)s.vel + Vector3.right * GameManager.I.ScrollSpeed; f.gravity = s.gravity;
    }

    public void Damage(int n){
      if (mode == Mode.Entering){ Fizzle(); return; }       // entry/grace: immune, but show it
      if (Hp <= 0 || mode == Mode.Defeated) return;
      int before = Phase;
      Hp -= n; flash = 0.1f; SetTint(Color.white, 0.8f);
      Puff.Burst(transform.position + Vector3.left, GameConfig.I.sparkleMat, 6);
      if (art && Hp > 0) BroomArt.Trigger(art, "Hurt");
      GameEvents.BossHurt?.Invoke(Hp);
      if (Hp <= 0){ StartDefeat(); return; }
      if (Phase != before){ Puff.Burst(transform.position, GameConfig.I.fireMat, 14, 0.25f, 6f); GameEvents.BossPhaseChanged?.Invoke(Phase); }
    }

    // Defeat: stagger/shake 1.1 s → topple and fall 0.9 s → poof → 1 s pause → win screen.
    void StartDefeat(){
      mode = Mode.Defeated; defeatT = 0; poofed = false; ClearTells(); StopAllCoroutines(); SetTint(Color.white, 0.8f);
      foreach (var f in FindObjectsByType<Fireball>(FindObjectsSortMode.None)) Destroy(f.gameObject);
      if (pc) pc.GrantInvulnerability(5f);
      if (art) BroomArt.Trigger(art, "Defeat");
      GameEvents.BossDefeated?.Invoke();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Debug.Log("[Werewolf] DEFEATED");
#endif
    }
    void DefeatTick(float dt){
      defeatT += dt; var p = transform.position;
      p.x = Mathf.Max(p.x, TargetX);
      if (defeatT < 1.1f){
        transform.position = new Vector3(TargetX + Mathf.Sin(defeatT * 60f) * 0.15f, p.y, 0);
        transform.rotation = Quaternion.Euler(0, 0, Mathf.Sin(defeatT * 25f) * 8f);
        if (Mathf.Repeat(defeatT * 8f, 1f) < 0.5f) SetTint(Color.white, 0.8f); else SetTint(null);
      } else if (defeatT < 2.0f){
        float k = (defeatT - 1.1f) / 0.9f;
        transform.position = new Vector3(TargetX, Mathf.Lerp(p.y, 0.8f, k), 0); transform.rotation = Quaternion.Euler(0, 0, -80f * k);
      } else if (!poofed){
        poofed = true; SetTint(null);
        Puff.Burst(transform.position, GameConfig.I.ghostMat, 30, 0.45f, 8f);
        foreach (var r in rends) if (r) r.enabled = false;
      } else if (defeatT > 3.0f){
        foreach (var r in rends) if (r) r.enabled = true;
        gameObject.SetActive(false); GameManager.I.Win();
      }
    }

    public const float TellBlend = 0.45f;
    /// <summary>Blends each material slot's own base colour toward <paramref name="c"/> by <paramref name="amount"/> (keeps the art
    /// readable instead of painting it flat). null clears the override.</summary>
    void SetTint(Color? c, float amount = 1f){
      for (int i = 0; i < rends.Length; i++){
        var r = rends[i]; if (!r) continue;
        var mats = r.sharedMaterials;
        for (int m = 0; m < mats.Length; m++){
          if (!c.HasValue){ mpb.Clear(); r.SetPropertyBlock(mpb, m); continue; }
          var baseCol = mats[m] && mats[m].HasProperty(BaseColor) ? mats[m].GetColor(BaseColor) : Color.gray;
          var t = Color.Lerp(baseCol, c.Value, amount); t.a = baseCol.a;
          mpb.Clear(); mpb.SetColor(BaseColor, t); r.SetPropertyBlock(mpb, m);
        }
      }
    }

    // Spell during entry grace: fizzle sparkle on the boss + a short floating "IMMUNE" cue.
    float immuneCue; const float ImmuneCueTime = 1.2f;
    void Fizzle(){
      Puff.Burst(transform.position + new Vector3(-1.2f, 0.4f, -0.5f), GameConfig.I.sparkleMat, 5, 0.1f, 2f);
      immuneCue = ImmuneCueTime; GameEvents.BossImmune?.Invoke();
    }
    GUIStyle cueStyle;
    void OnGUI(){
      if (immuneCue <= 0) return;   // decremented in Update (OnGUI runs several times per frame)
      var cam0 = Camera.main; if (!cam0) return;
      var sp = cam0.WorldToScreenPoint(transform.position + new Vector3(-1.8f, 0.8f + (ImmuneCueTime - immuneCue) * 0.6f, 0));
      if (sp.z < 0) return;
      float sc = Screen.height / 720f;
      cueStyle ??= new GUIStyle(GUI.skin.label){ alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
      cueStyle.fontSize = Mathf.RoundToInt(26 * sc);
      GUI.color = new Color(1f, 0.95f, 0.5f, Mathf.Clamp01(immuneCue / 0.4f));
      GUI.Label(new Rect(sp.x - 100 * sc, Screen.height - sp.y - 20 * sc, 200 * sc, 40 * sc), "IMMUNE", cueStyle);
      GUI.color = Color.white;
    }
  }
}
