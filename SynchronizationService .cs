using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SalesforceDynamicsGPIntegration
{
    public class SynchronizationService
    {
        private SalesforceService SalesforceService { get; set; }
        private IConfigurationRoot ConfigurationBuilder { get; set; }

        private Logger Logger { get; set; } // Add logger

        public SynchronizationService(IConfigurationRoot configurationBuilder, Logger logger)
        {
            ConfigurationBuilder = configurationBuilder;
            SalesforceService = new SalesforceService(new SalesforceSyncInfo(configurationBuilder), logger);
            Logger = logger;
            Logger.LogInfo("Synchronization Service initialized");
        }

        public async Task<bool> ConnectWithSalesforce()
        {
            Logger.LogInfo("Attempting to connect to Salesforce...");
            try
            {
                AuthenticationResult authenticated = await this.SalesforceService.AuthenticateAsync();
                if (!authenticated.IsSuccess)
                {
                    string errorMsg = $"Salesforce Authentication Failed: {authenticated.ErrorMessage}";
                    Logger.LogError(errorMsg);
                    Console.WriteLine(errorMsg);
                }
                Console.WriteLine("Salesforce Authentication Succeeded.");
                Logger.LogInfo("Salesforce Authentication Succeeded.");
                return authenticated.IsSuccess;
            }
            catch (Exception ex)
            {
                Logger.LogError("Exception during Salesforce authentication", ex);
                Console.WriteLine($"Authentication exception: {ex.Message}");
                return false;
            }
        }

        public async Task StartSynchronizationAsync()
        {
            Logger.LogInfo("Starting synchronization process...");
            try
            {
                if (await ConnectWithSalesforce())
                {
                    try
                    {
                        List<SyncDataSettings> syncDataSettingsList = GetSyncDataSettings();
                        Logger.LogInfo($"Found {syncDataSettingsList.Count} sync data settings");
                        foreach (var syncDataSettings in syncDataSettingsList)
                        {

                            GPDataBaseDataReader dataReader = new GPDataBaseDataReader(ConfigurationBuilder, syncDataSettings, Logger);
                            int pages = dataReader.GetTotalPages();
                            Logger.LogInfo($"Total pages to process: {pages}");
                            Console.WriteLine("Record pages: " + pages + "\n");
                            for (int page = 1; page <= pages; page++)
                            {
                                try
                                {
                                    Logger.LogInfo($"Processing page {page} of {pages}");
                                    Console.WriteLine($"Reading page {page} of {pages}...");
                                    var gpDataSyncRequests = dataReader.GetData(page);
                                    var response = await SalesforceService.SyncGpDataAsync(gpDataSyncRequests);
                                    if (response.Status)
                                    {
                                        Logger.LogInfo($"Successfully synced page {page} to Salesforce");
                                        Logger.LogInfo($"Successfully synced  {gpDataSyncRequests.Count} records to Salesforce");
                                        Console.WriteLine($"Successfully synced page {page} to Salesforce.\n");
                                    }
                                    else
                                    {
                                        Logger.LogError($"Failed to sync page {page} to Salesforce. Message: {response.Message}");
                                        Console.WriteLine($"Failed to sync page {page} to Salesforce. Message: {response.Message}\n");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError($"Exception while syncing page {page} to Salesforce", ex);
                                    Console.WriteLine($"Exception while syncing page {page} to Salesforce: {ex.Message}\n");
                                }

                            }
                        }
                    }
                    catch (Exception settingsEx)
                    {
                        Logger.LogError("Exception processing sync data settings", settingsEx);
                        Console.WriteLine($"Error with sync settings: {settingsEx.Message}");
                    }

                }
                else
                {
                    Console.WriteLine("Failed to authenticate with Salesforce. Exiting...");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Fatal exception during synchronization", ex);
                Console.WriteLine($"Fatal error during synchronization: {ex.Message}");
            }
            finally
            {
                Logger.LogInfo("Synchronization process completed");
            }
        }

        private List<SyncDataSettings> GetSyncDataSettings()
        {
            // For now, In the future we can call to salesfroce to get settings
            return new List<SyncDataSettings>
            {
                new SyncDataSettings(ConfigurationBuilder)
            };
        }

    }
}