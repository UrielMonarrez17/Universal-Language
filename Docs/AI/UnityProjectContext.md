# Unity project context

Updated 2026-11-15: the immersive prototype now enters through `Assets/Scenes/ImmersiveHub.unity`, with one original XR rig, connected observatory/study geometry, an anchored geographic globe and a hinged 3D experience book. Runtime systems and ScriptableObject definitions are under `Assets/Alterlife/Immersive`; `Docs/ImmersiveHub.md` describes architecture, interactions, source assets and validation. The old flat menu and template scenes remain intact. The hub scopes a copied URP performance asset through `HubRendering`, restoring the prior pipeline on scene exit. Android Build Support is not installed in this Editor and no headset was used; hardware/build validation remains outstanding.

Inspected 2026-11-14 at baseline commit `461e28c`.

- Unity 6000.5.9f1, URP 17.5, Input System 1.20, XR Interaction Toolkit 3.5.1, OpenXR 1.17.1 and Meta OpenXR 2.5.
- Existing content is the VR template and XR samples. `SampleScene` is the original enabled build scene; `BasicScene` has the Starter Assets XR Origin, interaction manager and XR UI EventSystem.
- Existing uGUI/TMP assets include Inter Regular. Template scripts use serialized MonoBehaviour composition. No game-specific menu controller was found in the inspected scripts.
- Menu implementation lives in `Assets/Alterlife/Menu`; editor authoring builds a dedicated `Assets/Scenes/MainMenu.unity` and reusable menu prefab without changing the template scenes.
- Available validation: installed Unity Editor executable and local package sources. No Unity MCP bridge or live Editor process was available at inspection. Quest hardware validation remains separate.
- Existing user modifications to project settings and package lock were present before work; preserve them.
- Source evidence: Packages/manifest.json, ProjectSettings/ProjectVersion.txt, ProjectSettings/EditorBuildSettings.asset, Assets/Scenes/BasicScene.unity, Starter Assets XR Origin prefab, VRTemplateAssets/Scripts and Fonts/Inter.
