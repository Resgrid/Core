using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the Training T5 data exit (M0395) on both engines: the migration re-runs as a no-op, rolls back and applies
	/// again; the export queue (claim once, stale requeue, the expiry query), the zip kept in the database, the department-scoped reader over
	/// the export's tables (another department's rows never come back; tables outside the allow-list are refused), and department cleanup for
	/// that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsDataExitDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_exit_";
		private const string UserA = "5d1f2a33-1f3b-4d2c-9a6e-0b1c2d3e4f71";
		private const string UserB = "6e2a3b44-2a4c-4e3d-8b7f-1c2d3e4f5a82";
		private const int Department = 4701;
		private const int OtherDepartment = 4702;
		private static readonly string[] Tables = { "LmsDataExports", "LmsDataExportBlobs" };

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private string C(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepository Repository<TRepository>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepository> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectAllQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByDepartmentIdQuery) })
				queries[query] = (IQuery)Activator.CreateInstance(query, configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private ServiceProvider Runner() => new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
		{
			if (IsPostgres) r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All();
			else r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All();
		}).BuildServiceProvider(false);

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private async Task ExecuteAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(sql, parameters);
		}

		private string Exists(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

		[OneTimeSetUp]
		public async Task Migrate_an_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
			_previousType = DataConfig.DatabaseType;
			_previousConnection = DataConfig.CoreConnectionString;
			DataConfig.DatabaseType = type;

			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;
			DataConfig.CoreConnectionString = _connection;

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
				scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

			await using var database = Connect(_connection);
			await database.OpenAsync();
			if (database is NpgsqlConnection postgres)
				await postgres.ReloadTypesAsync();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previousType;
			DataConfig.CoreConnectionString = _previousConnection;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		[Test, Order(1)]
		public async Task The_migration_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 395");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");

				migrations.MigrateDown(394);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task The_export_queue_claims_once_requeues_stale_claims_and_finds_expired_zips()
		{
			var exports = Repository((c, s, u, q) => new LmsDataExportsRepository(c, s, u, q));
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

			var queued = await exports.InsertAsync(new LmsDataExport { DepartmentId = Department, Scope = 0, IncludeMedia = true, Status = 0, RequestedByUserId = UserA, RequestedOn = now.AddMinutes(-10) }, default, true);
			var second = await exports.InsertAsync(new LmsDataExport { DepartmentId = Department, Scope = 1, Status = 0, RequestedByUserId = UserA, RequestedOn = now.AddMinutes(-5) }, default, true);
			await exports.InsertAsync(new LmsDataExport { DepartmentId = OtherDepartment, Status = 0, RequestedByUserId = UserB, RequestedOn = now.AddMinutes(-20) }, default, true);

			(await exports.GetForDepartmentAsync(Department)).Select(e => e.LmsDataExportId).Should().Equal(second.LmsDataExportId, queued.LmsDataExportId);
			(await exports.GetByIdForDepartmentAsync(OtherDepartment, queued.LmsDataExportId)).Should().BeNull("department scoped");
			(await exports.GetQueuedAsync(10)).Should().HaveCount(3).And.BeInAscendingOrder(e => e.RequestedOn);

			(await exports.TryClaimAsync(queued.LmsDataExportId, now)).Should().BeTrue();
			(await exports.TryClaimAsync(queued.LmsDataExportId, now)).Should().BeFalse("a claimed export is not claimed twice");
			var claimed = await exports.GetByIdForDepartmentAsync(Department, queued.LmsDataExportId);
			claimed.Status.Should().Be((int)LmsDataExportStatuses.Running);
			claimed.Attempts.Should().Be(1);
			(await exports.GetQueuedAsync(10)).Should().NotContain(e => e.LmsDataExportId == queued.LmsDataExportId);

			(await exports.RequeueStaleAsync(now.AddMinutes(-1))).Should().Be(0, "a fresh claim stays");
			(await exports.RequeueStaleAsync(now.AddMinutes(1))).Should().Be(1);
			(await exports.GetByIdForDepartmentAsync(Department, queued.LmsDataExportId)).Status.Should().Be((int)LmsDataExportStatuses.Queued);

			claimed = await exports.GetByIdForDepartmentAsync(Department, queued.LmsDataExportId);
			claimed.Status = (int)LmsDataExportStatuses.Completed;
			claimed.CompletedOn = now;
			claimed.ExpiresOn = now.AddDays(7);
			claimed.ObjectKey = "lms/4701/shared/exports/1/x.zip";
			claimed.SizeBytes = 5_000_000_000;
			claimed.CountsJson = "{\"learningRecords\":12}";
			await exports.UpdateAsync(claimed, default, true);
			var stored = await exports.GetByIdForDepartmentAsync(Department, queued.LmsDataExportId);
			stored.SizeBytes.Should().Be(5_000_000_000);
			stored.Counts["learningRecords"].Should().Be(12);
			stored.IncludeMedia.Should().BeTrue();

			(await exports.GetExpiredUnpurgedAsync(now.AddDays(6), 10)).Should().BeEmpty();
			(await exports.GetExpiredUnpurgedAsync(now.AddDays(7), 10)).Select(e => e.LmsDataExportId).Should().Equal(queued.LmsDataExportId);
			stored.PurgedOn = now.AddDays(7);
			stored.Status = (int)LmsDataExportStatuses.Expired;
			await exports.UpdateAsync(stored, default, true);
			(await exports.GetExpiredUnpurgedAsync(now.AddDays(8), 10)).Should().BeEmpty();
		}

		[Test, Order(3)]
		public async Task A_zip_kept_in_the_database_is_read_only_for_its_department()
		{
			var blobs = Repository((c, s, u, q) => new LmsDataExportBlobsRepository(c, s, u, q));
			var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)(i % 251)).ToArray();
			await blobs.InsertAsync(new LmsDataExportBlob { DepartmentId = Department, LmsDataExportId = 77, Data = bytes, CreatedOn = DateTime.UtcNow }, default, true);
			await blobs.InsertAsync(new LmsDataExportBlob { DepartmentId = OtherDepartment, LmsDataExportId = 78, Data = new byte[] { 1 }, CreatedOn = DateTime.UtcNow }, default, true);

			(await blobs.GetDataAsync(Department, 77)).Should().Equal(bytes);
			(await blobs.GetDataAsync(OtherDepartment, 77)).Should().BeNull("department scoped");
			(await blobs.DeleteForExportAsync(OtherDepartment, 77)).Should().Be(0);
			(await blobs.DeleteForExportAsync(Department, 77)).Should().Be(1);
			(await blobs.GetDataAsync(Department, 77)).Should().BeNull();
			await blobs.InsertAsync(new LmsDataExportBlob { DepartmentId = Department, LmsDataExportId = 79, Data = new byte[] { 2 }, CreatedOn = DateTime.UtcNow }, default, true);
		}

		[Test, Order(4)]
		public async Task The_reader_returns_one_departments_rows_and_refuses_tables_outside_the_export()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var courses = Repository((c, s, u, q) => new LmsCoursesRepository(c, s, u, q));
			var source = Repository((c, s, u, q) => new LmsDataExportSourceRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			foreach (var (department, user, title) in new[] { (Department, UserA, "Ladder drill"), (Department, UserB, "Pump ops"), (OtherDepartment, UserB, "Theirs") })
				await records.InsertAsync(new LearningRecord
				{
					DepartmentId = department, UserId = user, SourceKind = (int)LearningRecordSourceKinds.ManualCredit, SourceRefId = Guid.NewGuid().ToString("N"), Title = title,
					CompletedOn = now, Hours = 1.25m, Status = (int)LearningRecordStatuses.Verified, CreatedOn = now
				}, default, true);
			await courses.InsertAsync(new LmsCourse { DepartmentId = Department, Title = "Hose handling", Status = 1, CreatedOn = now }, default, true);
			await courses.InsertAsync(new LmsCourse { DepartmentId = 0, PublisherKind = 1, SystemKey = "academy:test-" + Guid.NewGuid().ToString("N"), Title = "Academy", Status = 1, CreatedOn = now }, default, true);

			var mine = await source.GetDepartmentRowsAsync<LearningRecord>(Department);
			mine.Select(r => r.Title).Should().BeEquivalentTo(new[] { "Ladder drill", "Pump ops" });
			mine.Should().OnlyContain(r => r.DepartmentId == Department && r.Hours == 1.25m);
			(await source.GetDepartmentRowsAsync<LmsCourse>(Department)).Select(c => c.Title).Should().Equal("Hose handling");
			(await source.GetDepartmentRowsAsync<LmsCourse>(0)).Should().BeEmpty("the system publisher is never a department to export");

			Func<Task> files = () => source.GetDepartmentRowsAsync<LmsLessonFile>(Department);
			await files.Should().ThrowAsync<InvalidOperationException>("learner uploads hold bytes and are not part of the export");
			Func<Task> evidence = () => source.GetDepartmentRowsAsync<LearningRecordFile>(Department);
			await evidence.Should().ThrowAsync<InvalidOperationException>();

			// Every allow-listed table exists and answers the department query on this engine.
			foreach (var table in LmsDataExportSourceRepository.ExportedTables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().BeGreaterThanOrEqualTo(0, table);
		}

		[Test, Order(5)]
		public async Task Department_cleanup_removes_this_departments_rows_only()
		{
			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().BeGreaterThan(0, table + " was seeded for the department");

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
			{
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().Be(0, table);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0, table);
			}
		}
	}
}
