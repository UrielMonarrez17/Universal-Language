using UnityEngine;
using UnityEngine.Rendering;

namespace Alterlife.Immersive
{
    /// <summary>Scoped hub rendering budget; restores the host project's pipeline on scene exit.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class HubRendering : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        RenderPipelineAsset previous;
        void Awake() { previous = QualitySettings.renderPipeline; if (pipeline) QualitySettings.renderPipeline = pipeline; }
        void OnDestroy() { QualitySettings.renderPipeline = previous; }
    }
}
