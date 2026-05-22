using MessageProxyApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Xml.Linq;

namespace MessageProxyApi.Pages
{
    public class CasLoginModel : PageModel
    {
        private readonly XNamespace _ns = "http://www.yale.edu/tp/cas";
        private const string TicketQuery = "ticket";
        private readonly IHttpClientFactory _clientFactory;
        private readonly CasSettings _settings;
        private readonly List<string> _casAttributesToCapture = new() { "authenticationDate", "credentialType" };

        public CasLoginModel(IHttpClientFactory clientFactory, IOptions<CasSettings> settingsOptions)
        {
            _clientFactory = clientFactory;
            _settings = settingsOptions.Value;
        }

        public async Task<IActionResult> OnGet()
        {
            string? ticket = Request.Query[TicketQuery];
            string? returnUrl = Request.Query["ReturnUrl"];
            string service = WebUtility.UrlEncode(HttpHelper.GetRootURL() + Request.Path + "?ReturnUrl=" + WebUtility.UrlEncode(returnUrl));
            var client = _clientFactory.CreateClient("CAS");

            try
            {
                var response = await client.GetAsync(_settings.CasBaseUrl + "p3/serviceValidate?ticket=" + ticket + "&service=" + service, HttpContext.RequestAborted);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(responseBody);

                var serviceResponse = doc.Element(_ns + "serviceResponse");
                var successNode = serviceResponse?.Element(_ns + "authenticationSuccess");
                var userNode = successNode?.Element(_ns + "user");
                var validatedUserName = userNode?.Value;

                if (!string.IsNullOrEmpty(validatedUserName))
                {
                    var claimsIdentity = new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.Name, validatedUserName),
                        new Claim(ClaimTypes.NameIdentifier, validatedUserName),
                        new Claim(ClaimTypes.AuthenticationMethod, "CAS")
                    }, CookieAuthenticationDefaults.AuthenticationScheme);

                    var attributesNode = successNode?.Element(_ns + "attributes");
                    if (attributesNode != null)
                    {
                        foreach (string attributeName in _casAttributesToCapture)
                        {
                            foreach (var element in attributesNode.Elements(_ns + attributeName))
                            {
                                claimsIdentity.AddClaim(new Claim(element.Name.LocalName, element.Value));
                            }
                        }
                    }

                    var user = new ClaimsPrincipal(claimsIdentity);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, user);

                    return LocalRedirect(!string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : "/");
                }
            }
            catch (TaskCanceledException)
            {
                // Request was aborted by the client; do not treat as application error.
            }

            return new ForbidResult();
        }
    }
}
