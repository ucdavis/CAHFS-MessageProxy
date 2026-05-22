using MessageProxyApi.Models;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Net;

namespace MessageProxyApi.Pages
{
    public class LoginModel : PageModel
    {
        private readonly CasSettings _settings;

        public LoginModel(IOptions<CasSettings> settingsOptions)
        {
            _settings = settingsOptions.Value;
        }

        public IActionResult OnGet()
        {
            var url = new Uri(Request.GetDisplayUrl());
            string baseUrl = url.GetLeftPart(UriPartial.Authority);
            string returnUrl = HttpHelper.GetRootURL().Replace(baseUrl, string.Empty).Replace("Login", string.Empty, StringComparison.CurrentCultureIgnoreCase);

            if (!string.IsNullOrEmpty(Request.Query["ReturnUrl"]))
            {
                returnUrl = Request.Query["ReturnUrl"].ToString();
            }

            var redirectUrl = HttpHelper.GetRootURL() + new PathString("/CasLogin");
            var authorizationEndpoint = _settings.CasBaseUrl + "login?service=" +
                WebUtility.UrlEncode(redirectUrl + "?ReturnUrl=" + WebUtility.UrlEncode(returnUrl));

            return new RedirectResult(authorizationEndpoint);
        }
    }
}
