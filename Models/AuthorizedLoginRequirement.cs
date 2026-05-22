using Microsoft.AspNetCore.Authorization;

namespace MessageProxyApi.Models
{
    public class AuthorizedLoginRequirement : IAuthorizationRequirement
    {
        public IReadOnlyCollection<string> AllowedLoginIds { get; }

        public AuthorizedLoginRequirement(IEnumerable<string> allowedLoginIds)
        {
            AllowedLoginIds = allowedLoginIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .ToArray() ?? Array.Empty<string>();
        }
    }
}
