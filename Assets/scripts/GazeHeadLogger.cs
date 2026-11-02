using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class GazeHeadLogger : MonoBehaviour
{
    [Header("Session")]
    public string participantId = "P001";
    public string trialId = "T001";

    [Header("Mode")]
    [Tooltip("true: Editor用Mock視線 / false: Quest Pro実機のOVREyeGazeを使用")]
    public bool useMockGaze = true;

    public MockGazeProvider mockGazeProvider;

    [Header("Quest Mode References")]
    [Tooltip("通常は OVRCameraRig/TrackingSpace/CenterEyeAnchor を割り当てます。頭部方向として forward を使います。")]
    public Transform headTransform;

    [Tooltip("OVREyeGaze が付いたGameObjectのTransformを割り当てます。視線方向として forward を使います。")]
    public Transform questGazeTransform;

    [Tooltip("Quest実機で視線信頼度とEyeTrackingEnabledを読むために割り当てます。")]
    public OVREyeGaze questEyeGaze;

    [Tooltip("trueなら視線レイの原点をheadTransform.positionにします。falseならquestGazeTransform.positionにします。")]
    public bool useHeadPositionAsGazeOrigin = true;

    [Range(0f, 1f)]
    public float minGazeConfidence = 0.0f;

    [Header("Raycast")]
    [Tooltip("AOI Layerだけを指定してください。")]
    public LayerMask aoiLayerMask = ~0;

    public float maxRayDistance = 10f;

    [Tooltip("AOIのColliderをIs Triggerにしても当たるように、通常はCollideにします。")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Logging")]
    [Tooltip("50Hzなら0.02。100Hzなら0.01。")]
    [Min(0.001f)]
    public float logInterval = 0.02f;

    [Tooltip("何サンプルごとにFlushするか。50なら50Hzで約1秒ごと。")]
    public int flushEverySamples = 50;

    [Tooltip("Trackingが無効なサンプルもCSVに残す。後で欠損として扱いやすいのでtrue推奨。")]
    public bool recordInvalidSamples = true;

    [Header("Debug")]
    public bool drawDebugRay = true;
    public float debugRayLength = 5f;
    public Color gazeRayColor = Color.cyan;
    public Color headRayColor = Color.yellow;

    private StreamWriter writer;
    private string currentFilePath;
    private float nextLogTime;
    private int sampleCount;

    private bool warnedNoMockProvider;
    private bool warnedNoHeadTransform;
    private bool warnedNoQuestGazeTransform;
    private bool warnedLayerMaskZero;

    private bool distractorActive;
    private string distractorLit = "None";

    public bool IsRecording { get; private set; }
    public string CurrentFilePath => currentFilePath;

    private void Awake()
    {
        if (mockGazeProvider == null)
        {
            mockGazeProvider = FindFirstObjectByType<MockGazeProvider>();
        }

        if (headTransform == null && Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }

        if (aoiLayerMask.value == 0 && !warnedLayerMaskZero)
        {
            warnedLayerMaskZero = true;
            Debug.LogWarning("GazeHeadLogger: aoiLayerMask が 0 です。AOI Layerを指定しないとRaycastが何にも当たりません。");
        }
    }

    private void Update()
    {
        if (!IsRecording)
        {
            return;
        }

        if (Time.unscaledTime >= nextLogTime)
        {
            WriteSample();
            nextLogTime = Time.unscaledTime + Mathf.Max(0.001f, logInterval);
        }
    }

    public void StartRecording()
    {
        StartRecording(participantId, trialId);
    }

    public void StartRecording(string newParticipantId, string newTrialId)
    {
        participantId = string.IsNullOrWhiteSpace(newParticipantId) ? "P_UNKNOWN" : newParticipantId.Trim();
        trialId = string.IsNullOrWhiteSpace(newTrialId) ? "T_UNKNOWN" : newTrialId.Trim();

        if (IsRecording)
        {
            Debug.LogWarning("GazeHeadLogger: すでに記録中です。");
            return;
        }

        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);

            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string safeParticipant = MakeSafeFileName(participantId);
            string safeTrial = MakeSafeFileName(trialId);

            string fileName = $"gaze_head_{safeParticipant}_{safeTrial}_{timestamp}.csv";
            currentFilePath = Path.Combine(Application.persistentDataPath, fileName);

            writer = new StreamWriter(currentFilePath, false, new UTF8Encoding(false));
            WriteHeader();

            sampleCount = 0;
            distractorActive = false;
            distractorLit = "None";
            IsRecording = true;
            nextLogTime = Time.unscaledTime;

            Debug.Log($"GazeHeadLogger: Recording started. File = {currentFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"GazeHeadLogger: CSVファイル作成に失敗しました。\n{e}");
            if (writer != null)
            {
                try { writer.Dispose(); } catch { }
                writer = null;
            }
            IsRecording = false;
        }
    }

    public void StopRecording()
    {
        if (!IsRecording && writer == null)
        {
            return;
        }

        IsRecording = false;

        try
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Close();
                writer.Dispose();
                writer = null;
            }

            Debug.Log($"GazeHeadLogger: Recording stopped. File = {currentFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"GazeHeadLogger: CSVファイルのCloseに失敗しました。\n{e}");
        }
    }

    /// <summary>
    /// distractorイベント期間中かどうかと、光っているdistractorのラベルを設定する。
    /// 各サンプルに distractor_active(0/1) と distractor_lit(None/Phone/Plush/Car...) として記録される。
    /// StimulusPresenter の点滅コルーチンから呼ばれる。
    /// </summary>
    public void SetDistractorActive(bool active, string label)
    {
        distractorActive = active;
        distractorLit = string.IsNullOrWhiteSpace(label) ? "None" : label.Trim();
    }

    private void WriteHeader()
    {
        writer.WriteLine(
            "participant_id," +
            "trial_id," +
            "timestamp," +
            "gaze_origin_x," +
            "gaze_origin_y," +
            "gaze_origin_z," +
            "gaze_dir_x," +
            "gaze_dir_y," +
            "gaze_dir_z," +
            "head_dir_x," +
            "head_dir_y," +
            "head_dir_z," +
            "angle_deg," +
            "hit_aoi," +
            "is_task_area," +
            "is_distractor_area," +
            "gaze_confidence," +
            "tracking_valid," +
            "distractor_active," +
            "distractor_lit"
        );

        writer.Flush();
    }

    private void WriteSample()
    {
        if (writer == null)
        {
            Debug.LogError("GazeHeadLogger: writer が null です。記録を停止します。");
            StopRecording();
            return;
        }

        bool gotGaze = TryGetCurrentGaze(
            out Vector3 gazeOrigin,
            out Vector3 gazeDirection,
            out Vector3 headDirection,
            out float gazeConfidence,
            out bool trackingValid
        );

        if (!gotGaze && !recordInvalidSamples)
        {
            return;
        }

        gazeDirection = SafeNormalize(gazeDirection, Vector3.forward);
        headDirection = SafeNormalize(headDirection, Vector3.forward);

        float angleDeg = Vector3.Angle(gazeDirection, headDirection);

        string hitAoi = "None";
        bool isTaskArea = false;
        bool isDistractorArea = false;

        if (gotGaze)
        {
            ResolveAOI(gazeOrigin, gazeDirection, out hitAoi, out isTaskArea, out isDistractorArea);
        }
        else
        {
            hitAoi = "Unknown";
            trackingValid = false;
            gazeConfidence = -1f;
        }

        if (drawDebugRay && gotGaze)
        {
            Debug.DrawRay(gazeOrigin, gazeDirection * debugRayLength, gazeRayColor, logInterval);
            Debug.DrawRay(gazeOrigin, headDirection * debugRayLength, headRayColor, logInterval);
        }

        string timestamp = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);

        string line =
            CsvEscape(participantId) + "," +
            CsvEscape(trialId) + "," +
            CsvEscape(timestamp) + "," +
            F(gazeOrigin.x) + "," +
            F(gazeOrigin.y) + "," +
            F(gazeOrigin.z) + "," +
            F(gazeDirection.x) + "," +
            F(gazeDirection.y) + "," +
            F(gazeDirection.z) + "," +
            F(headDirection.x) + "," +
            F(headDirection.y) + "," +
            F(headDirection.z) + "," +
            F(angleDeg) + "," +
            CsvEscape(hitAoi) + "," +
            Bool01(isTaskArea) + "," +
            Bool01(isDistractorArea) + "," +
            F(gazeConfidence) + "," +
            Bool01(trackingValid) + "," +
            Bool01(distractorActive) + "," +
            CsvEscape(distractorLit);

        try
        {
            writer.WriteLine(line);
            sampleCount++;

            if (flushEverySamples > 0 && sampleCount % flushEverySamples == 0)
            {
                writer.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"GazeHeadLogger: CSV書き込みに失敗しました。記録を停止します。\n{e}");
            StopRecording();
        }
    }

    private bool TryGetCurrentGaze(
        out Vector3 gazeOrigin,
        out Vector3 gazeDirection,
        out Vector3 headDirection,
        out float gazeConfidence,
        out bool trackingValid)
    {
        gazeOrigin = Vector3.zero;
        gazeDirection = Vector3.forward;
        headDirection = Vector3.forward;
        gazeConfidence = -1f;
        trackingValid = false;

        if (useMockGaze)
        {
            if (mockGazeProvider == null)
            {
                if (!warnedNoMockProvider)
                {
                    warnedNoMockProvider = true;
                    Debug.LogWarning("GazeHeadLogger: useMockGaze=true ですが MockGazeProvider が未設定です。");
                }

                return false;
            }

            return mockGazeProvider.TryGetGaze(
                out gazeOrigin,
                out gazeDirection,
                out headDirection,
                out gazeConfidence,
                out trackingValid
            );
        }

        Transform head = headTransform;

        if (head == null)
        {
            if (Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        if (head == null)
        {
            if (!warnedNoHeadTransform)
            {
                warnedNoHeadTransform = true;
                Debug.LogWarning("GazeHeadLogger: headTransform が未設定です。CenterEyeAnchorを割り当ててください。");
            }

            return false;
        }

        headDirection = head.forward.normalized;

        Transform gazeTf = questGazeTransform;

        if (gazeTf == null && questEyeGaze != null)
        {
            gazeTf = questEyeGaze.transform;
        }

        if (gazeTf == null)
        {
            if (!warnedNoQuestGazeTransform)
            {
                warnedNoQuestGazeTransform = true;
                Debug.LogWarning("GazeHeadLogger: questGazeTransform が未設定です。EyeGazeControllerを割り当ててください。");
            }

            gazeOrigin = head.position;
            gazeDirection = head.forward.normalized;
            gazeConfidence = -1f;
            trackingValid = false;
            return recordInvalidSamples;
        }

        gazeOrigin = useHeadPositionAsGazeOrigin ? head.position : gazeTf.position;
        gazeDirection = gazeTf.forward.normalized;

        if (questEyeGaze != null)
        {
            gazeConfidence = questEyeGaze.Confidence;
            trackingValid = questEyeGaze.EyeTrackingEnabled && gazeConfidence >= minGazeConfidence;
        }
        else
        {
            gazeConfidence = -1f;
            trackingValid = true;
        }

        return true;
    }

    private void ResolveAOI(
        Vector3 gazeOrigin,
        Vector3 gazeDirection,
        out string hitAoi,
        out bool isTaskArea,
        out bool isDistractorArea)
    {
        hitAoi = "None";
        isTaskArea = false;
        isDistractorArea = false;

        Ray ray = new Ray(gazeOrigin, gazeDirection);

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aoiLayerMask, triggerInteraction))
        {
            AOITag tag = hit.collider.GetComponentInParent<AOITag>();

            if (tag != null)
            {
                hitAoi = string.IsNullOrWhiteSpace(tag.aoiId) ? tag.gameObject.name : tag.aoiId;
                isTaskArea = tag.isTaskArea;
                isDistractorArea = tag.isDistractorArea;
            }
            else
            {
                hitAoi = hit.collider.gameObject.name;
                isTaskArea = false;
                isDistractorArea = false;
            }
        }
    }

    private void OnApplicationQuit()
    {
        StopRecording();
    }

    private void OnDestroy()
    {
        StopRecording();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause && writer != null)
        {
            try
            {
                writer.Flush();
            }
            catch (Exception e)
            {
                Debug.LogError($"GazeHeadLogger: OnApplicationPause Flush 失敗。\n{e}");
            }
        }
    }

    private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        if (v.sqrMagnitude < 0.000001f)
        {
            return fallback.normalized;
        }

        return v.normalized;
    }

    private static string F(float value)
    {
        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string Bool01(bool value)
    {
        return value ? "1" : "0";
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        bool mustQuote =
            s.Contains(",") ||
            s.Contains("\"") ||
            s.Contains("\n") ||
            s.Contains("\r");

        string escaped = s.Replace("\"", "\"\"");

        return mustQuote ? $"\"{escaped}\"" : escaped;
    }

    private static string MakeSafeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "UNKNOWN";
        }

        string result = s.Trim();

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        if (result == "." || result == "..")
        {
            return "UNKNOWN";
        }

        return result;
    }
}