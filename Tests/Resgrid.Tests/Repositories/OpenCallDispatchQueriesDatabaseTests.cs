using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Calls;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the open-dispatch queries behind status attribution, the unit working call (ActiveCallId) and
	/// the auto-close on both engines: a scheduled call the unit or person is listed on only counts once it has gone out or its
	/// dispatch time has passed (PR #546 review). Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class OpenCallDispatchQueriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "open_call_dispatches_";
		private const int DepartmentId = 7;
		private const int UnitId = 10;
		private const string UserId = "user-1";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private (CallDispatchUnitRepository Units, CallDispatchesRepository People) Repositories()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(SelectOpenCallUnitDispatchesForUnitQuery)] = new SelectOpenCallUnitDispatchesForUnitQuery(configuration);
			queries[typeof(SelectOpenCallUnitDispatchesForDepartmentQuery)] = new SelectOpenCallUnitDispatchesForDepartmentQuery(configuration);
			queries[typeof(SelectOpenCallDispatchesForUserQuery)] = new SelectOpenCallDispatchesForUserQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			var factory = new QueryFactory(list.Object);

			return (new CallDispatchUnitRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), factory),
				new CallDispatchesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), factory));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// Minimal stand-ins for the tables the three queries read.
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await database.ExecuteAsync(IsPostgres
				? @"CREATE EXTENSION IF NOT EXISTS citext;
					CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, state int NOT NULL, dispatchon timestamp NULL,
						hasbeendispatched boolean NULL, isdeleted boolean NOT NULL DEFAULT false);
					CREATE TABLE calldispatchunits (calldispatchunitid serial PRIMARY KEY, callid int NOT NULL, unitid int NOT NULL,
						dispatchcount int NOT NULL DEFAULT 1, lastdispatchedon timestamp NULL, dispatchedon timestamp NOT NULL);
					CREATE TABLE calldispatches (calldispatchid serial PRIMARY KEY, callid int NOT NULL, userid citext NOT NULL,
						dispatchedon timestamp NOT NULL, lastdispatchedon timestamp NULL);
					CREATE TABLE calldispatchgroups (calldispatchgroupid serial PRIMARY KEY, callid int NOT NULL, departmentgroupid int NOT NULL,
						dispatchedon timestamp NOT NULL, lastdispatchedon timestamp NULL);
					CREATE TABLE departmentgroupmembers (departmentgroupmemberid serial PRIMARY KEY, departmentgroupid int NOT NULL, userid citext NOT NULL);
					CREATE TABLE calldispatchroles (calldispatchroleid serial PRIMARY KEY, callid int NOT NULL, roleid int NOT NULL,
						dispatchedon timestamp NOT NULL, lastdispatchedon timestamp NULL);
					CREATE TABLE personnelroleusers (personnelroleuserid serial PRIMARY KEY, personnelroleid int NOT NULL, userid citext NOT NULL);"
				: @"CREATE TABLE Calls (CallId int PRIMARY KEY, DepartmentId int NOT NULL, State int NOT NULL, DispatchOn datetime2 NULL,
						HasBeenDispatched bit NULL, IsDeleted bit NOT NULL DEFAULT 0);
					CREATE TABLE CallDispatchUnits (CallDispatchUnitId int IDENTITY PRIMARY KEY, CallId int NOT NULL, UnitId int NOT NULL,
						DispatchCount int NOT NULL DEFAULT 1, LastDispatchedOn datetime2 NULL, DispatchedOn datetime2 NOT NULL);
					CREATE TABLE CallDispatches (CallDispatchId int IDENTITY PRIMARY KEY, CallId int NOT NULL, UserId nvarchar(128) NOT NULL,
						DispatchedOn datetime2 NOT NULL, LastDispatchedOn datetime2 NULL);
					CREATE TABLE CallDispatchGroups (CallDispatchGroupId int IDENTITY PRIMARY KEY, CallId int NOT NULL, DepartmentGroupId int NOT NULL,
						DispatchedOn datetime2 NOT NULL, LastDispatchedOn datetime2 NULL);
					CREATE TABLE DepartmentGroupMembers (DepartmentGroupMemberId int IDENTITY PRIMARY KEY, DepartmentGroupId int NOT NULL, UserId nvarchar(128) NOT NULL);
					CREATE TABLE CallDispatchRoles (CallDispatchRoleId int IDENTITY PRIMARY KEY, CallId int NOT NULL, RoleId int NOT NULL,
						DispatchedOn datetime2 NOT NULL, LastDispatchedOn datetime2 NULL);
					CREATE TABLE PersonnelRoleUsers (PersonnelRoleUserId int IDENTITY PRIMARY KEY, PersonnelRoleId int NOT NULL, UserId nvarchar(128) NOT NULL);");

			// The connection loaded its types before citext existed; without a reload a citext parameter cannot be sent.
			if (database is NpgsqlConnection npgsql)
				await npgsql.ReloadTypesAsync();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task SeedCallAsync(int callId, CallStates state, DateTime? dispatchOn, bool? hasBeenDispatched, bool deleted = false)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? @"INSERT INTO calls (callid, departmentid, state, dispatchon, hasbeendispatched, isdeleted) VALUES (@CallId, @DepartmentId, @State, @DispatchOn, @HasBeenDispatched, @Deleted);
					INSERT INTO calldispatchunits (callid, unitid, dispatchedon) VALUES (@CallId, @UnitId, @DispatchedOn);
					INSERT INTO calldispatches (callid, userid, dispatchedon) VALUES (@CallId, @UserId, @DispatchedOn);"
				: @"INSERT INTO Calls (CallId, DepartmentId, State, DispatchOn, HasBeenDispatched, IsDeleted) VALUES (@CallId, @DepartmentId, @State, @DispatchOn, @HasBeenDispatched, @Deleted);
					INSERT INTO CallDispatchUnits (CallId, UnitId, DispatchedOn) VALUES (@CallId, @UnitId, @DispatchedOn);
					INSERT INTO CallDispatches (CallId, UserId, DispatchedOn) VALUES (@CallId, @UserId, @DispatchedOn);",
				new
				{
					CallId = callId, DepartmentId, State = (int)state, DispatchOn = dispatchOn, HasBeenDispatched = hasBeenDispatched, Deleted = deleted,
					UnitId, DispatchedOn = DateTime.UtcNow.AddMinutes(-30), UserId
				});
		}

		[Test]
		public async Task a_scheduled_call_counts_only_once_it_has_gone_out_or_is_due()
		{
			var now = DateTime.UtcNow;
			await SeedCallAsync(1, CallStates.Active, null, null);                       // an ordinary call
			await SeedCallAsync(2, CallStates.Active, now.AddHours(1), false);           // scheduled, not yet sent
			await SeedCallAsync(3, CallStates.Active, now.AddMinutes(-1), false);        // due, the worker has not sent it yet
			await SeedCallAsync(4, CallStates.Active, now.AddHours(1), true);            // sent early (Dispatch Now)
			await SeedCallAsync(5, CallStates.Closed, null, null);                       // closed
			await SeedCallAsync(6, CallStates.Pending, null, null);                      // pending
			await SeedCallAsync(7, CallStates.Active, null, null, deleted: true);        // deleted

			var (units, people) = Repositories();

			(await units.GetOpenCallUnitDispatchesForUnitAsync(DepartmentId, UnitId)).Select(x => x.CallId).Should().BeEquivalentTo(new[] { 1, 3, 4 });
			(await units.GetOpenCallUnitDispatchesForDepartmentAsync(DepartmentId)).Select(x => x.CallId).Should().BeEquivalentTo(new[] { 1, 3, 4 });
			(await people.GetOpenCallDispatchesForUserAsync(DepartmentId, UserId)).Select(x => x.CallId).Should().BeEquivalentTo(new[] { 1, 3, 4 });
		}

		// Write-time attribution compares these times with the person's last clear (PR #546 follow-up).
		[Test]
		public async Task a_person_gets_one_row_per_dispatch_with_its_latest_time()
		{
			const int OtherDepartmentId = 8, CallId = 20, GroupId = 5, RoleId = 9;
			var now = DateTime.UtcNow;
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync(IsPostgres
					? @"INSERT INTO calls (callid, departmentid, state, dispatchon, hasbeendispatched, isdeleted) VALUES (@CallId, @DepartmentId, 0, NULL, NULL, false);
						INSERT INTO calldispatches (callid, userid, dispatchedon) VALUES (@CallId, @UserId, @Direct);
						INSERT INTO calldispatchgroups (callid, departmentgroupid, dispatchedon, lastdispatchedon) VALUES (@CallId, @GroupId, @GroupFirst, @GroupAgain);
						INSERT INTO departmentgroupmembers (departmentgroupid, userid) VALUES (@GroupId, @UserId), (@GroupId, 'someone-else');
						INSERT INTO calldispatchroles (callid, roleid, dispatchedon) VALUES (@CallId, @RoleId, @Role);
						INSERT INTO personnelroleusers (personnelroleid, userid) VALUES (@RoleId, @UserId);"
					: @"INSERT INTO Calls (CallId, DepartmentId, State, DispatchOn, HasBeenDispatched, IsDeleted) VALUES (@CallId, @DepartmentId, 0, NULL, NULL, 0);
						INSERT INTO CallDispatches (CallId, UserId, DispatchedOn) VALUES (@CallId, @UserId, @Direct);
						INSERT INTO CallDispatchGroups (CallId, DepartmentGroupId, DispatchedOn, LastDispatchedOn) VALUES (@CallId, @GroupId, @GroupFirst, @GroupAgain);
						INSERT INTO DepartmentGroupMembers (DepartmentGroupId, UserId) VALUES (@GroupId, @UserId), (@GroupId, 'someone-else');
						INSERT INTO CallDispatchRoles (CallId, RoleId, DispatchedOn) VALUES (@CallId, @RoleId, @Role);
						INSERT INTO PersonnelRoleUsers (PersonnelRoleId, UserId) VALUES (@RoleId, @UserId);",
					new
					{
						CallId, DepartmentId = OtherDepartmentId, UserId, GroupId, RoleId, Direct = now.AddMinutes(-30), GroupFirst = now.AddMinutes(-40),
						GroupAgain = now.AddMinutes(-2), Role = now.AddMinutes(-20)
					});
			}

			var (_, people) = Repositories();
			var rows = (await people.GetOpenCallDispatchesForUserAsync(OtherDepartmentId, UserId)).OrderBy(x => x.DispatchedOn).ToList();

			rows.Should().HaveCount(3);
			rows.Should().OnlyContain(x => x.CallId == CallId);
			rows[0].Paged.Should().BeFalse("the direct dispatch");
			rows[0].DispatchedOn.Should().BeCloseTo(now.AddMinutes(-30), TimeSpan.FromSeconds(1));
			rows[1].Paged.Should().BeTrue("the role page");
			rows[1].DispatchedOn.Should().BeCloseTo(now.AddMinutes(-20), TimeSpan.FromSeconds(1));
			rows[2].Paged.Should().BeTrue("the group page, at its redispatch");
			rows[2].DispatchedOn.Should().BeCloseTo(now.AddMinutes(-2), TimeSpan.FromSeconds(1));
		}
	}
}
