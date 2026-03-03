using System;
using System.IO;
using UnityEngine;
using SQLite;

public class StatsDatabase
{
    private SQLiteConnection _db;
    private readonly string _dbPath;

    public class SessionEvent
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public string SessionId { get; set; }
        public string EventType { get; set; }
        public string VideoName { get; set; }
        public string Timestamp { get; set; }
        public double SessionSeconds { get; set; }
        public double VideoPosition { get; set; }
    }

    public class AppState
    {
        [PrimaryKey] public string Key { get; set; }
        public string Value { get; set; }
    }

    public StatsDatabase()
    {
        string statsDir = Application.persistentDataPath + "/../Stats";
        Directory.CreateDirectory(statsDir);
        _dbPath = Path.Combine(statsDir, "stats.db");

        try
        {
            _db = new SQLiteConnection(_dbPath);
            _db.CreateTable<SessionEvent>();
            _db.CreateTable<AppState>();
        }
        catch (Exception e)
        {
            Debug.LogError("[StatsDatabase] Failed to open DB: " + e);
            _db = null;
        }
    }

    public void LogEvent(string sessionId, string eventType, string videoName,
                         float sessionSeconds, double videoPosition)
    {
        if (_db == null) return;
        try
        {
            _db.Insert(new SessionEvent
            {
                SessionId = sessionId,
                EventType = eventType,
                VideoName = videoName ?? "",
                Timestamp = DateTime.UtcNow.ToString("o"),
                SessionSeconds = sessionSeconds,
                VideoPosition = videoPosition
            });
        }
        catch (Exception e) { Debug.LogError("[StatsDatabase] LogEvent: " + e); }
    }

    public void SetCleanShutdown(bool clean)
    {
        if (_db == null) return;
        try
        {
            _db.InsertOrReplace(new AppState { Key = "clean_shutdown", Value = clean ? "1" : "0" });
        }
        catch (Exception e) { Debug.LogError("[StatsDatabase] SetCleanShutdown: " + e); }
    }

    public bool WasPreviousShutdownClean()
    {
        if (_db == null) return true;
        try
        {
            var row = _db.Find<AppState>("clean_shutdown");
            return row != null && row.Value == "1";
        }
        catch { return true; }
    }

    public int GetTodayTotalSeconds()
    {
        if (_db == null) return 0;
        try
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var events = _db.Query<SessionEvent>(
                "SELECT * FROM SessionEvent WHERE date(Timestamp) = ? AND EventType IN ('start','stop','pause_headset','resume_headset')",
                today);

            double total = 0;
            double lastStart = 0;
            foreach (var e in events)
            {
                if (e.EventType == "start" || e.EventType == "resume_headset")
                    lastStart = e.SessionSeconds;
                else if (e.EventType == "stop" || e.EventType == "pause_headset")
                    total += e.SessionSeconds - lastStart;
            }
            return (int)total;
        }
        catch { return 0; }
    }

    public string GetDbFilePath() => _dbPath;

    public void Close()
    {
        try { _db?.Close(); } catch { }
        _db = null;
    }
}
