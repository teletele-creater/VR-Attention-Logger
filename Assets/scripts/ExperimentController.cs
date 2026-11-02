using UnityEngine;

public class ExperimentController : MonoBehaviour
{
    [Header("Session Info")]
    public string participantId = "P001";
    public string trialId = "T001";

    [Header("References")]
    public GazeHeadLogger logger;

    [Tooltip("AOI表示切り替え対象。未設定ならシーン内から自動取得。")]
    public AOITag[] aoiTags;

    [Header("Keyboard Controls")]
    public KeyCode toggleRecordingKey = KeyCode.R;
    public KeyCode stopRecordingKey = KeyCode.Escape;
    public KeyCode toggleAOIVisibleKey = KeyCode.V;

    [Header("Options")]
    public bool autoStartOnPlay = false;

    [Tooltip("Play開始時にAOIを表示するか。調整時 true、本番時 false。")]
    public bool showAOIOnStart = true;

    private bool aoiVisible;

    private void Awake()
    {
        if (logger == null)
        {
            logger = Object.FindFirstObjectByType<GazeHeadLogger>();
        }

        if (aoiTags == null || aoiTags.Length == 0)
        {
            aoiTags = Object.FindObjectsByType<AOITag>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
        }
    }

    private void Start()
    {
        SetAOIVisible(showAOIOnStart);

        if (autoStartOnPlay)
        {
            StartRecording();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleRecordingKey))
        {
            ToggleRecording();
        }

        if (Input.GetKeyDown(stopRecordingKey))
        {
            StopRecording();
        }

        if (Input.GetKeyDown(toggleAOIVisibleKey))
        {
            ToggleAOIVisibility();
        }
    }

    public void StartRecording()
    {
        if (logger == null)
        {
            Debug.LogError("ExperimentController: logger が未設定です。");
            return;
        }

        logger.StartRecording(participantId, trialId);
    }

    public void StopRecording()
    {
        if (logger == null)
        {
            return;
        }

        logger.StopRecording();
    }

    public void ToggleRecording()
    {
        if (logger == null)
        {
            Debug.LogError("ExperimentController: logger が未設定です。");
            return;
        }

        if (logger.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    public void SetAOIVisible(bool visible)
    {
        aoiVisible = visible;

        if (aoiTags == null)
        {
            return;
        }

        foreach (AOITag tag in aoiTags)
        {
            if (tag != null)
            {
                tag.SetDebugVisible(visible);
            }
        }

        Debug.Log($"ExperimentController: AOI Visible = {aoiVisible}");
    }

    public void ToggleAOIVisibility()
    {
        SetAOIVisible(!aoiVisible);
    }

    public void SetParticipantId(string id)
    {
        participantId = id;
    }

    public void SetTrialId(string id)
    {
        trialId = id;
    }
}