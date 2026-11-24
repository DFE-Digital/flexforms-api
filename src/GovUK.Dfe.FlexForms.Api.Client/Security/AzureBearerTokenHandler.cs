using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GovUK.Dfe.FlexForms.Api.Client.Security;

public class AzureBearerTokenHandler(
    ITokenAcquisitionService tokenAcquisitionService,
    ILogger<AzureBearerTokenHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Always use Azure token for tokens client authentication
            var azureToken = await tokenAcquisitionService.GetTokenAsync();
            
            if (string.IsNullOrEmpty(azureToken))
            {
                throw new InvalidOperationException("Azure token acquisition returned null or empty token");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", azureToken);
            
            var response = await base.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(">>>>>>>>>> Authentication >>> Request failed with status: {StatusCode} {ReasonPhrase} for {Method} {Uri}", 
                    response.StatusCode, response.ReasonPhrase, request.Method, request.RequestUri);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    logger.LogError(">>>>>>>>>> Authentication >>> 401 Unauthorized response received - Azure token may be invalid or expired for {Method} {Uri}", 
                        request.Method, request.RequestUri);
            }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    logger.LogError(">>>>>>>>>> Authentication >>> 403 Forbidden response received - Azure token may lack required permissions for {Method} {Uri}", 
                        request.Method, request.RequestUri);
                }
            }
            
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ">>>>>>>>>> Authentication >>> Exception in AzureBearerTokenHandler for request: {Method} {Uri}", 
                request.Method, request.RequestUri);
            throw;
        }
    }
} 