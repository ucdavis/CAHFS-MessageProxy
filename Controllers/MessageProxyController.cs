using MessageProxyApi.Data;
using MessageProxyApi.Models;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Http;
using System.Text;

namespace MessageProxyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessageProxyController : ControllerBase
    {
        private readonly ILogger<MessageProxyController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ProxyDbContext _dbContext;
        private const string ProxyServiceUrl = "https://mrpvsmsg.aphis.usda.gov/PublisherService/PublisherServiceApi/api/publish/LMS";

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
            try
            {
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
                        Received = DateTime.UtcNow 
                    };
                    _dbContext.CProxyMessages.Add(messageLog);
                    await _dbContext.SaveChangesAsync();
                    messageLogCreated = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving proxy message: {Message}", ex.Message);
                }
                
                string? apiKey = _configuration["NAHLNAPIKey"];

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(10);

                var request = new HttpRequestMessage(HttpMethod.Post, ProxyServiceUrl);

                // Note: Content-Specific headers go on the HttpContent object, not the request object directly
                var content = new StringContent(body, Encoding.UTF8, "application/xml");
                request.Content = content;

                // Request headers
                request.Headers.Accept.ParseAdd("application/xml");
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Add("x-auth-token", apiKey);
                }

                _logger.LogInformation("NAHLN request body (first 500 chars): {Body}", 
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
                    _logger.LogError("NAHLN response status: {StatusCode}", (int)response.StatusCode);
                    _logger.LogError("NAHLN response body: {ResponseBody}", result);
                    return StatusCode((int)response.StatusCode, new { error = $"Upstream error: {response.ReasonPhrase}" });
                }

                // 7. Return 200 OK with the XML/String result
                return Content(result, "application/xml", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NAHLN error: {Message}", ex.Message);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
