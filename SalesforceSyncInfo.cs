
using Microsoft.Extensions.Configuration;
namespace SalesforceDynamicsGPIntegration
{
    public class SalesforceSyncInfo
    {
        public string LoginUrl { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string SecurityToken { get; set; }
        public string SyncEndPointName { get; set; }

        public SalesforceSyncInfo(IConfigurationRoot configurationBuilder)
        {

            LoginUrl = configurationBuilder["Salesforce:LoginUrl"];
            ClientId = configurationBuilder["Salesforce:ClientId"];
            ClientSecret = configurationBuilder["Salesforce:ClientSecret"];
            Username = configurationBuilder["Salesforce:Username"];
            Password = configurationBuilder["Salesforce:Password"];
            SecurityToken = configurationBuilder["Salesforce:SecurityToken"];
            SyncEndPointName = configurationBuilder["Salesforce:SyncEndPointName"];
        }
    }
}