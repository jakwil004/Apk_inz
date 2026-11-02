using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Usterki.Api.Auth;
using Usterki.Infrastructure.Identity;
using Usterki.Infrastructure.Persistence;
using Usterki.Api.Authorization;
using Usterki.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Usterki.Api.Errors;
using Microsoft.OpenApi;
using Usterki.Contracts.Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<ApiProblemResultFilter>();
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(
                context.ModelState)
            {
                Type = "about:blank",
                Title = "Nieprawidłowe dane wejściowe",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Popraw wskazane pola."
            };

            problem.Extensions["code"] = "validation_failed";

            ApiErrors.Enrich(context.HttpContext, problem);

            var result = new BadRequestObjectResult(problem);
            result.ContentTypes.Add("application/problem+json");

            return result;
        };
    });

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        ApiErrors.Enrich(
            context.HttpContext,
            context.ProblemDetails);
    };
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer(
        (document, context, cancellationToken) =>
        {
            document.Info.Title = "Usterki API";
            document.Info.Version = "v1";
            document.Info.Description =
                "API systemu zgłaszania i obsługi usterek.";

            document.Components ??= new OpenApiComponents();

            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>();

            document.Components.SecuritySchemes["Bearer"] =
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description =
                        "Access token otrzymany po zalogowaniu."
                };

            return Task.CompletedTask;
        });

    options.AddOperationTransformer(
        async (operation, context, cancellationToken) =>
        {
            var allowsAnonymous =
                context.Description.ActionDescriptor.EndpointMetadata
                    .OfType<IAllowAnonymous>()
                    .Any();

            operation.Security = new List<OpenApiSecurityRequirement>();

            //api ma zabezpieczenie JWT
            //wszystkie endpointy wymagają autoryzacji chyba że będa miały [allowanonymous]
            if (!allowsAnonymous)
            {
                operation.Security.Add(
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(
                            "Bearer",
                            context.Document)] = []
                    });
            }

            var errorSchema = await context.GetOrCreateSchemaAsync(
                typeof(ApiProblemResponse),
                null,
                cancellationToken);

            operation.Responses ??= new OpenApiResponses();

            //ogólne błędy API, które nie są obsługiwane przez inne odpowiedzi
            operation.Responses["default"] = new OpenApiResponse
            {
                Description =
                    "Błąd API. Pole code zawiera stabilny kod błędu.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = errorSchema
                    }
                }
            };
        });
});


//PostgreSQL
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Brak ConnectionStrings:DefaultConnection.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

//Konta użytkowników
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

//Konfiguracja JWT
var jwtOptions = builder.Configuration
    .GetSection("Jwt")
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("Brak konfiguracji Jwt.");
if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    jwtOptions.AccessTokenMinutes is < 1 or > 60 ||
    jwtOptions.RefreshTokenDays is < 1 or > 90)
{
    throw new InvalidOperationException(
        "Niepoprawna konfiguracja Jwt.");
}



byte[] signingKeyBytes;

try
{
    signingKeyBytes =
        Convert.FromBase64String(jwtOptions.SigningKey);
}
catch (FormatException)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey musi być poprawnym ciągiem Base64.");
}

if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException(
        "Klucz JWT musi zawierać co najmniej 32 losowe bajty.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthSessionService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(signingKeyBytes),

            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,

            ValidAlgorithms = new[]
            {
                SecurityAlgorithms.HmacSha256
            },

            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?
                    .FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

                var sessionClaim = context.Principal?
                    .FindFirst("sid")?.Value;

                if (!Guid.TryParse(subject, out var userId) ||
                    !Guid.TryParse(sessionClaim, out var sessionId))
                {
                    context.Fail("Niepoprawne dane tokenu.");
                    return;
                }

                var db = context.HttpContext.RequestServices
                    .GetRequiredService<AppDbContext>();

                var now = DateTime.UtcNow;

                var validSession = await (
                    from session in db.UserSessions.AsNoTracking()
                    join user in db.Users.AsNoTracking()
                        on session.UserId equals user.Id
                    where session.Id == sessionId
                          && session.UserId == userId
                          && session.RevokedAtUtc == null
                          && session.ExpiresAtUtc > now
                          && user.IsActive
                    select session.Id
                ).AnyAsync(context.HttpContext.RequestAborted);

                if (!validSession)
                {
                    context.Fail("Sesja jest niedostępna.");
                }
            }
        };
    });

//Wszystkie endpointy wymagają logowania
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy(
        AuthorizationPolicies.ManageProperties,
        policy =>
        {
            policy.RequireAuthenticatedUser();

            policy.AddRequirements(
                new OrganizationRoleRequirement(
                    OrganizationRole.Owner,
                    OrganizationRole.Manager));
        });
    options.AddPolicy(
    AuthorizationPolicies.ManageTickets,
    policy =>
    {
        policy.RequireAuthenticatedUser();

        policy.AddRequirements(
            new OrganizationRoleRequirement(
                OrganizationRole.Owner,
                OrganizationRole.Manager));
    });
});

//Wspólny limit dla rejestracji i logowania w tej instancji API
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 20;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

builder.Services.AddScoped<
    IAuthorizationHandler,
    OrganizationRoleHandler>();

var app = builder.Build();

app.UseExceptionHandler();

app.UseStatusCodePages(async statusContext =>
{
    var httpContext = statusContext.HttpContext;

    var problem = ApiErrors.Create(
        httpContext,
        httpContext.Response.StatusCode);

    await ApiErrors.WriteAsync(httpContext, problem);
});
app.UseHttpsRedirection();
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    app.MapGet("/api/health/database", async (
        AppDbContext db,
        CancellationToken cancellationToken) =>
    {
        var canConnect =
            await db.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? Results.Ok(new { status = "ok" })
            : Results.StatusCode(
                StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();
}

app.MapControllers();

app.Run();

public partial class Program
{
}