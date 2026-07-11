using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    public class GPDataBaseDataReader
    {
        private static readonly string[] RequiredColumns = new[]
        {
            "DocumentDate", "SalesPersonID", "SalesPerson", "SOPNumber", "SOPType",
            "ComponentSequence", "LineItemSequence", "CustomerNumber", "CustomerName",
            "BillingCity", "ItemNumber", "ItemDesc", "ItemFamily", "Qty", "Amount",
            "ItemClassCode", "ShippingState", "ShippingCity", "ShippingZipCode"
        };

        private static readonly Regex ForbiddenKeywordsRegex = new Regex(
            @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|EXEC|EXECUTE|MERGE|GRANT|REVOKE|CREATE|xp_cmdshell|sp_executesql)\b",
            RegexOptions.IgnoreCase);

        private static readonly Regex OrderByRegex = new Regex(@"\bORDER\s+BY\b", RegexOptions.IgnoreCase);
        private static readonly Regex OffsetRegex = new Regex(@"\bOFFSET\b", RegexOptions.IgnoreCase);

        private string connectionString { get; set; }
        private SyncDataSettings syncDataSettings { get; set; }
        private Logger Logger { get; set; }
        private string coreQuery { get; set; }
        private bool schemaValidated = false;

        public GPDataBaseDataReader(IConfigurationRoot configurationBuilder, SyncDataSettings syncDataSettings, Logger logger)
        {
            this.connectionString = configurationBuilder.GetConnectionString("DynamicsGP");
            Console.WriteLine(connectionString);
            this.syncDataSettings = syncDataSettings;
            this.Logger = logger;
            Logger.LogInfo("Filters:" + JsonSerializer.Serialize(syncDataSettings));

            string queryFilePath = configurationBuilder["GpSyncQuery:FilePath"];
            if (string.IsNullOrWhiteSpace(queryFilePath))
            {
                queryFilePath = "GpSyncQuery.sql";
            }
            if (!Path.IsPathRooted(queryFilePath))
            {
                queryFilePath = Path.Combine(AppContext.BaseDirectory, queryFilePath);
            }

            this.coreQuery = LoadAndValidateQueryFile(queryFilePath);

            string filters = syncDataSettings.GetFilters();
            string assembledQuery = BuildDataQuery(filters);
            Logger.LogInfo("Assembled GP sync query: " + assembledQuery);
        }

        private string BuildDataQuery(string filters)
        {
            return $@"SELECT * FROM ({coreQuery}) AS Core
                    WHERE 1=1{filters}
                    ORDER BY DocumentDate, SalesPersonID, ComponentSequence, LineItemSequence, SOPNumber
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        }

        private string BuildCountQuery(string filters)
        {
            return $"SELECT COUNT(*) FROM ({coreQuery}) AS Core WHERE 1=1{filters};";
        }

        private static string StripSqlComments(string sql)
        {
            string noBlockComments = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            string noLineComments = Regex.Replace(noBlockComments, @"--[^\r\n]*", " ");
            return noLineComments;
        }

        private static string LoadAndValidateQueryFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new GpSyncQueryValidationException($"GP sync query file not found at '{filePath}'.");
            }

            string rawQuery = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                throw new GpSyncQueryValidationException($"GP sync query file at '{filePath}' is empty.");
            }

            string codeOnly = StripSqlComments(rawQuery).Trim();
            var errors = new List<string>();

            if (!codeOnly.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("the query must start with a single SELECT statement.");
            }
            if (ForbiddenKeywordsRegex.IsMatch(codeOnly))
            {
                errors.Add("the query contains a disallowed keyword (only a single read-only SELECT is allowed - no INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE/EXEC/EXECUTE/MERGE/GRANT/REVOKE/CREATE/xp_cmdshell/sp_executesql).");
            }
            string codeWithoutTrailingSemicolon = codeOnly.EndsWith(";") ? codeOnly.Substring(0, codeOnly.Length - 1) : codeOnly;
            if (codeWithoutTrailingSemicolon.Contains(";"))
            {
                errors.Add("the query must be a single statement (no ';' allowed except optionally at the very end).");
            }
            if (OrderByRegex.IsMatch(codeOnly))
            {
                errors.Add("the query must not include ORDER BY - it is appended automatically by the application.");
            }
            if (OffsetRegex.IsMatch(codeOnly))
            {
                errors.Add("the query must not include OFFSET/FETCH - pagination is appended automatically by the application.");
            }
            foreach (var requiredParam in new[] { "@userid", "@StartDate", "@EndDate" })
            {
                if (codeOnly.IndexOf(requiredParam, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    errors.Add($"the query is missing required parameter '{requiredParam}'.");
                }
            }

            if (errors.Count > 0)
            {
                throw new GpSyncQueryValidationException($"GP sync query file at '{filePath}' failed validation: " + string.Join(" ", errors));
            }

            string trimmedOriginal = rawQuery.TrimEnd();
            if (trimmedOriginal.EndsWith(";"))
            {
                trimmedOriginal = trimmedOriginal.Substring(0, trimmedOriginal.Length - 1).TrimEnd();
            }
            return trimmedOriginal;
        }

        private void EnsureSchemaValidated()
        {
            if (schemaValidated)
            {
                return;
            }

            using (SqlConnection connection = new SqlConnection(connectionString))
            using (SqlCommand command = new SqlCommand(coreQuery, connection))
            {
                command.Parameters.Add(new SqlParameter("@userid", SqlDbType.VarChar, 50) { Value = syncDataSettings.UserId });
                command.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.Date) { Value = syncDataSettings.StartDate });
                command.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.Date) { Value = syncDataSettings.EndDate.AddDays(1) });

                try
                {
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SchemaOnly))
                    {
                        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            actualColumns.Add(reader.GetName(i));
                        }

                        var missing = RequiredColumns.Where(c => !actualColumns.Contains(c)).ToList();
                        if (missing.Count > 0)
                        {
                            throw new GpSyncQueryValidationException(
                                "GpSyncQuery.sql is missing required output column(s): " + string.Join(", ", missing));
                        }
                    }
                }
                catch (SqlException ex)
                {
                    throw new GpSyncQueryValidationException("GpSyncQuery.sql failed validation against the database: " + ex.Message, ex);
                }
            }

            schemaValidated = true;
        }

        public List<GpDataSync> GetData(int pageNumber)
        {
            EnsureSchemaValidated();

            List<GpDataSync> gpDataSyncRequests = new List<GpDataSync>();
            string filters = syncDataSettings.GetFilters();
            string query = BuildDataQuery(filters);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddRange(syncDataSettings.GetParameters());
                    command.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = (pageNumber - 1) * this.syncDataSettings.BatchSize });
                    command.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = this.syncDataSettings.BatchSize });
                    try
                    {

                        connection.Open();
                        Console.WriteLine("Connected to SQL Server successfully.\n");
                        using (SqlDataReader reader = command.ExecuteReader())
                        {

                            while (reader.Read())
                            {
                                GpDataSync gpDataSyncRequest = new GpDataSync
                                {
                                    salesRepId = reader["SalesPersonID"].ToString(),
                                    salesRepName = reader["SalesPerson"].ToString(),
                                    productCode = reader["ItemNumber"].ToString(),
                                    productId = reader["ItemNumber"].ToString(),
                                    productName = reader["ItemDesc"].ToString(),
                                    productFamily = reader["ItemFamily"].ToString(),
                                    accountNumber = reader["CustomerNumber"].ToString(),
                                    accountName = reader["CustomerName"].ToString(),
                                    salesDate = ((DateTime)reader["DocumentDate"]).Ticks,
                                    quantity = Convert.ToDecimal(reader["Qty"]),
                                    amount = Convert.ToDecimal(reader["Amount"]),
                                    invoiceNumber = reader["SOPNumber"].ToString(),
                                    sopType = Convert.ToInt32(reader["SOPType"]),
                                    lineItemSequence = Convert.ToInt64(reader["LineItemSequence"]),
                                    componentSequence = Convert.ToInt64(reader["ComponentSequence"]),
                                    productClassCode = reader["ItemClassCode"].ToString(),
                                    billingCity = reader["BillingCity"].ToString(),
                                    shippingCity = reader["ShippingCity"].ToString(),
                                    shippingState = reader["ShippingState"].ToString(),
                                    shippingZipCode = reader["ShippingZipCode"].ToString()


                                };
                                gpDataSyncRequests.Add(gpDataSyncRequest);
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error: " + ex.Message);
                        Logger.LogError("Exception while reading data from GP database", ex);
                    }
                    return gpDataSyncRequests;
                }
            }
        }
        public int GetTotalPages()
        {
            EnsureSchemaValidated();

            int count = 0;
            string filters = syncDataSettings.GetFilters();
            string query = BuildCountQuery(filters);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    // Add parameters to the query
                    command.Parameters.AddRange(syncDataSettings.GetParameters());
                    try
                    {

                        connection.Open();
                        // ExecuteScalar retrieves the single value and returns it as an object
                        object result = command.ExecuteScalar();

                        // Check if the result is not null before converting
                        if (result != null)
                        {
                            count = Convert.ToInt32(result);
                             Logger.LogInfo($"Total Items to process: {count}");
                        }

                    }
                    catch (Exception ex)
                    {
                        // Handle exceptions appropriately (log or throw)
                        Console.WriteLine("Error: " + ex.Message);
                        Logger.LogError("Exception while counting records from GP database", ex);
                    }
                }
            }
            return (int)Math.Ceiling((double)count / syncDataSettings.BatchSize);
        }
    }
}
