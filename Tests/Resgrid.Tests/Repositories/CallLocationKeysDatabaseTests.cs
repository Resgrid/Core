using System;
using System.Collections.Concurrent;
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
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for M0259 on both engines: location keys upsert and read back by address key, bounding box and
	/// contact link (deleted calls never surface), they cascade with their call, the backfill state and department
	/// selection queries work, and the data protection suppress/resume queries read the policy table. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallLocationKeysDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "call_location_keys_";
		private const int DepartmentId = 42;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private static DateTime Now
		{
			get
			{
				var now = DateTime.UtcNow;
				return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
			}
		}

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private SqlConfiguration Configuration() => IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private CallLocationKeysRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			var configuration = Configuration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new CallLocationKeysRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
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

			// Minimal stand-ins for the M0001 / M0032 / ADP tables the migration and the queries touch.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? @"CREATE EXTENSION IF NOT EXISTS citext;
						CREATE TABLE departments (departmentid serial PRIMARY KEY, name citext NULL);
						CREATE TABLE calls (callid serial PRIMARY KEY, departmentid int NOT NULL, number citext NULL, name citext NULL, address citext NULL,
							geolocationdata citext NULL, loggedon timestamp NOT NULL, isdeleted boolean NOT NULL DEFAULT false, state int NOT NULL DEFAULT 0, priority int NOT NULL DEFAULT 0);
						CREATE TABLE callcontacts (callcontactid citext PRIMARY KEY, departmentid int NOT NULL, callid int NOT NULL, contactid citext NOT NULL, callcontacttype int NOT NULL);
						CREATE TABLE callnotes (callnoteid serial PRIMARY KEY, callid int NOT NULL, userid citext NULL, note citext NULL, timestamp timestamp NOT NULL, isdeleted boolean NOT NULL DEFAULT false);
						CREATE TABLE departmentdataprotectionpolicies (departmentid int PRIMARY KEY, state int NOT NULL);"
					: @"CREATE TABLE Departments (DepartmentId int IDENTITY PRIMARY KEY, Name nvarchar(100) NULL);
						CREATE TABLE Calls (CallId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, Number nvarchar(50) NULL, Name nvarchar(max) NULL, Address nvarchar(max) NULL,
							GeoLocationData nvarchar(max) NULL, LoggedOn datetime2 NOT NULL, IsDeleted bit NOT NULL DEFAULT 0, State int NOT NULL DEFAULT 0, Priority int NOT NULL DEFAULT 0);
						CREATE TABLE CallContacts (CallContactId nvarchar(128) PRIMARY KEY, DepartmentId int NOT NULL, CallId int NOT NULL, ContactId nvarchar(128) NOT NULL, CallContactType int NOT NULL);
						CREATE TABLE CallNotes (CallNoteId int IDENTITY PRIMARY KEY, CallId int NOT NULL, UserId nvarchar(128) NULL, Note nvarchar(max) NULL, Timestamp datetime2 NOT NULL, IsDeleted bit NOT NULL DEFAULT 0);
						CREATE TABLE DepartmentDataProtectionPolicies (DepartmentId int PRIMARY KEY, State int NOT NULL);");

				if (database is NpgsqlConnection postgres)
					postgres.ReloadTypes();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0259_AddCallLocationIndexPg() }
				: new IMigration[] { new M0259_AddCallLocationIndex() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
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
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task<int> SeedDepartmentAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO departments (name) VALUES ('d') RETURNING departmentid"
				: "INSERT INTO Departments (Name) OUTPUT INSERTED.DepartmentId VALUES ('d')");
		}

		private async Task<int> SeedCallAsync(string address, DateTime loggedOn, int departmentId = DepartmentId, bool deleted = false, string geo = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO calls (departmentid, number, name, address, geolocationdata, loggedon, isdeleted) VALUES (@DepartmentId, 'N', 'Call', @Address, @Geo, @LoggedOn, @Deleted) RETURNING callid"
				: "INSERT INTO Calls (DepartmentId, Number, Name, Address, GeoLocationData, LoggedOn, IsDeleted) OUTPUT INSERTED.CallId VALUES (@DepartmentId, 'N', 'Call', @Address, @Geo, @LoggedOn, @Deleted)",
				new { DepartmentId = departmentId, Address = address, Geo = geo, LoggedOn = loggedOn, Deleted = deleted });
		}

		private async Task LinkContactAsync(int callId, string contactId, int departmentId = DepartmentId)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "INSERT INTO callcontacts (callcontactid, departmentid, callid, contactid, callcontacttype) VALUES (@Id, @DepartmentId, @CallId, @ContactId, 0)"
				: "INSERT INTO CallContacts (CallContactId, DepartmentId, CallId, ContactId, CallContactType) VALUES (@Id, @DepartmentId, @CallId, @ContactId, 0)",
				new { Id = Guid.NewGuid().ToString(), DepartmentId = departmentId, CallId = callId, ContactId = contactId });
		}

		private static CallLocationKey Key(int callId, string address, DateTime loggedOn, string geo = null, int departmentId = DepartmentId)
			=> Resgrid.Services.CallLocationHistoryService.BuildKey(callId, departmentId, address, geo, loggedOn, Now);

		[Test]
		public async Task Keys_upsert_and_are_found_by_address_key_newest_first_without_deleted_calls()
		{
			// Its own department: the fixture database is shared by every test in it.
			const int dept = 501;
			var older = await SeedCallAsync("110 S Main St", Now.AddDays(-10), dept);
			var newer = await SeedCallAsync("110 South Main", Now.AddDays(-1), dept);
			var deleted = await SeedCallAsync("110 S Main Street", Now.AddDays(-2), dept, deleted: true);
			var foreign = await SeedCallAsync("110 S Main St", Now.AddDays(-3), dept + 1);

			await Repository().UpsertAsync(new[]
			{
				Key(older, "110 S Main St", Now.AddDays(-10), "39.7817,-89.6501", dept), Key(newer, "110 South Main", Now.AddDays(-1), departmentId: dept),
				Key(deleted, "110 S Main Street", Now.AddDays(-2), departmentId: dept), Key(foreign, "110 S Main St", Now.AddDays(-3), departmentId: dept + 1)
			});

			var rows = await Repository().GetByAddressKeyAsync(dept, "110|MAIN", 10);
			rows.Select(r => r.CallId).Should().Equal(newer, older);
			rows[1].Latitude.Should().Be(39.7817m);
			rows[1].Indexed.Should().BeTrue();
			ParsedStreetAddress.FromCanonical(rows[0].AddressCanonical).StreetName.Should().Be("MAIN");
			(await Repository().GetByAddressKeyAsync(dept, "110|MAIN", 1)).Should().ContainSingle(r => r.CallId == newer, "the cap keeps the newest");

			// An edit moves the call to another address.
			await Repository().UpsertAsync(new[] { Key(newer, "500 Elm St", Now.AddDays(-1), departmentId: dept) });
			(await Repository().GetByAddressKeyAsync(dept, "110|MAIN", 10)).Select(r => r.CallId).Should().Equal(older);
			(await Repository().GetByAddressKeyAsync(dept, "500|ELM", 10)).Select(r => r.CallId).Should().Equal(newer);
		}

		[Test]
		public async Task Keys_are_found_by_bounding_box_and_cascade_with_their_call()
		{
			var inside = await SeedCallAsync(null, Now.AddHours(-5), geo: "39.7817,-89.6501");
			var outside = await SeedCallAsync(null, Now.AddHours(-4), geo: "39.9000,-89.6501");
			await Repository().UpsertAsync(new[] { Key(inside, null, Now.AddHours(-5), "39.7817,-89.6501"), Key(outside, null, Now.AddHours(-4), "39.9000,-89.6501") });

			var found = await Repository().GetWithinBoundsAsync(DepartmentId, 39.7810m, 39.7820m, -89.6510m, -89.6490m, 10);
			found.Should().ContainSingle(r => r.CallId == inside);

			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM calls WHERE callid = @Id" : "DELETE FROM Calls WHERE CallId = @Id", new { Id = inside });
			(await Repository().GetWithinBoundsAsync(DepartmentId, 39.7810m, 39.7820m, -89.6510m, -89.6490m, 10)).Should().BeEmpty("the key cascades with its call");

			await Repository().DeleteForCallAsync(outside);
			(await Repository().GetWithinBoundsAsync(DepartmentId, 39.8m, 40m, -90m, -89m, 10)).Should().BeEmpty();
		}

		[Test]
		public async Task Contact_candidates_carry_their_index_row_and_counts_skip_deleted_calls()
		{
			var indexed = await SeedCallAsync("110 S Main St", Now.AddDays(-1));
			var unindexed = await SeedCallAsync("Walmart", Now.AddDays(-2));
			var deleted = await SeedCallAsync("110 S Main St", Now.AddDays(-3), deleted: true);
			await LinkContactAsync(indexed, "acme");
			await LinkContactAsync(unindexed, "acme");
			await LinkContactAsync(deleted, "acme");
			await LinkContactAsync(indexed, "other");
			await Repository().UpsertAsync(new[] { Key(indexed, "110 S Main St", Now.AddDays(-1)) });

			var candidates = await Repository().GetContactCallCandidatesAsync(DepartmentId, new[] { "acme" }, 10);
			candidates.Select(c => c.CallId).Should().Equal(indexed, unindexed);
			candidates[0].Indexed.Should().BeTrue();
			candidates[0].AddressKey.Should().Be("110|MAIN");
			candidates[1].Indexed.Should().BeFalse();
			(await Repository().GetContactCallCandidatesAsync(DepartmentId, Array.Empty<string>(), 10)).Should().BeEmpty();

			var counts = await Repository().GetCallCountsByContactAsync(DepartmentId);
			counts["acme"].Should().Be(2);
			counts["other"].Should().Be(1);
		}

		[Test]
		public async Task Calls_and_their_live_notes_load_by_id()
		{
			var callId = await SeedCallAsync("110 S Main St", Now.AddDays(-1));
			var deleted = await SeedCallAsync("110 S Main St", Now.AddDays(-1), deleted: true);
			await using (var database = Connect(_connection))
			{
				var sql = IsPostgres
					? "INSERT INTO callnotes (callid, userid, note, timestamp, isdeleted) VALUES (@CallId, 'u1', @Note, @Timestamp, @Deleted)"
					: "INSERT INTO CallNotes (CallId, UserId, Note, Timestamp, IsDeleted) VALUES (@CallId, 'u1', @Note, @Timestamp, @Deleted)";
				await database.ExecuteAsync(sql, new { CallId = callId, Note = "second", Timestamp = Now, Deleted = false });
				await database.ExecuteAsync(sql, new { CallId = callId, Note = "first", Timestamp = Now.AddMinutes(-5), Deleted = false });
				await database.ExecuteAsync(sql, new { CallId = callId, Note = "removed", Timestamp = Now, Deleted = true });
			}

			var calls = await Repository().GetCallsAsync(DepartmentId, new[] { callId, deleted });
			calls.Should().ContainSingle(c => c.CallId == callId && c.Address == "110 S Main St");
			(await Repository().GetCallsAsync(DepartmentId + 1, new[] { callId })).Should().BeEmpty();
			(await Repository().GetNotesForCallsAsync(new[] { callId })).Select(n => n.Note).Should().Equal("first", "second");
		}

		[Test]
		public async Task Backfill_state_and_department_selection_follow_progress_and_data_protection()
		{
			var fresh = await SeedDepartmentAsync();
			var done = await SeedDepartmentAsync();
			var stale = await SeedDepartmentAsync();
			var protectedDepartment = await SeedDepartmentAsync();
			var repository = Repository();

			await repository.SaveStateAsync(new CallLocationIndexState { DepartmentId = done, KeyVersion = ParsedStreetAddress.Version, CompletedOn = Now, ModifiedOn = Now });
			await repository.SaveStateAsync(new CallLocationIndexState { DepartmentId = stale, KeyVersion = ParsedStreetAddress.Version - 1, NextCallId = 5, CompletedOn = Now, ModifiedOn = Now });
			await repository.SaveStateAsync(new CallLocationIndexState { DepartmentId = protectedDepartment, KeyVersion = ParsedStreetAddress.Version, CompletedOn = Now, ModifiedOn = Now });

			var pending = await repository.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, 50);
			pending.Should().Contain(new[] { fresh, stale }).And.NotContain(new[] { done, protectedDepartment });
			pending.IndexOf(stale).Should().BeLessThan(pending.IndexOf(fresh), "departments already under way finish first");

			// The update path of the upsert.
			await repository.SaveStateAsync(new CallLocationIndexState { DepartmentId = stale, KeyVersion = ParsedStreetAddress.Version, NextCallId = 3, ModifiedOn = Now });
			var state = await repository.GetStateAsync(stale);
			state.NextCallId.Should().Be(3);
			state.CompletedOn.Should().BeNull();

			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "INSERT INTO departmentdataprotectionpolicies (departmentid, state) VALUES (@D, 5), (@Done, 0)"
					: "INSERT INTO DepartmentDataProtectionPolicies (DepartmentId, State) VALUES (@D, 5), (@Done, 0)", new { D = protectedDepartment, Done = done });
			(await repository.GetDepartmentsToSuppressAsync(10)).Should().Equal(protectedDepartment);

			await repository.SaveStateAsync(new CallLocationIndexState { DepartmentId = protectedDepartment, KeyVersion = ParsedStreetAddress.Version, IsSuppressed = true, ModifiedOn = Now });
			(await repository.GetDepartmentsToSuppressAsync(10)).Should().BeEmpty();
			(await repository.GetDepartmentsToResumeAsync(10)).Should().BeEmpty("protection is still on");
			(await repository.GetDepartmentsNeedingIndexAsync(ParsedStreetAddress.Version, 50)).Should().NotContain(protectedDepartment);

			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "UPDATE departmentdataprotectionpolicies SET state = 0 WHERE departmentid = @D"
					: "UPDATE DepartmentDataProtectionPolicies SET State = 0 WHERE DepartmentId = @D", new { D = protectedDepartment });
			(await repository.GetDepartmentsToResumeAsync(10)).Should().Equal(protectedDepartment);
		}

		[Test]
		public async Task Sources_page_down_from_the_cursor_and_department_purge_removes_keys()
		{
			const int purgeDepartment = 77;
			var a = await SeedCallAsync("1 A St", Now, purgeDepartment);
			var b = await SeedCallAsync("2 B St", Now, purgeDepartment);
			var c = await SeedCallAsync("3 C St", Now, purgeDepartment);
			await SeedCallAsync("4 D St", Now, purgeDepartment, deleted: true);

			var first = await Repository().GetSourcesAsync(purgeDepartment, null, 2);
			first.Select(s => s.CallId).Should().Equal(c, b);
			first[0].Address.Should().Be("3 C St");
			(await Repository().GetSourcesAsync(purgeDepartment, b, 2)).Select(s => s.CallId).Should().Equal(a);

			await Repository().UpsertAsync(new[] { Key(a, "1 A St", Now, departmentId: purgeDepartment), Key(b, "2 B St", Now, departmentId: purgeDepartment) });
			(await Repository().DeleteForDepartmentAsync(purgeDepartment)).Should().Be(2);
			(await Repository().GetByAddressKeyAsync(purgeDepartment, "1|A", 10)).Should().BeEmpty();
		}

		[Test]
		public async Task Batched_address_candidates_preserve_the_newest_cap_for_each_key()
		{
			const int dept = 811;
			var old = await SeedCallAsync("110 Main St", Now.AddDays(-2), dept);
			var latest = await SeedCallAsync("110 Main St", Now, dept);
			var elm = await SeedCallAsync("500 Elm St", Now.AddDays(-3), dept);
			var deleted = await SeedCallAsync("500 Elm St", Now, dept, deleted: true);
			await Repository().UpsertAsync(new[] { Key(old, "110 Main St", Now.AddDays(-2), departmentId: dept), Key(latest, "110 Main St", Now, departmentId: dept),
				Key(elm, "500 Elm St", Now.AddDays(-3), departmentId: dept), Key(deleted, "500 Elm St", Now, departmentId: dept) });
			var rows = await Repository().GetByAddressKeysAsync(dept, new[] { "110|MAIN", "500|ELM", "110|MAIN" }, 1);
			rows.Select(r => r.CallId).Should().Equal(latest, elm);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Protected_departments_with_late_keys_are_selected_even_without_an_unsuppressed_state(bool hasState)
		{
			var dept = await SeedDepartmentAsync();
			var call = await SeedCallAsync("110 Main St", Now, dept);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "INSERT INTO departmentdataprotectionpolicies (departmentid, state) VALUES (@D, 5)"
					: "INSERT INTO DepartmentDataProtectionPolicies (DepartmentId, State) VALUES (@D, 5)", new { D = dept });
			if (hasState) await Repository().SaveStateAsync(new CallLocationIndexState { DepartmentId = dept, IsSuppressed = true, ModifiedOn = Now });
			await Repository().UpsertAsync(new[] { Key(call, "110 Main St", Now, departmentId: dept) });
			(await Repository().GetDepartmentsToSuppressAsync(100)).Should().Contain(dept);
			await Repository().DeleteForDepartmentAsync(dept);
			(await Repository().GetDepartmentsToSuppressAsync(100)).Should().NotContain(dept);
		}
	}
}
