# Immersive Meta Quest navigation prototype

Entry scene: `Assets/Scenes/ImmersiveHub.unity` (first enabled scene in Build Settings).
The menus are now separate scenes: **ImmersiveHub** owns the globe/observatory; **BookSelection** owns the reading room/book. Selecting France loads BookSelection with a comfort fade. Its WORLD SELECTION control returns to ImmersiveHub. The restaurant chapter loads Paris, whose return control loads BookSelection.
Open the existing project in **Unity 6000.5.9f1**. The original `MainMenu`, `BasicScene` and `SampleScene` are preserved.

## Implemented flow

1. Spawn in the observatory. Physical room-scale movement is driven by the existing tracked camera.
2. Teleport near the globe; hold the controller **grip/select** on Earth and sweep the controller left/right. Only horizontal yaw rotates the Earth; wrist pitch and roll are ignored and the authored tilt stays fixed. Release to leave Earth anchored to its pedestal.
3. Point at France's flag pin or geographic surface and select it. The flag stays facing the player and its hover label identifies France / French.
4. Selection saves the country and fades into BookSelection, with the player positioned in front of the desk. The former connecting doorway is closed; the two rooms are independent scenes.
5. Select **OPEN YOUR JOURNEY** on the book's cover. The cover rotates above the binding and settles beneath the left page.
6. Select the physical **TURN** and **BACK** page corners. A rigid page sweeps through 3D space; all chapter controls are disabled during the animation.
7. Explore Contents, **Life at Home (1/5)**, **The Neighborhood Café (3/5)**, and **Customs Interview (5/5)**.
8. Select a miniature or its integrated **SELECT EXPERIENCE** control. A SELECTED stamp appears on that page, the selection is saved, and the adjacent summary shows its description and rating.
9. Select **CONFIRM EXPERIENCE**. The restaurant now loads the playable Paris prototype; see [Paris instructions](ParisPrototype.md). Unimplemented experiences retain their preparation state. Returning from Paris places the player at the book.

The integrated page-corner controls are an intentional prototype alternative to continuously dragging a flexible sheet. They animate actual page geometry, not a swapped background image.
The desk mug can be picked up; the lamp has an interactive switch. The restaurant assessment is implemented separately; apartment and customs gameplay remain future work.

## Movement and comfort

- Push a controller stick forward to enter the template's teleport mode, point at a valid floor location, then release. The existing teleport ray and valid/blocked reticles are retained.
- Move the right stick sideways for snap turning. Smooth turning remains off.
- The comfort station switches the **left** stick between teleportation and optional smooth movement. The right controller retains teleportation.
- Floors, walls, windows, door, desk and furniture have physical colliders. `ClearTeleportArea` rejects destinations whose standing capsule overlaps obstacles. The CharacterController handles artificial movement collisions.
- A physical user's head cannot be stopped by a virtual wall. `HeadCollisionFade` obscures the view when the tracked head penetrates geometry; move back to clear the fade. Keep the Quest boundary enabled.
- One original Starter Assets XR Origin is instantiated, with head tracking, two controller Near/Far interactors, input bindings, CharacterController, locomotion mediator, gravity, teleportation and snap turning. No second rig or additional XR package was introduced.
- Both controller far casters use precise rays rather than broad cone/sphere targeting, particularly important for the small France surface.

## Project and XR findings

- Unity 6000.5.9f1; URP 17.5; XR Interaction Toolkit 3.5.1; Input System 1.20; OpenXR 1.17.1; Meta OpenXR 2.5.
- The previous menu deliberately disabled locomotion and placed its content on one world-space Canvas. This hub replaces that entry flow with geometry and collider-based interaction. The old scene remains available.
- Existing Android OpenXR settings already enable Meta Quest support, Oculus Touch and Touch Plus profiles; Android uses ARM64 and IL2CPP. Existing package and user XR setting changes were preserved.
- The new rig requests floor tracking. Stereoscopic rendering and 6DoF are supplied by the existing OpenXR loader and tracked rig when a headset is connected; these are **not hardware-verified here**.
- `HubRendering` scopes a copied URP performance pipeline to this scene and restores the previous quality pipeline on scene exit. It uses 4x MSAA, one 1024-pixel directional shadow map, a 12 m shadow distance, and vertex additional lights. Point lights have no shadows. No expensive post-processing was added.
- Lighting is currently ambient plus inexpensive real-time lights, **not baked lighting**. Device profiling, light baking and final texture-platform tuning remain hardware-dependent work.

