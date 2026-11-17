using System.Collections;
using TMPro;
using UnityEngine;

namespace Alterlife.Immersive
{
    public sealed class StudyDoor : MonoBehaviour
    {
        public Transform panel;
        public Behaviour[] studyTeleportAreas;
        public TMP_Text sign;
        public bool IsUnlocked { get; private set; }
        public bool IsOpen { get; private set; }
        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            sign.text = "THE READING ROOM\nCome in • Your French journey starts here";
            StartCoroutine(Open());
        }
        IEnumerator Open()
        {
            Vector3 from = panel.localPosition;
            for (float t = 0; t < 1; t += Time.deltaTime / 1.2f)
            {
                panel.localPosition = from + Vector3.right * (2.3f * Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            panel.localPosition = from + Vector3.right * 2.3f;
            foreach (var area in studyTeleportAreas) area.enabled = true;
            IsOpen = true;
        }
    }
}
