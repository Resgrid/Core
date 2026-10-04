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
using Resgrid.Model.Security;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Real-database proof for M0255 and its statements on both engines (passkey plan section 6.5, slice 17): recent MFA
	/// activity reads newest first inside its window, is reported exactly once however many callers race, and is purged by
	/// age or with its account; and stopping approvals changes only the user's active Responder installations.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MfaActivityDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_activity_";
		private const string UserId = "user-1";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		// Whole seconds: the assertions compare stored instants.
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

		private Mock<IConnectionProvider> Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return connections;
		}

		private MfaActivityRepository Activity() => new(Connections().Object, Configuration());

		private UserSessionsRepository Sessions()
		{
			var connections = Connections();
			return new UserSessionsRepository(connections.Object, Configuration(), new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(connections.Object),
				Mock.Of<Resgrid.Model.Repositories.Queries.IQueryFactory>());
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(type == DatabaseTypes.Postgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");
			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = type == DatabaseTypes.Postgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// Only the session columns the statements read, as the table stood before M0255.
			await using (var db = Connect(_connection))
				await db.ExecuteAsync(type == DatabaseTypes.Postgres
					? @"CREATE TABLE usersessions (usersessionid varchar(128) PRIMARY KEY, userid varchar(128) NOT NULL, state int NOT NULL,
							clientapplication int NOT NULL, createdon timestamp NOT NULL, lastactiveon timestamp NOT NULL, expireson timestamp NOT NULL);"
					: @"CREATE TABLE UserSessions (UserSessionId nvarchar(128) PRIMARY KEY, UserId nvarchar(128) NOT NULL, State int NOT NULL,
							ClientApplication int NOT NULL, CreatedOn datetime2 NOT NULL, LastActiveOn datetime2 NOT NULL, ExpiresOn datetime2 NOT NULL);");

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[]
			{
				type == DatabaseTypes.Postgres ? new M0255_AddMfaActivityAndApprovalInstallationsPg() : new M0255_AddMfaActivityAndApprovalInstallations()
			});
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
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

		private static MfaActivity Row(string userId, DateTime occurredOnUtc, bool successful = true, string id = null) => new()
		{
			MfaActivityId = id ?? Guid.NewGuid().ToString(), UserId = userId, OccurredOnUtc = occurredOnUtc, Method = (int)MfaEvidenceMethod.PasskeyApproval,
			Purpose = (int)MfaEvidencePurpose.StepUp, Successful = successful, ClientApplication = (int)UserSessionClientApplication.Unit,
			InstallationLabel = "Engine 7 tablet", SharedMode = true, DepartmentId = null, SessionId = "unit-1", ApproverSessionId = "responder-1"
		};

		[Test]
		public async Task Activity_reads_newest_first_inside_its_window_is_reported_once_and_is_purged_by_age_or_with_the_account()
		{
			var now = Now;
			var user = "activity-" + Guid.NewGuid().ToString("N");
			var other = "activity-other-" + Guid.NewGuid().ToString("N");
			var repository = Activity();
			var newest = Row(user, now);
			await repository.InsertAsync(newest);
			await repository.InsertAsync(Row(user, now.AddHours(-1), successful: false));
			await repository.InsertAsync(Row(user, now.AddDays(-2), successful: false));
			await repository.InsertAsync(Row(user, now.AddDays(-40)));
			await repository.InsertAsync(Row(other, now));

			var recent = await repository.GetRecentAsync(user, now.AddDays(-30), 100);
			recent.Select(a => a.OccurredOnUtc).Should().Equal(now, now.AddHours(-1), now.AddDays(-2));
			(await repository.GetRecentAsync(user, now.AddDays(-30), 2)).Should().HaveCount(2);
			(await repository.CountDeniedSinceAsync(user, now.AddHours(-2))).Should().Be(1);

			var read = await repository.GetAsync(newest.MfaActivityId);
			read.Should().BeEquivalentTo(newest, o => o.Excluding(a => a.OccurredOnUtc));
			read.OccurredOnUtc.Should().Be(now);

			(await repository.TryMarkReportedAsync(newest.MfaActivityId, other, now)).Should().BeFalse("only the account's own activity");
			var reports = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Activity().TryMarkReportedAsync(newest.MfaActivityId, user, now)));
			reports.Count(r => r).Should().Be(1, "reported exactly once however many callers race");
			(await repository.GetAsync(newest.MfaActivityId)).ReportedOnUtc.Should().Be(now);

			(await repository.PurgeBeforeAsync(now.AddDays(-30))).Should().BeGreaterThanOrEqualTo(1);
			(await repository.GetRecentAsync(user, now.AddDays(-100), 100)).Should().HaveCount(3, "only the row past retention went");
			(await repository.DeleteForUserAsync(other)).Should().Be(1);
			(await repository.GetRecentAsync(user, now.AddDays(-100), 100)).Should().HaveCount(3, "another account's deletion leaves this one alone");
		}

		[Test]
		public async Task Approvals_stop_only_on_the_users_active_responder_installations_and_only_once()
		{
			var now = Now;
			var user = "installations-" + Guid.NewGuid().ToString("N");
			async Task<string> Session(string userId, UserSessionClientApplication client, UserSessionState state = UserSessionState.Active)
			{
				var id = Guid.NewGuid().ToString("N");
				await using var db = Connect(_connection);
				await db.ExecuteAsync(type == DatabaseTypes.Postgres
					? "INSERT INTO usersessions (usersessionid, userid, state, clientapplication, createdon, lastactiveon, expireson) VALUES (@Id, @UserId, @State, @Client, @Now, @Now, @Now)"
					: "INSERT INTO UserSessions (UserSessionId, UserId, State, ClientApplication, CreatedOn, LastActiveOn, ExpiresOn) VALUES (@Id, @UserId, @State, @Client, @Now, @Now, @Now)",
					new { Id = id, UserId = userId, State = (int)state, Client = (int)client, Now = now });
				return id;
			}

			var phone = await Session(user, UserSessionClientApplication.Responder);
			var tablet = await Session(user, UserSessionClientApplication.Responder);
			var unit = await Session(user, UserSessionClientApplication.Unit);
			var ended = await Session(user, UserSessionClientApplication.Responder, UserSessionState.Revoked);
			var someoneElses = await Session("someone-else-" + user, UserSessionClientApplication.Responder);
			var sessions = Sessions();

			(await sessions.DisableApprovalsAsync(user, phone, now, default)).Should().Be(1);
			(await sessions.DisableApprovalsAsync(user, phone, now.AddMinutes(1), default)).Should().Be(0, "already stopped; the first time stands");
			(await sessions.DisableApprovalsAsync(user, unit, now, default)).Should().Be(0, "not a Responder installation");
			(await sessions.DisableApprovalsAsync(user, someoneElses, now, default)).Should().Be(0, "another account's installation");
			(await sessions.DisableApprovalsAsync(user, null, now, default)).Should().Be(1, "every other active Responder installation: the tablet");

			// This fixture supplies a minimal session schema and no generic repository query factory.
			async Task<DateTime?> DisabledOn(string id)
			{
				await using var db = Connect(_connection);
				return await db.QuerySingleAsync<DateTime?>(type == DatabaseTypes.Postgres
					? "SELECT approvalsdisabledonutc FROM usersessions WHERE usersessionid = @Id"
					: "SELECT ApprovalsDisabledOnUtc FROM UserSessions WHERE UserSessionId = @Id", new { Id = id });
			}
			(await DisabledOn(phone)).Should().Be(now);
			(await DisabledOn(tablet)).Should().Be(now);
			(await DisabledOn(unit)).Should().BeNull();
			(await DisabledOn(ended)).Should().BeNull();
			(await DisabledOn(someoneElses)).Should().BeNull();
		}
	}
}
