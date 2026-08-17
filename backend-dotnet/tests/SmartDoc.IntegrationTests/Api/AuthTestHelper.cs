using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Api.Features.Auth;
using SmartDoc.Domain.Entities;

namespace SmartDoc.IntegrationTests.Api;

internal static class AuthTestHelper
{
    /// <summary>
    /// Attaches a valid bearer token for the given user, minted directly via
    /// JwtTokenGenerator — bypasses the real POST /api/auth/login flow (already covered end
    /// to end by AuthEndpointsTests) so other test classes can focus on their own endpoint's
    /// behavior instead of re-proving login works.
    /// </summary>
    public static void AuthenticateAs(this HttpClient client, WebApplicationFactory<Program> factory, User user)
    {
        var tokenGenerator = factory.Services.GetRequiredService<JwtTokenGenerator>();
        var (token, _) = tokenGenerator.GenerateToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
