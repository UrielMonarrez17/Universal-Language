using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alterlife.Immersive
{
    public sealed class SceneJourney : MonoBehaviour
    {
        public const string WorldScene = "Assets/Scenes/ImmersiveHub.unity";
        public const string BookScene = "Assets/Scenes/BookSelection.unity";
        public bool bookRoom;
        bool loading;
        IEnumerator Start()
        {
            var camera = Camera.main;
            var fade = camera ? camera.GetComponent<HeadCollisionFade>() : null;
            if (bookRoom)
            {
                var session = FindAnyObjectByType<LearningSession>();
                // Supports opening the book scene directly in the Editor for authoring/testing.
                if (session && !session.FranceSelected) session.SelectFrance();
            }
            if (fade)
            {
                fade.TransitionOpacity = 1;
                for (float t = 0; t < .3f; t += Time.unscaledDeltaTime)
                { fade.TransitionOpacity = 1 - t / .3f; yield return null; }
                fade.TransitionOpacity = 0;
            }
        }
        public void OpenBook() { Load(BookScene); }
        public void OpenWorld() { Load(WorldScene); }
        public void Load(string scene)
        {
            if (loading || !Application.CanStreamedLevelBeLoaded(scene)) return;
            loading = true; StartCoroutine(Transition(scene));
        }
        IEnumerator Transition(string scene)
        {
            var camera = Camera.main;
            var fade = camera ? camera.GetComponent<HeadCollisionFade>() : null;
            for (float t = 0; t < .2f; t += Time.unscaledDeltaTime)
            { if (fade) fade.TransitionOpacity = t / .2f; yield return null; }
            if (fade) fade.TransitionOpacity = 1;
            yield return SceneManager.LoadSceneAsync(scene);
        }
    }
}
