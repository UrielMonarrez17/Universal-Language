using System.Collections;
using UnityEngine;

namespace Alterlife.Immersive
{
    public sealed class InteractiveBook : MonoBehaviour
    {
        public Transform coverPivot;
        public Transform turningPage;
        public GameObject leftPageBlock;
        public GameObject[] spreads;
        public GameObject openControl;
        public GameObject nextControl;
        public GameObject previousControl;
        public ExperienceNavigator navigator;
        public bool IsOpen { get; private set; }
        public bool IsTurning { get; private set; }
        public int CurrentSpread { get; private set; }
        public float turnDuration = .8f;

        void Start() { if (leftPageBlock) leftPageBlock.SetActive(false); ShowSpread(); }
        public void Open()
        {
            if (IsOpen || IsTurning) return;
            StartCoroutine(OpenCover());
        }
        IEnumerator OpenCover()
        {
            IsTurning = true;
            openControl.SetActive(false);
            Vector3 binding = coverPivot.localPosition;
            for (float t = 0; t < 1; t += Time.deltaTime / 1.2f)
            {
                coverPivot.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(0, 180, Mathf.SmoothStep(0, 1, t)));
                // The leather settles beneath the exposed left page instead of covering its text.
                coverPivot.localPosition = binding + Vector3.down * (.07f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1, t)));
                yield return null;
            }
            coverPivot.localRotation = Quaternion.Euler(0, 0, 180);
            coverPivot.localPosition = binding + Vector3.down * .07f;
            if (leftPageBlock) leftPageBlock.SetActive(true);
            IsOpen = true;
            IsTurning = false;
            ShowSpread();
        }
        public void Next() => TurnTo(CurrentSpread + 1);
        public void Previous() => TurnTo(CurrentSpread - 1);
        public void TurnTo(int index)
        {
            if (!IsOpen || IsTurning || index < 0 || index >= spreads.Length || index == CurrentSpread) return;
            StartCoroutine(Turn(index));
        }
        IEnumerator Turn(int index)
        {
            IsTurning = true;
            navigator.ClearPreview();
            foreach (var spread in spreads) spread.SetActive(false);
            nextControl.SetActive(false);
            previousControl.SetActive(false);
            turningPage.gameObject.SetActive(true);
            bool forward = index > CurrentSpread;
            yield return Rotate(turningPage, forward ? 0 : 180, forward ? 180 : 0, turnDuration);
            turningPage.gameObject.SetActive(false);
            CurrentSpread = index;
            IsTurning = false;
            ShowSpread();
        }
        IEnumerator Rotate(Transform pivot, float from, float to, float duration)
        {
            for (float t = 0; t < 1; t += Time.deltaTime / duration)
            {
                pivot.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t)));
                yield return null;
            }
            pivot.localRotation = Quaternion.Euler(0, 0, to);
        }
        void ShowSpread()
        {
            for (int i = 0; i < spreads.Length; i++) spreads[i].SetActive(IsOpen && i == CurrentSpread);
            nextControl.SetActive(IsOpen && CurrentSpread < spreads.Length - 1);
            previousControl.SetActive(IsOpen && CurrentSpread > 0);
            turningPage.gameObject.SetActive(false);
        }
    }
}
