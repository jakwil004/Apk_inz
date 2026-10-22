using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Usterki.Infrastructure.Persistence;

namespace Usterki.IntegrationTests.Infrastructure;

public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    private readonly string _signingKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public TestApiFactory()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<TestApiFactory>(optional: false)
            .Build();

        _connectionString =
            configuration.GetConnectionString("TestDatabase")
            ?? throw new InvalidOperationException(
                "Brak ConnectionStrings:TestDatabase " +
                "w User Secrets projektu testowego.");

        ValidateTestConnection(_connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [HostDefaults.EnvironmentKey] = "Testing",

                    ["ConnectionStrings:DefaultConnection"] =
                        _connectionString,

                    ["Jwt:Issuer"] = "Usterki.IntegrationTests",
                    ["Jwt:Audience"] = "Usterki.TestClient",
                    ["Jwt:SigningKey"] = _signingKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "30"
                });
        });

        var host = base.CreateHost(builder);

        try
        {
            using var scope = host.Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();


            var actualConnection =
                db.Database.GetDbConnection().ConnectionString;

            ValidateTestConnection(actualConnection);

            var expected =
                new NpgsqlConnectionStringBuilder(_connectionString);

            var actual =
                new NpgsqlConnectionStringBuilder(actualConnection);

            if (actual.Host != expected.Host ||
                actual.Port != expected.Port)
            {
                throw new InvalidOperationException(
                    "API testowe wskazuje inny serwer PostgreSQL.");
            }

            db.Database.Migrate();

            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static void ValidateTestConnection(string connectionString)
    {
        var connection =
            new NpgsqlConnectionStringBuilder(connectionString);

        if (connection.Database != "usterki_tests" ||
            connection.Username != "usterki_test_app")
        {
            throw new InvalidOperationException(
                "Testy wymagają bazy usterki_tests " +
                "i użytkownika usterki_test_app.");
        }
    }
}