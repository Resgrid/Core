using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Resgrid.Model.Repositories;
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
	/// Real-database proof for M0284 on both engines: the CRR activity alarm columns round-trip, alarm requests insert and
	/// read back within their department only, the queue filters by status / assignee / window and orders oldest first,
	/// deleted rows drop out, and the row-version claim lets exactly one writer through. M0186 runs first over a stub
	/// FeatureFlags table (it seeds flags). Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class RmsCrrAlarmRequestsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "rms_crr_alarm_requests_";
		private const int Dept = 41;
		private static readonly DateTime Base = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private DateTime Stored(DateTime value) => IsPostgres ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified) : value;

		private (SqlConfiguration Configuration, Mock<IConnectionProvider> Connections, QueryFactory Queries) Plumbing()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return (configuration, connections, new QueryFactory(list.Object));
		}

		private RmsCrrAlarmRequestsRepository Requests() { var p = Plumbing(); return new RmsCrrAlarmRequestsRepository(p.Connections.Object, p.Configuration, Mock.Of<IUnitOfWork>(), p.Queries); }
		private RmsCrrActivitiesRepository Activities() { var p = Plumbing(); return new RmsCrrActivitiesRepository(p.Connections.Object, p.Configuration, Mock.Of<IUnitOfWork>(), p.Queries); }

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_configured = true;
			var database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + database);
			_database = database;
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var setup = Connect(_connection))
			{
				await setup.OpenAsync();
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await setup.ExecuteAsync("CREATE TABLE featureflags (flagkey varchar(200) PRIMARY KEY, name varchar(200), description text, category varchar(100), isenabledglobally boolean NOT NULL DEFAULT false);");
					// The data source loaded its types before citext existed; without a reload a citext column cannot be read back.
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [FeatureFlags] ([FlagKey] nvarchar(200) NOT NULL PRIMARY KEY, [Name] nvarchar(200) NULL, [Description] nvarchar(max) NULL, [Category] nvarchar(100) NULL, [IsEnabledGlobally] bit NOT NULL DEFAULT 0);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0186_AddRmsPreventionAndInvestigationsPg(), new M0284_AddCrrAlarmTrackingPg() }
				: new IMigration[] { new M0186_AddRmsPreventionAndInvestigations(), new M0284_AddCrrAlarmTracking() });
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
			_runner?.Dispose();
			if (_configured) DataConfig.DatabaseType = _previous;
			// Only a database this fixture created is ever dropped.
			if (_database == null) return;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[SetUp]
		public async Task Empty_the_tables()
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres ? "DELETE FROM rmscrralarmrequests; DELETE FROM rmscrractivities;" : "DELETE FROM [RmsCrrAlarmRequests]; DELETE FROM [RmsCrrActivities];");
		}

		private Task<RmsCrrAlarmRequest> AddAsync(int departmentId, int status, DateTime requestedOn, string occupancyId = null, string assignee = null)
		{
			return Requests().InsertAsync(new RmsCrrAlarmRequest
			{
				RmsCrrAlarmRequestId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				RequestNumber = "ALM-2026-" + Guid.NewGuid().ToString("N").Substring(0, 4), Status = status, Source = (int)RmsAlarmRequestSource.Phone,
				Reason = (int)RmsAlarmReason.Faulty, RequestedOn = Stored(requestedOn), SmokeAlarmsRequested = 1, CoAlarmsRequested = 2, ComboAlarmsRequested = 3,
				RmsOccupancyId = occupancyId, LocationText = "4 Elm St", Latitude = 45.5m, Longitude = -122.6m, RequesterContactId = "contact-1", CallId = 77,
				Notes = "Hallway, needs a ladder", AssignedToUserId = assignee, CreatedOn = Stored(Base), CreatedByUserId = "admin", ModifiedOn = Stored(Base), RowVersion = 1
			}, CancellationToken.None);
		}

		[Test]
		public async Task Activity_alarm_columns_round_trip()
		{
			var activity = await Activities().InsertAsync(new RmsCrrActivity
			{
				RmsCrrActivityId = Guid.NewGuid().ToString(), DepartmentId = Dept, Kind = (int)RmsCrrActivityKind.SmokeAlarmInstallation, OccurredOn = Stored(Base), Title = "Elm St",
				RmsOccupancyId = "occ-1", SmokeAlarmsInstalled = 2, CoAlarmsInstalled = 1, ComboAlarmsInstalled = 4, AlarmReason = (int)RmsAlarmReason.Expired, CallId = 77,
				RmsCrrAlarmRequestId = "req-1", CreatedOn = Stored(Base), ModifiedOn = Stored(Base), RowVersion = 1
			}, CancellationToken.None);

			var read = await Activities().GetByIdForDepartmentAsync(Dept, activity.RmsCrrActivityId);
			read.SmokeAlarmsInstalled.Should().Be(2); read.CoAlarmsInstalled.Should().Be(1); read.ComboAlarmsInstalled.Should().Be(4);
			read.AlarmReason.Should().Be((int)RmsAlarmReason.Expired); read.CallId.Should().Be(77); read.RmsCrrAlarmRequestId.Should().Be("req-1");
			(await Activities().GetForOccupancyAsync(Dept, "occ-1")).Should().ContainSingle();
		}

		[Test]
		public async Task A_request_reads_back_in_its_own_department_only()
		{
			var request = await AddAsync(Dept, (int)RmsAlarmRequestStatus.Open, Base, "occ-1");

			var read = await Requests().GetByIdForDepartmentAsync(Dept, request.RmsCrrAlarmRequestId);
			read.Should().NotBeNull();
			read.RequestNumber.Should().Be(request.RequestNumber);
			read.ComboAlarmsRequested.Should().Be(3); read.CallId.Should().Be(77); read.Latitude.Should().Be(45.5m);
			read.Notes.Should().Be("Hallway, needs a ladder");
			(await Requests().GetByIdForDepartmentAsync(Dept + 1, request.RmsCrrAlarmRequestId)).Should().BeNull();
			(await Requests().GetForOccupancyAsync(Dept, "occ-1")).Should().ContainSingle();
		}

		[Test]
		public async Task The_queue_filters_and_orders_and_skips_deleted_rows()
		{
			var oldest = await AddAsync(Dept, (int)RmsAlarmRequestStatus.Open, Base.AddDays(-5), assignee: "member-1");
			var newer = await AddAsync(Dept, (int)RmsAlarmRequestStatus.Open, Base.AddDays(-1));
			await AddAsync(Dept, (int)RmsAlarmRequestStatus.Completed, Base.AddDays(-3));
			await AddAsync(Dept + 1, (int)RmsAlarmRequestStatus.Open, Base.AddDays(-4));
			var deleted = await AddAsync(Dept, (int)RmsAlarmRequestStatus.Open, Base.AddDays(-2));
			deleted.DeletedOn = Stored(Base); await Requests().UpdateAsync(deleted, CancellationToken.None);

			var open = new RmsAlarmRequestQuery { Statuses = new List<int> { (int)RmsAlarmRequestStatus.Open }, OldestFirst = true };
			(await Requests().QueryAsync(Dept, open)).Select(r => r.RmsCrrAlarmRequestId).Should().Equal(oldest.RmsCrrAlarmRequestId, newer.RmsCrrAlarmRequestId);
			(await Requests().CountAsync(Dept, open)).Should().Be(2);
			(await Requests().CountAsync(Dept, new RmsAlarmRequestQuery { Statuses = new List<int> { (int)RmsAlarmRequestStatus.Open, (int)RmsAlarmRequestStatus.Completed } })).Should().Be(3);
			(await Requests().QueryAsync(Dept, new RmsAlarmRequestQuery { AssignedToUserId = "member-1" })).Should().ContainSingle(r => r.RmsCrrAlarmRequestId == oldest.RmsCrrAlarmRequestId);
			(await Requests().CountAsync(Dept, new RmsAlarmRequestQuery { RequestedFrom = Stored(Base.AddDays(-4)), RequestedBefore = Stored(Base) })).Should().Be(2, "the window is [from, before)");
			(await Requests().QueryAsync(Dept, new RmsAlarmRequestQuery { Take = 1 })).Single().RmsCrrAlarmRequestId.Should().Be(newer.RmsCrrAlarmRequestId, "newest first by default");
		}

		[Test]
		public async Task The_row_version_claim_lets_one_writer_through()
		{
			var request = await AddAsync(Dept, (int)RmsAlarmRequestStatus.Open, Base);

			(await Requests().TryBumpRowVersionAsync(Dept, request.RmsCrrAlarmRequestId, 1)).Should().BeTrue();
			(await Requests().TryBumpRowVersionAsync(Dept, request.RmsCrrAlarmRequestId, 1)).Should().BeFalse("the first claim moved the version on");
			(await Requests().TryBumpRowVersionAsync(Dept + 1, request.RmsCrrAlarmRequestId, 2)).Should().BeFalse("another department never claims it");
			(await Requests().GetByIdForDepartmentAsync(Dept, request.RmsCrrAlarmRequestId)).RowVersion.Should().Be(2);
		}

		[Test]
		public async Task Down_removes_the_table_and_columns_and_up_restores_them()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			runner.MigrateDown(186);
			await using (var database = Connect(_connection))
			{
				var table = await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'rmscrralarmrequests'" : "SELECT COUNT(*) FROM sys.tables WHERE name = 'RmsCrrAlarmRequests'");
				var column = await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'rmscrractivities' AND column_name = 'coalarmsinstalled'" : "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('RmsCrrActivities') AND name = 'CoAlarmsInstalled'");
				table.Should().Be(0); column.Should().Be(0);
			}
			runner.MigrateUp();
			(await Requests().CountAsync(Dept, null)).Should().Be(0);
		}
	}
}
