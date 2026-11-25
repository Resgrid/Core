using System;
using System.Collections.Concurrent;
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
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the Protocols P0 queries on both engines: M0336/M0337 over the protocol tables (trigger windows
	/// to UTC, call type ids filled from names, call protocol duplicates removed before the unique index), the byte-free
	/// attachment info, the batch question and trigger loads, the snapshot written once per hash, the usage count, and the
	/// dual-dialect department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolRepositoryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p0_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private (IConnectionProvider Connections, SqlConfiguration Configuration, QueryFactory Queries) Parts()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(new ConcurrentDictionary<Type, IQuery>());
			return (connections.Object, IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), new QueryFactory(list.Object));
		}

		private T Repository<T>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T> build)
		{
			var (connections, configuration, queries) = Parts();
			return build(connections, configuration, Mock.Of<IUnitOfWork>(), queries);
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

			// The M0001 shape of the tables M0336/M0337 change (plus M0333's soft delete), with seed rows written before the migrations.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres ? @"
CREATE EXTENSION IF NOT EXISTS citext;
CREATE TABLE departments (departmentid serial PRIMARY KEY, name citext, timezone citext);
CREATE TABLE calltypes (calltypeid serial PRIMARY KEY, departmentid integer NOT NULL, type citext);
CREATE TABLE calls (callid serial PRIMARY KEY, departmentid integer NOT NULL, loggedon timestamp NOT NULL);
CREATE TABLE dispatchprotocols (dispatchprotocolid serial PRIMARY KEY, departmentid integer NOT NULL REFERENCES departments (departmentid), name citext NOT NULL, code citext NOT NULL,
	isdisabled boolean NOT NULL DEFAULT false, isdeleted boolean NOT NULL DEFAULT false, deletedon timestamp NULL, description citext, protocoltext citext,
	createdon timestamp NOT NULL DEFAULT now(), createdbyuserid citext NOT NULL DEFAULT 'u', updatedon timestamp, minimumweight integer NOT NULL DEFAULT 0, updatedbyuserid citext NOT NULL DEFAULT 'u');
CREATE TABLE dispatchprotocoltriggers (dispatchprotocoltriggerid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), type integer NOT NULL,
	startson timestamp, endson timestamp, priority integer, calltype citext, geofence citext);
CREATE TABLE dispatchprotocolquestions (dispatchprotocolquestionid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), question citext);
CREATE TABLE dispatchprotocolquestionanswers (dispatchprotocolquestionanswerid serial PRIMARY KEY, dispatchprotocolquestionid integer NOT NULL REFERENCES dispatchprotocolquestions (dispatchprotocolquestionid), answer citext, weight integer NOT NULL);
CREATE TABLE dispatchprotocolattachments (dispatchprotocolattachmentid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), filename citext, filetype citext, data bytea);
CREATE TABLE callprotocols (callprotocolid serial PRIMARY KEY, callid integer NOT NULL, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), score integer NOT NULL, trigger integer NOT NULL, data citext);
INSERT INTO departments (name, timezone) VALUES ('One', 'America/New_York'), ('Two', 'Eastern Standard Time');
" : @"
CREATE TABLE Departments (DepartmentId int IDENTITY(1,1) PRIMARY KEY, Name nvarchar(100), TimeZone nvarchar(100));
CREATE TABLE CallTypes (CallTypeId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Type nvarchar(max));
CREATE TABLE Calls (CallId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, LoggedOn datetime NOT NULL);
CREATE TABLE DispatchProtocols (DispatchProtocolId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL REFERENCES Departments (DepartmentId), Name nvarchar(100) NOT NULL, Code nvarchar(4) NOT NULL,
	IsDisabled bit NOT NULL DEFAULT 0, IsDeleted bit NOT NULL DEFAULT 0, DeletedOn datetime2 NULL, Description nvarchar(500), ProtocolText nvarchar(max),
	CreatedOn datetime NOT NULL DEFAULT GETUTCDATE(), CreatedByUserId nvarchar(max) NOT NULL DEFAULT 'u', UpdatedOn datetime, MinimumWeight int NOT NULL DEFAULT 0, UpdatedByUserId nvarchar(max) NOT NULL DEFAULT 'u');
CREATE TABLE DispatchProtocolTriggers (DispatchProtocolTriggerId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Type int NOT NULL,
	StartsOn datetime, EndsOn datetime, Priority int, CallType nvarchar(max), Geofence nvarchar(max));
