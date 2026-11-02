using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Usterki.Contracts.Auth;
using Usterki.Contracts.Common;
using Usterki.Contracts.Properties;
using Usterki.Contracts.Residents;
using Usterki.Contracts.Tickets;
using Usterki.Domain.Entities;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Persistence;
using Usterki.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Usterki.IntegrationTests;

public sealed class SecurityIntegrationTests : IDisposable
{
    private const string TestPassword = "TestoweHaslo123!";

    private readonly TestApiFactory _factory;
    private readonly HttpClient _client;

    public SecurityIntegrationTests()
    {

        _factory = new TestApiFactory();

        _client = _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }
    private async Task<TicketResponse> CreateTicketForAsync(
    TestOwner resident,
    Guid unitId)
    {
        UseToken(resident.Auth.AccessToken);

        var response = await _client.PostAsJsonAsync(
            $"/api/units/{unitId}/tickets",
            ValidTicketRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadAsync<TicketResponse>(response);
    }

    private async Task<(
    TestOwner Owner,
    TestOwner Resident,
    BuildingResponse Building,
    UnitResponse Unit)> CreateTicketScenarioAsync()
    {
        var owner = await RegisterAndLoginAsync();
        var resident = await RegisterAndLoginAsync();

        var building = await CreateBuildingAsync(owner);

        var response = await _client.PostAsJsonAsync(
            $"{BuildingsUrl(owner.OrganizationId)}/{building.Id}/units",
            new CreateUnitRequest
            {
                Number = "1",
                Floor = 1
            });




        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var unit = await ReadAsync<UnitResponse>(response);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = owner.OrganizationId,
            UserId = resident.UserId,
            Role = OrganizationRole.Resident,
            IsActive = true
        });

        db.UnitResidents.Add(new UnitResident
        {
            UnitId = unit.Id,
            UserId = resident.UserId,
            IsActive = true
        });

        await db.SaveChangesAsync();

