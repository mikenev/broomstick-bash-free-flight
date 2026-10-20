using UnityEngine;
using UnityEngine.InputSystem;
namespace BroomstickBash {
  /// <summary>Desktop + touch input for free flight. Move: arrows/WASD (8-way, normalized) or the floating thumbstick.
  /// Cast: X, F, J, Enter, left-click, or the on-screen cast button. Space is NOT bound to gameplay (the page script stops it
  /// scrolling). Mouse clicks synthesized from touches are ignored.</summary>
  public static class FlightInput {
    public static Vector2? DebugOverride;   // dev QA autopilot only

    public static Vector2 Move(){
      if (DebugOverride.HasValue) return Vector2.ClampMagnitude(DebugOverride.Value, 1f);
      Vector2 v = Vector2.zero; var kb = Keyboard.current;
      if (kb != null){
        if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) v.x -= 1;
        if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) v.x += 1;
        if (kb.upArrowKey.isPressed || kb.wKey.isPressed) v.y += 1;
        if (kb.downArrowKey.isPressed || kb.sKey.isPressed) v.y -= 1;
      }
      if (TouchControls.I && TouchControls.I.Stick.sqrMagnitude > 0) v += TouchControls.I.Stick;
      return Vector2.ClampMagnitude(v, 1f);   // diagonals normalized
    }

    public static bool CastPressed(){
      var kb = Keyboard.current;
      if (kb != null && (kb.xKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame
          || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return true;
      if (MouseClick()) return true;
      return TouchControls.I && TouchControls.I.CastPressedThisFrame;
    }

    /// <summary>Start / restart: any key, a real left-click, or any touch.</summary>
    public static bool StartPressed(){
      var kb = Keyboard.current; if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
      if (MouseClick()) return true;
      var t = Touchscreen.current; return t != null && t.primaryTouch.press.wasPressedThisFrame;
    }

    static bool MouseClick(){
      var m = Mouse.current; if (m == null || !m.leftButton.wasPressedThisFrame) return false;
      return !TouchControls.RecentTouch;   // browsers emit compatibility mouse events after touches
    }
  }
}
