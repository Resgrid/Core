using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Services
{
    [TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
    public class AdpAccessDatabaseTests(DatabaseTypes type)
    {
        private DatabaseTypes _previous;
        private string _master, _connection, _database;
        private ServiceProvider _runner;
        private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
        private SqlConfiguration Configuration() => type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
        private IConnectionProvider Connections()
        {
            var connections = new Mock<IConnectionProvider>();
            connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
            return connections.Object;
        }

        [OneTimeSetUp]
        public async Task Create_isolated_database()
        {
            _master = Environment.GetEnvironmentVariable(type == DatabaseTypes.Postgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
            if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set an ADP test connection to run real database concurrency checks.");
            _previous = DataConfig.DatabaseType;
            DataConfig.DatabaseType = type;
            _database = "adp_verification_" + Guid.NewGuid().ToString("N");
            await using var master = Connect(_master);
            await master.ExecuteAsync("CREATE DATABASE " + _database);
            _connection = type == DatabaseTypes.Postgres
                ? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
                : new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;
            var source = new Mock<IMigrationSource>();
            source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { type == DatabaseTypes.Postgres ? new M0236_AddAdpAuditPg() : new M0236_AddAdpAudit() });
            _runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r => {
                if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
                r.WithGlobalConnectionString(_connection);
            }).AddSingleton(source.Object).BuildServiceProvider();
            _runner.GetRequiredService<IMigrationRunner>().MigrateUp();
        }

        [Test]
        public async Task Concurrent_appends_form_one_chain_that_survives_database_roundtrip()
        {
            var audit = new AdpAuditRepository(Connections(), Configuration());
            await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => audit.AppendAsync(new AdpAuditEvent {
                DepartmentId = 7, Layer = "broker", Operation = "decrypt", Outcome = "requested" })));
            var rows = await audit.ReadAsync(7);
            rows.Count.Should().Be(16);
            AdpAuditChain.Verify(rows, 16, rows.Last().Hash).Should().BeTrue();
            (await audit.ReadAsync(8)).Should().BeEmpty();
        }

        [Test]
        public async Task Compare_and_swap_allows_only_one_consuming_host()
        {
            var store = new AdpAccessStore(Connections(), Configuration());
            var key = Guid.NewGuid().ToString("N");
            (await store.SaveAsync(key, "unused", 0)).Should().BeTrue();
            var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => store.SaveAsync(key, "consumed", 1)));
            winners.Count(w => w).Should().Be(1);
            var stored = await store.GetAsync(key);
            stored.Version.Should().Be(2);
            stored.Json.Should().Be("consumed");
        }

        [OneTimeTearDown]
        public async Task Remove_only_this_fixture_database()
        {
            if (_database == null) return;
            _runner?.Dispose();
            DataConfig.DatabaseType = _previous;
            if (!_database.StartsWith("adp_verification_", StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring("adp_verification_".Length), "N", out _))
                throw new InvalidOperationException("Unexpected test database name.");
            if (type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
            await using var master = Connect(_master);
            await master.ExecuteAsync(type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
                : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
        }
    }
}