        return (owner, resident, building, unit);
    }

    private static CreateTicketRequest ValidTicketRequest()
    {
        return new CreateTicketRequest
        {
            Title = "Testowe zgłoszenie",
            Description = "Testowa treść zgłoszenia",
            LocationDetails = "Łazienka",
            Category = "Plumbing"
        };
    }

    [Fact]
    public async Task Buildings_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync(
            BuildingsUrl(Guid.NewGuid()));

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.Manager, HttpStatusCode.OK)]
    [InlineData(OrganizationRole.Resident, HttpStatusCode.Forbidden)]
    [InlineData(OrganizationRole.Technician, HttpStatusCode.Forbidden)]
    public async Task Buildings_UsesCurrentOrganizationRole(
        OrganizationRole role,
        HttpStatusCode expectedStatus)
    {
        var owner = await RegisterAndLoginAsync();

        //JWT sprawdzenie czy uprawnienia są odczytywane z bazy
        await ChangeMembershipAsync(owner, role, isActive: true);

        UseToken(owner.Auth.AccessToken);

        var response = await _client.GetAsync(
            BuildingsUrl(owner.OrganizationId));

        Assert.Equal(expectedStatus, response.StatusCode);
    }
    [Fact]
    public async Task UnexpectedException_ReturnsSafeProblemDetails()
    {//test sprawdzający czy nie ujawnia się szczegółów wyjątku w odpowiedzi API
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(
                        typeof(FailureProbeController).Assembly);
            });
        });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });

        var response = await client.GetAsync("/_tests/failure");

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("INTERNAL_TEST_MARKER_DO_NOT_EXPOSE", body);
        Assert.DoesNotContain("InvalidOperationException", body);

        var problem = await ReadAsync<ApiProblemResponse>(response);

        Assert.Equal("internal_error", problem.Code);
        Assert.Equal(500, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task Resident_CannotCreateTicketForUnassignedUnit()
    {
        var scenario = await CreateTicketScenarioAsync();

        UseToken(scenario.Owner.Auth.AccessToken);

        var secondUnitResponse = await _client.PostAsJsonAsync(
            $"{BuildingsUrl(scenario.Owner.OrganizationId)}" +
            $"/{scenario.Building.Id}/units",
            new CreateUnitRequest
            {
                Number = "2",
                Floor = 1
            });

        Assert.Equal(
            HttpStatusCode.Created,
            secondUnitResponse.StatusCode);

        var secondUnit = await ReadAsync<UnitResponse>(secondUnitResponse);

        UseToken(scenario.Resident.Auth.AccessToken);

        var response = await _client.PostAsJsonAsync(
            $"/api/units/{secondUnit.Id}/tickets",
            ValidTicketRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var exists = await db.Tickets.AnyAsync(
            ticket => ticket.UnitId == secondUnit.Id);

        Assert.False(exists);
    }
    [Fact]
    public async Task StatusChange_AddsHistory_AndRejectsStaleVersion()
    {
        var scenario = await CreateTicketScenarioAsync();

        var ticket = await CreateTicketForAsync(
            scenario.Resident,
            scenario.Unit.Id);

        UseToken(scenario.Owner.Auth.AccessToken);

        var url =
            $"/api/organizations/{scenario.Owner.OrganizationId}" +
            $"/tickets/{ticket.Id}/status";

        var changed = await _client.PatchAsJsonAsync(
            url,
            new ChangeTicketStatusRequest
            {
                Status = "InProgress",
                Reason = "Rozpoczęcie pracy.",
                ExpectedVersion = ticket.Version
            });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var updated = await ReadAsync<TicketResponse>(changed);

        Assert.Equal("InProgress", updated.Status);
        Assert.Equal(ticket.Version + 1, updated.Version);
        //próba zmiany statusu ze starej wersji
        var staleChange = await _client.PatchAsJsonAsync(
            url,
            new ChangeTicketStatusRequest
            {
                Status = "Resolved",
                Reason = "Próba zapisu ze starego widoku.",
                ExpectedVersion = ticket.Version
            });

        Assert.Equal(HttpStatusCode.Conflict, staleChange.StatusCode);

        var conflictProblem = await ReadAsync<ApiProblemResponse>(staleChange);
        Assert.Equal(
            "ticket_version_conflict",
            conflictProblem.Code);

        var historyResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}/status-history");

        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var history =
            await ReadAsync<PagedResponse<TicketStatusHistoryResponse>>(
                historyResponse);
        //sprawdzamy czy historia zawiera wpisy o zmianach statusu
        Assert.Equal(2, history.TotalCount);

        Assert.Contains(history.Items, item =>
            item.FromStatus == null &&
            item.ToStatus == "New");

        Assert.Contains(history.Items, item =>
            item.FromStatus == "New" &&
            item.ToStatus == "InProgress" &&
            item.ChangedByUserId == scenario.Owner.UserId);

        var detailsResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        var details = await ReadAsync<TicketResponse>(detailsResponse);

        Assert.Equal("InProgress", details.Status);
        Assert.Equal(updated.Version, details.Version);
    }
    [Fact]
    public async Task StatusChange_EnforcesRoleAndAllowedTransitions()
    {
        var scenario = await CreateTicketScenarioAsync();

        var ticket = await CreateTicketForAsync(
            scenario.Resident,
            scenario.Unit.Id);

        var url =
            $"/api/organizations/{scenario.Owner.OrganizationId}" +
            $"/tickets/{ticket.Id}/status";
        //próba zmiany statusu przez mieszkańca
        var residentChange = await _client.PatchAsJsonAsync(
            url,
            new ChangeTicketStatusRequest
            {
                Status = "InProgress",
                Reason = "Próba mieszkańca.",
                ExpectedVersion = ticket.Version
            });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            residentChange.StatusCode);
        
        UseToken(scenario.Owner.Auth.AccessToken);
        //próba zmiany statusu z New do Closed pomijając InProgress i Resolved
        var invalidTransition = await _client.PatchAsJsonAsync(
            url,
            new ChangeTicketStatusRequest
            {
                Status = "Closed",
                Reason = "Niedozwolone pominięcie realizacji.",
                ExpectedVersion = ticket.Version
            });
        //sprawdzenie czy niedozwolona zmiana statusu nie zmienia wersji encji
        Assert.Equal(
            HttpStatusCode.BadRequest,
            invalidTransition.StatusCode);

        var detailsResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        var details = await ReadAsync<TicketResponse>(detailsResponse);

        Assert.Equal("New", details.Status);
        Assert.Equal(ticket.Version, details.Version);
    }
    [Fact]
    public async Task Comments_AreSharedWithOwner_ButHiddenFromOtherUsers()
    {
        var scenario = await CreateTicketScenarioAsync();
        var outsider = await RegisterAndLoginAsync();

        var ticket = await CreateTicketForAsync(
            scenario.Resident,
            scenario.Unit.Id);

        var url = $"/api/tickets/{ticket.Id}/comments";

        var residentComment = await _client.PostAsJsonAsync(
            url,
            new CreateTicketCommentRequest
            {
                Content = "Jestem dostępny po godzinie 16."
            });

        Assert.Equal(
            HttpStatusCode.Created,
            residentComment.StatusCode);

        var residentResult =
            await ReadAsync<TicketCommentResponse>(residentComment);

        Assert.Equal(scenario.Resident.UserId, residentResult.AuthorId);

        UseToken(scenario.Owner.Auth.AccessToken);
        //próba odczytu komentarzy przez właściciela organizacji
        var ownerComment = await _client.PostAsJsonAsync(
            url,
            new CreateTicketCommentRequest
            {
                Content = "Dziękuję za informację."
            });

        Assert.Equal(HttpStatusCode.Created, ownerComment.StatusCode);

        var ownerResult =
            await ReadAsync<TicketCommentResponse>(ownerComment);

        Assert.Equal(scenario.Owner.UserId, ownerResult.AuthorId);

        UseToken(outsider.Auth.AccessToken);

        var forbiddenRead = await _client.GetAsync(url);

        Assert.Equal(
            HttpStatusCode.NotFound,
            forbiddenRead.StatusCode);
        //próba dodania komentarza przez osobę niepowiązaną z organizacją
        var forbiddenWrite = await _client.PostAsJsonAsync(
            url,
            new CreateTicketCommentRequest
            {
                Content = "Niedozwolony komentarz."
            });

        Assert.Equal(
            HttpStatusCode.NotFound,
            forbiddenWrite.StatusCode);

        UseToken(scenario.Resident.Auth.AccessToken);

        var commentsResponse = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, commentsResponse.StatusCode);

        var comments =
            await ReadAsync<PagedResponse<TicketCommentResponse>>(
                commentsResponse);

        Assert.Equal(2, comments.TotalCount);
    }

    [Fact]
    public async Task InactiveUnitAssignment_RemovesResidentTicketAccess()
    {
        var scenario = await CreateTicketScenarioAsync();

        UseToken(scenario.Resident.Auth.AccessToken);

        var created = await _client.PostAsJsonAsync(
            $"/api/units/{scenario.Unit.Id}/tickets",
            ValidTicketRequest());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var ticket = await ReadAsync<TicketResponse>(created);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            var assignment = await db.UnitResidents.SingleAsync(
                assignment =>
                    assignment.UnitId == scenario.Unit.Id &&
                    assignment.UserId == scenario.Resident.UserId);

            assignment.IsActive = false;

            await db.SaveChangesAsync();
        }

        var details = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);

        var anotherCreate = await _client.PostAsJsonAsync(
            $"/api/units/{scenario.Unit.Id}/tickets",
            ValidTicketRequest());

        Assert.Equal(HttpStatusCode.NotFound, anotherCreate.StatusCode);

        //zgłoszenie pozostaje widoczne dla właściciela
        UseToken(scenario.Owner.Auth.AccessToken);

        var ownerDetails = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, ownerDetails.StatusCode);
    }

    [Fact]
    public async Task Ticket_IsVisibleToReporterAndOwner_ButNotOtherResident()
    {
        var scenario = await CreateTicketScenarioAsync();
        var otherResident = await RegisterAndLoginAsync();

        //drugi mieszkaniec
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = scenario.Owner.OrganizationId,
                UserId = otherResident.UserId,
                Role = OrganizationRole.Resident,
                IsActive = true
            });

            db.UnitResidents.Add(new UnitResident
            {
                UnitId = scenario.Unit.Id,
                UserId = otherResident.UserId,
                IsActive = true
            });

            await db.SaveChangesAsync();
        }

        UseToken(scenario.Resident.Auth.AccessToken);

        var created = await _client.PostAsJsonAsync(
            $"/api/units/{scenario.Unit.Id}/tickets",
            ValidTicketRequest());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var ticket = await ReadAsync<TicketResponse>(created);

        Assert.Equal("New", ticket.Status);
        Assert.Equal(scenario.Resident.UserId, ticket.ReporterId);
        Assert.Equal(scenario.Owner.OrganizationId, ticket.OrganizationId);

        var reporterResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, reporterResponse.StatusCode);

        UseToken(scenario.Owner.Auth.AccessToken);

        var ownerResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        var organizationResponse = await _client.GetAsync(
            $"/api/organizations/{scenario.Owner.OrganizationId}/tickets");

        Assert.Equal(HttpStatusCode.OK, organizationResponse.StatusCode);

        var organizationTickets =
            await ReadAsync<PagedResponse<TicketResponse>>(
                organizationResponse);

        Assert.Equal(ticket.Id, Assert.Single(organizationTickets.Items).Id);

        UseToken(otherResident.Auth.AccessToken);

        var otherResponse = await _client.GetAsync(
            $"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);

        var otherMineResponse = await _client.GetAsync("/api/tickets/mine");

        Assert.Equal(HttpStatusCode.OK, otherMineResponse.StatusCode);

        var otherMine =
            await ReadAsync<PagedResponse<TicketResponse>>(otherMineResponse);

        Assert.Empty(otherMine.Items);
    }

    [Fact]
    public async Task InactiveMembership_DeniesAccess_WithExistingJwt()
    {
        var owner = await RegisterAndLoginAsync();

        await ChangeMembershipAsync(
            owner,
            OrganizationRole.Owner,
            isActive: false);

        UseToken(owner.Auth.AccessToken);

        var response = await _client.GetAsync(
            BuildingsUrl(owner.OrganizationId));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Owner_CannotAccessAnotherOrganizationsBuilding()
    {
        var ownerA = await RegisterAndLoginAsync();
        var ownerB = await RegisterAndLoginAsync();

        var buildingB = await CreateBuildingAsync(ownerB);

        UseToken(ownerA.Auth.AccessToken);

        //próba dostępu do organizacji
        var foreignOrganizationResponse = await _client.GetAsync(
            BuildingsUrl(ownerB.OrganizationId));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            foreignOrganizationResponse.StatusCode);

        //próba zapisu do organizacji
        var foreignCreateResponse = await _client.PostAsJsonAsync(
            BuildingsUrl(ownerB.OrganizationId),
            BuildingRequest());

        Assert.Equal(
            HttpStatusCode.Forbidden,
            foreignCreateResponse.StatusCode);

        //własna organizacja w URL ale zły identyfikator budynku
        var mixedPath =
            $"{BuildingsUrl(ownerA.OrganizationId)}/{buildingB.Id}";

        var mixedGetResponse = await _client.GetAsync(mixedPath);

        Assert.Equal(
            HttpStatusCode.NotFound,
            mixedGetResponse.StatusCode);

        //próba dodania lokalu do cudzego budynku pod adresem własnej organizacji
        var mixedCreateResponse = await _client.PostAsJsonAsync(
            $"{mixedPath}/units",
            new CreateUnitRequest
            {
                Number = "NIEDOZWOLONY",
                Floor = 1
            });

        Assert.Equal(
            HttpStatusCode.NotFound,
            mixedCreateResponse.StatusCode);

        //brak w bazie
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var unexpectedUnitExists = await db.Units.AnyAsync(
            unit => unit.BuildingId == buildingB.Id);

        Assert.False(unexpectedUnitExists);

        var buildingsInB = await db.Buildings.CountAsync(
            building =>
                building.OrganizationId == ownerB.OrganizationId);

        Assert.Equal(1, buildingsInB);
    }

    [Fact]
    public async Task DuplicateUnitNumber_ReturnsConflict_AndDoesNotDuplicate()
    {
        var owner = await RegisterAndLoginAsync();
        var building = await CreateBuildingAsync(owner);

        UseToken(owner.Auth.AccessToken);

        var url =
            $"{BuildingsUrl(owner.OrganizationId)}/{building.Id}/units";

        var request = new CreateUnitRequest
        {
            Number = "12A",
            Floor = 2
        };

        var first = await _client.PostAsJsonAsync(url, request);
        var second = await _client.PostAsJsonAsync(url, request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var count = await db.Units.CountAsync(
            unit =>
                unit.BuildingId == building.Id &&
                unit.Number == "12A");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ReusedRefreshToken_RevokesEntireSession()
    {
        var owner = await RegisterAndLoginAsync();

        _client.DefaultRequestHeaders.Authorization = null;

        var firstRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest
            {
                RefreshToken = owner.Auth.RefreshToken
            });

        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        var rotated = await ReadAsync<AuthResponse>(firstRefresh);

        Assert.NotEqual(
            owner.Auth.RefreshToken,
            rotated.RefreshToken);

        UseToken(rotated.AccessToken);

        var beforeReplay = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, beforeReplay.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;

        var replay = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest
            {
                RefreshToken = owner.Auth.RefreshToken
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replay.StatusCode);

        UseToken(rotated.AccessToken);

        var afterReplay = await _client.GetAsync("/api/auth/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            afterReplay.StatusCode);

        _client.DefaultRequestHeaders.Authorization = null;

        var newRefreshAfterReplay = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest
            {
                RefreshToken = rotated.RefreshToken
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            newRefreshAfterReplay.StatusCode);
    }
    [Fact]
    public async Task Invitation_WrongAccountCannotAccept_AndDoesNotConsumeToken()
    {
        var owner = await RegisterAndLoginAsync();
        var recipient = await RegisterAndLoginAsync();
        var otherUser = await RegisterAndLoginAsync();

        var building = await CreateBuildingAsync(owner);

        var unitsUrl =
            $"{BuildingsUrl(owner.OrganizationId)}/{building.Id}/units";

        var unitResponse = await _client.PostAsJsonAsync(
            unitsUrl,
            new CreateUnitRequest { Number = "10", Floor = 2 });

        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);

        var unit = await ReadAsync<UnitResponse>(unitResponse);

        var invitationResponse = await _client.PostAsJsonAsync(
            $"{unitsUrl}/{unit.Id}/invitations",
            new CreateResidentInvitationRequest
            {
                Email = recipient.Email
            });

        Assert.Equal(HttpStatusCode.Created, invitationResponse.StatusCode);

        var invitation =
            await ReadAsync<ResidentInvitationResponse>(invitationResponse);

        UseToken(otherUser.Auth.AccessToken);

        var rejected = await _client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptResidentInvitationRequest
            {
                Token = invitation.Token
            });

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var otherUnitsResponse = await _client.GetAsync("/api/me/units");

        Assert.Equal(HttpStatusCode.OK, otherUnitsResponse.StatusCode);

        var otherUnits =
            await ReadAsync<PagedResponse<MyUnitResponse>>(otherUnitsResponse);

        Assert.Empty(otherUnits.Items);

        //próba tokenu z innego konta nie usuwa zaproszenia
        UseToken(recipient.Auth.AccessToken);

        var accepted = await _client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptResidentInvitationRequest
            {
                Token = invitation.Token
            });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
    [Fact]
    public async Task Invitation_GrantsOnlyInvitedUnit_AndCanBeUsedOnce()
    {
        var owner = await RegisterAndLoginAsync();

        //konto jest właścicielem organizacji i mieszkańcem w innej
        
        var resident = await RegisterAndLoginAsync();

        var building = await CreateBuildingAsync(owner);

        var unitsUrl =
            $"{BuildingsUrl(owner.OrganizationId)}/{building.Id}/units";

        var firstUnitResponse = await _client.PostAsJsonAsync(
            unitsUrl,
            new CreateUnitRequest { Number = "1", Floor = 1 });

        Assert.Equal(HttpStatusCode.Created, firstUnitResponse.StatusCode);

        var firstUnit = await ReadAsync<UnitResponse>(firstUnitResponse);

        var secondUnitResponse = await _client.PostAsJsonAsync(
            unitsUrl,
            new CreateUnitRequest { Number = "2", Floor = 1 });

        Assert.Equal(HttpStatusCode.Created, secondUnitResponse.StatusCode);

        var invitationResponse = await _client.PostAsJsonAsync(
            $"{unitsUrl}/{firstUnit.Id}/invitations",
            new CreateResidentInvitationRequest
            {
                Email = resident.Email
            });

        Assert.Equal(HttpStatusCode.Created, invitationResponse.StatusCode);

        var invitation =
            await ReadAsync<ResidentInvitationResponse>(invitationResponse);

        UseToken(resident.Auth.AccessToken);

        var accepted = await _client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptResidentInvitationRequest
            {
                Token = invitation.Token
            });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var unitsResponse = await _client.GetAsync("/api/me/units");

        Assert.Equal(HttpStatusCode.OK, unitsResponse.StatusCode);

        var units = await ReadAsync<PagedResponse<MyUnitResponse>>(
            unitsResponse);

        var onlyUnit = Assert.Single(units.Items);

        Assert.Equal(firstUnit.Id, onlyUnit.UnitId);
        Assert.Equal(owner.OrganizationId, onlyUnit.OrganizationId);
        Assert.Equal(1, units.TotalCount);

        var managementResponse = await _client.GetAsync(
            BuildingsUrl(owner.OrganizationId));

        Assert.Equal(
            HttpStatusCode.Forbidden,
            managementResponse.StatusCode);

        var secondAccept = await _client.PostAsJsonAsync(
            "/api/invitations/accept",
            new AcceptResidentInvitationRequest
            {
                Token = invitation.Token
            });

        Assert.Equal(HttpStatusCode.BadRequest, secondAccept.StatusCode);
    }
    [Fact]
    public async Task MissingJwt_ReturnsProblemDetails_AndBearerChallenge()
    {//brak tokenu JWT w nagłówku autoryzacji
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        Assert.Contains(
            response.Headers.WwwAuthenticate,
            header => header.Scheme == "Bearer");

        var problem = await ReadAsync<ApiProblemResponse>(response);

        Assert.Equal(401, problem.Status);
        Assert.Equal("unauthorized", problem.Code);
        Assert.Equal("/api/auth/me", problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task InvalidRegistration_ReturnsValidationProblemDetails()
    {//próba rejestracji z niepoprawnymi danymi
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                FirstName = "",
                LastName = "Test",
                Email = "niepoprawny-email",
                Password = "123"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadAsync<ApiProblemResponse>(response);

        Assert.Equal("validation_failed", problem.Code);
        Assert.NotNull(problem.Errors);
        Assert.NotEmpty(problem.Errors!);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task ForeignOrganization_ReturnsForbiddenProblemDetails()
    {
        var ownerA = await RegisterAndLoginAsync();
        var ownerB = await RegisterAndLoginAsync();

        UseToken(ownerA.Auth.AccessToken);
        //próba dostępu do organizacji B przy użyciu tokenu właściciela organizacji A
        var response = await _client.GetAsync(
            BuildingsUrl(ownerB.OrganizationId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await ReadAsync<ApiProblemResponse>(response);

        Assert.Equal(403, problem.Status);
        Assert.Equal("forbidden", problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task Logout_RevokesOnlyCurrentSession()
    {
        var owner = await RegisterAndLoginAsync();

        //drugie logowanie tego samego użytkownika
        var secondLogin = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                Email = owner.Email,
                Password = TestPassword
            });

        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);

        var secondSession = await ReadAsync<AuthResponse>(secondLogin);

        //wylogowanie sesji
        UseToken(owner.Auth.AccessToken);

        var logout = await _client.PostAsync(
            "/api/auth/logout",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var firstSessionMe = await _client.GetAsync("/api/auth/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstSessionMe.StatusCode);

        //sesja tego samego konta nadal działa
        UseToken(secondSession.AccessToken);

        var secondSessionMe = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, secondSessionMe.StatusCode);

        //test refresh tokenu wylogowanej sesji
        _client.DefaultRequestHeaders.Authorization = null;

        var firstSessionRefresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest
            {
                RefreshToken = owner.Auth.RefreshToken
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstSessionRefresh.StatusCode);
    }

    private async Task<TestOwner> RegisterAndLoginAsync()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var email = $"test-{Guid.NewGuid():N}@example.com";

        var registration = await _client.PostAsJsonAsync(
            "/api/auth/register-owner",
            new RegisterOwnerRequest
            {
                FirstName = "Test",
                LastName = "Integracyjny",
                Email = email,
                Password = TestPassword,
                OrganizationName = $"Organizacja {Guid.NewGuid():N}"
            });

        Assert.Equal(
            HttpStatusCode.Created,
            registration.StatusCode);

        var account = await ReadAsync<RegisterOwnerResponse>(
            registration);

        var login = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest
            {
                Email = email,
                Password = TestPassword
            });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var auth = await ReadAsync<AuthResponse>(login);

        return new TestOwner(
            account.UserId,
            account.OrganizationId,
            email,
            auth);
    }

    private async Task<BuildingResponse> CreateBuildingAsync(
        TestOwner owner)
    {
        UseToken(owner.Auth.AccessToken);

        var response = await _client.PostAsJsonAsync(
            BuildingsUrl(owner.OrganizationId),
            BuildingRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadAsync<BuildingResponse>(response);
    }

    private async Task ChangeMembershipAsync(
        TestOwner owner,
        OrganizationRole role,
        bool isActive)
    {
        using var scope = _factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        var membership = await db.OrganizationMemberships
            .SingleAsync(membership =>
                membership.OrganizationId == owner.OrganizationId &&
                membership.UserId == owner.UserId);

        membership.Role = role;
        membership.IsActive = isActive;

        await db.SaveChangesAsync();
    }

    private void UseToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private static string BuildingsUrl(Guid organizationId)
    {
        return $"/api/organizations/{organizationId}/buildings";
    }

    private static CreateBuildingRequest BuildingRequest()
    {
        return new CreateBuildingRequest
        {
            Name = "Budynek testowy",
            City = "Bydgoszcz",
            PostalCode = "85-888",
            Street = "Testowa",
            BuildingNumber = "1"
        };
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response)
        where T : class
    {
        var result = await response.Content.ReadFromJsonAsync<T>();

        Assert.NotNull(result);

        return result!;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record TestOwner(
        Guid UserId,
        Guid OrganizationId,
        string Email,
        AuthResponse Auth);
}

