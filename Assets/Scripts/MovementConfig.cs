using UnityEngine;
namespace BroomstickBash {
  /// <summary>FF1 movement model: the SINGLE source of truth for the witch's free-flight numbers. FF2 (street validator /
  /// rebalance) and FF3 (boss validator) should model the witch with exactly these values.
  ///
  /// Kinematic model (per axis, per frame, dt):
  ///   wish     = input (8-way keyboard or analog stick; length clamped to 1, so diagonals are normalized) * MaxSpeed
  ///   rate     = MaxSpeed / (|wish| > 0.01 ? AccelTime : DecelTime)              (m/s^2, applied to the velocity VECTOR)
  ///   velocity = Vector2.MoveTowards(velocity, wish, rate * dt)                   (input drives velocity, never position)
  ///   offset  += velocity * dt;   world.x = trackX + offset.x;  trackX += scrollSpeed * dt (camera follows trackX)
  ///   clamp:  offset.x to HorizontalBounds, y to [BottomY, TopY]; hitting a bound zeroes velocity into that bound.
  ///   street: reaching BottomY costs a heart (unless in StreetGrace or invulnerable) and kicks velocity.y to +StreetBounceSpeed.
  /// So: full speed reached in 0.12 s over 0.42 m; from full speed to a stop in 0.12 s / 0.42 m.</summary>
  public static class MovementConfig {
    // --- speed / smoothing ---
    public const float MaxSpeed = 7f;            // m/s, same on both axes; diagonals normalized (7 m/s along the diagonal)
    public const float AccelTime = 0.12f;        // s from rest to MaxSpeed
    public const float DecelTime = 0.12f;        // s from MaxSpeed to rest after input is released
    public static float AccelRate => MaxSpeed / AccelTime;   // 58.3 m/s^2
    public static float DecelRate => MaxSpeed / DecelTime;   // 58.3 m/s^2

    // --- vertical bounds (world y; GameConfig ground 0, ceiling 10) ---
    public const float BottomY = 0.45f;          // witch centre at street contact
    public const float TopY = 9.6f;              // ceiling clamp (no damage)
    public const float StreetBounceSpeed = 4f;   // m/s upward kick after touching the street
    public const float StreetGrace = 1.25f;      // s after start/restart where street contact is free

    // --- horizontal bounds (offset from the scroll anchor trackX; the camera centre is trackX + CameraLead) ---
    public const float CameraLead = 5f;          // must match SideCamera.lead
    public const float ScreenFractionX = 0.45f;  // witch may use only the left 45% of the visible width
    public const float LeftMargin = 0.9f;        // m kept free at the left screen edge
    public const float StartOffsetX = 0f, StartY = 5f;
    // Reference values at 16:9 (camera z = -17, FOV 38): visible half-width 10.4 m -> offset.x in [-4.5, +4.0].
    public const float RefMinOffsetX = -4.5f, RefMaxOffsetX = 4.0f;

    /// <summary>Horizontal offset bounds for the current camera/aspect (falls back to the 16:9 reference).</summary>
    public static Vector2 HorizontalBounds(Camera cam){
      if (!cam) return new Vector2(RefMinOffsetX, RefMaxOffsetX);
      float dist = Mathf.Abs(cam.transform.position.z);   // gameplay plane is z = 0
      float halfH = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfW = halfH * cam.aspect;
      float left = CameraLead - halfW;                     // screen left edge relative to trackX
      return new Vector2(left + LeftMargin, left + 2f * halfW * ScreenFractionX);
    }

    // --- other gameplay constants that the validators need ---
    public const float PlayerRadius = 0.42f;     // trigger sphere radius
    public const int Hearts = 3;
    public const float InvulnTime = 1.5f, CastCooldown = 0.45f;

    // --- touch controls (pixels at 720p, scaled by Screen.height / 720) ---
    public const float StickRadiusPx = 70f, StickDeadzone = 0.12f, CastButtonRadiusPx = 64f, CastButtonMarginPx = 40f;
  }
}
