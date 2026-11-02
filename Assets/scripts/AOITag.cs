using UnityEngine;

[DisallowMultipleComponent]
public class AOITag : MonoBehaviour
{
    [Header("AOI Label")]
    public string aoiId = "TaskText";

    [Tooltip("読解文・設問など、タスク領域なら true")]
    public bool isTaskArea = false;

    [Tooltip("スマホなど、注意逸脱候補領域なら true")]
    public bool isDistractorArea = false;

    [Header("Debug Visualization")]
    [Tooltip("Play開始時にAOIのRendererを見せるか。調整時 true、本番時 false。")]
    public bool showRendererAtStart = true;

    [Tooltip("表示/非表示を切り替えたいRenderer。未設定なら子を含めて自動取得。")]
    public Renderer[] renderersToControl;

    private void Reset()
    {
        renderersToControl = GetComponentsInChildren<Renderer>(true);
    }

    private void Awake()
    {
        EnsureRenderers();
        SetDebugVisible(showRendererAtStart);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsureRenderers();
            SetDebugVisible(showRendererAtStart);
        }
    }
#endif

    private void EnsureRenderers()
    {
        if (renderersToControl == null || renderersToControl.Length == 0)
        {
            renderersToControl = GetComponentsInChildren<Renderer>(true);
        }
    }

    public void SetDebugVisible(bool visible)
    {
        if (renderersToControl == null)
        {
            return;
        }

        foreach (Renderer r in renderersToControl)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }
}