# Alterlife

Alterlife is an immersive French-learning prototype for Meta Quest 2. Players explore a globe, select France, choose the restaurant through a physical book, and practise a conversation with a waiter in Paris.

## Problem and intended users

Cost, time, and location can limit access to travel and face-to-face language practice. Alterlife provides a repeatable setting for rehearsing an unfamiliar social encounter.

The intended audience is adult beginner French learners who understand English instructions and have access to a VR headset, including university students preparing for travel. The prototype aims to broaden opportunities for practice. Learning gains and accessibility for specific disabilities have not been established by the reported tests. Headset access remains a barrier.

## Experience and scope

The main route is **globe → France → book → Paris restaurant → assessment results → repeat practice or return**. The restaurant is the playable experience. Other countries and chapters are future scope.

Players greet the waiter, inspect a menu, ask for a recommendation, order, answer follow-up questions, and confirm their order. Milestones and assistance usage describe progress. The offline beta uses recorded French speech, local French recognition, English help-request recognition, and bounded scenario rules. Free conversation requires an optional online provider. The prototype does not provide a validated pronunciation or general fluency score.

The team reports that the final build includes initial controller guidance, a companion tutorial before Paris, and expanded movement around the scene. These additions were not re-inspected while preparing these documents, as requested by the team.

## Links

- [Repository](https://github.com/UrielMonarrez17/Universal-Language)
- [Figma](https://www.figma.com/design/PYyMcxZCLBrb6vIM5RGhGS/Final?node-id=0-1)
- [Design justification](DesignJustification.md)
- [User research](UserResearch.md)
- [User testing](UserTesting.md)
- [AI use and authorship](AIUsage.md)
- [Technical prototype notes](Docs/ParisPrototype.md)
- [Add video](Video_Add/Alterlife_Add.mp4)

The repository retains its historical name Universal-Language. The product name is **Alterlife**.

## Open and build

1. Clone the repository and add its root folder to Unity Hub. It contains Assets, Packages, and ProjectSettings.
2. Install **Unity 6000.5.9f1**, matching ProjectSettings/ProjectVersion.txt, with Android Build Support, SDK & NDK Tools, and OpenJDK.
3. Open the project and allow package restoration and asset import. Dependencies include URP, OpenXR, XR Interaction Toolkit, XR Hands, and the Input System.
4. Open Assets/Scenes/ImmersiveHub.unity. The following scenes are Assets/Scenes/BookSelection.unity and Assets/Scenes/Paris.unity.
5. Use Play mode for inspection. Verify tracking, controllers, microphone input, and comfort on Quest hardware.
6. Run **Alterlife > Quest > Validate Quest Readiness**, then **Alterlife > Quest > Build Quest 2 Beta APK**. Resolve errors before distributing the result.
7. The existing builder outputs Builds/Quest/UniversalLanguage-Quest2-Beta.apk and a build summary, packaging the three main scenes.

## Install and run on Quest 2

Enable developer mode, connect a USB data cable, and accept USB debugging inside the headset. From Android SDK platform-tools run:

    adb devices
    adb install -r "PATH/UniversalLanguage-Quest2-Beta.apk"

Replace PATH with the actual APK location. Open the application from Unknown Sources. Existing builds may display the historical label **Universal Language**. Follow onboarding and accept microphone permission when entering Paris.

A generated APK is not guaranteed to be included in a repository clone. Attach the final tested executable to the submission or a GitHub release and identify its version.

## Controls and demonstration

- Move within the play area or use the available teleportation and movement options.
- Grip and rotate the globe horizontally, then select France.
- Open the book, turn pages, select the restaurant, and confirm.
- Use **X** for the wrist menu and the opposite controller to choose an action.
- Use **Y** or wrist help to summon the fox.
- Listen to the waiter, inspect the menu, and speak when the listening indicator appears. Begin with “Bonjour, merci beaucoup.”
- Select a help card to hear pronunciation, then say the answer yourself.
- Complete the encounter, review milestones, and repeat or return.

If voice fails, check microphone permission and allow initialization. Repeat clearly or request assistance. If adb reports unauthorized, accept the prompt inside the headset. Re-center the view and consult the controls if navigation feels incorrect. Audio and model attribution is in Assets/StreamingAssets/ParisVoiceCredits.txt and accompanying licenses.

## Contributions and deliverables

| Git author label | Example commit | Recorded subject |
| --- | --- | --- |
| Pato23K | 54163b3 | Upgrading the experience of the user in the French restaurant |
| Uriel | ba47b62 | CHanges on controls menu and API implementation |
| JP | 320ec0e | FoxPet |

These observed records identify commits, not exclusive authorship or individual understanding. Each member must confirm their full name/account, inspect their actual diffs, and explain decisions, verification, and AI-assisted work.

The assignment also requires Figma, the executable, the commercial, and pitch materials. Confirm participant consent, missing research details, final-build evidence, and instructor authorization for AI use before submission.
