using System;
using System.Collections;
using UnityEngine;

namespace Alterlife.Immersive
{
    public sealed class WaiterPresentation : MonoBehaviour
    {
        public Transform actor;
        public Transform speech;
        [Tooltip("Legacy GLB assets face -Z; animated Blender FBX assets face +Z.")]
        public bool invertFacing = true;
        [Tooltip("Keeps the waiter's floating dialogue bubble smoothly anchored above Lucas as he walks.")]
        public bool followActorWithSpeech = true;
        public Vector3 speechBubbleOffset = new Vector3(0f, 1.98f, 0f);

        /// <summary>Set by WaiterServiceChoreographer while Lucas is walking between the table and café counter.</summary>
        [NonSerialized] public bool SuspendFacingOverride;

        Vector3 rest;
        Camera viewer;

        IEnumerator Start()
        {
            viewer = Camera.main;
            if (actor == null) yield break;
            rest = actor.position;
            Vector3 from = rest + Vector3.forward * 1.1f;
            for (float t = 0; t < 1; t += Time.deltaTime / 1.7f)
            {
                if (SuspendFacingOverride) yield break;
                actor.position = Vector3.Lerp(from, rest, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            if (!SuspendFacingOverride)
                actor.position = rest;
        }

        void LateUpdate()
        {
            if (!viewer) viewer = Camera.main;
            if (!SuspendFacingOverride && viewer && actor)
            {
                var facing = Vector3.ProjectOnPlane(viewer.transform.position - actor.position, Vector3.up);
                // Legacy GLB faces -Z of the actor wrapper; animated FBX faces +Z.
                if (facing.sqrMagnitude > .001f)
                {
                    Quaternion desired = Quaternion.LookRotation(invertFacing ? -facing : facing);
                    actor.rotation = Quaternion.Slerp(actor.rotation, desired, 1f - Mathf.Exp(-7f * Time.deltaTime));
                }
            }
            if (speech && actor && followActorWithSpeech)
            {
                Vector3 targetSpeechPos = actor.position + speechBubbleOffset;
                speech.position = Vector3.Lerp(speech.position, targetSpeechPos, 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
            if (viewer && speech)
                speech.rotation = Quaternion.LookRotation(speech.position - viewer.transform.position, Vector3.up);
        }
    }
}
