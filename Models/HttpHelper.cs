using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace MessageProxyApi.Models
{
    public class HttpHelper
    {
        private static IHttpContextAccessor? _httpContextAccessor;

        public static IConfiguration? Settings { get; private set; }
        public static IWebHostEnvironment? Environment { get; private set; }
        //public static IMemoryCache? Cache { get; private set; }
        //IMemoryCache? memoryCache, 

        public static HttpContext? HttpContext => _httpContextAccessor?.HttpContext;

        public static void Configure(IConfiguration? configurationSettings, IWebHostEnvironment env, IHttpContextAccessor? httpContextAccessor)
        {
            //Cache = memoryCache;
            Settings = configurationSettings;
            Environment = env;
            _httpContextAccessor = httpContextAccessor;
        }

        public static string GetRootURL()
        {
            var rootURL = string.Empty;
            HttpRequest? request = _httpContextAccessor?.HttpContext?.Request;

            if (request != null)
            {
                var url = new Uri(request.GetDisplayUrl());
                rootURL = url.GetLeftPart(UriPartial.Authority);

                if (!string.IsNullOrEmpty(request.PathBase))
                {
                    rootURL += request.PathBase.Value;
                }
                else if (Environment != null && Environment.IsEnvironment("Test"))
                {
                    rootURL += "/messageproxytest";
                }
                else if (Environment != null && Environment.IsEnvironment("Production"))
                {
                    rootURL += "/messageproxy";
                }
            }

            return rootURL;
        }
    }
}
