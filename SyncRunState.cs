using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    /// <summary>
    /// Remembers which sync data settings already synchronized successfully, so a scheduled run
    /// that fires several times a night only does the work that is still pending.
    /// The state is per sync data setting (RecordId) plus a fingerprint of its filters, and it is
    /// only honoured for the current local calendar day.
    /// </summary>
    public class SyncRunState
    {
        private const string DEFAULT_RELATIVE_PATH = "state/sync-state.json";

        // Successes older than this are dropped on save so the file stays small.
        private const int RETENTION_DAYS = 30;

        private readonly string _filePath;
        private readonly Logger _logger;
        private readonly Dictionary<string, RecordState> _records;

        public class RecordState
        {
            [JsonPropertyName("fingerprint")]
            public string Fingerprint { get; set; } = String.Empty;

            /// <summary>Local calendar day (yyyy-MM-dd) of the last successful sync.</summary>
            [JsonPropertyName("lastSuccessLocalDate")]
            public string LastSuccessLocalDate { get; set; } = String.Empty;

            [JsonPropertyName("lastSuccessAt")]
            public string LastSuccessAt { get; set; } = String.Empty;

            [JsonPropertyName("pagesProcessed")]
            public int PagesProcessed { get; set; }
        }

        // Serialized shape of the state file.
        private class StateFile
        {
            [JsonPropertyName("records")]
            public Dictionary<string, RecordState> Records { get; set; } = new Dictionary<string, RecordState>();
        }

        private SyncRunState(string filePath, Logger logger, Dictionary<string, RecordState> records)
        {
            _filePath = filePath;
            _logger = logger;
            _records = records;
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>
        /// Loads the state file. A missing, unreadable or corrupt file yields an empty state: when in
        /// doubt we synchronize again (duplicated work is recoverable, missing data is not).
        /// </summary>
        public static SyncRunState Load(IConfigurationRoot configurationBuilder, Logger logger)
        {
            string filePath = ResolveFilePath(configurationBuilder);

            if (!File.Exists(filePath))
            {
                logger.LogInfo($"No sync state file yet at '{filePath}' - every active sync data setting will be processed.");
                return new SyncRunState(filePath, logger, new Dictionary<string, RecordState>(StringComparer.OrdinalIgnoreCase));
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var stateFile = JsonSerializer.Deserialize<StateFile>(json) ?? new StateFile();
                var records = new Dictionary<string, RecordState>(StringComparer.OrdinalIgnoreCase);
                if (stateFile.Records != null)
                {
                    foreach (var entry in stateFile.Records)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                        {
                            records[entry.Key] = entry.Value;
                        }
                    }
                }
                logger.LogInfo($"Loaded sync state from '{filePath}' ({records.Count} record(s)).");
                return new SyncRunState(filePath, logger, records);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not read sync state file '{filePath}' ({ex.Message}) - continuing with an empty state, every active sync data setting will be processed.");
                return new SyncRunState(filePath, logger, new Dictionary<string, RecordState>(StringComparer.OrdinalIgnoreCase));
            }
        }

        private static string ResolveFilePath(IConfigurationRoot configurationBuilder)
        {
            string configured = configurationBuilder["Run:StateFilePath"];
            string filePath = string.IsNullOrWhiteSpace(configured) ? DEFAULT_RELATIVE_PATH : configured;
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(AppContext.BaseDirectory, filePath);
            }
            return filePath;
        }

        /// <summary>
        /// Identity of a sync data setting. Kept as readable plain text to help troubleshooting.
        /// <para>
        /// StartDate/EndDate are deliberately NOT part of it: the Salesforce side advances the window
        /// of a recurring filter as soon as it receives the closing isLastOne payload
        /// (GPDataSyncService.updateFilter), so the window we just synced is never the window the next
        /// GET returns. Including it would make every 01:10 run look like new work. A genuine ad-hoc
        /// window arrives as a new single-use filter with its own RecordId, so it still runs.
        /// </para>
        /// BatchSize is excluded too: it only changes pagination, not the set of records synchronized.
        /// </summary>
        public static string ComputeFingerprint(SyncDataSettings syncDataSettings)
        {
            return string.Join("|", new[]
            {
                syncDataSettings.RecordId ?? String.Empty,
                syncDataSettings.SalesPersonId ?? String.Empty,
                syncDataSettings.ItemClassType ?? String.Empty,
                syncDataSettings.CustomerNumber ?? String.Empty,
                syncDataSettings.ProductNumber ?? String.Empty,
                syncDataSettings.UserId ?? String.Empty
            });
        }

        /// <summary>
        /// True when this exact sync data setting (same window and filters) already synchronized
        /// successfully earlier today. Changed filters produce a different fingerprint, so it is
        /// processed again even on the same day.
        /// </summary>
        public bool HasSucceededToday(string recordId, string fingerprint, out RecordState prior)
        {
            prior = null;
            if (string.IsNullOrWhiteSpace(recordId))
            {
                // Without a RecordId there is nothing to key the state on - always process it.
                return false;
            }

            if (!_records.TryGetValue(recordId, out var state))
            {
                return false;
            }
            if (!string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.Equals(state.LastSuccessLocalDate, Today(), StringComparison.Ordinal))
            {
                return false;
            }

            prior = state;
            return true;
        }

        public void MarkSuccess(string recordId, string fingerprint, int pagesProcessed)
        {
            if (string.IsNullOrWhiteSpace(recordId))
            {
                return;
            }

            _records[recordId] = new RecordState
            {
                Fingerprint = fingerprint,
                LastSuccessLocalDate = Today(),
                LastSuccessAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PagesProcessed = pagesProcessed
            };
        }

        /// <summary>
        /// Persists the state. Called after each successful sync data setting so a crash mid-run does
        /// not lose the successes already achieved. A write failure is logged but never aborts the
        /// run - the worst case is that the next run repeats work.
        /// </summary>
        public void Save()
        {
            try
            {
                Prune();

                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var stateFile = new StateFile { Records = _records };
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_filePath, JsonSerializer.Serialize(stateFile, options));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save sync state to '{_filePath}' - the next run may repeat work already done", ex);
            }
        }

        private void Prune()
        {
            string cutoff = DateTime.Now.AddDays(-RETENTION_DAYS).ToString("yyyy-MM-dd");
            var stale = _records
                .Where(entry => string.Compare(entry.Value.LastSuccessLocalDate, cutoff, StringComparison.Ordinal) < 0)
                .Select(entry => entry.Key)
                .ToList();
            foreach (var key in stale)
            {
                _records.Remove(key);
            }
        }

        private static string Today()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }
}
