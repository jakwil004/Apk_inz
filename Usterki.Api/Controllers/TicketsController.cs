using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usterki.Api.Authorization;
using Usterki.Api.Errors;
using Usterki.Api.Queries;
using Usterki.Contracts.Properties;
using Usterki.Contracts.Tickets;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Domain.Rules;
using Usterki.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Usterki.Api.Models;
using Usterki.Infrastructure.Photos;
using Usterki.Infrastructure.Storage;
using Usterki.Infrastructure.Queries;
using Usterki.Infrastructure.Notifications;
namespace Usterki.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TicketsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("ticket-categories")]
    public ActionResult<IReadOnlyList<TicketCategoryResponse>> GetCategories()
    {
        return Ok(new[]
        {
            new TicketCategoryResponse(
                nameof(TicketCategory.Plumbing),
                "Instalacja wodna i kanalizacja"),

            new TicketCategoryResponse(
                nameof(TicketCategory.Electrical),
                "Instalacja elektryczna"),

            new TicketCategoryResponse(
                nameof(TicketCategory.Heating),
                "Ogrzewanie"),

            new TicketCategoryResponse(
                nameof(TicketCategory.Building),
                "Elementy budynku i wyposażenie"),

            new TicketCategoryResponse(
                nameof(TicketCategory.Other),
                "Inne")
        });
    }
    [HttpPost("tickets/{ticketId:guid}/photos")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("photo-upload")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(
    Guid ticketId,
    [FromForm] UploadTicketPhotoRequest request,
    [FromServices] PhotoProcessor processor,
    [FromServices] IPhotoStorage storage,
    [FromServices] ILogger<TicketsController> logger,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var ticket = await VisibleTickets(userId)
            .AsTracking()
            .SingleOrDefaultAsync(
                item => item.Id == ticketId,
                cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        if (request.File.Length <= 0 ||
            request.File.Length > PhotoProcessor.MaxUploadBytes)
        {
            return BadRequest(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "invalid_photo_size",
                "Zdjęcie musi mieć od 1 bajta do 10 MiB."));
        }

        var count = await _db.TicketPhotos.CountAsync(
            photo => photo.TicketId == ticketId,
            cancellationToken);

        if (count >= 10)
        {
            return Conflict(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                "photo_limit_reached",
                "Zgłoszenie może mieć maksymalnie 10 zdjęć."));
        }

        ProcessedPhoto processed;

        try
        {
            using var buffer = new MemoryStream();

            await request.File.CopyToAsync(
                buffer,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            processed = processor.Process(buffer.ToArray());

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (PhotoValidationException exception)
        {
            return BadRequest(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "invalid_photo",
                exception.Message));
        }

        var storageId = await storage.SaveAsync(
            processed.Content,
            cancellationToken);

        var photo = new TicketPhoto
        {
            TicketId = ticketId,
            UploadedByUserId = userId,
            StorageId = storageId,
            ContentType = processed.ContentType,
            SizeBytes = processed.Content.LongLength,
            Width = processed.Width,
            Height = processed.Height,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.TicketPhotos.Add(photo);

        //zwiększamy wersję zgłoszenia, aby wykryć konflikt przy równoczesnym dodawaniu zdjęć
        ticket.Version++;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await TryDeletePhotoFileAsync(storage, storageId, logger);

            return Conflict(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                "ticket_version_conflict",
                "Zgłoszenie zostało zmienione. Ponów dodanie zdjęcia."));
        }
        catch (Exception exception)
        {
            
            logger.LogError(
                exception,
                "Nie udało się potwierdzić zapisu zdjęcia. " +
                "StorageId: {StorageId}, TicketId: {TicketId}",
                storageId,
                ticketId);

            throw;
        }

        var response = new TicketPhotoResponse(
            photo.Id,
            photo.TicketId,
            photo.UploadedByUserId,
            photo.ContentType,
            photo.SizeBytes,
            photo.Width,
            photo.Height,
            photo.CreatedAtUtc);

        return CreatedAtAction(
            nameof(DownloadPhoto),
            new { ticketId, photoId = photo.Id },
            response);
    }
    [HttpGet("tickets/{ticketId:guid}/photos")]
    public async Task<IActionResult> GetPhotos(
    Guid ticketId,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var canRead = await VisibleTickets(userId)
            .AnyAsync(
                ticket => ticket.Id == ticketId,
                cancellationToken);

        if (!canRead)
        {
            return NotFound();
        }

        var photos = await _db.TicketPhotos
            .AsNoTracking()
            .Where(photo => photo.TicketId == ticketId)
            .OrderBy(photo => photo.CreatedAtUtc)
            .ThenBy(photo => photo.Id)
            .Select(photo => new TicketPhotoResponse(
                photo.Id,
                photo.TicketId,
                photo.UploadedByUserId,
                photo.ContentType,
                photo.SizeBytes,
                photo.Width,
                photo.Height,
                photo.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(photos);
    }
    [HttpGet("tickets/{ticketId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DownloadPhoto(
    Guid ticketId,
    Guid photoId,
    [FromServices] IPhotoStorage storage,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var canRead = await VisibleTickets(userId)
            .AnyAsync(
                ticket => ticket.Id == ticketId,
                cancellationToken);

        if (!canRead)
        {
            return NotFound();
        }

        var photo = await _db.TicketPhotos
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == photoId &&
                        item.TicketId == ticketId,
                cancellationToken);

        if (photo is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(
            photo.StorageId,
            cancellationToken);

        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

       
        return File(
            stream,
            photo.ContentType,
            $"photo-{photo.Id:N}.jpg",
            enableRangeProcessing: false);
    }
    [HttpDelete("tickets/{ticketId:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> DeletePhoto(
    Guid ticketId,
    Guid photoId,
    [FromServices] IPhotoStorage storage,
    [FromServices] ILogger<TicketsController> logger,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var ticket = await VisibleTickets(userId)
            .AsTracking()
            .SingleOrDefaultAsync(
                item => item.Id == ticketId,
                cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        var photo = await _db.TicketPhotos
            .SingleOrDefaultAsync(
                item => item.Id == photoId &&
                        item.TicketId == ticketId,
                cancellationToken);

        if (photo is null)
        {
            return NotFound();
        }

        var organizationId = await _db.Units
            .Where(unit => unit.Id == ticket.UnitId)
            .Select(unit => unit.Building.OrganizationId)
            .SingleAsync(cancellationToken);

        var isManager = await _db.OrganizationMemberships.AnyAsync(
            membership =>
                membership.OrganizationId == organizationId &&
                membership.UserId == userId &&
                membership.IsActive &&
                (membership.Role == OrganizationRole.Owner ||
                 membership.Role == OrganizationRole.Manager),
            cancellationToken);

        if (photo.UploadedByUserId != userId && !isManager)
        {
            return Forbid();
        }

        _db.TicketPhotos.Remove(photo);
        ticket.Version++;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                "ticket_version_conflict",
                "Zgłoszenie zostało zmienione. Ponów usunięcie zdjęcia."));
        }

        await TryDeletePhotoFileAsync(
            storage,
            photo.StorageId,
            logger);

        return NoContent();
    }
    [HttpPost("units/{unitId:guid}/tickets")]
    public async Task<ActionResult<TicketResponse>> Create(
        Guid unitId,
        CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }


        if (!Enum.TryParse<TicketCategory>(
                request.Category,
                ignoreCase: false,
                out var category) ||
            !Enum.IsDefined(typeof(TicketCategory), category) ||
            category.ToString() != request.Category)
        {
            return BadRequest(new
            {
                message = "Nieprawidłowa kategoria zgłoszenia."
            });
        }

        var canReport = await _db.UnitResidents.AnyAsync(
            assignment =>
                assignment.UnitId == unitId &&
                assignment.UserId == userId &&
                assignment.IsActive &&
                _db.OrganizationMemberships.Any(membership =>
                    membership.UserId == userId &&
                    membership.OrganizationId ==
                        assignment.Unit.Building.OrganizationId &&
                    membership.IsActive &&
                    membership.Role == OrganizationRole.Resident),
            cancellationToken);

        if (!canReport)
        {
            
            return NotFound();
        }

        var ticket = new Ticket
        {
            UnitId = unitId,
            ReporterId = userId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            LocationDetails = request.LocationDetails.Trim(),
            Category = category,
            Status = TicketStatus.New,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Tickets.Add(ticket);
        _db.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = null,
            ToStatus = TicketStatus.New,
            ChangedByUserId = userId,
            Reason = "Utworzenie zgłoszenia.",
            ChangedAtUtc = ticket.CreatedAtUtc
        });
        await _db.SaveChangesAsync(cancellationToken);

        var response = await _db.Tickets
            .AsNoTracking()
            .Where(item => item.Id == ticket.Id)
            .Select(ToResponse)
            .SingleAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { ticketId = ticket.Id },
            response);
    }

    [HttpGet("tickets/{ticketId:guid}/status-history")]
    public async Task<ActionResult<PagedResponse<TicketStatusHistoryResponse>>>
    GetStatusHistory(
        Guid ticketId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var canRead = await VisibleTickets(userId)
            .AnyAsync(ticket => ticket.Id == ticketId, cancellationToken);

        if (!canRead)
        {
            return NotFound();
        }
        //pobranie historii statusów zgłoszenia
        var query = _db.TicketStatusHistories
            .AsNoTracking()
            .Where(history => history.TicketId == ticketId);
        
        var totalCount = await query.CountAsync(cancellationToken);
        //pobranie historii statusów zgłoszenia z dzieleniem danych na mniejsze
        var items = await query
            .OrderBy(history => history.ChangedAtUtc)
            .ThenBy(history => history.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(history => new TicketStatusHistoryResponse(
                history.Id,
                history.FromStatus.HasValue
                    ? history.FromStatus.Value.ToString()
                    : null,
                history.ToStatus.ToString(),
                history.ChangedByUserId,
                history.Reason,
                history.ChangedAtUtc))
            .ToListAsync(cancellationToken);
        
        return Ok(new PagedResponse<TicketStatusHistoryResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("tickets/mine")]
    public async Task<ActionResult<PagedResponse<TicketResponse>>> GetMine(
        [FromQuery] TicketListQuery filters,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query = VisibleTickets(userId)
            .Where(ticket => ticket.ReporterId == userId);

        return Ok(await ToPageAsync(
            query,
            filters,
            cancellationToken));
    }
    [HttpPost("tickets/{ticketId:guid}/comments")]
    public async Task<ActionResult<TicketCommentResponse>> AddComment(
    Guid ticketId,
    CreateTicketCommentRequest request,
    [FromServices] TicketNotificationService notifications,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var ticket = await VisibleTickets(userId)
    .SingleOrDefaultAsync(
        item => item.Id == ticketId,
        cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        var comment = new TicketComment
        {
            TicketId = ticketId,
            AuthorId = userId,
            Content = request.Content.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.TicketComments.Add(comment);
        await notifications.StageAsync(ticket,userId,NotificationType.CommentAdded,cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var authorName = await _db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.FirstName + " " + user.LastName)
            .SingleAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new TicketCommentResponse(
                comment.Id,
                comment.TicketId,
                comment.AuthorId,
                authorName,
                comment.Content,
                comment.CreatedAtUtc));
    }

    [HttpGet("tickets/{ticketId:guid}/comments")]
    public async Task<ActionResult<PagedResponse<TicketCommentResponse>>>
    GetComments(
        Guid ticketId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var canRead = await VisibleTickets(userId)
            .AnyAsync(
                ticket => ticket.Id == ticketId,
                cancellationToken);

        if (!canRead)
        {
            return NotFound();
        }

        //zachowujemy encje do filtrowania, sortowania i stronicowania
        var query =
            from comment in _db.TicketComments.AsNoTracking()
            join author in _db.Users.AsNoTracking()
                on comment.AuthorId equals author.Id
            where comment.TicketId == ticketId
            select new
            {
                Comment = comment,
                Author = author
            };

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(item => item.Comment.CreatedAtUtc)
            .ThenBy(item => item.Comment.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(item => new TicketCommentResponse(
                item.Comment.Id,
                item.Comment.TicketId,
                item.Comment.AuthorId,
                item.Author.FirstName + " " + item.Author.LastName,
                item.Comment.Content,
                item.Comment.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<TicketCommentResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<ActionResult<TicketResponse>> GetById(
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var ticket = await VisibleTickets(userId)
            .Where(ticket => ticket.Id == ticketId)
            .Select(ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        return Ok(ticket);
    }
    [HttpPatch("organizations/{organizationId:guid}/tickets/{ticketId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.ManageTickets)]
    public async Task<ActionResult<TicketResponse>> ChangeStatus(
    Guid organizationId,
    Guid ticketId,
    ChangeTicketStatusRequest request,
    [FromServices] TicketNotificationService notifications,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<TicketStatus>(
                request.Status,
                ignoreCase: false,
                out var targetStatus) ||
            !Enum.IsDefined(typeof(TicketStatus), targetStatus) ||
            targetStatus.ToString() != request.Status)
        {
            return BadRequest(new
            {
                message = "Nieprawidłowy status."
            });
        }

        //śledzenie encji
        var ticket = await _db.Tickets
            .SingleOrDefaultAsync(
                ticket =>
                    ticket.Id == ticketId &&
                    ticket.Unit.Building.OrganizationId == organizationId,
                cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Version != request.ExpectedVersion)
        {
            return TicketVersionConflict();
        }

        if (!TicketStatusRules.CanChange(ticket.Status, targetStatus))
        {
            return BadRequest(new
            {
                message = $"Zmiana z {ticket.Status} na {targetStatus} " +
                          "jest niedozwolona."
            });
        }

        var previousStatus = ticket.Status;

        ticket.Status = targetStatus;
        ticket.Version++;

        _db.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            FromStatus = previousStatus,
            ToStatus = targetStatus,
            ChangedByUserId = userId,
            Reason = request.Reason.Trim(),
            ChangedAtUtc = DateTime.UtcNow
        });

        try
        {
            //jeden zapis histori i statusu
            await notifications.StageAsync(ticket,userId,NotificationType.StatusChanged,cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            //zmiana statusu przez innego użytkownika w tym samym czasie
            return TicketVersionConflict();
        }

        var response = await _db.Tickets
            .AsNoTracking()
            .Where(item => item.Id == ticketId)
            .Select(ToResponse)
            .SingleAsync(cancellationToken);

        return Ok(response);
    }

    private ConflictObjectResult TicketVersionConflict()
    {
        return new ConflictObjectResult(
            ApiErrors.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                code: "ticket_version_conflict",
                detail: "Zgłoszenie zostało zmienione. " +
                        "Pobierz aktualne dane i ponów operację."));
    }

    [HttpGet("organizations/{organizationId:guid}/tickets")]
    [Authorize(Policy = AuthorizationPolicies.ManageTickets)]
    public async Task<ActionResult<PagedResponse<TicketResponse>>>
        GetOrganizationTickets(
            Guid organizationId,
            [FromQuery] TicketListQuery filters,
            CancellationToken cancellationToken)
    {
        var query = _db.Tickets
            .AsNoTracking()
            .Where(ticket =>
                ticket.Unit.Building.OrganizationId == organizationId);

        return Ok(await ToPageAsync(
            query,
            filters,
            cancellationToken));
    }
    [HttpGet(
    "organizations/{organizationId:guid}/tickets/{ticketId:guid}/assignment-history")]
    [Authorize(Policy = AuthorizationPolicies.ManageTickets)]
    public async Task<ActionResult<PagedResponse<TicketAssignmentHistoryResponse>>>
    GetAssignmentHistory(
        Guid organizationId,
        Guid ticketId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken cancellationToken)
    {
        var ticketExists = await _db.Tickets.AnyAsync(
            ticket =>
                ticket.Id == ticketId &&
                ticket.Unit.Building.OrganizationId == organizationId,
            cancellationToken);

        if (!ticketExists)
        {
            return NotFound();
        }

        var query = _db.TicketAssignmentHistories
            .AsNoTracking()
            .Where(history => history.TicketId == ticketId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(history => history.ChangedAtUtc)
            .ThenBy(history => history.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(history => new TicketAssignmentHistoryResponse(
                history.Id,
                history.PreviousTechnicianId,
                history.NewTechnicianId,
                history.ChangedByUserId,
                history.Reason,
                history.ChangedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<TicketAssignmentHistoryResponse>(
            items,
            pagination.Page,
            pagination.PageSize,
            totalCount));
    }

    [HttpGet("tickets/assigned")]
    public async Task<ActionResult<PagedResponse<TicketResponse>>> GetAssigned(
    [FromQuery] TicketListQuery filters,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query = VisibleTickets(userId)
            .Where(ticket =>
                ticket.AssignedTechnicianId == userId &&
                _db.OrganizationMemberships.Any(membership =>
                    membership.UserId == userId &&
                    membership.OrganizationId ==
                        ticket.Unit.Building.OrganizationId &&
                    membership.Role == OrganizationRole.Technician &&
                    membership.IsActive));

        return Ok(await ToPageAsync(
            query,
            filters,
            cancellationToken));
    }

    [HttpPut("organizations/{organizationId:guid}/tickets/{ticketId:guid}/assignment")]
    [Authorize(Policy = AuthorizationPolicies.ManageTickets)]
    public async Task<ActionResult<TicketResponse>> ChangeAssignment(
    Guid organizationId,
    Guid ticketId,
    ChangeTicketAssignmentRequest request,
    [FromServices] TicketNotificationService notifications,
    CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (request.TechnicianId == Guid.Empty)
        {
            return BadRequest(ApiErrors.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                code: "invalid_technician_id",
                detail: "Podaj identyfikator serwisanta albo null."));
        }

        var ticket = await _db.Tickets.SingleOrDefaultAsync(
            ticket =>
                ticket.Id == ticketId &&
                ticket.Unit.Building.OrganizationId == organizationId,
            cancellationToken);

        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Version != request.ExpectedVersion)
        {
            return TicketVersionConflict();
        }

        if (request.TechnicianId is Guid technicianId)
        {
            if (ticket.Status is TicketStatus.Closed or TicketStatus.Rejected)
            {
                return Conflict(ApiErrors.Create(
                    HttpContext,
                    StatusCodes.Status409Conflict,
                    code: "ticket_assignment_not_allowed",
                    detail: "Nie można przydzielić serwisanta do " +
                            "zamkniętego lub odrzuconego zgłoszenia."));
            }

            var validTechnician = await (
                from membership in _db.OrganizationMemberships
                join user in _db.Users
                    on membership.UserId equals user.Id
                where membership.OrganizationId == organizationId
                      && membership.UserId == technicianId
                      && membership.Role == OrganizationRole.Technician
                      && membership.IsActive
                      && user.IsActive
                select membership.UserId
            ).AnyAsync(cancellationToken);

            if (!validTechnician)
            {
                return BadRequest(ApiErrors.Create(
                    HttpContext,
                    StatusCodes.Status400BadRequest,
                    code: "technician_not_available",
                    detail: "Wybrane konto nie jest aktywnym serwisantem " +
                            "tej organizacji."));
            }
        }

        //jeżeli zgłoszenie już ma przypisanego serwisanta i jest to ten sam serwisant, to nie wykonujemy żadnych zmian
        if (ticket.AssignedTechnicianId == request.TechnicianId)
        {
            var unchanged = await _db.Tickets
                .AsNoTracking()
                .Where(item => item.Id == ticketId)
                .Select(ToResponse)
                .SingleAsync(cancellationToken);

            return Ok(unchanged);
        }

        var previousTechnicianId = ticket.AssignedTechnicianId;

        ticket.AssignedTechnicianId = request.TechnicianId;
        ticket.Version++;

        _db.TicketAssignmentHistories.Add(new TicketAssignmentHistory
        {
            TicketId = ticket.Id,
            PreviousTechnicianId = previousTechnicianId,
            NewTechnicianId = request.TechnicianId,
            ChangedByUserId = userId,
            Reason = request.Reason.Trim(),
            ChangedAtUtc = DateTime.UtcNow
        });

        try
        {
            await notifications.StageAsync(ticket,userId,NotificationType.AssignmentChanged,cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TicketVersionConflict();
        }

        var response = await _db.Tickets
            .AsNoTracking()
            .Where(item => item.Id == ticketId)
            .Select(ToResponse)
            .SingleAsync(cancellationToken);

        return Ok(response);
    }

    private IQueryable<Ticket> VisibleTickets(Guid userId)
    {
        return _db.Tickets
            .AsNoTracking()
            .VisibleTo(_db, userId);
    }

    private static async Task<PagedResponse<TicketResponse>> ToPageAsync(
       IQueryable<Ticket> query,
       TicketListQuery filters,
       CancellationToken cancellationToken)
    {
        var filteredQuery = query.ApplyFilters(filters);

        var totalCount = await filteredQuery.CountAsync(
            cancellationToken);

        var items = await filteredQuery
            .ApplySorting(filters.Sort)
            .Skip((filters.Page - 1) * filters.PageSize)
            .Take(filters.PageSize)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

        return new PagedResponse<TicketResponse>(
            items,
            filters.Page,
            filters.PageSize,
            totalCount);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var subject = User.FindFirst(
            JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out userId);
    }
    private static async Task TryDeletePhotoFileAsync(
    IPhotoStorage storage,
    Guid storageId,
    ILogger logger)
    {
        try
        {
            await storage.DeleteAsync(
                storageId,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Nie udało się usunąć pliku zdjęcia {StorageId}.",
                storageId);
        }
    }

    private static readonly Expression<Func<Ticket, TicketResponse>>
        ToResponse = ticket => new TicketResponse(
            ticket.Id,
            ticket.Unit.Building.OrganizationId,
            ticket.Unit.BuildingId,
            ticket.UnitId,
            ticket.Unit.Number,
            ticket.ReporterId,
            ticket.Title,
            ticket.Description,
            ticket.LocationDetails,
            ticket.Category.ToString(),
            ticket.Status.ToString(),
            ticket.CreatedAtUtc,
            ticket.Version,
            ticket.AssignedTechnicianId);
}