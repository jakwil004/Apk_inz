using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usterki.Contracts.Notifications;
using Usterki.Contracts.Properties;
using Usterki.Domain.Entities;
using Usterki.Infrastructure.Persistence;
using Usterki.Infrastructure.Queries;

namespace Usterki.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(
        [FromQuery] PaginationQuery pagination,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query = VisibleNotifications(userId);

        if (unreadOnly)
        {
            query = query.Where(
                notification => notification.ReadAtUtc == null);
        }

        var totalCount = await query.CountAsync(
            cancellationToken);

        var items = await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(notification => new NotificationResponse(
                notification.Id,
                notification.TicketId,
                notification.Type.ToString(),
                notification.Message,
                notification.CreatedAtUtc,
                notification.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<NotificationResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var count = await VisibleNotifications(userId)
            .CountAsync(
                notification => notification.ReadAtUtc == null,
                cancellationToken);

        return Ok(new UnreadNotificationsCountResponse(count));
    }

    [HttpPatch("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query = VisibleNotifications(userId)
            .Where(notification => notification.Id == notificationId);

        var now = DateTime.UtcNow;

        var changed = await query
            .Where(notification => notification.ReadAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    notification => notification.ReadAtUtc,
                    (DateTime?)now),
                cancellationToken);

        if (changed > 0)
        {
            return NoContent();
        }
        var exists = await query.AnyAsync(cancellationToken);

        return exists ? NoContent() : NotFound();
    }

    private IQueryable<UserNotification> VisibleNotifications(
        Guid userId)
    {
        var visibleTicketIds = _db.Tickets
            .VisibleTo(_db, userId)
            .Select(ticket => ticket.Id);

        return _db.UserNotifications
            .AsNoTracking()
            .Where(notification =>
                notification.RecipientUserId == userId &&
                visibleTicketIds.Contains(notification.TicketId));
    }

    private bool TryGetUserId(out Guid userId)
    {
        return Guid.TryParse(
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            out userId);
    }
}