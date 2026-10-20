using UnityEngine;
namespace BroomstickBash {
  /// <summary>Builds gray-box obstacles (or instantiates the entry prefab). Colliders are added explicitly (see link.xml).</summary>
  public static class ObstacleFactory {
    public static ObstacleBase Create(SpawnEntry e, float x, GameConfig cfg){
      GameObject go; ObstacleBase ob;
      if (e.kind == ObstacleKind.LampPost) // M2 kind: build via the planned-item path
        return Create(new PlannedItem{ kind = ObstacleKind.LampPost, y = cfg.groundY, size = Random.Range(3.2f, 5f) }, x, cfg, null);
      switch (e.kind){
        case ObstacleKind.Kid: {
          float h = Random.Range(1.1f, 1.5f);
          go = cfg.MakeBlock("Kid", cfg.capsule, cfg.kidMat, new Vector3(x, cfg.groundY + h * 0.5f, 0), new Vector3(0.7f, h * 0.5f, 0.7f));
          // pumpkin bucket
          var b = cfg.MakeBlock("Bucket", cfg.sphere, cfg.fireMat, Vector3.zero, Vector3.one * 0.5f, go.transform);
          b.transform.localPosition = new Vector3(-0.6f, -0.2f, -0.3f);
          go.AddComponent<CapsuleCollider>();
          go.layer = Layers.Kid; ob = go.AddComponent<Kid>(); ob.speed = Random.Range(0.3f, 1.0f); break;
        }
        case ObstacleKind.Car: {
          go = cfg.MakeBlock("Car", cfg.cube, cfg.carMat, new Vector3(x, cfg.groundY + 0.6f, 0), new Vector3(3.2f, 1.2f, 1.6f));
          var top = cfg.MakeBlock("Cabin", cfg.cube, cfg.carMat, Vector3.zero, Vector3.one, go.transform);
          top.transform.localPosition = new Vector3(-0.05f, 0.8f, 0); top.transform.localScale = new Vector3(0.55f, 0.6f, 0.9f);
          go.AddComponent<BoxCollider>().size = new Vector3(1, 1.6f, 1); go.GetComponent<BoxCollider>().center = new Vector3(0, 0.3f, 0);
          go.layer = Layers.Obstacle; ob = go.AddComponent<ObstacleBase>(); ob.speed = Random.Range(1.5f, 3f); break;
        }
        case ObstacleKind.Branch: {
          float len = Random.Range(2.5f, 4.2f);
          go = cfg.MakeBlock("Branch", cfg.cube, cfg.branchMat, new Vector3(x, cfg.ceilingY - len * 0.5f, 0), new Vector3(0.8f, len, 0.8f));
          go.transform.rotation = Quaternion.Euler(0, 0, Random.Range(-12f, 12f));
          go.AddComponent<BoxCollider>();
          go.layer = Layers.Obstacle; ob = go.AddComponent<ObstacleBase>(); ob.speed = 0; break;
        }
        case ObstacleKind.Ghost: {
          go = cfg.MakeBlock("Ghost", cfg.capsule, cfg.ghostMat, new Vector3(x, Y(e, cfg), 0), new Vector3(0.9f, 0.6f, 0.9f));
          go.AddComponent<CapsuleCollider>();
          go.layer = Layers.Enemy; var lg = go.AddComponent<Ghost>(); lg.targetY = go.transform.position.y; ob = lg; ob.speed = 1.2f; ob.hp = 2; ob.scoreValue = 25; break;
        }
        default: {
          go = cfg.MakeBlock("Bat", cfg.cube, cfg.batMat, new Vector3(x, Y(e, cfg), 0), new Vector3(1.3f, 0.35f, 0.5f));
          go.AddComponent<BoxCollider>();
          go.layer = Layers.Enemy; var lb = go.AddComponent<Bat>(); lb.targetY = go.transform.position.y; ob = lb; ob.speed = 2.5f; ob.scoreValue = 15; break;
        }
      }
      ob.kind = e.kind; return ob;
    }
    /// <summary>M2: build exactly what LevelPlanner planned (geometry must match PlannedItem.Envelope).</summary>
    public static ObstacleBase Create(PlannedItem p, float x, GameConfig cfg, SpawnTable table){
      x += p.dx; GameObject go; ObstacleBase ob;
      float gy = cfg.groundY;
      switch (p.kind){
        case ObstacleKind.Kid: {
          float h = p.size; // costumed kids: capsule body + per-height costume bits (gray-box)
          go = cfg.MakeBlock("Kid", cfg.capsule, cfg.kidMat, new Vector3(x, gy + h * 0.5f, 0), new Vector3(0.45f + h * 0.18f, h * 0.5f, 0.6f));
          var b = cfg.MakeBlock("Bucket", cfg.sphere, cfg.fireMat, Vector3.zero, Vector3.one * 0.5f, go.transform);
          b.transform.localPosition = new Vector3(-0.6f, -0.2f, -0.3f);
          int costume = Mathf.Abs(Mathf.RoundToInt(x * 7.3f)) % 3; // 0 witch hat, 1 sheet-ghost cap, 2 pumpkin head
          var hat = cfg.MakeBlock("Costume", costume == 2 ? cfg.sphere : cfg.cylinder, costume == 2 ? cfg.fireMat : costume == 1 ? cfg.ghostMat : cfg.batMat, Vector3.zero, Vector3.one, go.transform);
          hat.transform.localPosition = new Vector3(0, 0.95f, 0); hat.transform.localScale = costume == 0 ? new Vector3(0.9f, 0.25f, 0.9f) : new Vector3(0.8f, 0.3f, 0.8f);
          go.AddComponent<CapsuleCollider>(); go.layer = Layers.Kid; ob = go.AddComponent<Kid>(); break;
        }
        case ObstacleKind.Car: {
          go = cfg.MakeBlock("Car", cfg.cube, cfg.carMat, new Vector3(x, gy + 0.6f, 0), new Vector3(3.2f, 1.2f, 1.6f));
          var top = cfg.MakeBlock("Cabin", cfg.cube, cfg.carMat, Vector3.zero, Vector3.one, go.transform);
          top.transform.localPosition = new Vector3(-0.05f, 0.8f, 0); top.transform.localScale = new Vector3(0.55f, 0.6f, 0.9f);
          var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(1, 1.6f, 1); bc.center = new Vector3(0, 0.3f, 0); // top = 1.92
          go.layer = Layers.Obstacle; ob = go.AddComponent<ObstacleBase>(); break;
        }
        case ObstacleKind.LampPost: {
          go = new GameObject("LampPost"); go.transform.position = new Vector3(x, gy, 0);
          var pole = cfg.MakeBlock("Pole", cfg.cylinder, cfg.houseMat, Vector3.zero, Vector3.one, go.transform);
          pole.transform.localPosition = new Vector3(0, p.size * 0.5f, 0); pole.transform.localScale = new Vector3(0.3f, p.size * 0.5f, 0.3f);
          pole.AddComponent<CapsuleCollider>(); pole.layer = Layers.Obstacle;
          var head = cfg.MakeBlock("Lamp", cfg.sphere, cfg.sparkleMat, Vector3.zero, Vector3.one * (LevelPlanner.LampHeadR * 2), go.transform);
          head.transform.localPosition = new Vector3(0, p.size, 0); head.AddComponent<SphereCollider>(); head.layer = Layers.Obstacle;
          go.layer = Layers.Obstacle; ob = go.AddComponent<ObstacleBase>(); break;
        }
        case ObstacleKind.Branch: {
          float len = p.size;
          go = cfg.MakeBlock("Branch", cfg.cube, cfg.branchMat, new Vector3(x, cfg.ceilingY - len * 0.5f, 0), new Vector3(0.8f, len, 0.8f));
          go.transform.rotation = Quaternion.Euler(0, 0, ((x * 13.7f) % 24f) - 12f);
          go.AddComponent<BoxCollider>(); go.layer = Layers.Obstacle; ob = go.AddComponent<ObstacleBase>(); break;
        }
        case ObstacleKind.Ghost: {
          go = cfg.MakeBlock("Ghost", cfg.capsule, cfg.ghostMat, new Vector3(x, p.y, 0), new Vector3(0.9f, 0.6f, 0.9f));
          go.AddComponent<CapsuleCollider>(); go.layer = Layers.Enemy; var g = go.AddComponent<Ghost>(); g.amp = p.amp; g.targetY = p.targetY; ob = g; ob.hp = 2; ob.scoreValue = 25; break;
        }
        default: {
          go = cfg.MakeBlock("Bat", cfg.cube, cfg.batMat, new Vector3(x, p.y, 0), new Vector3(1.3f, 0.35f, 0.5f));
          go.AddComponent<BoxCollider>(); go.layer = Layers.Enemy; var bt = go.AddComponent<Bat>(); bt.amp = p.amp; bt.targetY = p.targetY; ob = bt; ob.scoreValue = 15; break;
        }
      }
      // Art: applied by BroomArtSkin (name-based, one instance per root). SpawnEntry.prefab is intentionally not used.
      ob.speed = p.speed; ob.kind = p.kind; return ob;
    }
    static float Y(SpawnEntry e, GameConfig cfg) => e.maxY > e.minY ? Random.Range(e.minY, e.maxY) : (cfg.groundY + cfg.ceilingY) * 0.5f;
  }
}
