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

    public void SendMessage(string text)
    {
        if (!_configured) return;
        CoroutineRunner.Run(SendMessageCoroutine(text));
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
