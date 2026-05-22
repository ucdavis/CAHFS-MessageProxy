using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MessageProxyApi.Models
{
    public class AuthorizedLoginHandler : AuthorizationHandler<AuthorizedLoginRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthorizedLoginRequirement requirement)
        {
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                return Task.CompletedTask;
            }

            if (!context.User.HasClaim(c => c.Type == ClaimTypes.AuthenticationMethod && c.Value == "CAS"))
            {
                return Task.CompletedTask;
            }

            string? loginId = context.User.FindFirst(ClaimTypes.Name)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(loginId))
            {
                return Task.CompletedTask;
            }

            if (requirement.AllowedLoginIds.Any(id => string.Equals(id, loginId, StringComparison.OrdinalIgnoreCase)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
