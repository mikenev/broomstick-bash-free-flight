using UnityEngine;
namespace BroomstickBash {
  /// <summary>Side camera: follows the player's x with a lead, fixed height; slight vertical easing for 2.5D feel.</summary>
  public class SideCamera : MonoBehaviour {
    public Transform target; PlayerController pc; public float lead = MovementConfig.CameraLead, height = 5f, distance = 17f, yFollow = 0.15f;
    void LateUpdate(){
      if (!target) return;
      float y = height + (target.position.y - height) * yFollow;
      if (!pc) pc = target.GetComponentInParent<PlayerController>();
      float ax = pc ? pc.TrackX : target.position.x;            // free flight: follow the scroll anchor, not the witch
      transform.position = new Vector3(ax + lead, y, -distance);
      transform.rotation = Quaternion.Euler(2f, 0, 0);
    }
  }
}
