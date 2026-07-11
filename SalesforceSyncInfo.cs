
using Microsoft.Extensions.Configuration;
namespace SalesforceDynamicsGPIntegration
{
    public class SalesforceSyncInfo
    {
        public string LoginUrl { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string SyncEndPointName { get; set; }

        public SalesforceSyncInfo(IConfigurationRoot configurationBuilder)
        {

            LoginUrl = configurationBuilder["Salesforce:LoginUrl"];
            ClientId = configurationBuilder["Salesforce:ClientId"];
            ClientSecret = configurationBuilder["Salesforce:ClientSecret"];
            SyncEndPointName = configurationBuilder["Salesforce:SyncEndPointName"];
        }
    }
}