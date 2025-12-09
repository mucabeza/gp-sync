using System;
using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load configuration from appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Get connection string from config
            string connectionString = config.GetConnectionString("DynamicsGP");

            // Query adjusted to match your tables/columns
            string query = @"
                SELECT
                    R.SLPRSNID                                  AS SalesRepID,
                    R.SLPRSNFN                                  AS SalesRepFirstName,
                    R.SPRSNSLN                                  AS SalesRepLastName,
                    I.ITEMDESC                                  AS ProductName,
                    I.ITMCLSCD                                  AS ProductCategory,
                    H.DOCDATE                                   AS Date,
                    L.QUANTITY                                  AS TotalQuantity,
                    L.QUANTITY * L.UNITPRCE                     AS TotalSalesAmount
                FROM SOP30200 H
                INNER JOIN SOP30300 L
                    ON H.SOPTYPE = L.SOPTYPE
                    AND H.SOPNUMBE = L.SOPNUMBE
                INNER JOIN RM00101 C
                    ON H.CUSTNMBR = C.CUSTNMBR
                INNER JOIN RM00301 R
                    ON C.SLPRSNID = R.SLPRSNID
                INNER JOIN IV00101 I
                    ON L.ITEMNMBR = I.ITEMNMBR
                WHERE
                    H.SOPTYPE = 3                   -- invoices only
                    AND R.SLPRSNID = 'AAK'
                    AND H.DOCDATE >= '2025-01-01'   -- optional: start date
                    AND H.DOCDATE <  '2026-01-01'   -- optional: end date
                ORDER BY
                    SalesRepID,
                    Date;
            ";

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    Console.WriteLine("Connected to SQL Server successfully.\n");

                    Console.WriteLine("Retrieving data ...");

                    using(var writer = new StreamWriter("test.csv"))
                    {
                        writer.WriteLine("SalesRepID,SalesRepName,Product,Category,Date,Total Quantity,Total Sales Amount");
                        
                        using (SqlCommand command = new SqlCommand(query, connection))
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string salesRepId = reader["SalesRepID"].ToString();
                                string salesRepFirstName = reader["SalesRepFirstName"].ToString();
                                string salesRepLastName = reader["SalesRepLastName"].ToString();
                                string productName = reader["productName"].ToString();
                                string productCategory = reader["productCategory"].ToString();
                                string date = reader["Date"].ToString();
                                string totalQuantity = reader["TotalQuantity"].ToString();
                                string totalSalesAmount = reader["TotalSalesAmount"].ToString();

                                writer.WriteLine($"{salesRepId},{salesRepFirstName} {salesRepLastName},{productName},{productCategory},{date},{totalQuantity},{totalSalesAmount}");
                            }
                        }
                    }   

                    
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine("SQL Exception:");
                foreach (SqlError err in ex.Errors)
                {
                    Console.WriteLine($" - {err.Number}: {err.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:");
                Console.WriteLine(ex.ToString());
            }

            Console.WriteLine("\nFinished reading invoices.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
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
