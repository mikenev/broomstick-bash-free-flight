using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  /// <summary>Witch: free-flight steering (see MovementConfig for the model), hearts, invulnerability flash, spell casting.
  /// Position = scroll anchor (trackX, advanced at the scroll speed, followed by the camera) + a steerable offset.</summary>
  public class PlayerController : MonoBehaviour {
    public int maxHearts = MovementConfig.Hearts;
    public float invulnTime = MovementConfig.InvulnTime, castCooldown = MovementConfig.CastCooldown;
    public int Hearts { get; private set; }
    public Vector2 Velocity => vel;          // m/s relative to the scroll anchor
    public float VelocityY => vel.y;
    public float TrackX => trackX;           // scroll anchor the camera follows
    public Vector2 Offset => offset;
    public bool Invulnerable => invuln > 0;
    public float CooldownLeft => cool;
    Vector2 vel, offset; float trackX, invuln, cool; Renderer[] rends; Camera cam;

    GameObject art;
    /// <summary>Seconds after start/restart during which bouncing off the street is free (no heart lost).</summary>
    public float streetGrace = MovementConfig.StreetGrace; float grace;
    void Awake(){
      // M2 art: BA_Witch (origin = broom handle; gray-box broom sits at y -0.3). The hurt flash toggles only the art renderers
      // when art is present, so the hidden gray-box never reappears; falls back to the gray-box renderers otherwise.
      art = BroomstickBash.Art.BroomArt.Attach("Witch", transform);
      if (art){ art.transform.localPosition = new Vector3(0, -0.3f, 0); rends = art.GetComponentsInChildren<Renderer>(true); }
      else rends = GetComponentsInChildren<Renderer>();
    }
    public void StartGrace(){ grace = streetGrace; }
    public void ResetPlayer(){
      Hearts = maxHearts; vel = Vector2.zero; invuln = 0; cool = 0; grace = streetGrace;
      trackX = 0f; offset = new Vector2(MovementConfig.StartOffsetX, MovementConfig.StartY);
      transform.position = new Vector3(trackX + offset.x, offset.y, 0); transform.rotation = Quaternion.identity; SetVisible(true);
    }
    /// <summary>Invulnerability without a hit (boss entry grace, defeat sequence). Flashes like a hit.</summary>
    public void GrantInvulnerability(float seconds){ invuln = Mathf.Max(invuln, seconds); }

    public void Tick(float dt, float scroll){
      if (cool > 0) cool -= dt;
      if (grace > 0) grace -= dt;
      if (FlightInput.CastPressed() && cool <= 0) CastSpell();
      // input -> velocity (accel/decel smoothing) -> offset; never sets position from input directly
      Vector2 wish = FlightInput.Move() * MovementConfig.MaxSpeed;
      float rate = wish.sqrMagnitude > 0.0001f ? MovementConfig.AccelRate : MovementConfig.DecelRate;
      vel = Vector2.MoveTowards(vel, wish, rate * dt);
      offset += vel * dt;
      if (!cam) cam = Camera.main;
      var hb = MovementConfig.HorizontalBounds(cam);
      if (offset.x < hb.x){ offset.x = hb.x; vel.x = Mathf.Max(vel.x, 0); }
      if (offset.x > hb.y){ offset.x = hb.y; vel.x = Mathf.Min(vel.x, 0); }
      if (offset.y > MovementConfig.TopY){ offset.y = MovementConfig.TopY; vel.y = Mathf.Min(vel.y, 0); }
      if (offset.y < MovementConfig.BottomY){
        offset.y = MovementConfig.BottomY;
        if (grace <= 0) Hit();                                   // street contact costs a heart (grace + invuln apply)
        vel.y = MovementConfig.StreetBounceSpeed;
      }
      trackX += scroll * dt;
      transform.position = new Vector3(trackX + offset.x, offset.y, 0);
      transform.rotation = Quaternion.Euler(0, 0, Mathf.Clamp(vel.y * 2.5f, -20, 20) - Mathf.Clamp(vel.x, -7, 7));
      if (invuln > 0){ invuln -= dt; SetVisible(invuln <= 0 || Mathf.FloorToInt(invuln * 12f) % 2 == 0); }
    }
    public void CastSpell(){
      cool = castCooldown; BroomstickBash.Art.BroomArt.Trigger(art, "Cast");
      Spell.Fire(transform.position + new Vector3(0.9f, 0.1f, 0), GameManager.I.ScrollSpeed + 14f);
    }
    public void Hit(){
      if (invuln > 0 || Hearts <= 0 || !GameManager.I.IsActive) return;
      Hearts--; invuln = invulnTime; BroomstickBash.Art.BroomArt.Trigger(art, "Hurt");
      Puff.Burst(transform.position, GameConfig.I.fireMat, 8);
      GameEvents.PlayerHit?.Invoke(Hearts);
      if (Hearts <= 0){ SetVisible(true); GameManager.I.GameOver(); }
    }
    void OnTriggerEnter(Collider o){ Touch(o); }
    void OnTriggerStay(Collider o){ Touch(o); }
    void Touch(Collider o){
      int l = o.gameObject.layer;
      if (l == Layers.Enemy || l == Layers.Kid || l == Layers.Obstacle || l == Layers.Hazard){
        if (invuln > 0) return;
        Hit();
        if (l == Layers.Hazard) Destroy(o.gameObject);
      }
    }
    void SetVisible(bool v){ foreach (var r in rends) if (r) r.enabled = v; }
  }
}
