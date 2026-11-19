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
        Vector3 rest;
        Camera viewer;
        IEnumerator Start()
        {
            viewer = Camera.main; rest = actor.position;
            Vector3 from = rest + Vector3.forward * 1.1f;
            for (float t = 0; t < 1; t += Time.deltaTime / 1.7f)
            {
                actor.position = Vector3.Lerp(from, rest, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            actor.position = rest;
        }
        void LateUpdate()
        {
            if (!viewer) viewer = Camera.main;
            if (viewer && actor)
            {
                var facing = Vector3.ProjectOnPlane(viewer.transform.position - actor.position, Vector3.up);
                // Legacy GLB faces -Z of the actor wrapper; animated FBX faces +Z.
                if (facing.sqrMagnitude > .001f)
                    actor.rotation = Quaternion.LookRotation(invertFacing ? -facing : facing);
            }
            if (viewer && speech)
                speech.rotation = Quaternion.LookRotation(speech.position - viewer.transform.position, Vector3.up);
        }
    }
}
