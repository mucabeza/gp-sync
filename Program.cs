using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    class Program
    {
         private static readonly string[] SecretKeys = new[]
        {
            "ConnectionStrings:DynamicsGP",
            "Salesforce:ClientId",
            "Salesforce:ClientSecret",
            "Salesforce:Username",
            "Salesforce:Password",
            "Salesforce:SecurityToken"
        };

        static async Task Main(string[] args)
        {
            string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
              // --seal: encode plaintext secrets in appsettings.json and rewrite the file.
            if (args.Length > 0 && args[0] == "--seal")
            {
                SealSecrets(appSettingsPath);
                Console.WriteLine("Secrets sealed successfully. Run without --seal for normal operation.");
                return;
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
                    decoded[key] = SensitiveDataCodec.Decode(encoded);
            }

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddInMemoryCollection(decoded)
                .Build();

            var logger = new Logger(config);
            var salesforceService = new SynchronizationService(config, logger);
            await salesforceService.StartSynchronizationAsync();
            Console.WriteLine("\nFinished reading invoices.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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
                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
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