CREATE TABLE DispatchProtocolQuestions (DispatchProtocolQuestionId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Question nvarchar(max));
CREATE TABLE DispatchProtocolQuestionAnswers (DispatchProtocolQuestionAnswerId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolQuestionId int NOT NULL REFERENCES DispatchProtocolQuestions (DispatchProtocolQuestionId), Answer nvarchar(max), Weight int NOT NULL);
CREATE TABLE DispatchProtocolAttachments (DispatchProtocolAttachmentId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), FileName nvarchar(max), FileType nvarchar(max), Data varbinary(max));
CREATE TABLE CallProtocols (CallProtocolId int IDENTITY(1,1) PRIMARY KEY, CallId int NOT NULL, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Score int NOT NULL, [Trigger] int NOT NULL, Data nvarchar(max));
INSERT INTO Departments (Name, TimeZone) VALUES ('One', 'Eastern Standard Time'), ('Two', 'Eastern Standard Time');
");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();

				string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";
				await database.ExecuteAsync($@"
INSERT INTO {T("CallTypes")} ({T("DepartmentId")}, {T("Type")}) VALUES (1, 'Structure Fire'), (2, 'Structure Fire');
INSERT INTO {T("Calls")} ({T("DepartmentId")}, {T("LoggedOn")}) VALUES (1, @Recent), (1, @Recent), (1, @Old), (2, @Recent);
INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}, {T("Code")}, {T("ProtocolText")}) VALUES (1, 'Fire', 'SF', '<p>Size up</p>'), (1, 'Cardiac', 'CA', '<p>CPR</p>'), (2, 'Theirs', 'TH', '<p>x</p>');
INSERT INTO {T("DispatchProtocolTriggers")} ({T("DispatchProtocolId")}, {T("Type")}, {T("StartsOn")}, {T("CallType")}) VALUES (1, 1, @July8, ' structure fire '), (2, 1, NULL, 'No Type'), (3, 1, @July8, 'Structure Fire');
INSERT INTO {T("DispatchProtocolQuestions")} ({T("DispatchProtocolId")}, {T("Question")}) VALUES (2, 'Breathing?'), (2, 'Conscious?'), (3, 'Theirs?');
INSERT INTO {T("DispatchProtocolQuestionAnswers")} ({T("DispatchProtocolQuestionId")}, {T("Answer")}, {T("Weight")}) VALUES (1, 'No', 5), (1, 'Yes', 0), (2, 'No', 3), (3, 'x', 1);
INSERT INTO {T("DispatchProtocolAttachments")} ({T("DispatchProtocolId")}, {T("FileName")}, {T("FileType")}, {T("Data")}) VALUES (1, 'sop.pdf', 'application/pdf', @Bytes);
INSERT INTO {T("CallProtocols")} ({T("CallId")}, {T("DispatchProtocolId")}, {T("Score")}, {T("Trigger")}, {T("Data")}) VALUES (1, 1, 0, 0, 'A'), (1, 1, 0, 0, 'B'), (2, 1, 0, 0, 'C'), (3, 2, 0, 0, 'D'), (4, 3, 0, 0, 'E');",
					new { Recent = DateTime.UtcNow.AddDays(-2), Old = DateTime.UtcNow.AddDays(-90), July8 = new DateTime(2026, 7, 1, 8, 0, 0), Bytes = new byte[1234] });
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0336_ProtocolFoundationPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocolsPg() }
				: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0336_ProtocolFoundation(), new Resgrid.Providers.Migrations.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocols() });
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
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		[Test, Order(1)]
		public async Task The_migration_moves_windows_to_utc_fills_type_ids_and_removes_duplicates()
		{
			var triggers = (await Repository((c, s, u, q) => new DispatchProtocolTriggersRepository(c, s, u, q)).GetTriggersByDepartmentIdAsync(1)).ToList();

			var typed = triggers.Single(x => x.DispatchProtocolId == 1);
			typed.CallTypeId.Should().Be(1, "the name matched the department's type, whatever its spacing and case");
			typed.StartsOn.Should().Be(new DateTime(2026, 7, 1, 12, 0, 0), "08:00 in New York on 1 July is 12:00 UTC");
			ProtocolTriggerMatcher.IsNoTypeTrigger(triggers.Single(x => x.DispatchProtocolId == 2)).Should().BeTrue();
			triggers.Single(x => x.DispatchProtocolId == 2).CallType.Should().BeNull("the English \"No Type\" label is now a null name");

			await using var database = Connect(_connection);
			var rows = (await database.QueryAsync<string>(IsPostgres ? "SELECT data FROM callprotocols WHERE callid = 1" : "SELECT Data FROM CallProtocols WHERE CallId = 1")).ToList();
			rows.Should().Equal("A");

			Func<Task> duplicate = () => database.ExecuteAsync(IsPostgres
				? "INSERT INTO callprotocols (callid, dispatchprotocolid, score, trigger, data) VALUES (1, 1, 0, 0, 'Z')"
				: "INSERT INTO CallProtocols (CallId, DispatchProtocolId, Score, [Trigger], Data) VALUES (1, 1, 0, 0, 'Z')");
			await duplicate.Should().ThrowAsync<DbException>("a protocol is on a call once");
		}

		[Test, Order(2)]
		public async Task Attachment_info_has_the_size_but_never_the_bytes()
		{
			var info = (await Repository((c, s, u, q) => new DispatchProtocolAttachmentRepository(c, s, u, q)).GetAttachmentInfoByDepartmentIdAsync(1)).Single();

			info.FileName.Should().Be("sop.pdf");
			info.Size.Should().Be(1234);
			info.Data.Should().BeNull();
		}

		[Test, Order(3)]
		public async Task Questions_and_answers_load_for_the_whole_department_in_one_go()
		{
			var questions = (await Repository((c, s, u, q) => new DispatchProtocolQuestionsRepository(c, s, u, q)).GetQuestionsWithAnswersByDepartmentIdAsync(1)).ToList();

			questions.Select(x => x.Question).Should().Equal("Breathing?", "Conscious?");
			questions[0].Answers.Select(x => x.Answer).Should().Equal("No", "Yes");
			questions[1].Answers.Select(x => x.Weight).Should().Equal(3);
		}

		[Test, Order(4)]
		public async Task A_snapshot_is_written_once_per_protocol_and_hash()
		{
			var snapshots = Repository((c, s, u, q) => new DispatchProtocolSnapshotsRepository(c, s, u, q));
			var hash = ProtocolContentHash.Compute("<p>Size up</p>");

			for (var i = 0; i < 3; i++)
				await snapshots.InsertIfMissingAsync(new DispatchProtocolSnapshot { DepartmentId = 1, DispatchProtocolId = 1, ContentHash = hash, ProtocolText = "<p>Size up</p>", FirstUsedOn = DateTime.UtcNow });

			var stored = (await snapshots.GetByHashesAsync(1, new[] { hash })).ToList();
			stored.Should().ContainSingle().Which.ProtocolText.Should().Be("<p>Size up</p>");
			(await snapshots.GetByHashesAsync(2, new[] { hash })).Should().BeEmpty("another department never reads them");
		}

		[Test, Order(5)]
		public async Task Usage_counts_calls_in_the_window_and_skips_removed_rows()
		{
			var usage = (await Repository((c, s, u, q) => new CallProtocolsRepository(c, s, u, q)).GetUsageCountsAsync(1, DateTime.UtcNow.AddDays(-30))).ToList();

			usage.Should().ContainSingle(x => x.DispatchProtocolId == 1 && x.Calls == 2);
			usage.Should().NotContain(x => x.DispatchProtocolId == 2, "that call was logged 90 days ago");
		}

		[Test, Order(99)]
		public async Task The_department_cleanup_clears_the_protocol_tree_and_leaves_other_departments()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";
			foreach (var table in new[] { "DispatchProtocolSnapshots", "DispatchProtocolQuestions", "DispatchProtocolAttachments", "DispatchProtocolTriggers" })
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DispatchProtocolId")} IN (1, 2)")).Should().Be(0, table);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocolQuestionAnswers")} WHERE {T("DispatchProtocolQuestionId")} IN (1, 2)")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocolQuestionAnswers")} WHERE {T("DispatchProtocolQuestionId")} = 3")).Should().Be(1, "the other department's answers stay");

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocols")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocols")} WHERE {T("CallId")} IN (1, 2, 3)")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocols")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocols")} WHERE {T("CallId")} = 4")).Should().Be(1);
		}
	}
}
