using System;
using System.Collections.Generic;
using System.Text;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Usterki.Contracts.Auth;
using Usterki.Contracts.Properties;
using Usterki.Domain.Enums;
using Usterki.Infrastructure.Persistence;
using Usterki.IntegrationTests.Infrastructure;
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