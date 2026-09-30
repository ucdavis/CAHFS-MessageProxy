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
        /// <summary>
        /// Headers whose values may carry credentials and so are never written to the log.
        /// </summary>
        private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "AuthId",
            "Cookie",
            "Set-Cookie",
            "x-auth-token",
            "x-api-key",
            "api-key",
            "apikey",
            "x-functions-key",
            "x-csrf-token",
            "x-xsrf-token"
        };

        /// <summary>
        /// Name of the header on the incoming message that carries the CDFA URL to proxy to.
        /// </summary>
        private const string CdfaUrlHeaderName = "cdfaUrl";

        /// <summary>
        /// Configuration keys holding the CDFA URLs a message is allowed to name.
        /// </summary>
        private static readonly string[] CdfaUrlConfigKeys =
        {
            "ProxyServiceUrls:CDFA",
            "ProxyServiceUrls:CDFAReceipt"
        };

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
            LogRequestHeaders(CProxyMessage.NahlnMessageType);
            return await ProxyMessageAsync(_configuration["ProxyServiceUrls:NAHLN"], CProxyMessage.NahlnMessageType,
                "application/xml", _configuration["config:NAHLNAPIKey"], "x-auth-token");
        }

        /// <summary>
        /// Function to receive a post from Rhapsody and proxy to the CDFA lab order service. Records the request and response in the database.
        /// </summary>
        [HttpPost("CDFA")]
        public async Task<IActionResult> ProxyCdfaMessage()
        {
            // Logged up front so the headers are on record even when the request is rejected below.
            LogRequestHeaders(CProxyMessage.CdfaMessageType);

            var allowedUrls = CdfaUrlConfigKeys
                .Select(key => _configuration[key]?.Trim())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToList();

            if (allowedUrls.Count == 0)
            {
                _logger.LogError("{MessageType} service URL is not configured.", CProxyMessage.CdfaMessageType);
                return StatusCode(500, new { error = $"{CProxyMessage.CdfaMessageType} service URL is not configured." });
            }

            // The destination comes in on the message rather than from configuration, so it has to be one of
            // the configured URLs before we forward the body and our CDFA credential to it.
            if (!TryGetAllowedCdfaUrl(allowedUrls, out var serviceUrl))
            {
                return BadRequest(new { error = $"The {CdfaUrlHeaderName} header is missing or is not an allowed CDFA URL." });
            }

            // Pass the caller's content type through to CDFA, falling back to XML when it isn't supplied.
            var contentType = string.IsNullOrWhiteSpace(Request.ContentType)
                ? "application/xml"
                : Request.ContentType.Split(';')[0].Trim();
            return await ProxyMessageAsync(serviceUrl, CProxyMessage.CdfaMessageType,
                contentType, _configuration["config:CDFAAuthKey"], "AuthID");
        }

        /// <summary>
        /// Reads the CDFA URL off the incoming message and accepts it only when it matches one of the
        /// configured CDFA URLs. Anything else is rejected.
        /// </summary>
        private bool TryGetAllowedCdfaUrl(IEnumerable<string?> allowedUrls, out string? serviceUrl)
        {
            serviceUrl = null;

            if (!Request.Headers.TryGetValue(CdfaUrlHeaderName, out var headerValues) || headerValues.Count != 1)
            {
                _logger.LogError("{MessageType} request did not supply exactly one {HeaderName} header.",
                    CProxyMessage.CdfaMessageType, CdfaUrlHeaderName);
                return false;
            }

            var candidate = headerValues[0]?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                _logger.LogError("{MessageType} {HeaderName} header is empty.", CProxyMessage.CdfaMessageType,
                    CdfaUrlHeaderName);
                return false;
            }

            // Return the configured spelling rather than the caller's, so only URLs we control are sent to.
            serviceUrl = allowedUrls.FirstOrDefault(url =>
                string.Equals(candidate, url, StringComparison.OrdinalIgnoreCase));

            if (serviceUrl is null)
            {
                _logger.LogError("{MessageType} {HeaderName} header is not an allowed CDFA URL: {Url}",
                    CProxyMessage.CdfaMessageType, CdfaUrlHeaderName, candidate);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Logs the incoming headers. Called at the start of each action so they are recorded before any
        /// validation can reject the request.
        /// </summary>
        private void LogRequestHeaders(string messageType)
        {
            _logger.LogInformation("{MessageType} request headers: {Headers}", messageType,
                DescribeHeaders(Request.Headers));
        }

        /// <summary>
        /// Renders the incoming headers as a single log-friendly string, replacing the value of any header
        /// that could hold a credential with a placeholder. Authorization keeps its scheme, which is useful
        /// for diagnosing callers and is not itself a secret.
        /// </summary>
        private static string DescribeHeaders(IHeaderDictionary headers)
        {
            var described = headers.Select(header =>
            {
                if (!SensitiveHeaders.Contains(header.Key))
                {
                    return $"{header.Key}: {header.Value}";
                }

                // For "Scheme credentials" style values, keep the scheme only.
                var value = header.Value.ToString();
                var separator = value.IndexOf(' ');
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase) && separator > 0)
                {
                    return $"{header.Key}: {value.Substring(0, separator)} [redacted]";
                }

                return $"{header.Key}: [redacted]";
            });

            return string.Join("; ", described);
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
