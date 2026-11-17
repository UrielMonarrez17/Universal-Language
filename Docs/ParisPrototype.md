# Paris restaurant prototype

Updated November 17, 2026. Open this existing project in Unity **6000.5.9f1**. The authored scene is `Assets/Scenes/Paris.unity`; enter through `Assets/Scenes/ImmersiveHub.unity` to test the full flow.

## Run and interact

1. Grip Earth and sweep horizontally. The direction is corrected; pitch/roll do not tilt the globe. Its France flag faces the headset independently of Earth rotation.
2. Select France to save French as the language and fade into **BookSelection.unity**, spawning in front of the book. The globe and reading room now occupy separate scenes. WORLD SELECTION returns to the observatory.
3. Open the book, turn to **The Neighborhood Café**, select it and confirm. This loads Paris; the other chapters retain their existing behavior.
4. In Paris, use physical movement, teleportation and snap turning from the existing XRI rig. Point/select the tabletop HELP, REPEAT and microphone controls. Grip the physical menu to inspect it; release it back onto the table.
5. Without a voice service, use HELP and select one of the two French response cards. This is recorded as assistance. Dialogue remains visible in French; instructions and feedback are English.
6. After completion, inspect results, REPLAY CONVERSATION or RETURN TO BOOK. Returning also places the player at the book. KEEP PRACTICING requires the optional configured voice service.

For voice and AI setup see [local voice server](../Tools/RestaurantVoice/README.md). No provider secret is stored in Unity. No French audio clips were supplied, so the offline demo currently has written French dialogue, not spoken audio.

## Assessment script

| Objective | Waiter | Example response / action |
| --- | --- | --- |
| Greet and thank | Bonjour ! Bienvenue. Voici le menu. | Bonjour, merci beaucoup ! |
| Read the menu | Je vous laisse regarder le menu. Dites-moi quand vous êtes prêt. | Grab/read menu, then Je suis prêt/prête à commander. |
| Ask a recommendation | Vous voulez un conseil ? | Qu'est-ce que vous me conseillez ? |
| Order a dish | Je vous conseille le croque-monsieur… Vous préférez un plat chaud ou une salade ? | Je voudrais un croque-monsieur / Une salade, s'il vous plaît. |
| Answer a follow-up | Et comme boisson ? Then De l'eau plate ou gazeuse ? if water | Un café / De l'eau, then Plate / Gazeuse, s'il vous plaît. |
| Listen and confirm/correct | Waiter repeats the order. Water is deliberately repeated incorrectly. | Non, de l'eau plate, s'il vous plaît. Coffee branch: Oui, c'est ça, merci. |

The prototype recognizes bounded French vocabulary/phrases, ignores accents and punctuation, rejects negative dish orders and does not accept politeness alone as success. It is **not a pronunciation grader or a general semantic language examiner**. Valid unanticipated paraphrases may need additional rules. Empty/failed transcription is retriable and does not count as a language mistake. Six milestones and assistance usage are saved through `LearningSession`; no traditional exam score is invented. Existing saves retain their milestones, with completion recalculated against the current lesson requirements.

## Scene and code

- `Paris.unity`: one active XR rig, a detailed 3D terrace with folding bistro chairs, oak tabletops, paved floor, cross-braced railings, awning and planters. The view now includes Haussmann-style city blocks with French windows, balconies, mansard roofs and chimneys, a boulevard, pavements, street lamps, park trees and the Eiffel Tower. The original imported layout and previous terrace styling remain in inactive preservation groups.
- `ImmersiveHub.unity`: only the world-selection room. `BookSelection.unity`: only the furnished reading room, physical book and its chapter controls. Each has one rig, floor collision and teleportation. `SceneJourney` handles fades/loading; `FranceSelection` and restaurant return navigation use it. Build Settings includes all three scenes; ImmersiveHub remains first.
- `AnchoredGlobe`, `FranceSelection`, `HeadCollisionFade`, `ExperienceNavigator`: direction, billboard, automatic travel/fade and return navigation.
- `RestaurantEvaluation`, `RestaurantLesson`: assessment state and editable French script. `CommunicativeEncounter` delegates restaurant transcripts while retaining its existing nonrestaurant behavior.
- `RestaurantMenu`, `WaiterPresentation`: physical inspection and actor arrival/dialogue placement. The supplied barista is a static male model: arrival is a translation, not an animated walk or a hand-delivery animation.
- `RestaurantVoiceBridge`: microphone, WAV encoding, transcription callback, synthesized spatial audio and post-assessment conversation. `Tools/RestaurantVoice`: optional local backend and mocked contract tests.
- `ParisPrototypeBuilder`: initial authoring utility; intentionally refuses to overwrite an existing generated scene. Refine the saved scene instead. `ParisVisualRefinement` adjusts the authored composition; `ParisPrototypeValidation` verifies affected behavior in Play mode.

Assets reused: the supplied Eiffel Tower, cafe and barista GLBs and Bricktown buildings under `Assets/Assets-paris`; chairs, plants and mug from `Assets/nappin/OfficeEssentialsPack`; `Assets/SkySeries Freebie/FluffballDay.mat`; existing Inter font, XRI Starter Assets rig and scoped URP performance pipeline. Tables, railing, menu and control geometry are lightweight generated primitives. No new XR package or duplicate active rig was added.

City visual update: the old Bricktown block placement and lounge chairs are preserved inactive; the visible facade geometry and bistro seating are now authored specifically for this composition. `Assets/Nicrom/Shaders/Wind` supplies imported vegetation/flowers, with copied URP materials and its existing atlas. Additional faceted broadleaf canopies create the Paris park look. A procedural summer sky replaces the earlier photographic sky. Static city and terrace decoration are merged by material into mesh assets under `Paris/City`; their redundant source renderers/objects are removed from the active scene to reduce hierarchy and draw overhead. Near furniture and railings retain separate colliders; distant streets/buildings are scenery beyond the terrace boundary. The source authoring is in `ParisCityUpgrade.cs`; backups from this task are under `Artifacts/ParisUpgrade`.

## Validation and remaining work

Latest scene-flow run: **37 Play-mode assertions passed**, including world → book → restaurant → book → world, retained assessment behavior and supported visible city materials. The earlier four mocked Python voice-contract tests remain unchanged. The batch validator disables the optional desktop simulator in memory because its keyboard UI assumes bindings unavailable in batch mode. The authored simulator configuration is preserved. OpenXR reports no available headset in this environment, as expected.

Use **Alterlife > Restaurant > Validate Paris prototype** from Edit mode (save your work first; validation opens scenes and enters Play mode). It checks globe yaw, billboard, auto-teleport, one Paris rig, missing script references, menu grab, assessment branches, invalid input, assistance, save progression and WAV encoding. Captures and logs are written to `Artifacts/Paris`.

Desktop Play-mode validation and mocked backend tests are not Quest testing. No APK was built, no physical controller/microphone was exercised, no real voice/AI request was sent and no on-device frame rate was measured. Quest deployment uses the project's Android/OpenXR configuration and Build And Run after installing this Editor's Android Build Support if missing. Start with ImmersiveHub enabled first in Build Settings; connect a developer-mode headset by USB.

Next: device/controller comfort and performance checks, final lighting/texture optimization, a rigged waitress with walking/menu delivery animations, prerecorded French lines or configured voice service, broader phrase coverage and end-to-end conversational evaluation. The apartment and customs learning gameplay remain outside this restaurant prototype.