## Assets reused

| Supplied folder | Actual usage |
| --- | --- |
| `Assets/AllSkyFree/Space_AnotherPlanet` | Copied skybox material with the supplied environment map, visible through the observatory windows/roof. |
| `Assets/Planet Earth Free/Materials` | `Earth2kTexture.png` on a generated geographic UV sphere. The sphere's known longitude/latitude mapping keeps the country collider and texture aligned. |
| `Assets/nappin/OfficeEssentialsPack` | Desk1, OfficeChair, LoungeChair, Sofa1, Shelves2/3, CoffeTable, ConferenceTable, Mug, CoffePot, DeskLight, Plant1/2, Window, BookPile1 and BookOpen. Furniture is also reused in the chapter miniatures. |
| XR Starter Assets / template fonts | Existing XR Origin, controller input actions, teleport visuals, Near/Far interaction, Inter TMP font. |

Office material copies in `Assets/Alterlife/Immersive/Generated` use URP Lit and a coordinated wood/sage/navy palette. Source prefabs, models and materials are not rewritten.
No articulated book/page asset or customs miniature was supplied. The functional covers, binding, page mesh, passport and customs counter are authored geometry that can be replaced with final art while retaining their components and pivots.
Figma nodes `49:223` and `40:53` informed the globe, warm paper, chapter illustrations and difficulty stars. Their screenshots are **not used as room or menu textures**.

## Geographic accuracy

`Data/FranceGeometry.json` contains constrained triangulations of Natural Earth 1:10m France polygons, including Corsica and the overseas polygons included by that source. There is no approximate circular/rectangular country hit region.
The mesh is placed on the globe using the same longitude/latitude projection as the Earth UVs; it rotates with the globe. A 0.005-degree topology-preserving simplification is below the visual resolution of this prototype globe. This is a map-scale country boundary, not survey-grade coastline data.

Source: https://www.naturalearthdata.com/downloads/10m-cultural-vectors/10m-admin-0-countries/
The exact downloaded source URL and SHA256 are recorded in `Data/GeographySource.txt`. Natural Earth data is public domain.
`Tools/build_france_geometry.py` regenerates the JSON using Shapely 2.1+. Python/Shapely are **not** project or player dependencies; the generated JSON and meshes are committed assets ready for Unity.

## Systems and extension points

All runtime code is in `Assets/Alterlife/Immersive`:

| Component | Responsibility |
| --- | --- |
| `AnchoredGlobe` | Controller rotation delta, fixed globe position, geographic mapping. |
| `FranceSelection`, `SceneJourney` | Country hover/selection, session update and fade/loading into the standalone book scene. `StudyDoor` is retained for the original combined-room authoring utility. |
| `InteractiveBook`, `BookChapter` | Cover, page-turn animation, chapter visibility, per-page selection feedback and progress. |
| `PhysicalControl` | Reusable near/ray collider control with hover feedback and persistent scene events. |
| `ExperienceDefinition` | ScriptableObject ID, name, country, language, description, difficulty, default unlock, future scene path, milestone IDs. |
| `LearningSession` | Versioned save, country/language, selected experience, per-experience unlock/completion/progress/milestones/assistance. |
| `ExperienceNavigator` | Central selection, confirmation, future asynchronous scene loading, current in-room placeholder, return. |
| `ClearTeleportArea`, `ComfortOptions`, `HeadCollisionFade` | Safe destination checks, movement mode switch, physical head penetration feedback. |
| `HubRendering`, `LampSwitch` | Scoped rendering budget and small environmental interaction. |

