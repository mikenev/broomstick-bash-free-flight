using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
namespace BroomstickBash {
  /// <summary>Mobile controls: a floating virtual thumbstick anchored where the first touch lands in the LEFT half of the
  /// screen (drag = direction, capped at StickRadiusPx; releasing returns input to zero so the witch decelerates), and a
  /// cast button at the bottom-right. Shown once a touch is seen (or a touchscreen is present). Drawn with IMGUI.</summary>
  [DefaultExecutionOrder(-100)]   // read touches before GameManager/PlayerController consume Stick / CastPressedThisFrame
  public class TouchControls : MonoBehaviour {
    public static TouchControls I;
    public Vector2 Stick { get; private set; }
    public bool CastPressedThisFrame { get; private set; }
    public bool Visible => seenTouch || Application.isMobilePlatform;   // touch laptops show keyboard hints until actually touched
    public static float LastTouchTime = -99f;
    public static bool RecentTouch => Time.unscaledTime - LastTouchTime < 0.8f;
    int stickId = -1; Vector2 anchor, knob; bool seenTouch, castHeld; Texture2D disc; GUIStyle st; int stSize;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){ if (!I) DontDestroyOnLoad(new GameObject("TouchControls").AddComponent<TouchControls>().gameObject); }
    void Awake(){ I = this; disc = MakeDisc(64); }
    void OnDestroy(){ if (disc) Destroy(disc); if (I == this) I = null; }

    float Scale => Screen.height / 720f;
    Vector2 CastCenter => new Vector2(Screen.width - (MovementConfig.CastButtonMarginPx + MovementConfig.CastButtonRadiusPx) * Scale,
                                      (MovementConfig.CastButtonMarginPx + MovementConfig.CastButtonRadiusPx) * Scale); // screen coords, y up

    void Update(){
      CastPressedThisFrame = false; castHeld = false;
      var ts = Touchscreen.current; if (ts == null){ Stick = Vector2.zero; stickId = -1; return; }
      bool stickAlive = false;
      foreach (TouchControl t in ts.touches){
        var phase = t.phase.ReadValue();
        if (phase == UnityEngine.InputSystem.TouchPhase.None) continue;
        int id = t.touchId.ReadValue(); Vector2 pos = t.position.ReadValue();
        bool active = phase == UnityEngine.InputSystem.TouchPhase.Began || phase == UnityEngine.InputSystem.TouchPhase.Moved || phase == UnityEngine.InputSystem.TouchPhase.Stationary;
        if (active){ seenTouch = true; LastTouchTime = Time.unscaledTime; }
        if (phase == UnityEngine.InputSystem.TouchPhase.Began){
          if (Vector2.Distance(pos, CastCenter) <= MovementConfig.CastButtonRadiusPx * Scale * 1.25f){ CastPressedThisFrame = true; continue; }
          if (stickId < 0 && pos.x < Screen.width * 0.5f){ stickId = id; anchor = pos; }
        }
        if (active && Vector2.Distance(pos, CastCenter) <= MovementConfig.CastButtonRadiusPx * Scale * 1.25f && id != stickId) castHeld = true;
        if (id == stickId){
          if (active){ stickAlive = true; knob = pos; }
        }
      }
      if (!stickAlive){ stickId = -1; Stick = Vector2.zero; return; }
      float r = MovementConfig.StickRadiusPx * Scale;
      var d = Vector2.ClampMagnitude(knob - anchor, r); knob = anchor + d;
      var s = d / r; Stick = s.magnitude < MovementConfig.StickDeadzone ? Vector2.zero : s;
    }

    void OnGUI(){
      if (!Visible || Event.current.type != EventType.Repaint) return;
      var gm = GameManager.I; if (gm && !gm.IsActive && gm.State != GameState.Ready) return;
      float sc = Scale, r = MovementConfig.StickRadiusPx * sc;
      if (stickId >= 0){
        Disc(anchor, r, new Color(1, 1, 1, 0.18f));
        Disc(knob, r * 0.45f, new Color(1, 1, 1, 0.55f));
      } else {
        Disc(new Vector2(Screen.width * 0.16f, Screen.height * 0.25f), r, new Color(1, 1, 1, 0.08f));   // hint where the stick goes
      }
      float cr = MovementConfig.CastButtonRadiusPx * sc;
      Disc(CastCenter, cr, castHeld ? new Color(0.6f, 1f, 0.4f, 0.75f) : new Color(0.6f, 1f, 0.4f, 0.4f));
      int fs = Mathf.RoundToInt(22 * sc);
      if (st == null || stSize != fs){ st = new GUIStyle(GUI.skin.label){ alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = fs }; stSize = fs; }
      var c = CastCenter; GUI.Label(new Rect(c.x - cr, Screen.height - c.y - cr, cr * 2, cr * 2), "CAST", st);
    }
    void Disc(Vector2 screenPos, float radius, Color col){
      GUI.color = col; GUI.DrawTexture(new Rect(screenPos.x - radius, Screen.height - screenPos.y - radius, radius * 2, radius * 2), disc); GUI.color = Color.white;
    }
    static Texture2D MakeDisc(int n){
      var t = new Texture2D(n, n, TextureFormat.RGBA32, false){ wrapMode = TextureWrapMode.Clamp };
      float c = (n - 1) * 0.5f;
      for (int y = 0; y < n; y++) for (int x = 0; x < n; x++){
        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
        t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1f - d) * 8f)));
      }
      t.Apply(); return t;
    }
  }
}
