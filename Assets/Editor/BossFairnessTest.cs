using UnityEditor;
using UnityEngine;
using BroomstickBash;
/// <summary>Editor entry point for the boss fairness gate (BossFairnessSuite; also run automatically by BuildWeb.Build).
/// Headless: Unity -batchmode -nographics -projectPath . -executeMethod BossFairnessTest.RunBatch -logFile f.log</summary>
public static class BossFairnessTest {
  [MenuItem("Broomstick Bash/Check Boss Fairness")]
  public static bool Run() => BossFairnessSuite.Run();
  public static void RunBatch(){ bool ok = Run(); if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); }
}
