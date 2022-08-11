using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using PlayFab.ServerModels;
using PlayFab.Plugins.CloudScript;
using Moralis.Network;
using Moralis.AuthApi.Models;
using Moralis.AuthApi.Interfaces;
using Moralis.Web3Api.Models;
using Moralis.Web3Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace PlayFab.AzureFunctions
{
    public static class MoralisPlayFab
    {
        private static string AuthenticationApiUrl = Environment.GetEnvironmentVariable("MORALIS_AUTHENTICATION_API_URL", EnvironmentVariableTarget.Process);
        private static string Web3ApiUrl = Environment.GetEnvironmentVariable("MORALIS_WEB3_API_URL", EnvironmentVariableTarget.Process);
        private static string ApiKey = Environment.GetEnvironmentVariable("MORALIS_API_KEY", EnvironmentVariableTarget.Process);

        [FunctionName("ChallengeRequest")]
        public static async Task<dynamic> ChallengeRequest(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            /* Create the function execution's context through the request */
            var context = await FunctionContext<dynamic>.Create(req);
            var args = context.FunctionArgument;

            // Get the address from the request
            dynamic address = null;
            if (args != null && args["address"] != null)
            {
                address = args["address"];
            }

            // Get the chainid from the request
            dynamic chainid = null;
            if (args != null && args["chainid"] != null)
            {
                chainid = args["chainid"];
            }

            try
            {
                // Connect with the Moralis Authtication Server
                Moralis.AuthApi.MoralisAuthApiClient.Initialize(AuthenticationApiUrl, ApiKey);
                IAuthClientApi AuthenticationApi = Moralis.AuthApi.MoralisAuthApiClient.AuthenticationApi;

                // Create the authentication message and send it back to the client
                // Resources must be RFC 3986 URIs
                // More info here: https://eips.ethereum.org/EIPS/eip-4361#message-field-descriptions
                ChallengeRequestDto request = new ChallengeRequestDto()
                {
                    Address = address,
                    ChainId = (long)chainid,
                    Domain = "moralis.io",
                    ExpirationTime = DateTime.UtcNow.AddMinutes(60),
                    NotBefore = DateTime.UtcNow,
                    Resources = new string[] { "https://docs.moralis.io/" },
                    Timeout = 120,
                    Statement = "Please confirm",
                    Uri = "https://moralis.io/"
                };

                ChallengeResponseDto response = await AuthenticationApi.AuthEndpoint.Challenge(request, ChainNetworkType.evm);
                
                return new OkObjectResult(response.Message);
            }
            catch (ApiException aexp)
            {
                log.LogDebug($"aexp.Message: {aexp.ToString()}");
                return new BadRequestObjectResult(aexp.Message);
            }
            catch (Exception exp)
            {
                log.LogDebug($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }
        }

        [FunctionName("ChallengeVerify")]
        public static async Task<dynamic> ChallengeVerify(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            /* Create the function execution's context through the request */
            var context = await FunctionContext<dynamic>.Create(req);
            var args = context.FunctionArgument;

            // Get the message from the request
            dynamic message = null;
            if (args != null && args["message"] != null)
            {
                message = args["message"];
            }

            // Get the signature from the request
            dynamic signature = null;
            if (args != null && args["signature"] != null)
            {
                signature = args["signature"];
            }

            CompleteChallengeResponseDto response = null;

            try
            {
                // Connect with the Moralis Authtication Server
                Moralis.AuthApi.MoralisAuthApiClient.Initialize(AuthenticationApiUrl, ApiKey);
                IAuthClientApi AuthenticationApi = Moralis.AuthApi.MoralisAuthApiClient.AuthenticationApi;

                // Create the authentication message and send it back to the client
                CompleteChallengeRequestDto request = new CompleteChallengeRequestDto()
                {
                    Message = message,
                    Signature = signature
                };

                response = await AuthenticationApi.AuthEndpoint.CompleteChallenge(request, ChainNetworkType.evm);
            }
            catch (ApiException aexp)
            {
                log.LogInformation($"aexp.Message: {aexp.ToString()}");
                return new BadRequestObjectResult(aexp.Message);
            }
            catch (Exception exp)
            {
                log.LogInformation($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }

            try
            {
                // Get the setting from our Azure config and connect with the PlayFabAPI
                var settings = new PlayFabApiSettings
                {
                    TitleId = Environment.GetEnvironmentVariable("PLAYFAB_TITLE_ID", EnvironmentVariableTarget.Process),
                    DeveloperSecretKey = Environment.GetEnvironmentVariable("PLAYFAB_DEV_SECRET_KEY", EnvironmentVariableTarget.Process)
                };

                var serverApi = new PlayFabServerInstanceAPI(settings);

                // Update the user read-only data with the validated data and return the reponse to the client
                // Read-only data is data that the server can modify, but the client can only read
                var updateUserDataRequest = new UpdateUserDataRequest
                {
                    PlayFabId = context.CallerEntityProfile.Lineage.MasterPlayerAccountId,
                    Data = new Dictionary<string, string>()
                    {
                        {"MoralisProfileId", response.Id.ToString()},
                        {"Address", response.Address.ToString()},
                        {"ChainId", response.ChainId.ToString()}
                    }
                };

                PlayFabResult<UpdateUserDataResult> updateUserDateResult = await serverApi.UpdateUserReadOnlyDataAsync(updateUserDataRequest);
                
                if (updateUserDateResult.Error == null)
                {
                    return new OkObjectResult(updateUserDateResult.Result);
                }
                else
                {
                    log.LogInformation($"updateUserDateResult.Error.ErrorMessage: {updateUserDateResult.Error.ErrorMessage.ToString()}");
                    return new BadRequestObjectResult(updateUserDateResult.Error.ErrorMessage);
                }
            }
            catch (Exception exp)
            {
                log.LogInformation($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }
        }
 
        [FunctionName("GetNativeBalance")]
        public static async Task<dynamic> GetNativeBalance(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            // Create the function execution's context through the request.
            var context = await FunctionContext<dynamic>.Create(req);
            var args = context.FunctionArgument;

            // Get the wallet address from the request
            dynamic address = null;
            if (args != null && args["address"] != null)
            {
                address = args["address"];
            }

            // Get the signature from the request
            dynamic chainId = null;
            if (args != null && args["chainId"] != null)
            {
                chainId = args["chainId"];
            }

            // Get the providerUrl from the request
            dynamic providerUrl = null;
            if (args != null && args["providerUrl"] != null)
            {
                providerUrl = args["providerUrl"];
            }

            // Get the toBlock from the request
            dynamic toBlock = null;
            if (args != null && args["toBlock"] != null)
            {
                toBlock = args["toBlock"];
            }

            try
            {
                // Connect with the Moralis Authtication Server
                Moralis.Web3Api.MoralisWeb3ApiClient.Initialize(Web3ApiUrl, ApiKey);
                IWeb3Api Web3Api = Moralis.Web3Api.MoralisWeb3ApiClient.Web3Api;

                NativeBalance response = await Web3Api.Account.GetNativeBalance(address, chainId, providerUrl, toBlock);

                return new OkObjectResult(response);
            }
            catch (ApiException aexp)
            {
                log.LogInformation($"aexp.Message: {aexp.ToString()}");
                return new BadRequestObjectResult(aexp.Message);
            }
            catch (Exception exp)
            {
                log.LogInformation($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }
        }

        [FunctionName("GetTokenBalances")]
        public static async Task<dynamic> GetTokenBalances(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            // Create the function execution's context through the request.
            var context = await FunctionContext<dynamic>.Create(req);
            var args = context.FunctionArgument;

            // Get the wallet address from the request
            dynamic address = null;
            if (args != null && args["address"] != null)
            {
                address = args["address"];
            }

            // Get the signature from the request
            dynamic chainId = null;
            if (args != null && args["chainId"] != null)
            {
                chainId = args["chainId"];
            }

            // Get the subdomain from the request
            dynamic subdomain = null;
            if (args != null && args["subdomain"] != null)
            {
                subdomain = args["subdomain"];
            }

            // Get the toBlock from the request
            dynamic toBlock = null;
            if (args != null && args["toBlock"] != null)
            {
                toBlock = args["toBlock"];
            }

            try
            {
                // Connect with the Moralis Authtication Server
                Moralis.Web3Api.MoralisWeb3ApiClient.Initialize(Web3ApiUrl, ApiKey);
                IWeb3Api Web3Api = Moralis.Web3Api.MoralisWeb3ApiClient.Web3Api;

                List<Erc20TokenBalance> erc20Balnaces = await Web3Api.Account.GetTokenBalances(address, chainId, subdomain, toBlock);

                return new OkObjectResult(erc20Balnaces);
            }
            catch (ApiException aexp)
            {
                log.LogInformation($"aexp.Message: {aexp.ToString()}");
                return new BadRequestObjectResult(aexp.Message);
            }
            catch (Exception exp)
            {
                log.LogInformation($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }
        }

        [FunctionName("GetNfts")]
        public static async Task<dynamic> GetNfts(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req, ILogger log)
        {
            // Create the function execution's context through the request.
            var context = await FunctionContext<dynamic>.Create(req);
            var args = context.FunctionArgument;

            // Get the wallet address from the request
            dynamic address = null;
            if (args != null && args["address"] != null)
            {
                address = args["address"];
            }

            // Get the signature from the request
            dynamic chainId = null;
            if (args != null && args["chainId"] != null)
            {
                chainId = args["chainId"];
            }

            // Get the subdomain from the request
            dynamic cursor = null;
            if (args != null && args["cursor"] != null)
            {
                cursor = args["cursor"];
            }

            // Get the toBlock from the request
            dynamic format = null;
            if (args != null && args["format"] != null)
            {
                format = args["format"];
            }

            // Get the toBlock from the request
            dynamic limit = null;
            if (args != null && args["limit"] != null)
            {
                limit = args["limit"];
            }

            try
            {
                // Connect with the Moralis Authtication Server
                Moralis.Web3Api.MoralisWeb3ApiClient.Initialize(Web3ApiUrl, ApiKey);
                IWeb3Api Web3Api = Moralis.Web3Api.MoralisWeb3ApiClient.Web3Api;

                NftOwnerCollection nfts = await Web3Api.Account.GetNFTs(address, chainId, cursor, format, limit);

                return new OkObjectResult(nfts);
            }
            catch (ApiException aexp)
            {
                log.LogInformation($"aexp.Message: {aexp.ToString()}");
                return new BadRequestObjectResult(aexp.Message);
            }
            catch (Exception exp)
            {
                log.LogInformation($"exp.Message: {exp.ToString()}");
                return new BadRequestObjectResult(exp.Message);
            }
        }

    }
}
