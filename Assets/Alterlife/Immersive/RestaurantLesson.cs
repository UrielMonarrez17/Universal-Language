using System;
using UnityEngine;

namespace Alterlife.Immersive
{
    [Serializable]
    public sealed class RestaurantLine
    {
        public string milestoneId;
        [TextArea] public string prompt;
        [TextArea] public string hint;
        public string example;
        public string alternative;
        public AudioClip voice;
    }
    [CreateAssetMenu(menuName = "Alterlife/Restaurant lesson")]
    public sealed class RestaurantLesson : ScriptableObject
    {
        public RestaurantLine[] lines;
        public bool challengeWaterConfirmation = true;
    }
}
