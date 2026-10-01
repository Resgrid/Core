using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
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

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Real-database proof for M0254 and the shared-session statements on both engines (passkey plan section 12.5.3): existing
	/// rows take the personal, unlocked defaults; lock and unlock are single guarded statements that exactly one of many
	/// concurrent callers wins; and recorded activity never revives a lapsed or locked session.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SharedSessionDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "shared_sessions_";
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
		private string Sessions => type == DatabaseTypes.Postgres ? "usersessions" : "UserSessions";

		private UserSessionsRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return new UserSessionsRepository(connections.Object, Configuration(), new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(connections.Object),
				Mock.Of<Resgrid.Model.Repositories.Queries.IQueryFactory>());
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

			// Only the columns the statements read, as the tables stood before M0254, with rows that predate it.
			await using (var db = Connect(_connection))
				await db.ExecuteAsync(type == DatabaseTypes.Postgres
					? @"CREATE TABLE usersessions (usersessionid varchar(128) PRIMARY KEY, userid varchar(128) NOT NULL, state int NOT NULL,
							stateversion bigint NOT NULL DEFAULT 0, createdon timestamp NOT NULL, lastactiveon timestamp NOT NULL, expireson timestamp NOT NULL);
						INSERT INTO usersessions VALUES ('existing', 'user-1', 0, 0, now() at time zone 'utc', now() at time zone 'utc', (now() at time zone 'utc') + interval '1 day');
						CREATE TABLE departmentsecuritypolicies (departmentsecuritypolicyid serial PRIMARY KEY, departmentid int NOT NULL);
						INSERT INTO departmentsecuritypolicies (departmentid) VALUES (7);
						CREATE TABLE usertotpstates (userid varchar(128) PRIMARY KEY, lastacceptedtimestep bigint NOT NULL);
						INSERT INTO usertotpstates VALUES ('user-1', 0);"
					: @"CREATE TABLE UserSessions (UserSessionId nvarchar(128) PRIMARY KEY, UserId nvarchar(128) NOT NULL, State int NOT NULL,
							StateVersion bigint NOT NULL DEFAULT 0, CreatedOn datetime2 NOT NULL, LastActiveOn datetime2 NOT NULL, ExpiresOn datetime2 NOT NULL);
						INSERT INTO UserSessions VALUES ('existing', 'user-1', 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), DATEADD(day, 1, SYSUTCDATETIME()));
						CREATE TABLE DepartmentSecurityPolicies (DepartmentSecurityPolicyId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL);
						INSERT INTO DepartmentSecurityPolicies (DepartmentId) VALUES (7);
						CREATE TABLE UserTotpStates (UserId nvarchar(128) PRIMARY KEY, LastAcceptedTimeStep bigint NOT NULL);
						INSERT INTO UserTotpStates VALUES ('user-1', 0);");

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[]
			{
				type == DatabaseTypes.Postgres ? new M0254_AddSharedSessionsPg() : new M0254_AddSharedSessions()
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

		private DateTime Stored(DateTime utc) => type == DatabaseTypes.Postgres ? DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) : utc;

		private async Task<string> SharedSession(DateTime now, bool locked = false, long lockVersion = 0, DateTime? activity = null, DateTime? expiresOn = null)
		{
			var id = Guid.NewGuid().ToString("N");
			await using var db = Connect(_connection);
			await db.ExecuteAsync(type == DatabaseTypes.Postgres
				? @"INSERT INTO usersessions (usersessionid, userid, state, stateversion, createdon, lastactiveon, expireson, sharedmode, sharedmodesource,
						sharedidlelockminutes, lockversion, islocked, lastoperatoractivityon)
					VALUES (@Id, @UserId, 0, 0, @Now, @Now, @ExpiresOn, true, 1, 5, @LockVersion, @Locked, @Activity)"
				: @"INSERT INTO UserSessions (UserSessionId, UserId, State, StateVersion, CreatedOn, LastActiveOn, ExpiresOn, SharedMode, SharedModeSource,
						SharedIdleLockMinutes, LockVersion, IsLocked, LastOperatorActivityOn)
					VALUES (@Id, @UserId, 0, 0, @Now, @Now, @ExpiresOn, 1, 1, 5, @LockVersion, @Locked, @Activity)",
				new
				{
					Id = id, UserId, Now = Stored(now), ExpiresOn = Stored(expiresOn ?? now.AddHours(12)), LockVersion = lockVersion, Locked = locked,
					Activity = Stored(activity ?? now)
				});
			return id;
		}

		private async Task<(bool Locked, long LockVersion, DateTime? Activity)> Read(string id)
		{
			await using var db = Connect(_connection);
			var row = await db.QuerySingleAsync(type == DatabaseTypes.Postgres
				? "SELECT islocked AS Locked, lockversion AS LockVersion, lastoperatoractivityon AS Activity FROM usersessions WHERE usersessionid = @Id"
				: "SELECT IsLocked AS Locked, LockVersion, LastOperatorActivityOn AS Activity FROM UserSessions WHERE UserSessionId = @Id", new { Id = id });
			return ((bool)row.Locked, (long)row.LockVersion, (DateTime?)row.Activity);
		}

		[Test]
		public async Task Existing_rows_take_the_personal_unlocked_defaults()
		{
			await using var db = Connect(_connection);
			var session = await db.QuerySingleAsync(type == DatabaseTypes.Postgres
				? "SELECT sharedmode AS SharedMode, sharedmodesource AS Source, lockversion AS LockVersion, islocked AS Locked, lockedonutc AS LockedOn FROM usersessions WHERE usersessionid = 'existing'"
				: "SELECT SharedMode, SharedModeSource AS Source, LockVersion, IsLocked AS Locked, LockedOnUtc AS LockedOn FROM UserSessions WHERE UserSessionId = 'existing'");
			((bool)session.SharedMode).Should().BeFalse();
			((int)session.Source).Should().Be(0);
			((long)session.LockVersion).Should().Be(0);
			((bool)session.Locked).Should().BeFalse();
			((DateTime?)session.LockedOn).Should().BeNull();

			var policy = await db.QuerySingleAsync(type == DatabaseTypes.Postgres
				? "SELECT sharedidlelockminutes AS Idle, sharedshifthours AS Shift, sharedmoderequiredapps AS Apps FROM departmentsecuritypolicies WHERE departmentid = 7"
				: "SELECT SharedIdleLockMinutes AS Idle, SharedShiftHours AS Shift, SharedModeRequiredApps AS Apps FROM DepartmentSecurityPolicies WHERE DepartmentId = 7");
			((int)policy.Idle).Should().Be(5);
			((int)policy.Shift).Should().Be(12);
			((int)policy.Apps).Should().Be(0);

			(await db.ExecuteScalarAsync<bool>(type == DatabaseTypes.Postgres
				? "SELECT enrolledinsharedmode FROM usertotpstates WHERE userid = 'user-1'"
				: "SELECT EnrolledInSharedMode FROM UserTotpStates WHERE UserId = 'user-1'")).Should().BeFalse();
		}

		[Test]
		public async Task Sixteen_concurrent_locks_advance_the_version_exactly_once()
		{
			var now = Now;
			var id = await SharedSession(now);

			var won = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Repository().TryLockAsync(id, 0, (int)SharedSessionLockReason.Idle, now, CancellationToken.None)));

			won.Sum().Should().Be(1);
			(await Read(id)).Should().Be((true, 1L, now));
			(await Repository().TryLockAsync(id, 1, (int)SharedSessionLockReason.Explicit, now, CancellationToken.None)).Should().Be(0, "already locked");
			(await Repository().TryLockAsync("existing", 0, (int)SharedSessionLockReason.Explicit, now, CancellationToken.None)).Should().Be(0,
				"a personal session never locks");
		}

		[Test]
		public async Task Sixteen_concurrent_unlocks_of_one_lock_succeed_exactly_once_and_only_where_they_should()
		{
			var now = Now;
			var id = await SharedSession(now.AddMinutes(-10), locked: true, lockVersion: 3);

			(await Repository().TryUnlockAsync(UserId, id, 2, now, CancellationToken.None)).Should().Be(0, "an unlock begun before this lock");
			(await Repository().TryUnlockAsync("someone-else", id, 3, now, CancellationToken.None)).Should().Be(0, "another user's session");

			var won = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Repository().TryUnlockAsync(UserId, id, 3, now, CancellationToken.None)));
			won.Sum().Should().Be(1);
			(await Read(id)).Should().Be((false, 3L, now), "unlock restarts the idle deadline and never advances the version");

			var ended = await SharedSession(now.AddHours(-13), locked: true, lockVersion: 1, expiresOn: now.AddHours(-1));
			(await Repository().TryUnlockAsync(UserId, ended, 1, now, CancellationToken.None)).Should().Be(0, "past the shift ceiling");
		}

		[Test]
		public async Task Session_states_are_read_in_batches_for_the_connection_sweep()
		{
			var now = Now;
			var locked = await SharedSession(now, locked: true, lockVersion: 2);
			var open = await SharedSession(now, activity: now.AddMinutes(-1));
			var ids = new[] { locked, open, "existing", "missing" }.Concat(Enumerable.Range(0, 600).Select(i => "absent-" + i)).ToList();

			var rows = await Repository().GetStatesAsync(ids, CancellationToken.None);

			rows.Select(r => r.UserSessionId).Should().BeEquivalentTo(new[] { locked, open, "existing" }, "more than one batch, missing ids absent");
			rows.Single(r => r.UserSessionId == locked).IsLocked.Should().BeTrue();
			rows.Single(r => r.UserSessionId == open).LastOperatorActivityOn.Should().Be(Stored(now.AddMinutes(-1)));
			rows.Single(r => r.UserSessionId == "existing").SharedMode.Should().BeFalse();
		}

		[Test]
		public async Task Activity_is_bounded_and_never_revives_a_lapsed_or_locked_session()
		{
			var now = Now;
			var id = await SharedSession(now.AddMinutes(-4), activity: now.AddMinutes(-4));
			var repository = Repository();

			(await repository.RecordOperatorActivityAsync(id, now, now.AddSeconds(-30), now.AddMinutes(-5), CancellationToken.None)).Should().Be(1);
			(await Read(id)).Activity.Should().Be(Stored(now));
			(await repository.RecordOperatorActivityAsync(id, now.AddSeconds(10), now.AddSeconds(-20), now.AddMinutes(-5), CancellationToken.None))
				.Should().Be(0, "inside the write interval");

			var lapsed = await SharedSession(now.AddMinutes(-7), activity: now.AddMinutes(-6));
			(await repository.RecordOperatorActivityAsync(lapsed, now, now.AddSeconds(-30), now.AddMinutes(-5), CancellationToken.None)).Should().Be(0);

			var locked = await SharedSession(now.AddMinutes(-2), locked: true, lockVersion: 1, activity: now.AddMinutes(-2));
			(await repository.RecordOperatorActivityAsync(locked, now, now.AddSeconds(-30), now.AddMinutes(-5), CancellationToken.None)).Should().Be(0);
			(await repository.RecordOperatorActivityAsync("existing", now, now.AddSeconds(-30), now.AddMinutes(-5), CancellationToken.None)).Should().Be(0,
				"a personal session has no operator activity");
		}
	}
}
