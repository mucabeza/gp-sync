using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Load configuration from appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            var logger = new Logger(config);
            var salesforceService = new SynchronizationService(config, logger);
            await salesforceService.StartSynchronizationAsync();
            Console.WriteLine("\nFinished reading invoices.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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
