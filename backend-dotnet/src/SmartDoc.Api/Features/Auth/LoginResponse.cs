namespace SmartDoc.Api.Features.Auth;

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);
