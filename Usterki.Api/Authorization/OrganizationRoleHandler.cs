using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Usterki.Infrastructure.Persistence;

namespace Usterki.Api.Authorization;

public sealed class OrganizationRoleHandler
    : AuthorizationHandler<OrganizationRoleRequirement>
{
    private readonly AppDbContext _db;

    public OrganizationRoleHandler(AppDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrganizationRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var httpContext = context.Resource switch
        {
            HttpContext http => http,
            AuthorizationFilterContext filter => filter.HttpContext,
            _ => null
        };

        if (httpContext is null)
        {
            return;
        }

        var subject = context.User
            .FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        var organizationValue = httpContext.Request
            .RouteValues["organizationId"]?.ToString();

        if (!Guid.TryParse(subject, out var userId) ||
            !Guid.TryParse(organizationValue, out var organizationId))
        {
            return;
        }

        var allowedRoles = requirement.AllowedRoles;

        var hasAccess = await _db.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.UserId == userId &&
                    membership.OrganizationId == organizationId &&
                    membership.IsActive &&
                    allowedRoles.Contains(membership.Role),
                httpContext.RequestAborted);

        if (hasAccess)
        {
            context.Succeed(requirement);
        }
    }
}