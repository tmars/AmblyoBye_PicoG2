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

    /// <summary>
    /// Returns true if this is the first session today (no 'start' events for today yet).
    /// </summary>
    public bool IsFirstSessionToday()
    {
        if (_db == null) return false;
        try
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var count = _db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SessionEvent WHERE date(Timestamp) = ? AND EventType = 'start'",
                today);
            return count == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns a weekly summary string: date, total minutes, video names per day.
    /// </summary>
    public string GetWeeklySummary()
    {
        if (_db == null) return null;
        try
        {
            // Get stop events from the last 7 days to calculate durations
            var events = _db.Query<SessionEvent>(
                "SELECT * FROM SessionEvent WHERE date(Timestamp) >= date('now', '-7 days') " +
                "AND EventType IN ('start', 'stop', 'pause_headset', 'resume_headset') " +
                "ORDER BY Timestamp ASC");

            // Group by date
            var dayData = new System.Collections.Generic.Dictionary<string, DayInfo>();
            double lastStart = 0;
            string lastVideo = "";

            foreach (var e in events)
            {
                string day;
                try { day = DateTime.Parse(e.Timestamp).ToLocalTime().ToString("yyyy-MM-dd"); }
                catch { continue; }

                if (!dayData.ContainsKey(day))
                    dayData[day] = new DayInfo();

                if (e.EventType == "start" || e.EventType == "resume_headset")
                {
                    lastStart = e.SessionSeconds;
                    lastVideo = e.VideoName;
                    if (!string.IsNullOrEmpty(lastVideo))
                        dayData[day].Videos.Add(lastVideo);
                }
                else if (e.EventType == "stop" || e.EventType == "pause_headset")
                {
                    dayData[day].Seconds += e.SessionSeconds - lastStart;
                    if (!string.IsNullOrEmpty(e.VideoName))
                        dayData[day].Videos.Add(e.VideoName);
                }
            }

            if (dayData.Count == 0) return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📊 Last 7 days:");

            // Iterate last 7 days in order
            for (int i = 6; i >= 0; i--)
            {
                var date = DateTime.Now.AddDays(-i);
                string key = date.ToString("yyyy-MM-dd");
                string dayName = date.ToString("ddd dd MMM");

                if (dayData.ContainsKey(key))
                {
                    var info = dayData[key];
                    int mins = (int)(info.Seconds / 60);
                    var uniqueVideos = new System.Collections.Generic.List<string>();
                    foreach (var v in info.Videos)
                    {
                        string name = Path.GetFileNameWithoutExtension(v);
                        if (!uniqueVideos.Contains(name))
                            uniqueVideos.Add(name);
                    }
                    string videos = string.Join(", ", uniqueVideos.ToArray());
                    sb.AppendLine(dayName + "  " + mins + " min  " + videos);
                }
                else
                {
                    sb.AppendLine(dayName + "  —");
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[StatsDatabase] GetWeeklySummary: " + e);
            return null;
        }
    }

    private class DayInfo
    {
        public double Seconds = 0;
        public System.Collections.Generic.List<string> Videos = new System.Collections.Generic.List<string>();
    }

    public string GetDbFilePath() => _dbPath;

    public void Close()
    {
        try { _db?.Close(); } catch { }
        _db = null;
    }
}
