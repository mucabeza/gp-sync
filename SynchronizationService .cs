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

        private bool IsConnectedToSalesforce = false;

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
            if (IsConnectedToSalesforce)
            {
                Logger.LogInfo("Already connected to Salesforce.");
                return true;
            }
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
                IsConnectedToSalesforce = authenticated.IsSuccess;
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
                if (true) //await ConnectWithSalesforce()
                {
                    try
                    {
                        //List<SyncDataSettings> syncDataSettingsList = await GetSyncDataSettings();
                        List<SyncDataSettings> syncDataSettingsList = new List<SyncDataSettings>
                        {
                            new SyncDataSettings(ConfigurationBuilder)
                            
                        };
                        Logger.LogInfo($"Found {syncDataSettingsList.Count} sync data settings");
                        if (syncDataSettingsList.Count > 0)
                        {
                            foreach (var syncDataSettings in syncDataSettingsList)
                            {

                                GPDataBaseDataReader dataReader = new GPDataBaseDataReader(ConfigurationBuilder, syncDataSettings, Logger);
                                int pages = dataReader.GetTotalPages();
                                Logger.LogInfo($"Total pages to process: {pages}");
                                Console.WriteLine("Record pages: " + pages + "\n");
                                if (pages > 0)
                                {
                                    for (int page = 1; page <= pages; page++)
                                    {
                                        try
                                        {
                                            Logger.LogInfo($"Processing page {page} of {pages}");
                                            Console.WriteLine($"Reading page {page} of {pages}...");
                                            var gpDataSyncRequests = dataReader.GetData(page);
                                            GPRequestSync gPRequestSync = new GPRequestSync
                                            {
                                                gpData = gpDataSyncRequests,
                                                isLastOne = page == pages,
                                                filterRecordId = syncDataSettings.RecordId
                                            };
                                            foreach (var gpData in gPRequestSync.gpData)
                                            {
                                                Console.WriteLine($"Processing invoice {gpData.invoiceNumber} Account Name: {gpData.accountName}  SalesRepName {gpData.salesRepName} Product: {gpData.productName}");
                                            }

                                            // var response = await SalesforceService.SyncGpDataAsync(gPRequestSync);
                                            // if (response.Status)
                                            // {
                                            //     Logger.LogInfo($"Successfully sent to Salesforce Page Number: {page}");
                                            //     Logger.LogInfo(response.Message);

                                            // }
                                            // else
                                            // {
                                            //     Logger.LogError($"Failed to sync page {page} to Salesforce. Message: {response.Message}");
                                            //     Console.WriteLine($"Failed to sync page {page} to Salesforce. Message: {response.Message}\n");
                                            // }
                                            // response.Errors.ForEach(x =>
                                            // {
                                            //     Logger.LogError($"Salesforce Sync Error: {x.Message} for Invoice: {x.InvoiceNumber} SOP Type: {x.SopType} Line Item Sequence: {x.LineItemSequence} Component Sequence: {x.ComponentSequence}");
                                            // });
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.LogError($"Exception while syncing page {page} to Salesforce", ex);
                                            Console.WriteLine($"Exception while syncing page {page} to Salesforce: {ex.Message}\n");
                                        }

                                    }
                                }
                                else
                                {
                                    GPRequestSync gPRequestSync = new GPRequestSync
                                    {
                                        gpData = new List<GpDataSync>(),
                                        isLastOne = true,
                                        filterRecordId = syncDataSettings.RecordId
                                    };
                                    var response = await SalesforceService.SyncGpDataAsync(gPRequestSync);
                                    if (response.Status)
                                    {
                                        Logger.LogInfo($"Successfully sent to Salesforce Page Number: {0}");
                                        Logger.LogInfo(response.Message);

                                    }
                                    else
                                    {
                                        Logger.LogError($"Failed to sync page {0} to Salesforce. Message: {response.Message}");
                                        Console.WriteLine($"Failed to sync page {0} to Salesforce. Message: {response.Message}\n");
                                    }
                                    response.Errors.ForEach(x =>
                                    {
                                        Logger.LogError($"Salesforce Sync Error: {x.Message} for Invoice: {x.InvoiceNumber} SOP Type: {x.SopType} Line Item Sequence: {x.LineItemSequence} Component Sequence: {x.ComponentSequence}");
                                    });

                                }
                            }

                        }
                        else
                        {
                            Logger.LogInfo("No active sync data settings found.");
                            Console.WriteLine("No active sync data settings found.");
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
                    Logger.LogError("Failed to authenticate with Salesforce. Exiting...");
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
 

        private async Task<List<SyncDataSettings>> GetSyncDataSettings()
        {
            return await SalesforceService.GetActiveSettingsAsync();
        }

    }
}