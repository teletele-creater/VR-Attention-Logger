using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 読解パッセージ画像と問題画像を、1枚のパネルに順番に提示する。
/// パッセージ表示中だけ録画(GazeHeadLogger)をON/OFFする。
/// 問題画面ではスティック上下で a/b/c/d を選択し、トリガーで確定する。
/// 確定した解答・正誤は TrialResultLogger に1試行1行で記録する。
/// 進行は「実験者＝キーボード」または「被ってる人＝Questコントローラー」で。
/// </summary>
public class StimulusPresenter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("読解パネル(AOI_TaskText)の Mesh Renderer。ここに画像を表示する。")]
    public Renderer panelRenderer;

    [Tooltip("録画用。ExperimentLogger の GazeHeadLogger。")]
    public GazeHeadLogger logger;

    [Tooltip("試行結果(解答・正誤)記録用。TrialResultLogger。未設定でもパッセージ提示自体は動く。")]
    public TrialResultLogger resultLogger;

    [Tooltip("問題画面で現在の選択(A/B/C/D)を表示するTextMeshPro。未設定でも動くが、参加者に選択が見えないので推奨。")]
    public TextMeshPro selectionDisplay;

    [Header("Stimuli（passages[i] と questions[i] と correctAnswers[i] が対）")]
    public Texture2D[] passages;
    public Texture2D[] questions;

    [Tooltip("各問題の正解。0=a, 1=b, 2=c, 3=d。passages と同じ要素数にする。")]
    public int[] correctAnswers;

    [Header("Session")]
    public string participantId = "P001";

    [Header("Advance Input（パッセージ読了→問題へ）")]
    [Tooltip("実験者用：PCキーボードのこのキーで次へ。")]
    public KeyCode advanceKey = KeyCode.Space;

    [Tooltip("被ってる人用：Questコントローラーのボタンでも進める。")]
    public bool useControllerButton = true;

    [Header("Answer Input（問題画面での選択・確定）")]
    [Tooltip("実験者用：選択を上へ動かすキー。")]
    public KeyCode answerUpKey = KeyCode.UpArrow;

    [Tooltip("実験者用：選択を下へ動かすキー。")]
    public KeyCode answerDownKey = KeyCode.DownArrow;

    [Tooltip("実験者用：選択を確定するキー。")]
    public KeyCode confirmKey = KeyCode.Return;

    [Header("Distractor（注意逸脱の誘発）")]
    [Tooltip("各distractor（スマホ/くま/車）に仕込んだ点灯用Light。対象試行でこの中からランダムに1つ点滅させる。")]
    public Light[] distractorLights;

    [Tooltip("各Lightに対応するラベル。distractorLightsと同じ順・同じ数。AOITagのaoiIdと揃える(例: Phone, Plush, Car)。")]
    public string[] distractorLabels = { "Phone", "Plush", "Car" };

    [Tooltip("distractorを光らせる試行のindex（0始まり）。例: 3,6,8 = 4・7・9本目。")]
    public int[] distractorTrials = { 3, 6, 8 };

    [Tooltip("パッセージ表示から点滅開始までの遅延（秒）。")]
    public float distractorOnsetDelay = 2f;

    [Tooltip("点滅の回数。")]
    public int distractorBlinkCount = 3;

    [Tooltip("1回あたりの点灯時間（秒）。")]
    public float distractorBlinkOnTime = 0.2f;

    [Tooltip("点滅の消灯間隔（秒）。")]
    public float distractorBlinkOffTime = 0.2f;

    private int index = -1;
    private int selectedIndex = -1; // 0=a,1=b,2=c,3=d、-1=未選択
    private DateTimeOffset trialStartTime;
    private Coroutine distractorCo;

    private enum Phase { Passage, Question, Done }
    private Phase phase = Phase.Done;

    private void Start()
    {
        if (panelRenderer == null || passages == null || passages.Length == 0)
        {
            Debug.LogError("StimulusPresenter: panelRenderer か passages が未設定です。");
            enabled = false;
            return;
        }

        // 実機(Android)起動時、adbのIntent extra（--es participant_id Pxxx）があればIDを上書きする。
        // Editorやextra未指定のときはインスペクタのparticipantIdをそのまま使う。
        ApplyParticipantIdFromIntent();

        if (resultLogger != null)
        {
            resultLogger.BeginSession(participantId);
        }

        SetSelectionDisplayActive(false);
        AllLightsOff();

        index = 0;
        ShowPassage();
    }

    private void Update()
    {
        switch (phase)
        {
            case Phase.Passage:
                if (AdvancePressed()) ShowQuestion();
                break;
            case Phase.Question:
                HandleAnswerInput();
                break;
            case Phase.Done:
                break;
        }
    }

    private bool AdvancePressed()
    {
        if (Input.GetKeyDown(advanceKey)) return true;
        if (useControllerButton &&
            (OVRInput.GetDown(OVRInput.Button.One) ||
             OVRInput.GetDown(OVRInput.Button.Two) ||
             OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger)))
            return true;
        return false;
    }

    private void HandleAnswerInput()
    {
        int move = ReadVerticalStep(); // -1=上へ, +1=下へ, 0=なし
        if (move != 0)
        {
            if (selectedIndex < 0)
                selectedIndex = (move > 0) ? 0 : 3; // 未選択から：下で先頭a、上で末尾d
            else
                selectedIndex = (selectedIndex + move + 4) % 4; // 0..3を循環
            UpdateSelectionDisplay();
        }

        // 何か選んでいるときだけ確定できる（無回答での誤確定を防ぐ）
        if (selectedIndex >= 0 && ConfirmPressed())
        {
            ConfirmAnswer();
        }
    }

    private int ReadVerticalStep()
    {
        if (Input.GetKeyDown(answerDownKey)) return +1;
        if (Input.GetKeyDown(answerUpKey)) return -1;

        if (useControllerButton)
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickDown) ||
                OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickDown)) return +1;
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstickUp) ||
                OVRInput.GetDown(OVRInput.Button.SecondaryThumbstickUp)) return -1;
        }
        return 0;
    }

    private bool ConfirmPressed()
    {
        if (Input.GetKeyDown(confirmKey)) return true;
        if (useControllerButton &&
            (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) ||
             OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger)))
            return true;
        return false;
    }

    private void ConfirmAnswer()
    {
        // この試行の録画を停止
        if (logger != null) logger.StopRecording();

        // 結果を記録
        if (resultLogger != null)
        {
            int correct = (correctAnswers != null && index < correctAnswers.Length) ? correctAnswers[index] : -1;
            string trialId = "T" + (index + 1).ToString("D2");
            resultLogger.LogTrial(trialId, index, selectedIndex, correct, trialStartTime, DateTimeOffset.Now);
        }

        // 次のパッセージへ
        index++;
        if (index < passages.Length) ShowPassage();
        else ShowDone();
    }

    private void ShowPassage()
    {
        phase = Phase.Passage;
        selectedIndex = -1;
        SetSelectionDisplayActive(false);
        SetTexture(passages[index]);

        trialStartTime = DateTimeOffset.Now;
        if (logger != null) logger.StartRecording(participantId, "T" + (index + 1).ToString("D2"));
        Debug.Log($"[Presenter] パッセージ {index + 1}/{passages.Length} 表示・録画開始");

        ScheduleDistractor();
    }

    private void ShowQuestion()
    {
        phase = Phase.Question;
        StopDistractor();
        selectedIndex = -1;
        if (questions != null && index < questions.Length && questions[index] != null)
            SetTexture(questions[index]);

        SetSelectionDisplayActive(true);
        UpdateSelectionDisplay();
        Debug.Log($"[Presenter] 問題 {index + 1} 表示（録画継続中）。スティック上下で選択→トリガーで確定");
    }

    private void ShowDone()
    {
        phase = Phase.Done;
        StopDistractor();
        SetSelectionDisplayActive(false);
        if (resultLogger != null) resultLogger.EndSession();
        Debug.Log("[Presenter] 全試行終了。");
    }

    private void UpdateSelectionDisplay()
    {
        if (selectionDisplay == null) return;
        selectionDisplay.text = selectedIndex < 0 ? "?" : ((char)('A' + selectedIndex)).ToString();
    }

    private void SetSelectionDisplayActive(bool active)
    {
        if (selectionDisplay == null) return;
        selectionDisplay.gameObject.SetActive(active);
    }

    private void SetTexture(Texture2D tex)
    {
        if (tex == null) return;
        Material mat = panelRenderer.material; // インスタンス化される
        mat.SetTexture("_BaseMap", tex);       // URP Lit
        mat.mainTexture = tex;                 // 念のため
    }

    /// <summary>
    /// 実機(Android)起動時に、adbから渡されたIntent extra "participant_id" を読み、
    /// 値があれば participantId を上書きする。
    /// 例: adb shell am start -n <pkg>/<activity> --es participant_id P002
    /// Editorやextra未指定のときは何もしない（インスペクタ値のまま）。
    /// force-stop後に起動する運用なので、毎回 onCreate の新しいIntentからextraを読める。
    /// </summary>
    private void ApplyParticipantIdFromIntent()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
            {
                string pid = intent.Call<string>("getStringExtra", "participant_id");
                if (!string.IsNullOrWhiteSpace(pid))
                {
                    participantId = pid.Trim();
                    Debug.Log($"[Presenter] participantId をIntent extraから設定: {participantId}");
                }
                else
                {
                    Debug.Log($"[Presenter] Intent extra未指定。インスペクタ値を使用: {participantId}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Presenter] Intent extra読み取り失敗。インスペクタ値を使用: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// 現在の試行がdistractor対象(distractorTrialsに含まれる)なら、点滅コルーチンを開始する。
    /// </summary>
    private void ScheduleDistractor()
    {
        StopDistractor();

        if (distractorLights == null || distractorLights.Length == 0 || distractorTrials == null) return;

        bool isTarget = false;
        foreach (int t in distractorTrials)
        {
            if (t == index) { isTarget = true; break; }
        }
        if (!isTarget) return;

        // 対象試行では、登録された複数distractorからランダムに1つ選んで光らせる
        int pick = UnityEngine.Random.Range(0, distractorLights.Length);
        distractorCo = StartCoroutine(DistractorRoutine(pick));
    }

    /// <summary>
    /// パッセージ表示からdelay秒後に、選ばれたdistractorのライトを数回点滅させて消す。
    /// 点滅期間を distractor_active=1、選ばれたラベルを distractor_lit としてCSVに記録する。
    /// </summary>
    private IEnumerator DistractorRoutine(int pick)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, distractorOnsetDelay));

        Light light = (pick >= 0 && pick < distractorLights.Length) ? distractorLights[pick] : null;
        string label = (distractorLabels != null && pick >= 0 && pick < distractorLabels.Length)
            ? distractorLabels[pick] : "Distractor";

        if (logger != null) logger.SetDistractorActive(true, label);
        Debug.Log($"[Presenter] distractor点滅 開始（試行 {index + 1}, 対象={label}）");

        int blinks = Mathf.Max(1, distractorBlinkCount);
        for (int i = 0; i < blinks; i++)
        {
            if (light != null) light.enabled = true;
            yield return new WaitForSeconds(Mathf.Max(0.01f, distractorBlinkOnTime));
            if (light != null) light.enabled = false;
            yield return new WaitForSeconds(Mathf.Max(0.01f, distractorBlinkOffTime));
        }

        if (logger != null) logger.SetDistractorActive(false, "None");
        distractorCo = null;
    }

    /// <summary>
    /// 点滅を止め、全ライトを消し、記録フラグも下ろす。試行が切り替わるときに呼ぶ。
    /// </summary>
    private void StopDistractor()
    {
        if (distractorCo != null)
        {
            StopCoroutine(distractorCo);
            distractorCo = null;
        }
        AllLightsOff();
        if (logger != null) logger.SetDistractorActive(false, "None");
    }

    private void AllLightsOff()
    {
        if (distractorLights == null) return;
        foreach (Light l in distractorLights)
        {
            if (l != null) l.enabled = false;
        }
    }
}
