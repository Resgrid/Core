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
	/// Real-database proof for M0247 on both engines: existing policies take the documented switch defaults, the locking
	/// read works inside a transaction, and concurrent MFA policy version increments never lose one.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SecurityPolicySwitchDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_switches_";
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

		private (DepartmentSecurityPolicyRepository Repository, Resgrid.Repositories.DataRepository.Transactions.UnitOfWork Unit) Policies()
		{
			var unit = new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(Connections());
			return (new DepartmentSecurityPolicyRepository(Connections(), Configuration(), unit, Mock.Of<Resgrid.Model.Repositories.Queries.IQueryFactory>()), unit);
		}

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
				type == DatabaseTypes.Postgres ? new M0247_AddSecurityPolicyMfaSwitchesPg() : new M0247_AddSecurityPolicyMfaSwitches()
			});
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			// Only the columns M0247 extends, with one department that predates it.
			await using (var db = Connect(_connection))
				await db.ExecuteAsync(type == DatabaseTypes.Postgres
					? "CREATE TABLE departmentsecuritypolicies (departmentsecuritypolicyid serial PRIMARY KEY, departmentid int NOT NULL, requiremfa boolean NOT NULL DEFAULT false); INSERT INTO departmentsecuritypolicies (departmentid, requiremfa) VALUES (7, true)"
					: "CREATE TABLE DepartmentSecurityPolicies (DepartmentSecurityPolicyId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, RequireMfa bit NOT NULL DEFAULT 0); INSERT INTO DepartmentSecurityPolicies (DepartmentId, RequireMfa) VALUES (7, 1)");
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[Test]
		public async Task An_existing_policy_takes_the_documented_defaults_and_reads_under_lock()
		{
			var (repository, unit) = Policies();
			await unit.CreateOrGetConnectionAsync();
			try
			{
				var stored = await repository.GetByDepartmentIdForUpdateAsync(7);

				stored.RequireMfa.Should().BeTrue();
				stored.AllowPasskeysForLoginMfa.Should().BeTrue();
				stored.AllowPasskeysForAdp.Should().BeTrue();
				stored.AllowFederatedMfaForLoginMfa.Should().BeFalse();
				stored.AllowFederatedMfaForAdp.Should().BeFalse();
				stored.AllowResponderApproval.Should().BeTrue();
				stored.AcceptRecentLoginMfaForAdp.Should().BeTrue();
				stored.AcceptRecentUnlockMfaForAdp.Should().BeTrue();
			}
			finally { unit.DiscardChanges(); }
		}

		[Test]
		public async Task Concurrent_version_increments_never_lose_one()
		{
			var (probe, probeUnit) = Policies();
			await probeUnit.CreateOrGetConnectionAsync();
			var start = (await probe.GetByDepartmentIdForUpdateAsync(7)).MfaPolicyVersion;
			probeUnit.DiscardChanges();

			await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
			{
				var (repository, unit) = Policies();
				await unit.CreateOrGetConnectionAsync();
				try
				{
					await repository.GetByDepartmentIdForUpdateAsync(7);
					await repository.IncrementMfaPolicyVersionAsync(7);
					unit.CommitChanges();
				}
				catch { unit.DiscardChanges(); throw; }
			}));

			var (after, afterUnit) = Policies();
			await afterUnit.CreateOrGetConnectionAsync();
			(await after.GetByDepartmentIdForUpdateAsync(7)).MfaPolicyVersion.Should().Be(start + 8);
			afterUnit.DiscardChanges();
		}

		[Test]
		public async Task The_version_advances_only_inside_a_transaction()
		{
			var (repository, _) = Policies();

			var outside = () => repository.IncrementMfaPolicyVersionAsync(7);

			await outside.Should().ThrowAsync<InvalidOperationException>();
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
