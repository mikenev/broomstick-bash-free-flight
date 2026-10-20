using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BroomstickBash;

/// <summary>Headless scene setup + WebGL build. Usage:
/// Unity -batchmode -nographics -projectPath . -buildTarget WebGL -executeMethod BuildWeb.Build -logFile build.log
/// (no -quit; this calls EditorApplication.Exit). Menu: Broomstick Bash/...</summary>
public static class BuildWeb {
  const string ScenePath = "Assets/Scenes/Main.unity";
  const string Gen = "Assets/Generated";
  const string TablePath = "Assets/Data/DefaultSpawnTable.asset";

  static Mesh Prim(PrimitiveType t){ var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(g); return m; }

  [MenuItem("Broomstick Bash/Rebuild Main Scene")]
  public static void SetupScene(){
    Directory.CreateDirectory(Gen); Directory.CreateDirectory("Assets/Scenes"); Directory.CreateDirectory("Assets/Data");
    EnsureURP();
    var table = AssetDatabase.LoadAssetAtPath<SpawnTable>(TablePath);
    if (!table){ table = SpawnTable.CreateDefault(); AssetDatabase.CreateAsset(table, TablePath); AssetDatabase.SaveAssets(); }
    EnsureM2Table(table);

    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.32f, 0.27f, 0.45f);
    RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.16f, 0.1f, 0.26f); RenderSettings.fogMode = FogMode.Linear;
    RenderSettings.fogStartDistance = 25; RenderSettings.fogEndDistance = 70;

    var cfg = new GameObject("GameConfig").AddComponent<GameConfig>();
    cfg.cube = Prim(PrimitiveType.Cube); cfg.sphere = Prim(PrimitiveType.Sphere); cfg.cylinder = Prim(PrimitiveType.Cylinder); cfg.capsule = Prim(PrimitiveType.Capsule);
    cfg.kidMat = Mat("Kid", new Color(0.3f, 0.75f, 0.35f));
    cfg.carMat = Mat("Car", new Color(0.35f, 0.45f, 0.65f));
    cfg.branchMat = Mat("Branch", new Color(0.3f, 0.2f, 0.12f));
    cfg.batMat = Mat("Bat", new Color(0.12f, 0.1f, 0.14f));
    cfg.ghostMat = Mat("Ghost", new Color(0.9f, 0.92f, 1f), new Color(0.25f, 0.25f, 0.3f));
    cfg.bossMat = Mat("WolfFur", new Color(0.42f, 0.36f, 0.3f));
    cfg.fireMat = Mat("Fire", new Color(1f, 0.45f, 0.05f), new Color(1.5f, 0.5f, 0f));
    cfg.spellMat = Mat("Spell", new Color(0.6f, 1f, 0.4f), new Color(0.6f, 1.5f, 0.3f));
    cfg.sparkleMat = Mat("Sparkle", new Color(1f, 0.95f, 0.5f), new Color(1.5f, 1.3f, 0.4f));
    cfg.houseMat = Mat("House", new Color(0.25f, 0.22f, 0.3f));
    cfg.groundMat = Mat("Street", new Color(0.2f, 0.19f, 0.22f));
    var witchMat = Mat("Witch", new Color(0.45f, 0.2f, 0.6f));
    var skinMat = Mat("Skin", new Color(0.55f, 0.8f, 0.5f));
    var windowMat = Mat("Window", new Color(1f, 0.8f, 0.3f), new Color(1.2f, 0.8f, 0.2f));

    var moon = new GameObject("Moonlight").AddComponent<Light>(); moon.type = LightType.Directional; moon.color = new Color(0.75f, 0.75f, 1f);
    moon.transform.rotation = Quaternion.Euler(40, -25, 0); moon.intensity = 1.1f; moon.shadows = LightShadows.Soft;

    // Witch (gray-box): robe capsule, head, hat, broom. Single trigger collider on root.
    var player = new GameObject("Witch"); player.layer = Layers.Player;
    Part(cfg.capsule, witchMat, player.transform, new Vector3(0, 0.05f, 0), new Vector3(0.55f, 0.4f, 0.55f), Vector3.zero);
    Part(cfg.sphere, skinMat, player.transform, new Vector3(0.05f, 0.55f, 0), Vector3.one * 0.38f, Vector3.zero);
    Part(cfg.cylinder, witchMat, player.transform, new Vector3(0.05f, 0.72f, 0), new Vector3(0.7f, 0.02f, 0.7f), Vector3.zero);
    Part(cfg.cylinder, witchMat, player.transform, new Vector3(0.0f, 0.92f, 0), new Vector3(0.28f, 0.2f, 0.28f), new Vector3(0, 0, 10));
    Part(cfg.cylinder, witchMat, player.transform, new Vector3(-0.06f, 1.18f, 0), new Vector3(0.12f, 0.12f, 0.12f), new Vector3(0, 0, 25));
    Part(cfg.cylinder, cfg.branchMat, player.transform, new Vector3(0, -0.3f, 0), new Vector3(0.08f, 0.9f, 0.08f), new Vector3(0, 0, 90));
    Part(cfg.cube, Mat("Bristles", new Color(0.8f, 0.65f, 0.3f)), player.transform, new Vector3(-1.0f, -0.3f, 0), new Vector3(0.45f, 0.3f, 0.3f), Vector3.zero);
    var col = player.AddComponent<SphereCollider>(); col.isTrigger = true; col.radius = 0.42f; col.center = new Vector3(0, 0.1f, 0);
    var rb = player.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
    var pc = player.AddComponent<PlayerController>();

    var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
    var cam = camGo.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = RenderSettings.fogColor;
    cam.fieldOfView = 38; cam.farClipPlane = 120; camGo.AddComponent<UniversalAdditionalCameraData>(); camGo.AddComponent<AudioListener>();
    camGo.AddComponent<SideCamera>().target = player.transform; camGo.transform.position = new Vector3(5, 5, -17);

    // Scenery: street tiles + houses + lamps, recycled around the camera.
    var street = new GameObject("Street").AddComponent<Scenery>(); street.cam = camGo.transform; street.span = 60;
    for (int i = 0; i < 6; i++){
      var g = Block(cfg, "StreetTile", cfg.cube, cfg.groundMat, new Vector3(-25 + i * 10, -0.5f, 2), new Vector3(10, 1, 14), street.transform);
      Block(cfg, "Curb", cfg.cube, cfg.houseMat, new Vector3(-25 + i * 10, -0.35f, -4.6f), new Vector3(10, 0.4f, 0.6f), street.transform);
    }
    var houses = new GameObject("Houses").AddComponent<Scenery>(); houses.cam = camGo.transform; houses.span = 72;
    for (int i = 0; i < 8; i++){
      float h = 4 + (i * 37 % 5); float x = -30 + i * 9;
      var hs = Block(cfg, "House", cfg.cube, cfg.houseMat, new Vector3(x, h * 0.5f, 9), new Vector3(6, h, 5), houses.transform);
      var roof = Block(cfg, "Roof", cfg.cube, cfg.branchMat, Vector3.zero, Vector3.one, hs.transform);
      roof.transform.localPosition = new Vector3(0, 0.6f, 0); roof.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f); roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
      var w = Block(cfg, "Window", cfg.cube, windowMat, Vector3.zero, Vector3.one, hs.transform);
      w.transform.localPosition = new Vector3(0.15f * ((i % 2) * 2 - 1), 0.1f, -0.51f); w.transform.localScale = new Vector3(0.18f, 0.18f / h * 4, 0.02f);
      var lamp = Block(cfg, "Lamp", cfg.cylinder, cfg.houseMat, new Vector3(x + 4.5f, 1.6f, 4f), new Vector3(0.15f, 1.6f, 0.15f), houses.transform);
      Block(cfg, "LampLight", cfg.sphere, windowMat, new Vector3(x + 4.5f, 3.3f, 4f), Vector3.one * 0.4f, lamp.transform);
    }
    var grayMoon = Block(cfg, "Moon", cfg.sphere, Mat("Moon", new Color(1f, 1f, 0.85f), new Color(0.8f, 0.8f, 0.6f)), new Vector3(0, 0, 0), Vector3.one * 6, camGo.transform);
    grayMoon.transform.localPosition = new Vector3(14, 8, 60);
    var parallax = new GameObject("ParallaxBackground").AddComponent<ParallaxBackground>();
    parallax.cam = camGo.transform; parallax.grayHouses = houses.gameObject; parallax.grayMoon = grayMoon;

    var spawner = new GameObject("Spawner").AddComponent<Spawner>();
    spawner.table = table; spawner.cam = camGo.transform; spawner.player = player.transform;

    // Werewolf boss (gray-box). Art swap point: Werewolf.visualPrefab (hides the "GrayBox" child when assigned).
    var bossGo = new GameObject("Werewolf"); bossGo.layer = Layers.Enemy; bossGo.transform.position = new Vector3(40, 5, 0);
    var gb = new GameObject("GrayBox").transform; gb.SetParent(bossGo.transform, false); gb.gameObject.layer = Layers.Enemy;
    var fur = cfg.bossMat; var dark = Mat("WolfDark", new Color(0.18f, 0.15f, 0.13f));
    var eyes = Mat("WolfEyes", new Color(1f, 0.9f, 0.2f), new Color(2f, 1.6f, 0.2f));
    Part(cfg.cube, fur, gb, new Vector3(0, -0.1f, 0), new Vector3(1.5f, 1.8f, 1.1f), new Vector3(0, 0, -8));     // torso (hunched)
    Part(cfg.cube, fur, gb, new Vector3(-0.55f, 1.15f, 0), new Vector3(1.0f, 0.85f, 0.9f), Vector3.zero);       // head
    Part(cfg.cube, dark, gb, new Vector3(-1.2f, 1.0f, 0), new Vector3(0.6f, 0.4f, 0.5f), Vector3.zero);         // snout
    Part(cfg.cube, dark, gb, new Vector3(-0.35f, 1.7f, 0.25f), new Vector3(0.22f, 0.45f, 0.15f), new Vector3(0, 0, 15)); // ears
    Part(cfg.cube, dark, gb, new Vector3(-0.35f, 1.7f, -0.25f), new Vector3(0.22f, 0.45f, 0.15f), new Vector3(0, 0, 15));
    Part(cfg.cube, eyes, gb, new Vector3(-0.95f, 1.3f, -0.46f), new Vector3(0.14f, 0.1f, 0.05f), Vector3.zero);
    Part(cfg.cube, eyes, gb, new Vector3(-0.65f, 1.3f, -0.46f), new Vector3(0.14f, 0.1f, 0.05f), Vector3.zero);
    Part(cfg.cube, fur, gb, new Vector3(-1.0f, 0.35f, -0.35f), new Vector3(1.1f, 0.35f, 0.35f), new Vector3(0, 0, 20)); // throwing arm
    Part(cfg.cube, fur, gb, new Vector3(-0.4f, -0.3f, 0.4f), new Vector3(0.35f, 1.0f, 0.35f), new Vector3(0, 0, -15));
    Part(cfg.cube, dark, gb, new Vector3(0.2f, -1.3f, 0.3f), new Vector3(0.4f, 0.9f, 0.4f), new Vector3(0, 0, 10));     // legs
    Part(cfg.cube, dark, gb, new Vector3(0.45f, -1.3f, -0.3f), new Vector3(0.4f, 0.9f, 0.4f), new Vector3(0, 0, -10));
    Part(cfg.cube, fur, gb, new Vector3(0.95f, -0.4f, 0), new Vector3(0.9f, 0.25f, 0.25f), new Vector3(0, 0, -35));    // tail
    foreach (Transform c in gb) c.gameObject.layer = Layers.Enemy;
    var bcol = bossGo.AddComponent<BoxCollider>(); bcol.center = new Vector3(-0.3f, 0.2f, 0); bcol.size = new Vector3(2.0f, 3.2f, 1.2f);
    var boss = bossGo.AddComponent<Werewolf>(); boss.cam = camGo.transform; boss.player = player.transform; boss.grayBox = gb;
    boss.visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Resources/Art/BA_Werewolf.prefab");   // Art PR #3 (null -> gray box)
    var ga = new GameObject("GameAudio").AddComponent<GameAudio>();
    AudioClip Clip(string n){ var c = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + n); if (!c) Debug.LogWarning("[Audio] missing clip " + n); return c; }
    ga.mainTheme = Clip("Music/main_theme_loop.ogg"); ga.bossTheme = Clip("Music/boss_theme_loop.ogg");
    ga.titleSting = Clip("Music/title_sting.ogg"); ga.gameOverSting = Clip("Music/gameover_sting.ogg"); ga.victorySting = Clip("Music/victory_sting.ogg");
    ga.cast = Clip("SFX/spell_cast.ogg"); ga.spellHit = Clip("SFX/fireball_impact.ogg");   // non-lethal spell hit (lethal hits play enemy_pop)
    ga.sparkle = Clip("SFX/kid_sparkle.ogg"); ga.hurt = Clip("SFX/hit_ouch.ogg"); ga.honk = Clip("SFX/car_honk.ogg");
    ga.bat = Clip("SFX/bat_screech.ogg"); ga.ghost = Clip("SFX/ghost_wail.ogg"); ga.enemyPop = Clip("SFX/enemy_pop.ogg");
    ga.fireballImpact = Clip("SFX/fireball_impact.ogg"); ga.uiClick = Clip("SFX/ui_click.ogg");
    ga.roar = Clip("SFX/werewolf_roar.ogg"); ga.bossHurt = Clip("SFX/boss_hurt.ogg"); ga.fireballThrow = Clip("SFX/fireball_throw.ogg"); ga.bossDefeat = Clip("SFX/boss_defeat.ogg");

    var gm = new GameObject("GameManager").AddComponent<GameManager>();
    gm.player = pc; gm.spawner = spawner; gm.boss = boss; gm.cam = camGo.transform;
    bossGo.SetActive(false);

    EditorSceneManager.SaveScene(scene, ScenePath);
    EditorBuildSettings.scenes = new[]{ new EditorBuildSettingsScene(ScenePath, true) };
    AssetDatabase.SaveAssets();
  }

  static GameObject Block(GameConfig cfg, string n, Mesh m, Material mat, Vector3 pos, Vector3 scale, Transform parent){
    var g = new GameObject(n); if (parent) g.transform.SetParent(parent, false);
    g.transform.position = pos; g.transform.localScale = scale;
    g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat; return g;
  }
  static void Part(Mesh m, Material mat, Transform parent, Vector3 lp, Vector3 ls, Vector3 euler){
    var g = new GameObject("Part"); g.layer = parent.gameObject.layer; g.transform.SetParent(parent, false);
    g.transform.localPosition = lp; g.transform.localScale = ls; g.transform.localRotation = Quaternion.Euler(euler);
    g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
  }

  static void EnsureURP(){
    var path = Gen + "/URP_Asset.asset";
    var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
    if (!asset){
      var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
      AssetDatabase.CreateAsset(rd, Gen + "/URP_Renderer.asset");
      asset = UniversalRenderPipelineAsset.Create(rd);
      AssetDatabase.CreateAsset(asset, path);
      AssetDatabase.SaveAssets();
    }
    GraphicsSettings.defaultRenderPipeline = asset;
    for (int i = 0; i < QualitySettings.names.Length; i++){ QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = asset; }
  }

  static Material Mat(string name, Color c, Color? emission = null){
    var p = $"{Gen}/{name}.mat";
    var m = AssetDatabase.LoadAssetAtPath<Material>(p);
    if (!m){ m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
    m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.15f);
    if (emission.HasValue){ m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
    EditorUtility.SetDirty(m);
    return m;
  }


  /// <summary>M2: make sure the saved table has the LampPost entry and the M2 difficulty/speed/interval curves (keeps any art prefabs).</summary>
  static void EnsureM2Table(SpawnTable table){
    // applied once (version-gated) so later Inspector tuning is not clobbered by rebuilds
    if (table.m2TuningVersion >= 2) return;
    if (table.EntryFor(ObstacleKind.LampPost) == null) table.entries.Add(new SpawnEntry{ kind = ObstacleKind.LampPost, weight = 1.5f });
    SpawnTable.ApplyFF2(table); EditorUtility.SetDirty(table); AssetDatabase.SaveAssets();   // FF2: free-flight street tuning
  }

  /// <summary>FF2 street fairness validator (free-flight model). Usage:
  /// Unity -batchmode -nographics -projectPath . -executeMethod BuildWeb.ValidatePatterns -logFile v.log
  /// Also runs as a gate at the start of BuildWeb.Build. Simulates whole levels (spawn timer, difficulty/speed curves) for
  /// Seeds seeds and re-checks every planned column INDEPENDENTLY of the planner's analytic helpers:
  ///  - gap: brute-force samples the witch centre over [BottomY+0.05, TopY]; a free run must give a clear gap >= MinGap(d)
  ///    (>= 1.8 m; >= 2.6 m in columns with a kid);
  ///  - reachability: the witch can get from the previous column's reachable window into this one. Transition time is found by
  ///    time-stepping obstacle x positions against the witch for her extreme x offsets; the vertical distance she can cover in
  ///    that time is found by frame-simulating MovementConfig's MoveTowards kinematics (accelerate, then brake to rest);
  ///  - homing flyers have reached their fixed target height before any part of them can touch the witch;
  ///  - multi-item spawns are >= 0.9 s apart, scroll stays within 7..9.5 m/s;
  ///  - factory colliders stay inside the planner envelopes; exactly one art instance per obstacle.
  /// Also reports how often the top/bottom edges are blocked (edge-hugging is not a safe strategy).</summary>
  [MenuItem("Broomstick Bash/Validate Patterns")]
  public static void ValidatePatterns(){
    bool ok = RunStreetValidation(500);
    if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
  }

  /// <summary>Max vertical distance covered in T seconds starting and ending at rest, by simulating PlayerController's
  /// per-frame velocity update (MoveTowards toward wish at AccelRate/DecelRate) at 60 fps. Independent of LevelPlanner.Reach.</summary>
  static float SimReach(float T){
    T -= LevelPlanner.Reaction; if (T <= 0) return 0;
    const float dt = 1f / 60f; int n = Mathf.FloorToInt(T / dt); float best = 0;
    for (int brake = 0; brake <= n; brake++){            // full input for 'brake' frames, then release
      float y = 0, v = 0;
      for (int i = 0; i < n; i++){
        float wish = i < brake ? MovementConfig.MaxSpeed : 0f;
        float rate = Mathf.Abs(wish) > 0.0001f ? MovementConfig.AccelRate : MovementConfig.DecelRate;
        v = Mathf.MoveTowards(v, wish, rate * dt); y += v * dt;
      }
      if (v < 0.05f) best = Mathf.Max(best, y);        // must be (nearly) stopped at the end
    }
    return best;
  }
  static readonly System.Collections.Generic.Dictionary<int, float> reachCache = new();
  static float SimReachCached(float T){ int k = Mathf.Max(0, Mathf.FloorToInt(T * 120f)); if (!reachCache.TryGetValue(k, out var r)) reachCache[k] = r = SimReach(k / 120f); return r; }

  /// <summary>First / last time (s after spawn) any item of the column overlaps the witch along x, for witch offset ox.</summary>
  static Vector2 OverlapSpan(PlannedColumn c, float ox, float scroll){
    float first = float.MaxValue, last = float.MinValue; const float step = 1f / 240f;
    foreach (var it in c.items){
      float x0 = LevelPlanner.SpawnAhead + MovementConfig.CameraLead + it.dx - ox, hw = it.HalfWidth + MovementConfig.PlayerRadius;
      for (float t = 0; t < 6f; t += step){
        float x = x0 - (scroll + it.speed) * t;
        if (Mathf.Abs(x) < hw){ first = Mathf.Min(first, t); last = Mathf.Max(last, t); }
        if (x < -hw) break;
      }
    }
    return new Vector2(first, last);
  }

  public static bool RunStreetValidation(int Seeds){
    int fails = 0; var failKinds = new System.Collections.Generic.Dictionary<string, int>();
    void Fail(string m){
      fails++; if (fails <= 25) Debug.LogError("VALIDATE FAIL: " + m);
      string k = System.Text.RegularExpressions.Regex.Replace(m, @"[-0-9.,()\[\]]+", "#"); failKinds[k] = failKinds.TryGetValue(k, out var n) ? n + 1 : 1;
    }
    var table = AssetDatabase.LoadAssetAtPath<SpawnTable>(TablePath);
    if (!table){ table = SpawnTable.CreateDefault(); SpawnTable.ApplyFF2(table); } else EnsureM2Table(table);
    const float ground = 0f, ceiling = 10f, baseSpeed = 6f, dt = 1f / 60f;
    int columns = 0, fallbacks = 0, items = 0, multi = 0, kidCols = 0, topBlocked = 0, bottomBlocked = 0, homing = 0;
    float minGapSeen = 99, minSlack = 99, minMultiDt = 99, minTrans = 99, maxScroll = 0, minScroll = 99, minHomeMargin = 99;
    var templates = new System.Collections.Generic.Dictionary<string, int>(); var kinds = new System.Collections.Generic.Dictionary<ObstacleKind, int>();
    float levelTime = 0, fullAt = -1;
    float lo = LevelPlanner.WindowLo, hi = LevelPlanner.WindowHi;
    for (int seed = 0; seed < Seeds; seed++){
      var rng = new System.Random(seed); var pl = new LevelPlanner(rng, ground, ceiling){ table = table };
      float dist = 0, t = 0, timer = 1.5f, since = 0, lastMultiT = -99; PlannedColumn prevCol = null; System.Collections.Generic.List<Vector2> prevWins = null;
      while (dist < table.bossDistance){
        float d = Spawner.DifficultyAt(table, dist), v = baseSpeed * table.speedByDifficulty.Evaluate(d);
        if (seed == 0 && fullAt < 0 && d >= 0.999f) fullAt = t;
        maxScroll = Mathf.Max(maxScroll, v); minScroll = Mathf.Min(minScroll, v);
        dist += v * dt; t += dt; since += dt;
        if (dist > table.bossDistance - LevelPlanner.SpawnAhead) continue;
        timer -= dt; if (timer > 0) continue;
        var col = pl.Next(d, v, since); float dtSpawn = since; since = 0;
        timer = Mathf.Max(LevelPlanner.MinSingleInterval, table.intervalByDifficulty.Evaluate(d) * (0.9f + 0.2f * (float)rng.NextDouble()));
        columns++; items += col.items.Count; if (col.template == "fallback") fallbacks++;
        templates[col.template ?? "?"] = templates.TryGetValue(col.template ?? "?", out var tc) ? tc + 1 : 1;
        foreach (var it in col.items) kinds[it.kind] = kinds.TryGetValue(it.kind, out var kc) ? kc + 1 : 1;
        bool kid = false; foreach (var it in col.items) if (it.kind == ObstacleKind.Kid) kid = true;
        if (kid) kidCols++;
        // multi-lane spacing
        if (col.items.Count > 1){ multi++; if (t - lastMultiT < LevelPlanner.MinMultiSpacing - 1e-3f) Fail($"seed {seed} t={t:0.00}: multi spawns {t - lastMultiT:0.00}s apart"); minMultiDt = Mathf.Min(minMultiDt, t - lastMultiT); lastMultiT = t; }
        // homing flyers settled before they can reach the witch (worst case: witch at her right-most offset)
        foreach (var it in col.items){
          if (it.kind != ObstacleKind.Bat && it.kind != ObstacleKind.Ghost) continue; homing++;
          var one = new PlannedColumn(); one.items.Add(it);
          float firstTouch = OverlapSpan(one, LevelPlanner.WitchOffsetMax, v).x;
          minHomeMargin = Mathf.Min(minHomeMargin, firstTouch - LevelPlanner.HomeTime);
          if (firstTouch < LevelPlanner.HomeTime) Fail($"seed {seed}: {it.kind} can touch the witch at {firstTouch:0.00}s, before homing ends ({LevelPlanner.HomeTime}s)");
        }
        // reach band from the previous window
        float reach = 99;
        if (prevCol != null){
          float trans = float.MaxValue;
          foreach (var ox in new[]{ LevelPlanner.WitchOffsetMin, LevelPlanner.WitchOffsetMax }){
            float leaveA = OverlapSpan(prevCol, ox, prevCol.scroll).y - dtSpawn, enterB = OverlapSpan(col, ox, v).x;
            trans = Mathf.Min(trans, enterB - leaveA);
          }
          minTrans = Mathf.Min(minTrans, trans);
          reach = SimReachCached(trans);
        }
        // brute-force gap sampling of the witch centre; keep every free run (clipped to what is reachable from ANY previous
        // reachable run) whose clear gap is big enough. The set of reachable runs is carried forward, so the check is exact
        // for every route, not only the planner's choice.
        float needGap = Mathf.Max(LevelPlanner.MinGap(d), kid ? LevelPlanner.KidGap : 0f);
        bool Free(float y){ foreach (var it in col.items){ var e = it.Envelope(ground, ceiling); if (y + LevelPlanner.PlayerAbove > e.x && y - LevelPlanner.PlayerBelow < e.y) return false; } return true; }
        if (!Free(hi)) topBlocked++; if (!Free(lo + 0.3f)) bottomBlocked++;
        var runs = new System.Collections.Generic.List<Vector2>();
        int steps = Mathf.FloorToInt((hi - lo) / 0.01f); float runStart = -1;
        for (int si = 0; si <= steps; si++){
          float y = lo + si * 0.01f; bool free = Free(y), end = !free || si == steps;
          if (free && runStart < 0) runStart = y;
          if (end && runStart >= 0){ runs.Add(new Vector2(runStart, free ? y : y - 0.01f)); runStart = -1; }
        }
        var reachable = new System.Collections.Generic.List<Vector2>(); float bestGap = -1;
        foreach (var r in runs){
          if (prevWins == null){ Add(r); continue; }
          foreach (var pw in prevWins) Add(new Vector2(Mathf.Max(r.x, pw.x - reach), Mathf.Min(r.y, pw.y + reach)));
        }
        void Add(Vector2 w){ float gap = w.y - w.x + LevelPlanner.PlayerExtent; if (gap + 0.011f < needGap) return; reachable.Add(w); bestGap = Mathf.Max(bestGap, gap); }
        if (reachable.Count == 0) Fail($"seed {seed} t={t:0.0}s d={d:0.00}: no reachable gap >= {needGap:0.00} m ({col.template}{(kid ? ", kid" : "")}), reach {reach:0.00} m");
        else { minGapSeen = Mathf.Min(minGapSeen, bestGap); minSlack = Mathf.Min(minSlack, bestGap - needGap); }
        // the planner's chosen window must really be free
        for (float y = col.safe.x + 0.005f; y <= col.safe.y - 0.005f; y += 0.02f) if (!Free(y)){ Fail($"seed {seed}: planner window {col.safe} blocked at {y:0.00}"); break; }
        prevWins = reachable.Count > 0 ? reachable : new System.Collections.Generic.List<Vector2>{ col.safe }; prevCol = col;
      }
      if (seed == 0) levelTime = t;
    }
    if (minScroll < SpawnTable.FF2ScrollMin - 0.01f || maxScroll > SpawnTable.FF2ScrollMax + 0.01f) Fail($"scroll out of range {minScroll:0.00}..{maxScroll:0.00}");
    if (topBlocked == 0 || bottomBlocked == 0) Fail("an edge is never blocked: hugging the top/bottom would be always safe");
    // geometry: factory output must stay inside the planner envelopes
    var cfgGo = new GameObject("ValidateCfg"); var cfg = cfgGo.AddComponent<GameConfig>(); GameConfig.I = cfg;
    cfg.cube = Prim(PrimitiveType.Cube); cfg.sphere = Prim(PrimitiveType.Sphere); cfg.cylinder = Prim(PrimitiveType.Cylinder); cfg.capsule = Prim(PrimitiveType.Capsule);
    var grng = new System.Random(7); var gpl = new LevelPlanner(grng, ground, ceiling); int geo = 0, artChecks = 0; var artKinds = new System.Collections.Generic.HashSet<ObstacleKind>();
    for (int i = 0; i < 400; i++){
      var col = gpl.Next((float)grng.NextDouble(), 8f, 2f);
      foreach (var it in col.items){
        var ob = ObstacleFactory.Create(it, 0, cfg, null);
        bool flyer = it.kind == ObstacleKind.Bat || it.kind == ObstacleKind.Ghost;
        if (flyer){ var pp = ob.transform.position; pp.y = it.targetY; ob.transform.position = pp; Physics.SyncTransforms(); } // measured once homed
        var e = it.Envelope(ground, ceiling); float minY = 99, maxY = -99;
        foreach (var c in ob.GetComponentsInChildren<Collider>()){ minY = Mathf.Min(minY, c.bounds.min.y); maxY = Mathf.Max(maxY, c.bounds.max.y); }
        if (it.kind == ObstacleKind.Bat){ ob.transform.rotation = Quaternion.Euler(0, 0, 20); Physics.SyncTransforms(); foreach (var c in ob.GetComponentsInChildren<Collider>()){ maxY = Mathf.Max(maxY, c.bounds.max.y); minY = Mathf.Min(minY, c.bounds.min.y - it.amp); } }
        if (it.kind == ObstacleKind.Ghost){ minY -= it.amp; maxY += it.amp; }
        if (it.kind == ObstacleKind.Kid) maxY += 0.12f; // bob
        if (minY < e.x - 0.02f || maxY > e.y + 0.02f) Fail($"geometry {it.kind}: colliders y[{minY:0.00},{maxY:0.00}] outside envelope [{e.x:0.00},{e.y:0.00}]");
        float hwMeasured = 0; foreach (var c in ob.GetComponentsInChildren<Collider>()) hwMeasured = Mathf.Max(hwMeasured, Mathf.Abs(c.bounds.min.x - (ob.transform.position.x)), Mathf.Abs(c.bounds.max.x - ob.transform.position.x));
        if (hwMeasured > it.HalfWidth + 0.02f) Fail($"geometry {it.kind}: collider half-width {hwMeasured:0.00} > planner {it.HalfWidth:0.00}");
        if (it.kind != ObstacleKind.Branch) ob.transform.rotation = Quaternion.identity; // branch keeps its real spawn tilt
        Physics.SyncTransforms();
        var before = new System.Collections.Generic.List<Bounds>(); foreach (var c in ob.GetComponentsInChildren<Collider>()) before.Add(c.bounds);
        BroomstickBash.Art.BroomArtSkin.Skin(ob.gameObject); Physics.SyncTransforms();
        int artCount = 0; foreach (Transform tr in ob.transform) if (tr.name.StartsWith("Art_")) artCount++;
        var after = ob.GetComponentsInChildren<Collider>();
        bool same = after.Length == before.Count; for (int ci = 0; same && ci < after.Length; ci++) same = (after[ci].bounds.center - before[ci].center).sqrMagnitude < 1e-6f && (after[ci].bounds.size - before[ci].size).sqrMagnitude < 1e-6f;
        if (artCount != 1) Fail($"art {it.kind}: {artCount} art instances (expected 1)"); else artChecks++;
        if (!same) Fail($"art {it.kind}: colliders changed by skinning");
        artKinds.Add(it.kind);
        geo++; Object.DestroyImmediate(ob.gameObject);
      }
    }
    Object.DestroyImmediate(cfgGo);
    foreach (ObstacleKind k in System.Enum.GetValues(typeof(ObstacleKind))) if (!artKinds.Contains(k)) Fail($"art: kind {k} never checked");
    var sb = new System.Text.StringBuilder();
    foreach (var kv in templates) sb.Append($"{kv.Key}={kv.Value} "); sb.Append("| ");
    foreach (var kv in kinds) sb.Append($"{kv.Key}={kv.Value} ");
    var curve = new System.Text.StringBuilder();
    foreach (var p in new[]{ 0f, 0.25f, 0.5f, 0.68f, 0.76f, 0.85f, 1f }){
      float dd = Spawner.DifficultyAt(table, p * table.bossDistance);
      curve.Append($"{p:P0}: d={dd:0.00} scroll={baseSpeed * table.speedByDifficulty.Evaluate(dd):0.0} interval={Mathf.Max(LevelPlanner.MinSingleInterval, table.intervalByDifficulty.Evaluate(dd)):0.00}s minGap={LevelPlanner.MinGap(dd):0.00}m; ");
    }
    Debug.Log($"VALIDATE ART: {artChecks} obstacles skinned with exactly one art instance, colliders unchanged; kinds={string.Join(",", artKinds)}");
    Debug.Log($"VALIDATE CURVE: boss at {table.bossDistance} m, reached after {levelTime:0.0}s; full difficulty at {fullAt:0.0}s. scroll {minScroll:0.00}..{maxScroll:0.00} m/s. {curve}");
    Debug.Log($"VALIDATE STATS: multi={multi} (min spacing {minMultiDt:0.00}s) kidColumns={kidCols} homingFlyers={homing} (min margin after homing {minHomeMargin:0.00}s) minTransition={minTrans:0.00}s topEdgeBlocked={100f * topBlocked / Mathf.Max(1, columns):0.0}% bottomEdgeBlocked={100f * bottomBlocked / Mathf.Max(1, columns):0.0}% reach(0.3s)={SimReach(0.3f):0.00}m reach(1s)={SimReach(1f):0.00}m");
    foreach (var kv in failKinds) Debug.LogError($"VALIDATE FAIL KIND x{kv.Value}: {kv.Key}");
    Debug.Log($"VALIDATE RESULT: {(fails == 0 ? "PASS" : "FAIL")} fails={fails} seeds={Seeds} columns={columns} items={items} fallbacks={fallbacks} minGap={minGapSeen:0.00}m minSlack={minSlack:0.00}m geometryChecks={geo} [{sb}]");
    return fails == 0;
  }

  /// <summary>Free Flight page hardening (mobile + desktop), injected into the generated index.html after every build:
  /// canvas touch-action:none and preventDefault on touch events (no zoom / pull-to-refresh / long-press callout),
  /// no context menu on the canvas (right-click), and Space / arrow keys don't scroll the page. Unity still receives all
  /// events (preventDefault does not stop propagation).</summary>
  static void PatchIndexHtml(string outDir){
    var p = Path.Combine(outDir, "index.html"); if (!File.Exists(p)) return;
    var html = File.ReadAllText(p); if (html.Contains("bb-ff-input-guard")) return;
    const string css = "<style id=\"bb-ff-input-guard-css\">html,body{overscroll-behavior:none;}#unity-canvas{touch-action:none;-ms-touch-action:none;user-select:none;-webkit-user-select:none;-webkit-touch-callout:none;-webkit-tap-highlight-color:transparent;outline:none}</style>";
    const string js = @"<script id=""bb-ff-input-guard"">(function(){
  var c=document.getElementById('unity-canvas'); if(!c) return;
  var stop=function(e){ if(e.cancelable) e.preventDefault(); };
  ['touchstart','touchmove','touchend','touchcancel'].forEach(function(t){ c.addEventListener(t,stop,{passive:false}); });
  ['gesturestart','gesturechange','gestureend','dblclick','contextmenu'].forEach(function(t){ c.addEventListener(t,stop,{passive:false}); });
  document.addEventListener('contextmenu',function(e){ if(e.target===c) e.preventDefault(); });
  window.addEventListener('keydown',function(e){
    var k=e.key||'', tag=(e.target&&e.target.tagName)||'';
    if((k===' '||k==='Spacebar'||k.indexOf('Arrow')===0) && !/INPUT|TEXTAREA|SELECT/.test(tag)) e.preventDefault();
  },{passive:false});
  if(!c.hasAttribute('tabindex')) c.setAttribute('tabindex','-1');
  c.addEventListener('pointerdown',function(){ try{ c.focus(); }catch(_){} });
})();</script>";
    html = html.Replace("</head>", css + "\n  </head>").Replace("</body>", js + "\n  </body>");
    File.WriteAllText(p, html);
  }

  [MenuItem("Broomstick Bash/Build WebGL to docs")]
  public static void Build(){
    int code = 1;
    try {
      // TODO(FF3): the boss fairness sweep still models the OLD flap physics (FlapModel). It keeps passing (patterns are
      // unchanged), so it stays as a regression gate until FF3 re-models it with MovementConfig's free-flight kinematics.
      if (!BossFairnessTest.Run()) throw new System.Exception("Boss fairness sweep found unfair patterns; see [Fairness] warnings");
      // FF2 build gate: street fairness under the free-flight model (500 seeds)
      if (!RunStreetValidation(500)) throw new System.Exception("Street fairness validation failed; see VALIDATE FAIL errors");
      SetupScene();
      PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
      PlayerSettings.WebGL.decompressionFallback = true;
      PlayerSettings.WebGL.threadsSupport = false;
      PlayerSettings.productName = "Broomstick Bash: Free Flight"; PlayerSettings.companyName = "mikenev";
      bool dev = System.Environment.GetEnvironmentVariable("BB_DEV") == "1";   // dev build w/ stack traces, for debugging only
      if (dev){ PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace; PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full); }
      else PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
      var outDir = Path.GetFullPath(dev ? "Builds/WebGLDev" : "docs");
      var r = BuildPipeline.BuildPlayer(new[]{ ScenePath }, outDir, BuildTarget.WebGL, dev ? BuildOptions.Development : BuildOptions.None);
      Debug.Log("BUILD RESULT: " + r.summary.result + " size=" + r.summary.totalSize);
      if (r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded){ File.WriteAllText(Path.Combine(outDir, ".nojekyll"), ""); PatchIndexHtml(outDir); code = 0; }
    } catch (System.Exception e){ Debug.LogException(e); }
    if (Application.isBatchMode) EditorApplication.Exit(code);
  }
}
