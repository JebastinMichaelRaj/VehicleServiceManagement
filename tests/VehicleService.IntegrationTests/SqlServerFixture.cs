using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Xunit;

namespace VehicleService.IntegrationTests;

/// <summary>Starts a throwaway SQL Server container once per test class. Requires Docker.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
