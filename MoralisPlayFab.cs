using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using PlayFab.ServerModels;
using Moralis.Network;
using Moralis.AuthApi.Models;
using Moralis.AuthApi.Interfaces;
using Newtonsoft.Json;

namespace PlayFab.AzureFunctions
{
    public static class MoralisPlayFab
    {
        private static string AuthenticationApiUrl = Environment.GetEnvironmentVariable("MORALIS_AUTHENTICATION_API_URL", EnvironmentVariableTarget.Process);
        private static string Web3ApiUrl = Environment.GetEnvironmentVariable("MORALIS_WEB3_API_URL", EnvironmentVariableTarget.Process);
        private static string ApiKey = Environment.GetEnvironmentVariable("MORALIS_API_KEY", EnvironmentVariableTarget.Process);

        [FunctionName("ChallengeRequest")]
        public static async Task<dynamic> ChallengeRequest(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            // Create the function execution's context through the request
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            // Deserialize Playfab context
            dynamic context = JsonConvert.DeserializeObject(requestBody);
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
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            // Create the function execution's context through the request
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            // Deserialize Playfab context
            dynamic context = JsonConvert.DeserializeObject(requestBody);
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
    }
}
