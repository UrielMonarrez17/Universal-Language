using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alterlife.Immersive
{
    [Serializable]
    public sealed class ExperienceProgress
    {
        public string id;
        public bool completed;
        public bool unlocked;
        [Range(0, 1)] public float progress;
        public int assistanceUses;
        public bool requiresRetry;
        public List<string> milestones = new List<string>();
    }

    [Serializable]
    public sealed class LearningSave
    {
        public int version = 1;
        public string country;
        public string language;
        public string selectedExperience;
        public List<ExperienceProgress> experiences = new List<ExperienceProgress>();
    }

    /// <summary>One scene-owned session. Future scenes load the same versioned save through this component.</summary>
    public sealed class LearningSession : MonoBehaviour
    {
        public const string SaveKey = "Alterlife.Learning.v1";
        public ExperienceDefinition[] catalog;
        public LearningSave Data { get; private set; } = new LearningSave();
        public event Action Changed;
        public bool FranceSelected => Data.country == "France" && Data.language == "French";
        public bool PersistenceEnabled { get; set; } = true;

        void Awake() { Load(); }
        public void Load()
        {
            try { Data = JsonUtility.FromJson<LearningSave>(PlayerPrefs.GetString(SaveKey, "{}")) ?? new LearningSave(); }
            catch (ArgumentException) { Data = new LearningSave(); }
            if (Data.experiences == null) Data.experiences = new List<ExperienceProgress>();
            foreach (var item in catalog) GetProgress(item);
        }
        public ExperienceProgress GetProgress(ExperienceDefinition definition)
        {
            var entry = Data.experiences.Find(x => x.id == definition.id);
            if (entry != null) { Recalculate(entry, definition); return entry; }
            entry = new ExperienceProgress { id = definition.id, unlocked = definition.unlockedByDefault };
            Data.experiences.Add(entry);
            return entry;
        }
        public float LanguageProgress
        {
            get
            {
                if (catalog.Length == 0) return 0;
                float total = 0;
                foreach (var item in catalog) total += GetProgress(item).progress;
                return total / catalog.Length;
            }
        }
        public void SelectFrance() { Data.country = "France"; Data.language = "French"; Save(); }
        public bool SelectExperience(ExperienceDefinition definition)
        {
            if (!FranceSelected || definition.country != Data.country || !GetProgress(definition).unlocked) return false;
            Data.selectedExperience = definition.id;
            Save();
            return true;
        }
        public void RecordMilestone(ExperienceDefinition definition, string milestoneId, bool usedAssistance)
        {
            if (Array.IndexOf(definition.milestoneIds, milestoneId) < 0) return;
            var entry = GetProgress(definition);
            if (entry.milestones == null) entry.milestones = new List<string>();
            if (!entry.milestones.Contains(milestoneId)) entry.milestones.Add(milestoneId);
            if (usedAssistance) entry.assistanceUses++;
            entry.requiresRetry = false;
            Recalculate(entry, definition);
            Save();
        }

        public void RecordExamResult(
            ExperienceDefinition definition,
            IEnumerable<string> achievedMilestones,
            int autonomousObjectives,
            int level2AssistanceUses,
            bool passed)
        {
            if (definition == null) return;
            var entry = GetProgress(definition);
            if (entry.milestones == null) entry.milestones = new List<string>();
            var required = new HashSet<string>(definition.milestoneIds ?? Array.Empty<string>());
            if (required.Count == 0) return;

            entry.assistanceUses = Mathf.Max(0, level2AssistanceUses);

            if (passed)
            {
                entry.requiresRetry = false;
                if (achievedMilestones != null)
                {
                    foreach (var id in achievedMilestones)
                        if (required.Contains(id) && !entry.milestones.Contains(id))
                            entry.milestones.Add(id);
                }
                Recalculate(entry, definition);
            }
            else if (!entry.completed)
            {
                // Preserve incomplete status when autonomy threshold was not met.
                entry.requiresRetry = true;
                entry.completed = false;
                float autonomousRatio = Mathf.Clamp01((float)Mathf.Max(0, autonomousObjectives) / required.Count);
                entry.progress = Mathf.Min(autonomousRatio, (required.Count - 1f) / required.Count);
            }

            Save();
        }

        static void Recalculate(ExperienceProgress entry, ExperienceDefinition definition)
        {
            if (entry.milestones == null) entry.milestones = new List<string>();
            var required = new HashSet<string>(definition.milestoneIds ?? Array.Empty<string>());
            if (required.Count == 0) return;
            if (entry.requiresRetry && !entry.completed)
            {
                entry.progress = Mathf.Min(entry.progress, (required.Count - 1f) / required.Count);
                return;
            }
            int count = 0;
            foreach (var id in required) if (entry.milestones.Contains(id)) count++;
            entry.progress = (float)count / required.Count;
            entry.completed = count == required.Count;
        }
        public void Save()
        {
            if (PersistenceEnabled) { PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data)); PlayerPrefs.Save(); }
            Changed?.Invoke();
        }
        public void ResetInMemory() { Data = new LearningSave(); foreach (var item in catalog) GetProgress(item); Changed?.Invoke(); }
    }
}
