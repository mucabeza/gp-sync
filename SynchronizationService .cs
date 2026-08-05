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

        private RunOptions Options { get; set; }

        private SyncRunState State { get; set; }

        private bool IsConnectedToSalesforce = false;

        public SynchronizationService(IConfigurationRoot configurationBuilder, Logger logger, RunOptions options, SyncRunState state)
        {
            ConfigurationBuilder = configurationBuilder;
            SalesforceService = new SalesforceService(new SalesforceSyncInfo(configurationBuilder), logger);
            Logger = logger;
            Options = options;
            State = state;
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

        public async Task<SyncRunOutcome> StartSynchronizationAsync()
        {
            Logger.LogInfo("Starting synchronization process...");
            var outcome = new SyncRunOutcome();
            try
            {
                if (await ConnectWithSalesforce())
                {
                    try
                    {
                        List<SyncDataSettings> syncDataSettingsList = await GetSyncDataSettings();
                        Logger.LogInfo($"Found {syncDataSettingsList.Count} sync data settings");
                        if (syncDataSettingsList.Count > 0)
                        {
                            foreach (var syncDataSettings in syncDataSettingsList)
                            {
                                // A scheduled run fires several times a night: anything that already
                                // synchronized successfully today is left alone, so only pending work
                                // (or work that failed earlier) is retried.
                                string fingerprint = SyncRunState.ComputeFingerprint(syncDataSettings);
                                if (Options.SkipAlreadySucceeded && State.HasSucceededToday(syncDataSettings.RecordId, fingerprint, out var prior))
                                {
                                    Logger.LogInfo($"Skipping sync data setting (RecordId: {syncDataSettings.RecordId}) - already synced successfully today at {prior.LastSuccessAt} ({prior.PagesProcessed} page(s))");
                                    Console.WriteLine($"Skipping sync data setting (RecordId: {syncDataSettings.RecordId}) - already synced successfully today at {prior.LastSuccessAt}\n");
                                    outcome.Skipped++;
                                    continue;
                                }

                                // Any failure below leaves this setting unrecorded, so the next
                                // scheduled run retries it.
                                bool recordFailed = false;

                                GPDataBaseDataReader dataReader;
                                int pages;
                                try
                                {
                                    dataReader = new GPDataBaseDataReader(ConfigurationBuilder, syncDataSettings, Logger);
                                    pages = dataReader.GetTotalPages();
                                }
                                catch (GpSyncQueryValidationException queryEx)
                                {
                                    Logger.LogError($"GP sync query is invalid, skipping sync data setting (RecordId: {syncDataSettings.RecordId})", queryEx);
                                    Console.WriteLine($"GP sync query is invalid, skipping sync data setting (RecordId: {syncDataSettings.RecordId}): {queryEx.Message}\n");
                                    outcome.Failed++;
                                    continue;
                                }
                                catch (Exception readerEx)
                                {
                                    Logger.LogError($"Failed to prepare the GP query, skipping sync data setting (RecordId: {syncDataSettings.RecordId})", readerEx);
                                    Console.WriteLine($"Failed to prepare the GP query, skipping sync data setting (RecordId: {syncDataSettings.RecordId}): {readerEx.Message}\n");
                                    outcome.Failed++;
                                    continue;
                                }
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
                                           

                                            var response = await SalesforceService.SyncGpDataAsync(gPRequestSync);
                                            if (response.Status)
                                            {
                                                Logger.LogInfo($"Successfully sent to Salesforce Page Number: {page}");
                                                Logger.LogInfo(response.Message);

                                            }
                                            else
                                            {
                                                recordFailed = true;
                                                Logger.LogError($"Failed to sync page {page} to Salesforce. Message: {response.Message}");
                                                Console.WriteLine($"Failed to sync page {page} to Salesforce. Message: {response.Message}\n");
                                            }
                                            // Row-level errors are reported by Salesforce but do not
                                            // mark the setting as failed: re-sending the same window
                                            // cannot fix a data problem, and Salesforce already closed
                                            // the record with isLastOne, so retrying every night would
                                            // never converge.
                                            response.Errors.ForEach(x =>
                                            {
                                                Logger.LogError($"Salesforce Sync Error: {x.Message} for Invoice: {x.InvoiceNumber} SOP Type: {x.SopType} Line Item Sequence: {x.LineItemSequence} Component Sequence: {x.ComponentSequence}");
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            recordFailed = true;
                                            Logger.LogError($"Exception while syncing page {page} to Salesforce", ex);
                                            Console.WriteLine($"Exception while syncing page {page} to Salesforce: {ex.Message}\n");
                                        }

                                        if (recordFailed)
                                        {
                                            // Stop before the last page. Salesforce advances the
                                            // filter's window as soon as it receives isLastOne
                                            // (GPDataSyncService.updateFilter), so sending it after a
                                            // failed page would move the window past rows that never
                                            // arrived - they would never be read again. Leaving the
                                            // window untouched lets the next run resend the whole
                                            // window; the upsert is keyed on the GP line item
                                            // identifier, so resending is safe.
                                            Logger.LogError($"Stopping at page {page} of {pages} to keep the Salesforce window open for a retry (isLastOne was not sent)");
                                            Console.WriteLine($"Stopping at page {page} of {pages} to keep the Salesforce window open for a retry.\n");
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    // No rows for this window: Salesforce still needs the empty
                                    // isLastOne payload to close the record out.
                                    try
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
                                            Logger.LogInfo("Successfully sent to Salesforce an empty result set (0 pages)");
                                            Logger.LogInfo(response.Message);

                                        }
                                        else
                                        {
                                            recordFailed = true;
                                            Logger.LogError($"Failed to sync the empty result set (0 pages) to Salesforce. Message: {response.Message}");
                                            Console.WriteLine($"Failed to sync the empty result set (0 pages) to Salesforce. Message: {response.Message}\n");
                                        }
                                        response.Errors.ForEach(x =>
                                        {
                                            Logger.LogError($"Salesforce Sync Error: {x.Message} for Invoice: {x.InvoiceNumber} SOP Type: {x.SopType} Line Item Sequence: {x.LineItemSequence} Component Sequence: {x.ComponentSequence}");
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        recordFailed = true;
                                        Logger.LogError("Exception while syncing the empty result set (0 pages) to Salesforce", ex);
                                        Console.WriteLine($"Exception while syncing the empty result set (0 pages) to Salesforce: {ex.Message}\n");
                                    }
                                }

                                if (recordFailed)
                                {
                                    outcome.Failed++;
                                    Logger.LogError($"Sync data setting (RecordId: {syncDataSettings.RecordId}) finished with errors - it will be retried on the next run");
                                }
                                else
                                {
                                    outcome.Succeeded++;
                                    State.MarkSuccess(syncDataSettings.RecordId, fingerprint, pages);
                                    // Saved per setting so a crash mid-run keeps the successes already achieved.
                                    State.Save();
                                    Logger.LogInfo($"Sync data setting (RecordId: {syncDataSettings.RecordId}) completed successfully ({pages} page(s))");
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
                        outcome.Aborted = true;
                        Logger.LogError("Exception processing sync data settings", settingsEx);
                        Console.WriteLine($"Error with sync settings: {settingsEx.Message}");
                    }

                }
                else
                {
                    outcome.Aborted = true;
                    Logger.LogError("Failed to authenticate with Salesforce. Exiting...");
                    Console.WriteLine("Failed to authenticate with Salesforce. Exiting...");
                }
            }
            catch (Exception ex)
            {
                outcome.Aborted = true;
                Logger.LogError("Fatal exception during synchronization", ex);
                Console.WriteLine($"Fatal error during synchronization: {ex.Message}");
            }
            finally
            {
                Logger.LogInfo($"Synchronization process completed - {outcome.Summary}");
            }

            return outcome;
        }
 

        private async Task<List<SyncDataSettings>> GetSyncDataSettings()
        {
            return await SalesforceService.GetActiveSettingsAsync();
        }

    }
}