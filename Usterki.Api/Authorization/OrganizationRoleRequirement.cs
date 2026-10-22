using Microsoft.AspNetCore.Authorization;
using Usterki.Domain.Enums;

namespace Usterki.Api.Authorization;

public sealed class OrganizationRoleRequirement : IAuthorizationRequirement
{
    public OrganizationRole[] AllowedRoles { get; }

    public OrganizationRoleRequirement(
        params OrganizationRole[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}