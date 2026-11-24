# AI use declaration

## Scope

We used AI tools (such as ChatGPT and Gemini) to support the development and design of our VR project in Unity. This included obtaining VR development documentation, researching external API connections in Unity, exploring the XR Interaction Toolkit library, and brainstorming dynamic user interactions with the food served in the game.

Additionally, we implemented conversational AI APIs—specifically the Gemini API and an Alibaba Cloud API—directly into the game to enable realistic and fluid communication with the virtual waiter. AI image generation tools were also used to create visual assets for our UI/UX prototypes in Figma, instructional graphics for explaining the controls, and a helper mascot designed to make language learning easier.

## Usage record

Tools: Web ChatGPT, Gemini, Gemini API, Alibaba Cloud API, and AI Image Generators. The entries below summarize the main areas of assistance during development.

| Purpose | Prompt summary | Response used and adjustments | Verification and related work | Teammate | 
| ----- | ----- | ----- | ----- | ----- | 
| VR Documentation in Unity | Request structured documentation, best practices, and setup steps for developing Virtual Reality experiences in Unity. | Used the summarized guides to configure the project environment, build settings, and VR camera rigs properly. | Tested initial VR scene rendering and headset tracking inside the Unity Editor. | David Patricio, Uriel Monarrez and Juan Pablo | 
| External API Integration | Research how to connect external REST APIs and handle HTTP requests (GET/POST) asynchronously in Unity using C#. | Implemented `UnityWebRequest` scripts and JSON parsing structures adapted to our project's endpoints. | Verified API connection, response times, and error handling during runtime tests. | Uriel Monarrez | 
| XR Interaction Toolkit Exploration | Identify which components of the XR Interaction Toolkit library were most useful for our mechanics and how to implement them. | Obtained an overview of `XRGrabInteractable`, `XRSocketInteractor`, and ray/direct interactors, adapting them to our scene objects. | Tested object grabbing, hovering, and interaction events directly in VR hardware. | David Patricio | 
| Realistic Waiter Communication | Implement conversational AI APIs (Gemini API and Alibaba Cloud API) to allow the player to talk with the waiter and fox companion in a natural, realistic, and fluid way. | Connected the Gemini and Alibaba Cloud APIs to the waiter NPC, adjusting system prompts to keep the character in role and context. | Conducted conversational runtime tests to ensure coherent, fast, and natural responses. | Uriel Monarrez | 
| Visual Assets for Figma, Controls & Mascot | Generate custom images for Figma UI/UX prototypes, visual guides explaining the VR controls, and a helper mascot to facilitate language learning. | Selected and adapted the generated illustrations for the Figma layouts, control tutorials, and the language-learning mascot interface. | Reviewed visual clarity, UI consistency, and readability of the control guides and mascot inside the prototype and VR scene. | Juan Pablo | 
| Dynamic Food Interaction Ideation | Brainstorm mechanics and ideas for the user to interact dynamically with the food brought by the waiter. | Designed multi-step food interactions (e.g., grabbing, cutting, serving, or consuming items) based on the suggested mechanics. | Prototyped and tested the food physics and interaction states in the Unity scene. | David Patricio and Uriel Monarrez | 