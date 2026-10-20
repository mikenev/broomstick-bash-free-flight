using UnityEngine;
namespace BroomstickBash {
  public struct SpawnContext { public float difficulty; public float spawnX; public float y; public Transform player; }
  /// <summary>Anything the spawner creates implements this.</summary>
  public interface ISpawnable { void OnSpawned(SpawnContext ctx); void OnDespawned(); }

  /// <summary>Base class for all obstacles/enemies. Override Tick for movement. Layer decides interactions:
  /// Enemy = spell kills it, Kid = spell passes with sparkle, Obstacle = spell is blocked. All hurt the player.</summary>
  public class ObstacleBase : MonoBehaviour, ISpawnable {
    public ObstacleKind kind;
    public float speed;          // extra leftward world speed (m/s)
    public int hp = 1;
    public int scoreValue = 10;
    protected SpawnContext ctx; protected float t;
    public virtual void OnSpawned(SpawnContext c){ ctx = c; GameEvents.ObstacleSpawned?.Invoke(this); }
    public virtual void OnDespawned(){}
    protected virtual void Tick(float dt){ transform.position += Vector3.left * speed * dt; }
    void Update(){
      if (GameManager.I && !GameManager.I.IsActive) return;
      t += Time.deltaTime; Tick(Time.deltaTime);
    }
    /// <summary>Called by a spell hit. Returns true if the spell is consumed.</summary>
    public virtual bool OnSpellHit(){
      if (gameObject.layer != Layers.Enemy) return gameObject.layer == Layers.Obstacle;
      hp--; Puff.Burst(transform.position, GameConfig.I.sparkleMat, 5, 0.15f);
      if (hp <= 0) Kill();
      return true;
    }
    public virtual void Kill(){
      Puff.Burst(transform.position, GetComponent<MeshRenderer>() ? GetComponent<MeshRenderer>().sharedMaterial : GameConfig.I.sparkleMat, 10);
      GameEvents.EnemyKilled?.Invoke(this);
      Despawn();
    }
    public void Despawn(){ OnDespawned(); Destroy(gameObject); }
  }

  /// <summary>FF2 homing bat: glides from its spawn height to a target height fixed at spawn (LevelPlanner picks it, biased
  /// toward where the witch probably is) within LevelPlanner.HomeTime, then swoops down by up to <see cref="amp"/> and back.
  /// Deterministic (no live tracking), so the street fairness check stays exact.</summary>
  public class Bat : ObstacleBase {
    public float amp = 1.2f, targetY; float startY, phase;
    public override void OnSpawned(SpawnContext c){ base.OnSpawned(c); startY = transform.position.y; phase = Random.value * 6f; }
    protected override void Tick(float dt){
      float home = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / LevelPlanner.HomeTime));
      float s = Mathf.Sin(t * 2.2f + phase);
      float baseY = Mathf.Lerp(startY, targetY, home);
      var p = transform.position; p.x -= speed * dt; p.y = baseY - amp * s * s * home; transform.position = p;
      transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 20f) * 20f);
    }
  }
  /// <summary>FF2 homing ghost: drifts from its spawn height to a target height fixed at spawn within LevelPlanner.HomeTime,
  /// then floats on a sine wave of amplitude <see cref="amp"/> around it.</summary>
  public class Ghost : ObstacleBase {
    public float amp = 1f, targetY; float startY, phase;
    public override void OnSpawned(SpawnContext c){ base.OnSpawned(c); startY = transform.position.y; phase = Random.value * 6f; }
    protected override void Tick(float dt){
      float home = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / LevelPlanner.HomeTime));
      var p = transform.position; p.x -= speed * dt; p.y = Mathf.Lerp(startY, targetY, home) + amp * home * Mathf.Sin(t * 1.6f + phase); transform.position = p;
    }
  }
  public class Kid : ObstacleBase {
    protected override void Tick(float dt){
      base.Tick(dt); var p = transform.position; p.y = ctx.y + Mathf.Abs(Mathf.Sin(t * 8f)) * 0.12f; transform.position = p;
    }
  }
}
