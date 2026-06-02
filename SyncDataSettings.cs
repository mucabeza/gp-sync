using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    public class SyncDataSettings

    {
        private static string ALL_VALUES = "ALL";

        [JsonPropertyName("batchSize")]
        public int BatchSize { get; set; }

        [JsonPropertyName("startDate")]
        public DateTime StartDate { get; set; } = DateTime.Now;

        [JsonPropertyName("endDate")]
        public DateTime EndDate { get; set; } = DateTime.Now;

        [JsonPropertyName("salesPersonId")]
        public string SalesPersonId { get; set; } = ALL_VALUES;

        [JsonPropertyName("itemClassType")]
        public string ItemClassType { get; set; } = "NP";

        [JsonPropertyName("customerNumber")]
        public string CustomerNumber { get; set; } = ALL_VALUES;

        [JsonPropertyName("productNumber")] 
        public string ProductNumber { get; set;} = ALL_VALUES;

        [JsonPropertyName("userId")] 
        public string UserId { get; set;} = String.Empty;

        [JsonPropertyName("recordId")] 
        public String RecordId { get; set;} = String.Empty;
        public SyncDataSettings(IConfigurationRoot configurationBuilder)
        {
            var syncSettings = configurationBuilder.GetSection("SyncDataSettings");
            BatchSize = !string.IsNullOrEmpty(syncSettings["BatchSize"]) ? int.Parse(syncSettings["BatchSize"]) : 200;
            StartDate = !string.IsNullOrEmpty(syncSettings["StartDate"]) ? DateTime.Parse(syncSettings["StartDate"]) : DateTime.Now;
            EndDate = !string.IsNullOrEmpty(syncSettings["EndDate"]) ? DateTime.Parse(syncSettings["EndDate"]) : DateTime.Now;
            ItemClassType = syncSettings["ItemClassType"] ?? "NP";
            SalesPersonId = syncSettings["SalesPersonID"] ?? ALL_VALUES;
            CustomerNumber = syncSettings["CustomerNumber"] ?? ALL_VALUES;
            ProductNumber = syncSettings["ProductNumber"] ?? ALL_VALUES;
            UserId = syncSettings["UserId"] ?? String.Empty;
        }
        public SyncDataSettings()
        {
            BatchSize = 200;
            StartDate = DateTime.Now;
            EndDate = DateTime.Now;
            ItemClassType = "NP";
            SalesPersonId = ALL_VALUES;
            CustomerNumber = ALL_VALUES;
            ProductNumber = ALL_VALUES;
        }
        public string GetFilters()
        {
            string filters = " ";
            if (!string.IsNullOrEmpty(this.ItemClassType) && this.ItemClassType.ToUpper() != ALL_VALUES)
            {
                filters += " AND LEFT(I.ITMCLSCD, 2) =  @ItemClassType";
            }
            if (!string.IsNullOrEmpty(this.SalesPersonId) && this.SalesPersonId.ToUpper() != ALL_VALUES)
            {
                filters += " AND (CASE ISNULL(SH. CS_Shipto, 1) WHEN 1 THEN ISNULL(RM2.SLPRSNID, RM1.SLPRSNID) ELSE COALESCE(ST_CITY.SLPRSNID, ST_STATE.SLPRSNID, RM2.SLPRSNID, '')  END) = @SalesPersonId";
            }
            if (!string.IsNullOrEmpty(this.CustomerNumber) && this.CustomerNumber.ToUpper() != ALL_VALUES)
            {
                filters += " AND H.CUSTNMBR = @CustomerNumber";
            }
            if (!string.IsNullOrEmpty(this.ProductNumber) && this.ProductNumber.ToUpper() != ALL_VALUES)
            {
                filters += " AND  L.ITEMNMBR = @ProductNumber";
            }

            return filters;

        }
        public SqlParameter[] GetParameters()
        {
            var parameters = new List<SqlParameter>{
                new SqlParameter("@userid", SqlDbType.VarChar, 50) { Value = this.UserId },
                new SqlParameter("@StartDate", SqlDbType.Date) { Value = this.StartDate },
                new SqlParameter("@EndDate", SqlDbType.Date) { Value = this.EndDate.AddDays(1) }
            };
            if (!string.IsNullOrEmpty(this.ItemClassType) && this.ItemClassType.ToUpper() != ALL_VALUES)
            {
                parameters.Add(new SqlParameter("@ItemClassType", SqlDbType.VarChar, 50) { Value = this.ItemClassType });
            }
            if (!string.IsNullOrEmpty(this.SalesPersonId) && this.SalesPersonId.ToUpper() != ALL_VALUES)
            {
                parameters.Add(new SqlParameter("@SalesPersonId", SqlDbType.VarChar, 15) { Value = this.SalesPersonId });
            }
            if (!string.IsNullOrEmpty(this.CustomerNumber) && this.CustomerNumber.ToUpper() != ALL_VALUES)
            {
                parameters.Add(new SqlParameter("@CustomerNumber", SqlDbType.VarChar, 15) { Value = this.CustomerNumber });
            }
            if (!string.IsNullOrEmpty(this.ProductNumber) && this.ProductNumber.ToUpper() != ALL_VALUES)
            {
                parameters.Add(new SqlParameter("@ProductNumber", SqlDbType.VarChar, 15) { Value = this.ProductNumber });
            }
            return parameters.ToArray();

        }

    }
}