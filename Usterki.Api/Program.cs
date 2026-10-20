using Microsoft.EntityFrameworkCore;
using Usterki.Infrastructure.Persistence;
using Usterki.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddAuthorization();

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Brak ConnectionStrings:DefaultConnection w konfiguracji.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

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
    .AddEntityFrameworkStores<AppDbContext>();

var app = builder.Build();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

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
    });
}

app.UseAuthorization();

app.MapControllers();

app.Run();