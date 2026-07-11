using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SalesforceDynamicsGPIntegration
{
    public class SalesforceService
    {
        private string _loginUrl
        {
            get
            {
                return this._syncInfo.LoginUrl;
            }
        }
        private string _clientId
        {
            get
            {
                return this._syncInfo.ClientId;
            }
        }
        private string _clientSecret
        {
            get
            {
                return this._syncInfo.ClientSecret;
            }
        }

        private string _syncEndPointName
        {
            get
            {
                return this._syncInfo.SyncEndPointName;
            }
        }

        private bool _isAuthenticated = false;
        private string _instanceUrl;
        private string _accessToken;
        private readonly HttpClient _httpClient;
        private readonly SalesforceSyncInfo _syncInfo;

        private Logger Logger { get; set; }


        public SalesforceService(SalesforceSyncInfo _syncInfo, Logger logger)
        {
            _httpClient = new HttpClient();
            this._syncInfo = _syncInfo;
            this.Logger = logger;
        }

        public async Task<AuthenticationResult> AuthenticateAsync()
        {

            AuthenticationResult authenticationResult = new AuthenticationResult();
            try
            {
                Console.WriteLine("Connecting to Salesforce...");
                this.Logger.LogInfo("Connecting to Salesforce...");


                // Prepare OAuth token request
                var tokenEndpoint = $"{_loginUrl}/services/oauth2/token";

                var content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _clientId),
                    new KeyValuePair<string, string>("client_secret", _clientSecret)});
                // This is already set automatically, but you can be explicit:
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = await _httpClient.PostAsync(tokenEndpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = JsonSerializer.Deserialize<SalesforceAuthResponse>(responseContent);

                    _instanceUrl = authResponse.instance_url;
                    _accessToken = authResponse.access_token;
                    _isAuthenticated = true;
                    authenticationResult.IsSuccess = true;
                    this.Logger.LogInfo("Successfully authenticated with Salesforce.");
                    this.Logger.LogInfo($"Instance URL: {_instanceUrl}");
                    Console.WriteLine($"Successfully authenticated with Salesforce!");
                    Console.WriteLine($"Instance URL: {_instanceUrl}\n");

                }
                else
                {
                    Console.WriteLine($"Authentication failed. Status: {response.StatusCode}");
                    Console.WriteLine($"Response: {responseContent}");
                    Logger.LogError($"Authentication failed. Status: {response.StatusCode}");
                    Logger.LogError($"Response: {JsonSerializer.Serialize(responseContent)}");

                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<SalesforceAuthError>(responseContent);
                        authenticationResult.IsSuccess = false;
                        authenticationResult.ErrorMessage = $"{errorResponse.error}: {errorResponse.error_description}";
                        Logger.LogError($"Salesforce Auth Error: {authenticationResult.ErrorMessage}");

                        if (string.Equals(errorResponse.error, "invalid_grant", StringComparison.OrdinalIgnoreCase))
                        {
                            var hint = BuildInvalidGrantHint();
                            Logger.LogError(hint);
                            Console.WriteLine(hint);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Exception while parsing Salesforce auth error", ex);
                    }

                    _isAuthenticated = false;
                    authenticationResult.IsSuccess = false;
                    authenticationResult.ErrorMessage = "Authentication failed.";

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Salesforce authentication failed: {ex.Message}");
                Logger.LogError("Exception during Salesforce authentication", ex);
                _isAuthenticated = false;
                authenticationResult.IsSuccess = false;
                authenticationResult.ErrorMessage = ex.Message;

            }

            return authenticationResult;

        }

        private string BuildInvalidGrantHint()
        {
            return "Salesforce invalid_grant usually means the Connected App isn't set up for the " +
                   "Client Credentials Flow correctly. Verify: the Connected App has a 'Run As' user " +
                   "configured under Client Credentials Flow, the client_id/client_secret are current " +
                   "(Manage Consumer Details), and that 'Run As' user is active and not locked out.";
        }

        public async Task<ResponseWrapper> SyncGpDataAsync(GPRequestSync gPRequestSync)
        {
            ResponseWrapper responseWrapper = new ResponseWrapper();

            if (!_isAuthenticated)
            {
                throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
            }

            try
            {
                // Call custom REST endpoint /services/apexrest/gp-data-sync
                string endpoint = $"{_instanceUrl}/services/apexrest/{_syncEndPointName}";

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                String json = JsonSerializer.Serialize(gPRequestSync);
                request.Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                this.Logger.LogInfo($"Salesforce response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    this.Logger.LogInfo($"Successfully synced record to Salesforce");
                    responseWrapper = JsonSerializer.Deserialize<ResponseWrapper>(responseContent);
                }
                else
                {
                    this.Logger.LogError($"Failed to sync record. Status: {response.StatusCode}, Response: {responseContent}");
                    responseWrapper.Status = false;
                    responseWrapper.Message = responseContent;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing GP data: {ex.Message}");
                responseWrapper.Status = false;
                responseWrapper.Message = ex.Message;
            }

            return responseWrapper;
        }

        public async Task<List<SyncDataSettings>> GetActiveSettingsAsync()
        {
            List<SyncDataSettings> activeFilters = new List<SyncDataSettings>();

            if (!_isAuthenticated)
            {
                throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
            }

            try
            {
                // Call custom REST endpoint /services/apexrest/gp-data-sync
                string endpoint = $"{_instanceUrl}/services/apexrest/{_syncEndPointName}";
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                this.Logger.LogInfo($"Salesforce response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    this.Logger.LogInfo($"Successfully synced record to Salesforce");
                    activeFilters = JsonSerializer.Deserialize<List<SyncDataSettings>>(responseContent);
                }
                else
                {
                    this.Logger.LogError($"Failed to sync record. Status: {response.StatusCode}, Response: {responseContent}");
                    activeFilters = new List<SyncDataSettings>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing GP data: {ex.Message}");
                activeFilters = new List<SyncDataSettings>();
            }

            return activeFilters;
        }
    }

    public class AuthenticationResult
    {
        public bool IsSuccess { get; set; } = false;
        public string ErrorMessage { get; set; } = string.Empty;
    }

}