using UnityEngine;
namespace BroomstickBash {
  /// <summary>Werewolf fireball (Hazard layer). Velocity is world-space and already includes the scroll speed,
  /// so relative to the witch it moves at (vel - scroll). Optional gravity for lobbed arcs.</summary>
  public class Fireball : MonoBehaviour {
    public Vector3 vel; public float gravity; float life = 9f;
    void Update(){
      if (GameManager.I && !GameManager.I.IsActive) return;
      vel.y -= gravity * Time.deltaTime;
      transform.position += vel * Time.deltaTime; transform.Rotate(0, 0, 400 * Time.deltaTime);
      life -= Time.deltaTime; if (life <= 0 || transform.position.y < -9f || transform.position.y > 24f) Destroy(gameObject);   // drifting walls start off-screen
    }
  }
}