Progress belongs to the save, not to mutable ScriptableObject assets. JSON is stored in PlayerPrefs under `Alterlife.Learning.v1`. The new player starts at 0%. Country selection persists, so a returning player finds the study unlocked. Tests disable persistence and do not overwrite the user's save.

### Connecting next-phase gameplay

1. Create the Apartment, Restaurant or Customs gameplay scene with the project's existing XR rig conventions.
2. Add the scene to the Build Profile scene list.
3. Set its **full scene path** on the corresponding `Data/apartment.asset`, `restaurant.asset` or `customs.asset` definition. Until a valid path is assigned, confirmation opens Coming Soon.
4. Add a `LearningSession` with the same catalog to the future scene. It loads the saved country/language/experience/progress; the hub session is scene-owned, so it does not duplicate rigs or services across scenes.
5. Call `RecordMilestone(definition, milestoneId, usedAssistance)` when the communicative goal is achieved. Known milestone IDs increment completion progress; assistance usage is tracked separately. No exam/scoring gameplay has been invented in this phase.
6. Use `ExperienceNavigator.ReturnToHub()` from the future scene's return interaction.

To add an experience, create another `ExperienceDefinition`, add it to the catalog, and author a corresponding spread with `BookChapter` and selection controls in the scene. Existing country/globe/locomotion/navigation scripts do not need to be rewritten. Spreads are intentionally authored scene content, not automatically generated at runtime from the catalog.

## Run on Meta Quest

1. In Unity Hub, add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** to Unity 6000.5.9f1. They are missing from the installed Editor used for this work; no APK was built here.
2. Open this project and `Assets/Scenes/ImmersiveHub.unity`.
3. In **File > Build Profiles**, choose Android and switch platform. Keep `ImmersiveHub` first in the enabled scene list.
4. Review **Project Settings > XR Plug-in Management > Android > OpenXR** and its validation panel. Existing Meta Quest support and controller profiles are enabled. Retain ARM64 and IL2CPP; review graphics/API and SDK requirements for the target Quest model/runtime before release.
5. Enable Developer Mode on the Quest, connect USB, and authorize USB debugging in the headset.
6. Select the connected Quest in Run Device and choose **Build And Run**. For PC testing, use Quest Link with the matching OpenXR runtime and Play Mode.
7. On hardware, verify both hands, grip/select, precise France aiming, head height, physical movement, valid/blocked teleport markers, snap turning, page reach, text readability, and frame time. Test both standing and seated use before presenting the demo.

## Validation

Use **Alterlife > Immersive > Validate Hub** in the Editor. Batch equivalent:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe' -batchmode -projectPath "$PWD" -executeMethod Alterlife.Immersive.Editor.ImmersiveHubValidation.Run -logFile "$PWD\Artifacts\Immersive\validation.log"
```

Do not add `-quit`: this asynchronous Play Mode validator exits after its checks finish.

For the split scenes, this command routes to `ParisPrototypeValidation`: it checks globe direction/billboarding, the world-to-book transition, book spawn and opening/page turning, a single rig per destination, restaurant loading, supported city materials, missing scripts, assessment branches, saved milestones, assistance, and both return routes. The older combined-room validation remains in source for legacy authoring but is not the active split-scene test path.

Actual Unity renders are saved in `Artifacts/Immersive/01-observatory.png` through `09-preparation.png`.
Editor logs include `XR_ERROR_FORM_FACTOR_UNAVAILABLE` because no headset was connected. Passing Editor tests does **not** demonstrate physical controller tracking, stereo perception, standalone performance, Android build success or on-device comfort.

`ImmersiveHubBuilder.Create` initially authored the scene; `ImmersiveHubRefinement.Apply` applies the precision-ray and visual pass. The builder refuses to overwrite an existing scene. The final saved scene is the deliverable; no setup script needs to be run before playing it.

## Next phase

Implement the three actual communicative experiences and their milestone events, replace temporary book/passport artwork if desired, add localized voice/dialogue content, and profile/bake/tune lighting and textures on the target Quest. Flexible page deformation and continuous corner dragging are optional improvements beyond this reliable rigid-page prototype.
