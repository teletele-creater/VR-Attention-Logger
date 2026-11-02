using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 各試行の「参加者の解答・正解・正誤」を 1試行=1行 でCSVに記録する。
/// 視線の時系列(GazeHeadLogger)とは別ファイルにし、後で participant_id + trial_id で突き合わせる。
/// セッション全体（全試行）を1ファイルに追記していく方式。
/// </summary>
public class TrialResultLogger : MonoBehaviour
{
    [Header("Session")]
    public string participantId = "P001";

    [Tooltip("Flushを毎試行行う。試行数は少ないので毎回保存して取りこぼしを防ぐ。")]
    public bool flushEveryTrial = true;

    private StreamWriter writer;
    private string currentFilePath;

    public bool IsSessionOpen { get; private set; }
    public string CurrentFilePath => currentFilePath;

    /// <summary>
    /// セッションを開始し、CSVファイルを作成してヘッダーを書く。
    /// 既に開いている場合は何もしない。
    /// </summary>
    public void BeginSession(string newParticipantId)
    {
        participantId = string.IsNullOrWhiteSpace(newParticipantId) ? "P_UNKNOWN" : newParticipantId.Trim();

        if (IsSessionOpen)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);

            string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string safeParticipant = MakeSafeFileName(participantId);

            string fileName = $"trial_results_{safeParticipant}_{timestamp}.csv";
            currentFilePath = Path.Combine(Application.persistentDataPath, fileName);

            writer = new StreamWriter(currentFilePath, false, new UTF8Encoding(false));
            WriteHeader();

            IsSessionOpen = true;
            Debug.Log($"TrialResultLogger: Session started. File = {currentFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"TrialResultLogger: CSVファイル作成に失敗しました。\n{e}");
            if (writer != null)
            {
                try { writer.Dispose(); } catch { }
                writer = null;
            }
            IsSessionOpen = false;
        }
    }

    /// <summary>
    /// 1試行の結果を1行書く。selected/correct は 0=a,1=b,2=c,3=d。selected が -1 のときは無回答。
    /// セッションが未開始なら自動的に開始する（呼び忘れ対策）。
    /// </summary>
    public void LogTrial(string trialId, int passageIndex, int selected, int correct, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        if (!IsSessionOpen)
        {
            BeginSession(participantId);
        }

        if (writer == null)
        {
            Debug.LogError("TrialResultLogger: writer が null です。試行結果を記録できません。");
            return;
        }

        bool isCorrect = selected >= 0 && selected == correct;
        double durationSec = (endTime - startTime).TotalSeconds;

        string line =
            CsvEscape(participantId) + "," +
            CsvEscape(string.IsNullOrWhiteSpace(trialId) ? "T_UNKNOWN" : trialId.Trim()) + "," +
            passageIndex.ToString(CultureInfo.InvariantCulture) + "," +
            CsvEscape(AnswerToChar(selected)) + "," +
            CsvEscape(AnswerToChar(correct)) + "," +
            Bool01(isCorrect) + "," +
            startTime.ToString("o", CultureInfo.InvariantCulture) + "," +
            endTime.ToString("o", CultureInfo.InvariantCulture) + "," +
            durationSec.ToString("F3", CultureInfo.InvariantCulture);

        try
        {
            writer.WriteLine(line);
            if (flushEveryTrial)
            {
                writer.Flush();
            }

            Debug.Log($"TrialResultLogger: 試行記録 trial={trialId} passage={passageIndex} selected={AnswerToChar(selected)} correct={AnswerToChar(correct)} -> {(isCorrect ? "正解" : "不正解")}");
        }
        catch (Exception e)
        {
            Debug.LogError($"TrialResultLogger: CSV書き込みに失敗しました。\n{e}");
        }
    }

    /// <summary>
    /// セッションを終了し、ファイルを閉じる。
    /// </summary>
    public void EndSession()
    {
        if (!IsSessionOpen && writer == null)
        {
            return;
        }

        IsSessionOpen = false;

        try
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Close();
                writer.Dispose();
                writer = null;
            }

            Debug.Log($"TrialResultLogger: Session ended. File = {currentFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"TrialResultLogger: CSVファイルのCloseに失敗しました。\n{e}");
        }
    }

    private void WriteHeader()
    {
        writer.WriteLine(
            "participant_id," +
            "trial_id," +
            "passage_index," +
            "selected," +
            "correct," +
            "is_correct," +
            "timestamp_start," +
            "timestamp_end," +
            "duration_sec"
        );

        writer.Flush();
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
                Debug.LogError($"TrialResultLogger: OnApplicationPause Flush 失敗。\n{e}");
            }
        }
    }

    private void OnApplicationQuit()
    {
        EndSession();
    }

    private void OnDestroy()
    {
        EndSession();
    }

    private static string AnswerToChar(int index)
    {
        switch (index)
        {
            case 0: return "a";
            case 1: return "b";
            case 2: return "c";
            case 3: return "d";
            default: return "none";
        }
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
