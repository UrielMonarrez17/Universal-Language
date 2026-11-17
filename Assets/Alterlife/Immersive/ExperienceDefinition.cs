using UnityEngine;

namespace Alterlife.Immersive
{
    [CreateAssetMenu(menuName = "Alterlife/Learning Experience")]
    public sealed class ExperienceDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public string thematicTitle;
        public string country = "France";
        public string language = "French";
        [TextArea] public string description;
        [Range(1, 5)] public int difficulty = 1;
        public bool unlockedByDefault = true;
        [Tooltip("Full scene path included in Build Profiles. Empty uses the in-room preparation state.")]
        public string scenePath;
        public string[] milestoneIds;
    }
}
