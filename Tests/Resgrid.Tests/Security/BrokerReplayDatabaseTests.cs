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
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Real-database proof for the M0245 broker replay table on both engines: one of 16 concurrent claims of a key wins,
	/// a claimed key stays refused until it is purged, and a purge removes only expired keys.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class BrokerReplayDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "broker_replay_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		// Whole seconds: SQL Server datetime rounds to 1/300 s, and the assertions compare stored instants.
		private static DateTime Now
		{
			get
			{
				var now = DateTime.UtcNow;
				return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
			}
		}

		private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return connections.Object;
		}

		private BrokerReplayRepository Replay() => new(Connections(), Configuration());

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(type == DatabaseTypes.Postgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database concurrency checks.");
			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = type == DatabaseTypes.Postgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[]
			{
				type == DatabaseTypes.Postgres ? new M0245_AddBrokerReplayKeysPg() : new M0245_AddBrokerReplayKeys()
			});
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		private static string Key() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

		[Test]
		public async Task Concurrent_claims_of_one_key_have_exactly_one_winner()
		{
			var now = Now;
			var key = Key();

			var winners = await Task.WhenAll(Enumerable.Range(0, 16)
				.Select(_ => Replay().TryClaimAsync(key, BrokerReplayKind.RequestId, now.AddMinutes(15), now)));

			winners.Count(w => w).Should().Be(1);
		}

		[Test]
		public async Task A_claimed_key_stays_refused_until_it_is_purged()
		{
			var now = Now;
			var expired = Key();
			var live = Key();
			(await Replay().TryClaimAsync(expired, BrokerReplayKind.SessionAssertion, now.AddMinutes(-1), now.AddMinutes(-16))).Should().BeTrue();
			(await Replay().TryClaimAsync(live, BrokerReplayKind.RequestId, now.AddMinutes(15), now)).Should().BeTrue();

			(await Replay().TryClaimAsync(expired, BrokerReplayKind.SessionAssertion, now.AddMinutes(15), now))
				.Should().BeFalse("an expired claim still refuses until the purge removes it");

			(await Replay().PurgeExpiredBeforeAsync(now)).Should().BeGreaterThanOrEqualTo(1);

			(await Replay().TryClaimAsync(live, BrokerReplayKind.RequestId, now.AddMinutes(15), now)).Should().BeFalse("live claims survive a purge");
			(await Replay().TryClaimAsync(expired, BrokerReplayKind.SessionAssertion, now.AddMinutes(15), now)).Should().BeTrue();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}
	}
}
