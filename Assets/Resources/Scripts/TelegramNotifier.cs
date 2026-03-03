using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class TelegramNotifier
{
    private string _botToken;
    private string _chatId;
    private bool _configured = false;

    private const string CONFIG_FILE = "telegram.cfg";
    private const string BASE_URL = "https://api.telegram.org/bot";

    // Session message: one message per session, updated via editMessageText
    private int _sessionMessageId = -1;
    private string _sessionHeader = "";
    private List<string> _sessionLines = new List<string>();

    public bool IsConfigured => _configured;

    public TelegramNotifier()
    {
        LoadConfig();
    }

    private void LoadConfig()
    {
        try
        {
            string configPath = Application.persistentDataPath + "/../Settings/" + CONFIG_FILE;
            if (!File.Exists(configPath))
            {
                Debug.Log("[TelegramNotifier] No config at " + configPath + ". Disabled.");
                return;
            }

            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                string value = trimmed.Substring(eq + 1).Trim();

                if (key == "bot_token") _botToken = value;
                else if (key == "chat_id") _chatId = value;
            }

            _configured = !string.IsNullOrEmpty(_botToken) && !string.IsNullOrEmpty(_chatId);
            if (_configured) Debug.Log("[TelegramNotifier] Configured for chat " + _chatId);
        }
        catch (Exception e)
        {
            Debug.LogError("[TelegramNotifier] Config load error: " + e);
            _configured = false;
        }
    }

    private string TimeNow()
    {
        return DateTime.Now.ToString("HH:mm");
    }

    private string BuildSessionText()
    {
        string text = _sessionHeader;
        if (_sessionLines.Count > 0)
            text += "\n\n" + string.Join("\n", _sessionLines.ToArray());
        return text;
    }

    /// <summary>
    /// Start a new session message. Sends a new message and saves its ID for future edits.
    /// </summary>
    public void StartSessionMessage(string videoName)
    {
        _sessionHeader = "Started: " + videoName;
        _sessionLines.Clear();
        _sessionMessageId = -1;

        if (!_configured) return;
        CoroutineRunner.Run(SendAndSaveId(BuildSessionText()));
    }

    /// <summary>
    /// Append a status line and edit the session message.
    /// </summary>
    public void AppendSessionStatus(string line)
    {
        _sessionLines.Add(line + " (" + TimeNow() + ")");

        if (!_configured) return;
        if (_sessionMessageId > 0)
            CoroutineRunner.Run(EditMessageCoroutine(_sessionMessageId, BuildSessionText()));
        else
            CoroutineRunner.Run(SendAndSaveId(BuildSessionText()));
    }

    /// <summary>
    /// Finalize session message with a closing line.
    /// </summary>
    public void EndSession(string line)
    {
        _sessionLines.Add(line + " (" + TimeNow() + ")");

        if (!_configured) return;
        string text = BuildSessionText();
        if (_sessionMessageId > 0)
        {
            // Try sync edit for OnApplicationQuit
            EditMessageSync(_sessionMessageId, text);
        }
        else
        {
            SendMessageSync(text);
        }
        _sessionMessageId = -1;
    }

    /// <summary>
    /// Send a standalone message (not part of session message).
    /// </summary>
    public void SendMessage(string text)
    {
        if (!_configured) return;
        CoroutineRunner.Run(SendMessageCoroutine(text));
    }

    private IEnumerator SendAndSaveId(string text)
    {
        string url = BASE_URL + _botToken + "/sendMessage";
        WWWForm form = new WWWForm();
        form.AddField("chat_id", _chatId);
        form.AddField("text", text);

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                // Parse message_id from response JSON
                try
                {
                    string json = req.downloadHandler.text;
                    int idx = json.IndexOf("\"message_id\":");
                    if (idx >= 0)
                    {
                        int start = idx + 13;
                        int end = json.IndexOf(",", start);
                        if (end < 0) end = json.IndexOf("}", start);
                        string idStr = json.Substring(start, end - start).Trim();
                        _sessionMessageId = int.Parse(idStr);
                    }
                }
                catch (Exception e) { Debug.LogWarning("[TelegramNotifier] Parse message_id: " + e); }
            }
            else
            {
                Debug.LogWarning("[TelegramNotifier] Send failed: " + req.error);
            }
        }
    }

    private IEnumerator EditMessageCoroutine(int messageId, string text)
    {
        string url = BASE_URL + _botToken + "/editMessageText";
        WWWForm form = new WWWForm();
        form.AddField("chat_id", _chatId);
        form.AddField("message_id", messageId.ToString());
        form.AddField("text", text);

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[TelegramNotifier] Edit failed: " + req.error);
        }
    }

    private void EditMessageSync(int messageId, string text)
    {
        if (!_configured) return;
        try
        {
            string url = BASE_URL + _botToken + "/editMessageText";
            WWWForm form = new WWWForm();
            form.AddField("chat_id", _chatId);
            form.AddField("message_id", messageId.ToString());
            form.AddField("text", text);

            var req = UnityWebRequest.Post(url, form);
            req.timeout = 3;
            var op = req.SendWebRequest();
            while (!op.isDone) { }
            req.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TelegramNotifier] Sync edit failed: " + e);
        }
    }

    public void SendMessageSync(string text)
    {
        if (!_configured) return;
        try
        {
            string url = BASE_URL + _botToken + "/sendMessage";
            WWWForm form = new WWWForm();
            form.AddField("chat_id", _chatId);
            form.AddField("text", text);

            var req = UnityWebRequest.Post(url, form);
            req.timeout = 3;
            var op = req.SendWebRequest();
            while (!op.isDone) { }
            req.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[TelegramNotifier] Sync send failed: " + e);
        }
    }

    private IEnumerator SendMessageCoroutine(string text)
    {
        string url = BASE_URL + _botToken + "/sendMessage";
        WWWForm form = new WWWForm();
        form.AddField("chat_id", _chatId);
        form.AddField("text", text);

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[TelegramNotifier] Send failed: " + req.error);
        }
    }

    public IEnumerator SendFile(string filePath, string caption)
    {
        if (!_configured) yield break;
        if (!File.Exists(filePath)) yield break;

        byte[] fileData = File.ReadAllBytes(filePath);
        string filename = Path.GetFileName(filePath);

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormDataSection("chat_id", _chatId));
        formData.Add(new MultipartFormDataSection("caption", caption ?? ""));
        formData.Add(new MultipartFormFileSection("document", fileData, filename, "application/octet-stream"));

        string url = BASE_URL + _botToken + "/sendDocument";
        using (var req = UnityWebRequest.Post(url, formData))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning("[TelegramNotifier] SendFile failed: " + req.error);
            else
                Debug.Log("[TelegramNotifier] File sent");
        }
    }
}

public class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner _instance;

    public static void Run(IEnumerator coroutine)
    {
        if (_instance == null)
        {
            var go = new GameObject("[CoroutineRunner]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CoroutineRunner>();
        }
        _instance.StartCoroutine(coroutine);
    }
}
