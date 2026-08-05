using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    class Program
    {
         private static readonly string[] SecretKeys = new[]
        {
            "ConnectionStrings:DynamicsGP",
            "Salesforce:ClientId",
            "Salesforce:ClientSecret"
        };

        // Keeps two concurrent syncs (for example a manual run and the 01:10 scheduled run) from
        // overlapping. "Global\" so it is shared across sessions - the task runs as SYSTEM.
        private const string SINGLE_INSTANCE_MUTEX_NAME = @"Global\MaxSfGpSync";

        static async Task<int> Main(string[] args)
        {
            var options = RunOptions.Parse(args);

            if (options.UnknownArguments.Count > 0)
            {
                Console.WriteLine($"Unknown argument(s): {string.Join(", ", options.UnknownArguments)}");
                Console.WriteLine();
                RunOptions.PrintUsage();
                return 1;
            }
            if (options.Help)
            {
                RunOptions.PrintUsage();
                return 0;
            }

            string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
              // --seal: encode plaintext secrets in appsettings.json and rewrite the file.
            if (options.Seal)
            {
                SealSecrets(appSettingsPath);
                Console.WriteLine("Secrets sealed successfully. Run without --seal for normal operation.");
                return 0;
            }
            // Load configuration from appsettings.json
            // Load configuration from appsettings.json.
            var rawConfig = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            var decoded = new Dictionary<string, string>();
            foreach (var key in SecretKeys)
            {
                var encoded = rawConfig[key];
                if (!string.IsNullOrEmpty(encoded))
                {
                    if (LooksLikeBase64(encoded))
                    {
                        try
                        {
                            decoded[key] = SensitiveDataCodec.Decode(encoded);
                        }
                        catch
                        {
                            // Fall back to raw value when secret is not sealed with this codec.
                            decoded[key] = encoded;
                        }
                    }
                    else
                    {
                        decoded[key] = encoded;
                    }
                }
            }

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddInMemoryCollection(decoded)
                // Enables secure overrides like MAXSFGP_Salesforce__ClientSecret at install/runtime.
                .AddEnvironmentVariables(prefix: "MAXSFGP_")
                .Build();

            var logger = new Logger(config);
            logger.LogInfo(options.Scheduled
                ? $"Run mode: scheduled (skip settings already synced today: {options.SkipAlreadySucceeded})"
                : "Run mode: manual (every active sync data setting is processed)");

            Mutex singleInstance = null;
            try
            {
                // Only scheduled runs stand down: a manual run must never be blocked.
                if (options.Scheduled && !TryAcquireSingleInstanceLock(out singleInstance))
                {
                    logger.LogWarning("Another sync instance is already running - this scheduled run is skipped.");
                    Console.WriteLine("Another sync instance is already running - this scheduled run is skipped.");
                    return 0;
                }

                var state = SyncRunState.Load(config, logger);
                var salesforceService = new SynchronizationService(config, logger, options, state);
                var outcome = await salesforceService.StartSynchronizationAsync();

                Console.WriteLine($"\nFinished reading invoices. {outcome.Summary}");
                return outcome.ExitCode;
            }
            finally
            {
                if (singleInstance != null)
                {
                    try
                    {
                        singleInstance.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                        // Not owned (never acquired) - nothing to release.
                    }
                    singleInstance.Dispose();
                }
            }
        }

        /// <summary>
        /// Tries to take the single-instance lock. If the mutex cannot be created or inspected at all
        /// (unexpected ACL, for example), the run proceeds: skipping a sync is worse than overlapping.
        /// </summary>
        private static bool TryAcquireSingleInstanceLock(out Mutex mutex)
        {
            mutex = null;
            Mutex candidate = null;
            try
            {
                candidate = new Mutex(false, SINGLE_INSTANCE_MUTEX_NAME);
                if (!candidate.WaitOne(TimeSpan.Zero))
                {
                    candidate.Dispose();
                    return false;
                }
                mutex = candidate;
                return true;
            }
            catch (AbandonedMutexException)
            {
                // Previous holder died without releasing it: the wait succeeded and we own it now.
                mutex = candidate;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not evaluate the single-instance lock ({ex.Message}) - continuing.");
                return true;
            }
        }
        private static void SealSecrets(string appSettingsPath)
        {
            string json = File.ReadAllText(appSettingsPath);
            var doc = JsonDocument.Parse(json);
            var dict = JsonToDictionary(doc.RootElement, "");

            // Encode each secret key.
            foreach (var key in SecretKeys)
            {
                if (dict.TryGetValue(key, out var plain) && !string.IsNullOrEmpty(plain))
                    dict[key] = SensitiveDataCodec.Encode(plain);
            }

            // Rebuild the JSON object with encoded values.
            var root = DictionaryToNestedDict(dict);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(appSettingsPath, JsonSerializer.Serialize(root, options));
            Console.WriteLine($"Sealed: {appSettingsPath}");
        }

        // Flattens a JsonElement into "Section:Key" -> "value" pairs.
        private static Dictionary<string, string> JsonToDictionary(JsonElement element, string prefix)
        {
            var result = new Dictionary<string, string>();
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
                    foreach (var kv in JsonToDictionary(prop.Value, key))
                        result[kv.Key] = kv.Value;
                }
            }
            else
            {
                result[prefix] = element.GetRawText().Trim('"');
            }
            return result;
        }

        // Rebuilds a nested Dictionary<string, object> from flat "Section:Key" pairs.
        private static Dictionary<string, object> DictionaryToNestedDict(Dictionary<string, string> flat)
        {
            var root = new Dictionary<string, object>();
            foreach (var kv in flat)
            {
                var parts = kv.Key.Split(':');
                var current = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!current.ContainsKey(parts[i]))
                        current[parts[i]] = new Dictionary<string, object>();
                    current = (Dictionary<string, object>)current[parts[i]];
                }
                current[parts[^1]] = kv.Value;
            }
            return root;
        }
        private static String normalizeString(String input)
        {
            return input?.Trim().Replace(",", " ") ?? String.Empty;
        }

        private static bool LooksLikeBase64(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length % 4 != 0)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isValid =
                    (c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '+' || c == '/' || c == '=';

                if (!isValid)
                {
                    return false;
                }
            }

            return true;
        }

        static void VerifyTables(SqlConnection connection)
        {
            // Print all tables
            PrintAllTables(connection);

            // Validate tables exist
            string[] requiredTables = {
                "SOP30200",
                "SOP30300",
                "RM00101",
                "RM00301",
                "IV00101" };

            foreach (var table in requiredTables)
            {
                if (!TableExists(connection, table))
                {
                    Console.WriteLine($"ERROR: Required table '{table}' does NOT exist in this database!");
                    Console.WriteLine("Please check your Dynamics GP database selection or configuration.");
                    return; // stop program
                }
            }

            Console.WriteLine("All required tables verified.\n");
        }

        static bool TableExists(SqlConnection conn, string tableName)
        {
            using var cmd = new SqlCommand(@"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_NAME = @table", conn);

            cmd.Parameters.AddWithValue("@table", tableName);

            int count = (int)cmd.ExecuteScalar();
            return count > 0;
        }


        static void PrintAllTables(SqlConnection conn)
        {
            using var cmd = new SqlCommand(@"
                SELECT TABLE_SCHEMA, TABLE_NAME 
                FROM INFORMATION_SCHEMA.TABLES
                ORDER BY TABLE_SCHEMA, TABLE_NAME;", conn);

            using var reader = cmd.ExecuteReader();

            Console.WriteLine("Tables in the database:");
            Console.WriteLine("----------------------------------");

            while (reader.Read())
            {
                string schema = reader["TABLE_SCHEMA"].ToString();
                string name = reader["TABLE_NAME"].ToString();
                Console.WriteLine($"{schema}.{name}");
            }

            Console.WriteLine("----------------------------------\n");
        }

    }
}
