using System;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Enables omnidirectional direct finger poking on simple controls without allowing poke interactors to accidentally select grabables.
    /// </summary>
    public sealed class PhysicalPokeFilter : IXRPokeFilter
    {
        readonly Func<bool> enabledCheck;

        public PhysicalPokeFilter(Func<bool> isEnabled = null)
        {
            enabledCheck = isEnabled;
        }

        public bool canProcess => enabledCheck == null || enabledCheck();

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => canProcess;

        public float Process(IXRInteractor interactor, IXRInteractable interactable, float interactionStrength)
            => interactor is XRPokeInteractor ? 1f : interactionStrength;
    }
}
