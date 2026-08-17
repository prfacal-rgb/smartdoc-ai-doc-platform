using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SmartDoc.Api.Features.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Reads the UserId from the token's "sub" claim. Only callable on an already-validated
    /// principal (i.e. behind .RequireAuthorization()) — the JWT Bearer middleware rejects
    /// the request before the handler runs if the token is missing/invalid/expired.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token has no 'sub' claim.");

        return Guid.Parse(sub);
    }
}
