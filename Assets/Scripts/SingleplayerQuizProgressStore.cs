using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class SingleplayerQuizProgressStore
{
    private const int CurrentVersion = 1;
    private const string SaveFileName = "singleplayer_quiz_progress.json";
    private static readonly Dictionary<string, Session> sessions = new();
    private static bool loaded;

    public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public static bool TryGet(int generation, string typeFilter, out Session session)
    {
        EnsureLoaded();
        return sessions.TryGetValue(Session.KeyFor(generation, typeFilter), out session);
    }

    public static void Save(Session session)
    {
        if (session == null)
            return;

        EnsureLoaded();
        session.Normalize();
        if (!session.HasStarted)
        {
            if (sessions.Remove(session.Key))
                WriteFile();
            return;
        }

        sessions[session.Key] = session;
        WriteFile();
    }

    public static void Remove(int generation, string typeFilter)
    {
        EnsureLoaded();
        if (sessions.Remove(Session.KeyFor(generation, typeFilter)))
            WriteFile();
    }

    public static void ClearAll()
    {
        loaded = true;
        sessions.Clear();

        try
        {
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Progress] Failed to delete save file: {ex.Message}");
        }
    }

    public static bool HasAnyProgress()
    {
        EnsureLoaded();
        foreach (var session in sessions.Values)
        {
            if (session != null && session.HasStarted)
                return true;
        }

        return false;
    }

    public static List<Session> GetSessionsSnapshot()
    {
        EnsureLoaded();
        var snapshot = new List<Session>(sessions.Count);
        foreach (var session in sessions.Values)
        {
            if (session == null || !session.IsValid)
                continue;

            session.Normalize();
            if (!session.HasStarted)
                continue;

            snapshot.Add(
                new Session(
                    session.generation,
                    session.typeFilter,
                    session.solvedIds,
                    session.evolutionStageHintedIds,
                    session.hintedIds,
                    session.firstLetterHintedIds,
                    session.shadowedIds,
                    session.elapsed,
                    session.running,
                    session.usedFillQuiz
                )
            );
        }

        return snapshot;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        sessions.Clear();

        try
        {
            if (!File.Exists(SavePath))
                return;

            string json = File.ReadAllText(SavePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var data = JsonUtility.FromJson<FileData>(json);
            if (data?.sessions == null)
                return;

            bool discardedSessions = false;
            foreach (var session in data.sessions)
            {
                if (session == null || !session.IsValid)
                {
                    discardedSessions = true;
                    continue;
                }

                session.Normalize();
                if (!session.HasStarted)
                {
                    discardedSessions = true;
                    continue;
                }

                sessions[session.Key] = session;
            }

            if (discardedSessions)
                WriteFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Progress] Failed to load save file: {ex.Message}");
            sessions.Clear();
        }
    }

    private static void WriteFile()
    {
        try
        {
            if (sessions.Count == 0)
            {
                if (File.Exists(SavePath))
                    File.Delete(SavePath);
                return;
            }

            Directory.CreateDirectory(Application.persistentDataPath);
            var data = new FileData
            {
                version = CurrentVersion,
                sessions = new List<Session>(sessions.Values),
            };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Progress] Failed to write save file: {ex.Message}");
        }
    }

    [Serializable]
    private sealed class FileData
    {
        public int version = CurrentVersion;
        public List<Session> sessions = new();
    }

    [Serializable]
    public sealed class Session
    {
        public int generation;
        public string typeFilter;
        public List<int> solvedIds = new();
        public List<int> evolutionStageHintedIds = new();
        public List<int> hintedIds = new();
        public List<int> firstLetterHintedIds = new();
        public List<int> shadowedIds = new();
        public float elapsed;
        public bool running;
        public bool usedFillQuiz;

        public string Key => KeyFor(generation, typeFilter);
        public bool IsValid => generation >= 0;
        public bool HasStarted =>
            (solvedIds != null && solvedIds.Count > 0)
            || (evolutionStageHintedIds != null && evolutionStageHintedIds.Count > 0)
            || (hintedIds != null && hintedIds.Count > 0)
            || (firstLetterHintedIds != null && firstLetterHintedIds.Count > 0)
            || (shadowedIds != null && shadowedIds.Count > 0);

        public Session() { }

        public Session(
            int generation,
            string typeFilter,
            IReadOnlyCollection<int> solvedIds,
            IReadOnlyCollection<int> evolutionStageHintedIds,
            IReadOnlyCollection<int> hintedIds,
            IReadOnlyCollection<int> firstLetterHintedIds,
            IReadOnlyCollection<int> shadowedIds,
            float elapsed,
            bool running,
            bool usedFillQuiz = false
        )
        {
            this.generation = generation;
            this.typeFilter = NormalizeTypeFilter(typeFilter);
            this.solvedIds = solvedIds == null ? new List<int>() : new List<int>(solvedIds);
            this.evolutionStageHintedIds =
                evolutionStageHintedIds == null
                    ? new List<int>()
                    : new List<int>(evolutionStageHintedIds);
            this.hintedIds = hintedIds == null ? new List<int>() : new List<int>(hintedIds);
            this.firstLetterHintedIds =
                firstLetterHintedIds == null
                    ? new List<int>()
                    : new List<int>(firstLetterHintedIds);
            this.shadowedIds = shadowedIds == null ? new List<int>() : new List<int>(shadowedIds);
            this.elapsed = Mathf.Max(0f, elapsed);
            this.running = running;
            this.usedFillQuiz = usedFillQuiz;
        }

        public bool Matches(int generation, string typeFilter)
        {
            return this.generation == generation
                && string.Equals(
                    this.typeFilter,
                    NormalizeTypeFilter(typeFilter),
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public void Normalize()
        {
            typeFilter = NormalizeTypeFilter(typeFilter);
            solvedIds ??= new List<int>();
            evolutionStageHintedIds ??= new List<int>();
            hintedIds ??= new List<int>();
            firstLetterHintedIds ??= new List<int>();
            shadowedIds ??= new List<int>();
            elapsed = Mathf.Max(0f, elapsed);
        }

        public static string KeyFor(int generation, string typeFilter)
        {
            return $"{generation}|{NormalizeTypeFilter(typeFilter) ?? string.Empty}";
        }

        private static string NormalizeTypeFilter(string typeFilter)
        {
            return string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter.Trim().ToLowerInvariant();
        }
    }
}
