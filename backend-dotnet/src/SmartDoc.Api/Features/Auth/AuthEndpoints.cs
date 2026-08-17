using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartDoc.Domain.Entities;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", LoginAsync).WithTags("Auth");
    }

    private static async Task<Results<Ok<LoginResponse>, ValidationProblem, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        IValidator<LoginRequest> validator,
        SmartDocDbContext db,
        JwtTokenGenerator tokenGenerator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TypedResults.ValidationProblem(validationResult.ToDictionary());
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email, cancellationToken);
        if (user is null)
        {
            // Same response as a wrong password — doesn't reveal whether the email exists.
            return TypedResults.Unauthorized();
        }

        var hasher = new PasswordHasher<User>();
        var verificationResult = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return TypedResults.Unauthorized();
        }

        var (token, expiresAt) = tokenGenerator.GenerateToken(user);
        return TypedResults.Ok(new LoginResponse(token, expiresAt));
    }
}
