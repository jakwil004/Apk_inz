using System;
using System.Collections.Generic;
using System.Text;

using System.ComponentModel.DataAnnotations;

namespace Usterki.Contracts.Auth;

public sealed class RegisterOwnerRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string OrganizationName { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}

public sealed record RegisterOwnerResponse(
    Guid UserId,
    Guid OrganizationId);

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    string TokenType = "Bearer");

public sealed record OrganizationMembershipResponse(
    Guid OrganizationId,
    string OrganizationName,
    string Role);

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<OrganizationMembershipResponse> Organizations);
public sealed class RefreshRequest
{
    [Required, StringLength(200, MinimumLength = 40)]
    public string RefreshToken { get; set; } = string.Empty;
}