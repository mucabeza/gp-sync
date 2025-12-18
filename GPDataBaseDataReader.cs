using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    public class GPDataBaseDataReader
    {
        private string connectionString { get; set; }
        private SyncDataSettings syncDataSettings { get; set; }
        private Logger Logger { get; set; }

        public GPDataBaseDataReader(IConfigurationRoot configurationBuilder, SyncDataSettings syncDataSettings, Logger logger)
        {
            this.connectionString = configurationBuilder.GetConnectionString("DynamicsGP");
            this.syncDataSettings = syncDataSettings;
            this.Logger = logger;
            Logger.LogInfo("Filters:" + JsonSerializer.Serialize(syncDataSettings));
        }
        public List<GpDataSyncRequest> GetData(int pageNumber)
        {
            List<GpDataSyncRequest> gpDataSyncRequests = new List<GpDataSyncRequest>();
            string query = @"
                    SELECT  
                        H.DOCDATE  as   DocumentDate,
                            CASE ISNULL(SH. CS_Shipto, 1)
                                WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                                ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                            END
                         as   SalesPersonID,
                        H.SOPNUMBE as   SOPNumber, 
                        H.SOPTYPE  as   SOPType,
                        L.CMPNTSEQ as   ComponentSequence,
                        L.LNITMSEQ as   LineItemSequence,
                        H.CUSTNMBR as   CustomerNumber,
                        L.ITEMNMBR as   ItemNumber,
                        L.QUANTITY as   Qty,
                        L.QUANTITY * L.UNITPRCE as  Amount,
                        I.ITMCLSCD        as ItemClassCode             
                    FROM [PD].[dbo].[SOP30300] L
                        LEFT JOIN [PD].[dbo].[SOP30200] H
                            ON H.SOPTYPE = L.SOPTYPE
                            AND H.SOPNUMBE = L.SOPNUMBE

                        LEFT JOIN [PD].[dbo].[RM00101] RM1
                            ON RM1.CUSTNMBR = H.CUSTNMBR

                        LEFT JOIN [PD].[dbo].[CS_IV00101] I
                            ON L.ITEMNMBR = I.ITEMNMBR

                        LEFT JOIN [PD].[dbo].[IV40400] E
                            ON E.ITMCLSCD = I.ITMCLSCD

                        LEFT JOIN [PD].[dbo].[RM00102] RM2
                            ON RM2.CUSTNMBR = H.CUSTNMBR
                            AND RM2.ADRSCODE = H.PRSTADCD

                        LEFT JOIN [PD].[dbo].[CS_SHIPT] SH
                            ON SH. CUSTNMBR = RM1.CUSTNMBR

                        LEFT JOIN [PD].[dbo].[CS_STATE] ST_STATE
                            ON ST_STATE.STATE = H.STATE
                            AND LTRIM(RTRIM(ST_STATE.CITY)) = ''

                        LEFT JOIN [PD].[dbo].[CS_STATE] ST_CITY
                            ON ST_CITY.STATE = H.STATE
                            AND ST_CITY.CITY = H.CITY
                            AND LTRIM(RTRIM(ST_CITY. CITY)) <> ''
                     WHERE 
                        CASE ISNULL(SH. CS_Shipto, 1)
                                WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                                ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                            END IS NOT NULL AND 
                        CASE ISNULL(SH. CS_Shipto, 1)
                                WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                                ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                            END!='' AND  
                        H.SOPTYPE  IN (3,4) AND 
                        [VOIDSTTS]= 0 
                        AND L.QUANTITY <> 0
                        AND (
                                (
                                    ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 0)
                                    AND
                                    (
                                        ((SELECT COUNT(*) FROM CS_SRepList(@userid)) = 0) 
                                        OR
                                        (CASE ISNULL(SH.CS_Shipto, 1) 
                                            WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID) 
                                            ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '') 
                                        END IN (SELECT CSSREP FROM CS_SRepList(@userid)))
                                    )
                                )
                                OR
                                (
                                    ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 1)
                                    AND
                                    (RM1.SLPRSNID = (SELECT CS_SalesRep FROM CSUSRep WHERE CS_User = @userid))
                                )
                            )
                     AND [DOCDATE]>= @StartDate AND [DOCDATE]< @EndDate";

            // Add conditional SLPRSNID filter
            string filters = syncDataSettings.GetFilters();
            query += filters;
            query += " ORDER BY H.DOCDATE," +
            " CASE ISNULL(SH.CS_Shipto, 1)  WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID) ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '') END ASC " +
            " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

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
                                GpDataSyncRequest gpDataSyncRequest = new GpDataSyncRequest
                                {
                                    salesRepId = reader["SalesPersonID"].ToString(),
                                    productCode = reader["ItemNumber"].ToString(),
                                    accountNumber = reader["CustomerNumber"].ToString(),
                                    salesDate = ((DateTime)reader["DocumentDate"]).Ticks,
                                    quantity = Convert.ToDecimal(reader["Qty"]),
                                    amount = Convert.ToDecimal(reader["Amount"]),
                                    invoiceNumber = reader["SOPNumber"].ToString(),
                                    sopType = Convert.ToInt32(reader["SOPType"]),
                                    lineItemSequence = Convert.ToInt64(reader["LineItemSequence"]),
                                    componentSequence = Convert.ToInt64(reader["ComponentSequence"]),
                                    productClassCode = reader["ItemClassCode"].ToString()

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
            int count = 0;
            string query = @"
                    SELECT  
                        COUNT(*) 
                     FROM [PD].[dbo].[SOP30300] L
                        LEFT JOIN [PD].[dbo].[SOP30200] H
                            ON H.SOPTYPE = L.SOPTYPE
                            AND H.SOPNUMBE = L.SOPNUMBE

                        LEFT JOIN [PD].[dbo].[RM00101] RM1
                            ON RM1.CUSTNMBR = H.CUSTNMBR

                        LEFT JOIN [PD].[dbo].[CS_IV00101] I
                            ON L.ITEMNMBR = I.ITEMNMBR

                        LEFT JOIN [PD].[dbo].[IV40400] E
                            ON E.ITMCLSCD = I.ITMCLSCD

                        LEFT JOIN [PD].[dbo].[RM00102] RM2
                            ON RM2.CUSTNMBR = H.CUSTNMBR
                            AND RM2.ADRSCODE = H.PRSTADCD

                        LEFT JOIN [PD].[dbo].[CS_SHIPT] SH
                            ON SH. CUSTNMBR = RM1.CUSTNMBR

                        LEFT JOIN [PD].[dbo].[CS_STATE] ST_STATE
                            ON ST_STATE.STATE = H.STATE
                            AND LTRIM(RTRIM(ST_STATE.CITY)) = ''

                        LEFT JOIN [PD].[dbo].[CS_STATE] ST_CITY
                            ON ST_CITY.STATE = H.STATE
                            AND ST_CITY.CITY = H.CITY
                            AND LTRIM(RTRIM(ST_CITY. CITY)) <> ''
                     WHERE 
                        CASE ISNULL(SH. CS_Shipto, 1)
                                WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                                ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                            END IS NOT NULL AND 
                        CASE ISNULL(SH. CS_Shipto, 1)
                                WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID)
                                ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')
                            END!='' AND  
                        H.SOPTYPE  IN (3,4) AND 
                        [VOIDSTTS]= 0 
                        AND L.QUANTITY <> 0
                        AND (
                                (
                                    ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 0)
                                    AND
                                    (
                                        ((SELECT COUNT(*) FROM CS_SRepList(@userid)) = 0) 
                                        OR
                                        (CASE ISNULL(SH.CS_Shipto, 1) 
                                            WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID) 
                                            ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '') 
                                        END IN (SELECT CSSREP FROM CS_SRepList(@userid)))
                                    )
                                )
                                OR
                                (
                                    ((SELECT linked FROM CSUSRep WHERE CS_User = @userid) = 1)
                                    AND
                                    (RM1.SLPRSNID = (SELECT CS_SalesRep FROM CSUSRep WHERE CS_User = @userid))
                                )
                            )
                     AND [DOCDATE]>= @StartDate AND [DOCDATE]< @EndDate";

            // Add conditional SLPRSNID filter
            string filters = syncDataSettings.GetFilters();
            query += filters;

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