using MessageProxyApi.Data;
using MessageProxyApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Http;
using System.Text;

namespace MessageProxyApi.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [Route("api/[controller]")]
    public class MessageProxyController : ControllerBase
    {
        private readonly ILogger<MessageProxyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ProxyDbContext _dbContext;

        public MessageProxyController(ILogger<MessageProxyController> logger, IHttpClientFactory httpClientFactory,
            IConfiguration configuration, ProxyDbContext dbContext)
        {
            _logger = logger;
             _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Function to receive a post from Rhapsodty and proxy to the NAHLN. Records the request and response in the database.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ProxyMessage()
        {
            return await ProxyMessageAsync(_configuration["ProxyServiceUrls:NAHLN"], CProxyMessage.NahlnMessageType,
                "application/xml", _configuration["config:NAHLNAPIKey"], "x-auth-token");
        }

        /// <summary>
        /// Function to receive a post from Rhapsody and proxy to the CDFA lab order service. Records the request and response in the database.
        /// </summary>
        [HttpPost("CDFA")]
        public async Task<IActionResult> ProxyCdfaMessage()
        {
            // Pass the caller's content type through to CDFA, falling back to XML when it isn't supplied.
            var contentType = string.IsNullOrWhiteSpace(Request.ContentType)
                ? "application/xml"
                : Request.ContentType.Split(';')[0].Trim();

            return await ProxyMessageAsync(_configuration["ProxyServiceUrls:CDFA"], CProxyMessage.CdfaMessageType,
                contentType, _configuration["config:CDFAAuthKey"], "AuthID");
        }

        /// <summary>
        /// Reads the request body, logs it, posts it to the given service, and records the response.
        /// </summary>
        private async Task<IActionResult> ProxyMessageAsync(string? serviceUrl, string messageType, string contentType,
            string? apiKey, string apiKeyHeaderName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(serviceUrl))
                {
                    _logger.LogError("{MessageType} service URL is not configured.", messageType);
                    return StatusCode(500, new { error = $"{messageType} service URL is not configured." });
                }

                using var reader = new StreamReader(Request.Body, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();

                //try to create the db log. if it fails, continue after logging the error. if it succeeds, record
                //success so we can update with the response.
                var messageLogCreated = false;
                CProxyMessage? messageLog = null;

                try
                {
                    messageLog = new CProxyMessage
                    {
                        MessageContent = body,
                        Received = DateTime.UtcNow,
                        MessageType = messageType
                    };
                    _dbContext.CProxyMessages.Add(messageLog);
                    await _dbContext.SaveChangesAsync();
                    messageLogCreated = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving proxy message: {Message}", ex.Message);
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);

                var request = new HttpRequestMessage(HttpMethod.Post, serviceUrl);

                // Note: Content-Specific headers go on the HttpContent object, not the request object directly
                var content = new StringContent(body, Encoding.UTF8, contentType);
                request.Content = content;

                // Request headers
                request.Headers.Accept.ParseAdd(contentType);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Add(apiKeyHeaderName, apiKey);
                }

                _logger.LogInformation("{MessageType} request body (first 500 chars): {Body}", messageType,
                    body.Length > 500 ? body.Substring(0, 500) : body);

                var response = await client.SendAsync(request);
                string result = await response.Content.ReadAsStringAsync();

                if (messageLogCreated && messageLog is not null) {
                    try {
                        // Update the database log with response and status
                        messageLog.ResponseStatus = response.StatusCode.ToString();
                        messageLog.ResponseContent = result;
                        await _dbContext.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating proxy message with response: {Message}", ex.Message);
                    }
                }

                // Check if external API returned an error status (throws to catch block like request-promise does)
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("{MessageType} response status: {StatusCode}", messageType, (int)response.StatusCode);
                    _logger.LogError("{MessageType} response body: {ResponseBody}", messageType, result);
                    return StatusCode((int)response.StatusCode, new { error = $"Upstream error: {response.ReasonPhrase}" });
                }

                // 7. Return 200 OK with the XML/String result
                return Content(result, contentType, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{MessageType} error: {Message}", messageType, ex.Message);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
