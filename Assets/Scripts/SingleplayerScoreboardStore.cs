using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class SingleplayerScoreboardStore
{
    private const int CurrentVersion = 1;
    private const string SaveFileName = "singleplayer_scoreboard.json";
    private static readonly Dictionary<string, Record> records = new();
    private static bool loaded;

    public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

    public static bool TryGet(int generation, string typeFilter, out Record record)
    {
        EnsureLoaded();
        record = null;

        string normalizedTypeFilter = string.IsNullOrWhiteSpace(typeFilter)
            ? null
            : typeFilter.Trim().ToLowerInvariant();

        foreach (var stored in records.Values)
        {
            if (stored == null || !stored.IsValid)
                continue;

            if (stored.generation != generation)
                continue;

            if (string.IsNullOrWhiteSpace(normalizedTypeFilter))
            {
                if (!string.IsNullOrWhiteSpace(stored.typeFilter))
                    continue;
                if (record == null || stored.IsBetterThan(record))
                    record = stored;
            }
            else
            {
                if (
                    !string.Equals(
                        stored.typeFilter,
                        normalizedTypeFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;
                if (record == null || stored.CompletedAtUtcValue > record.CompletedAtUtcValue)
                    record = stored;
            }
        }

        if (record == null)
            return false;

        record = record.Clone();
        return true;
    }

    public static bool HasAnyRecords()
    {
        EnsureLoaded();
        return records.Count > 0;
    }

    public static List<Record> GetRecordsSnapshot()
    {
        EnsureLoaded();
        var snapshot = new List<Record>(records.Count);
        foreach (var record in records.Values)
        {
            if (record == null || !record.IsValid)
                continue;

            record.Normalize();
            snapshot.Add(record.Clone());
        }

        return snapshot;
    }

    public static bool Remove(Record record)
    {
        if (record == null)
            return false;

        EnsureLoaded();
        record.Normalize();
        if (!records.Remove(record.Key))
            return false;

        WriteFile();
        return true;
    }

    public static bool RecordCompletion(
        int generation,
        string typeFilter,
        int total,
        float elapsedSeconds,
        int typeRevealsUsed,
        int shadowsUsed,
        bool usedFillQuiz = false
    )
    {
        if (generation < 0 || total <= 0 || elapsedSeconds < 0f)
            return false;

        EnsureLoaded();
        var next = new Record(
            generation,
            typeFilter,
            total,
            elapsedSeconds,
            typeRevealsUsed,
            shadowsUsed,
            DateTime.UtcNow.ToString("o"),
            usedFillQuiz
        );

        EnsureUniqueCompletionTime(next);
        records[next.Key] = next;

        WriteFile();
        return true;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        loaded = true;
        records.Clear();

        try
        {
            if (!File.Exists(SavePath))
                return;

            string json = File.ReadAllText(SavePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var data = JsonUtility.FromJson<FileData>(json);
            if (data?.records == null)
                return;

            bool changed = false;
            foreach (var record in data.records)
            {
                if (record == null || !record.IsValid)
                {
                    changed = true;
                    continue;
                }

                record.Normalize();
                AddLoadedRecord(record);
            }

            if (changed)
                WriteFile();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Scoreboard] Failed to load save file: {ex.Message}");
            records.Clear();
        }
    }

    private static void AddLoadedRecord(Record record)
    {
        if (record == null)
            return;

        EnsureUniqueCompletionTime(record);
        records[record.Key] = record;
    }

    private static void EnsureUniqueCompletionTime(Record record)
    {
        if (record == null)
            return;

        int attempts = 0;
        while (records.ContainsKey(record.Key))
        {
            record.completedAtUtc = record
                .CompletedAtUtcValue
                .AddTicks(++attempts)
                .ToString("o");
        }
    }

    private static void WriteFile()
    {
        try
        {
            if (records.Count == 0)
            {
                if (File.Exists(SavePath))
                    File.Delete(SavePath);
                return;
            }

            Directory.CreateDirectory(Application.persistentDataPath);
            var data = new FileData
            {
                version = CurrentVersion,
                records = new List<Record>(records.Values),
            };
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Scoreboard] Failed to write save file: {ex.Message}");
        }
    }

    [Serializable]
    private sealed class FileData
    {
        public int version = CurrentVersion;
        public List<Record> records = new();
    }

    [Serializable]
    public sealed class Record
    {
        public int generation;
        public string typeFilter;
        public int total;
        public float elapsedSeconds;
        public int typeRevealsUsed;
        public int shadowsUsed;
        public string completedAtUtc;
        public bool usedFillQuiz;

        public string Key =>
            $"{KeyFor(generation, typeFilter)}|{completedAtUtc}";

        public DateTime CompletedAtUtcValue
        {
            get
            {
                if (
                    DateTime.TryParse(
                        completedAtUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var value
                    )
                )
                {
                    return value;
                }

                return DateTime.UtcNow;
            }
        }

        public bool IsValid => generation >= 0 && total > 0 && elapsedSeconds >= 0f;

        public Record() { }

        public Record(
            int generation,
            string typeFilter,
            int total,
            float elapsedSeconds,
            int typeRevealsUsed,
            int shadowsUsed,
            string completedAtUtc,
            bool usedFillQuiz = false
        )
        {
            this.generation = generation;
            this.typeFilter = NormalizeTypeFilter(typeFilter);
            this.total = Mathf.Max(1, total);
            this.elapsedSeconds = Mathf.Max(0f, elapsedSeconds);
            this.typeRevealsUsed = Mathf.Max(0, typeRevealsUsed);
            this.shadowsUsed = Mathf.Max(0, shadowsUsed);
            this.usedFillQuiz = usedFillQuiz;
            this.completedAtUtc = string.IsNullOrWhiteSpace(completedAtUtc)
                ? DateTime.UtcNow.ToString("o")
                : completedAtUtc;
        }

        public bool IsBetterThan(Record other)
        {
            if (other == null || !other.IsValid)
                return true;

            const float epsilon = 0.05f;
            if (elapsedSeconds < other.elapsedSeconds - epsilon)
                return true;
            if (elapsedSeconds > other.elapsedSeconds + epsilon)
                return false;

            int assists = typeRevealsUsed + shadowsUsed;
            int otherAssists = other.typeRevealsUsed + other.shadowsUsed;
            if (assists != otherAssists)
                return assists < otherAssists;

            if (typeRevealsUsed != other.typeRevealsUsed)
                return typeRevealsUsed < other.typeRevealsUsed;

            return shadowsUsed < other.shadowsUsed;
        }

        public Record Clone()
        {
            return new Record(
                generation,
                typeFilter,
                total,
                elapsedSeconds,
                typeRevealsUsed,
                shadowsUsed,
                completedAtUtc,
                usedFillQuiz
            );
        }

        public void Normalize()
        {
            typeFilter = NormalizeTypeFilter(typeFilter);
            total = Mathf.Max(1, total);
            elapsedSeconds = Mathf.Max(0f, elapsedSeconds);
            typeRevealsUsed = Mathf.Max(0, typeRevealsUsed);
            shadowsUsed = Mathf.Max(0, shadowsUsed);
            if (string.IsNullOrWhiteSpace(completedAtUtc))
                completedAtUtc = DateTime.UtcNow.ToString("o");
        }

        public static string KeyFor(int generation, string typeFilter)
        {
            return $"{generation}|{NormalizeTypeFilter(typeFilter) ?? string.Empty}";
        }

        private static string NormalizeTypeFilter(string typeFilter)
        {
            return string.IsNullOrWhiteSpace(typeFilter)
                ? null
                : typeFilter.Trim().ToLowerInvariant();
        }
    }
}
