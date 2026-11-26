// ArchNet · Hub — journal
// Every Hub action (install, service creation, error) goes to the Journal tab, the Unity
// console and Library/ArchNet/hub.log. An error carries a hint saying what to do about it.
// Docs: https://archnet-plateform.fr/#ArchNetHub

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ArchNet.Installer.Editor
{
    public enum LogLevel { Info, Warning, Error }

    [Serializable]
    public struct LogEntry
    {
        public string time;
        public LogLevel level;
        public string message;
        public string hint;
    }

    [InitializeOnLoad]
    public static class ArchNetLog
    {
        const int MaxEntries = 500;
        const long MaxFileBytes = 1_000_000;
        const string SessionKey = "ArchNet.Hub.Journal";

        [Serializable]
        class Saved { public List<LogEntry> entries = new List<LogEntry>(); }

        static readonly List<LogEntry> _entries = new List<LogEntry>();
        static bool _fileErrorReported;

        internal static string FilePath = Path.Combine("Library", "ArchNet", "hub.log");

        // Tests turn this off so their messages reach the console only, not the user's journal.
        internal static bool KeepEntries = true;

        public static event Action<LogEntry> Added;
        public static event Action Cleared;

        public static IReadOnlyList<LogEntry> Entries => _entries;

        // The journal survives script reloads (installing a package triggers one) but not an Editor restart; the file keeps everything.
        static ArchNetLog()
        {
            Saved saved = JsonUtility.FromJson<Saved>(SessionState.GetString(SessionKey, "{}"));
            if (saved?.entries != null) _entries.AddRange(saved.entries);
            AssemblyReloadEvents.beforeAssemblyReload += () => SessionState.SetString(SessionKey, JsonUtility.ToJson(new Saved { entries = _entries }));
        }

        public static void Info(string message) => Add(LogLevel.Info, message, null);
        public static void Warning(string message, string hint = null) => Add(LogLevel.Warning, message, hint);
        public static void Error(string message, string hint = null) => Add(LogLevel.Error, message, hint);

        public static void Clear()
        {
            _entries.Clear();
            Cleared?.Invoke();
        }

        /// <summary>Unity version, installed ArchNet packages and the last entries, ready to paste in a GitHub issue.</summary>
        public static string Report(IEnumerable<string> installedPackages)
        {
            var text = new StringBuilder();
            text.AppendLine($"Unity {Application.unityVersion} · {SystemInfo.operatingSystem}");
            text.AppendLine("Packages: " + string.Join(", ", installedPackages));
            text.AppendLine();
            for (int i = Math.Max(0, _entries.Count - 50); i < _entries.Count; i++) text.AppendLine(Format(_entries[i]));
            return text.ToString();
        }

        public static string Format(LogEntry entry) =>
            $"{entry.time} [{entry.level.ToString().ToUpperInvariant()}] {entry.message}" + (string.IsNullOrEmpty(entry.hint) ? "" : $" → {entry.hint}");

        static void Add(LogLevel level, string message, string hint)
        {
            var entry = new LogEntry { time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), level = level, message = message, hint = hint };
            string console = "[ArchNet Hub] " + message + (string.IsNullOrEmpty(hint) ? "" : "\n→ " + hint);
            if (level == LogLevel.Error) Debug.LogError(console);
            else if (level == LogLevel.Warning) Debug.LogWarning(console);
            else Debug.Log(console);
            if (!KeepEntries) return;

            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(0, _entries.Count - MaxEntries);
            WriteToFile(entry);
            Added?.Invoke(entry);
        }

        static void WriteToFile(LogEntry entry)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxFileBytes)
                {
                    // Keep one older file next to the current one.
                    File.Copy(FilePath, FilePath + ".1", true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath, Format(entry) + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                if (_fileErrorReported) return;
                _fileErrorReported = true;
                Debug.LogWarning($"[ArchNet Hub] Cannot write {FilePath}: {exception.Message}. The Journal tab still works.");
            }
        }
    }
}
