using System;
using System.Collections.Concurrent;
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
	/// Real-database proof for round 9B (M0408–M0410, training hours submissions) on both engines: Down removes exactly what Up added, Up over
	/// a T1 database gives its submissions their review state and links their evidence file, a second Up is a no-op; the ledger's review
	/// filter (a submission without a state waits for review), oldest-first order and the new updatable columns; evidence files without their
	/// bytes, linked once; the review history read case-insensitively; the daily digest claimed once per day; department cleanup of the new
	/// tables. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class TrainingSubmissionsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "r9b_ts_";
		private const string Member = "6d3f8b22-2a4c-4e3d-8b7f-1c2d3e4f5a61";
		private const string Officer = "6d3f8b22-2a4c-4e3d-8b7f-1c2d3e4f5a62";
		private const int Department = 9301;
		private const int OtherDepartment = 9302;

		private static readonly string[] Tables = { "LearningRecordReviewEvents", "TrainingReviewDigests" };

		private static readonly (string Table, string Column)[] Columns =
		{
			("LearningRecords", "ReviewState"), ("LearningRecords", "SubmittedOn"), ("LearningRecordFiles", "LearningRecordId"), ("LearningRecordFiles", "SizeBytes"),
			("LearningRecordFiles", "Sha256"), ("LearningRecordFiles", "EvidenceKind"), ("LearningRecordFiles", "ScanState")
		};

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

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

		private async Task<T> ScalarAsync<T>(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<T>(sql, parameters);
		}

		private Task<int> CountAsync(string sql, object parameters = null) => ScalarAsync<int>(sql, parameters);

		private async Task ExecuteAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(sql, parameters);
		}

		private string TablesExist(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

		private string ColumnsExist() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND (" +
			  string.Join(" OR ", Columns.Select(c => $"(table_name = '{c.Table.ToLowerInvariant()}' AND column_name = '{c.Column.ToLowerInvariant()}')")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE " + string.Join(" OR ", Columns.Select(c => $"(TABLE_NAME = '{c.Table}' AND COLUMN_NAME = '{c.Column}')"));

		private string Version => IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
		private string VersionColumn => IsPostgres ? "\"Version\"" : "[Version]";

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
		public async Task Down_removes_what_Up_added_Up_backfills_T1_submissions_and_a_second_Up_is_a_no_op()
		{
			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateDown(407);
				(await CountAsync(TablesExist(Tables))).Should().Be(0);
				(await CountAsync(ColumnsExist())).Should().Be(0, "Down removes exactly the columns Up added");
				(await CountAsync(TablesExist("LearningRecords", "LearningRecordFiles"))).Should().Be(2, "and nothing else");

				// A T1 database: a pending and a verified submission, the pending one with its single evidence file.
				var now = DateTime.UtcNow;
				int fileId;
				await using (var database = Connect(_connection))
				{
					fileId = await database.ExecuteScalarAsync<int>(IsPostgres
						? "INSERT INTO learningrecordfiles (departmentid, userid, filename, contenttype, data, uploadedon, uploadedbyuserid) VALUES (@D, @U, 'cert.pdf', 'application/pdf', @Data, @Now, @U) RETURNING learningrecordfileid"
						: "INSERT INTO [LearningRecordFiles] ([DepartmentId], [UserId], [FileName], [ContentType], [Data], [UploadedOn], [UploadedByUserId]) OUTPUT INSERTED.[LearningRecordFileId] VALUES (@D, @U, 'cert.pdf', 'application/pdf', @Data, @Now, @U)",
						new { D = Department, U = Member, Data = new byte[] { 1, 2, 3, 4, 5 }, Now = now });
					foreach (var (reference, status, verifiedBy, evidence) in new[] { ("submission:a", 0, (string)null, (int?)fileId), ("submission:b", 1, Officer, (int?)null) })
						await database.ExecuteAsync(
							$"INSERT INTO {T("LearningRecords")} ({T("DepartmentId")}, {T("UserId")}, {T("SourceKind")}, {T("SourceRefId")}, {T("ComponentKey")}, {T("Title")}, {T("CompletedOn")}, " +
							$"{T("Hours")}, {T("Status")}, {T("VerifiedByUserId")}, {T("EvidenceFileId")}, {T("IsBackfill")}, {T("CreatedOn")}) " +
							$"VALUES (@D, @U, 3, @Ref, '', 'Outside course', @Now, 4, @Status, @VerifiedBy, @Evidence, @False, @Now)",
							new { D = Department, U = Member, Ref = reference, Now = now, Status = status, VerifiedBy = verifiedBy, Evidence = evidence, False = false });
				}

				migrations.MigrateUp();
				(await CountAsync(TablesExist(Tables))).Should().Be(Tables.Length);
				(await CountAsync(ColumnsExist())).Should().Be(Columns.Length);

				var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
				var backfilled = (await records.GetBySourceAsync(Department, 3, "submission:a")).Single();
				backfilled.ReviewState.Should().Be((int)TrainingSubmissionReviewStates.AwaitingReview);
				backfilled.SubmittedOn.Should().NotBeNull();
				(await records.GetBySourceAsync(Department, 3, "submission:b")).Single().ReviewState.Should().Be((int)TrainingSubmissionReviewStates.Approved);
				var files = Repository((c, s, u, q) => new LearningRecordFilesRepository(c, s, u, q));
				var linked = (await files.GetForRecordsAsync(Department, new[] { backfilled.LearningRecordId })).Single();
				linked.LearningRecordId.Should().Be(backfilled.LearningRecordId);
				linked.SizeBytes.Should().Be(5);
				linked.ScanState.Should().Be(3, "files stored before scanning read as skipped");

				// A retry runs the guarded scripts again over everything that exists: nothing changes and nothing fails.
				await ExecuteAsync($"DELETE FROM {Version} WHERE {VersionColumn} >= 408 AND {VersionColumn} <= 410");
				migrations.MigrateUp();
			}

			(await CountAsync(TablesExist(Tables))).Should().Be(Tables.Length);
			(await CountAsync(ColumnsExist())).Should().Be(Columns.Length);
		}

		[Test, Order(2)]
		public async Task The_ledger_filters_review_states_orders_oldest_first_and_updates_the_review_columns()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			LearningRecord Submission(string reference, int? state, int status, DateTime submitted) => new LearningRecord
			{
				DepartmentId = Department, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, SourceRefId = reference, Title = "Course " + reference,
				CompletedOn = submitted, Hours = 2m, Status = status, ReviewState = state, SubmittedOn = submitted, CreatedOn = submitted
			};

			var now = DateTime.UtcNow;
			var newest = (await records.InsertIfAbsentAsync(Submission("submission:r2-new", 0, 0, now.AddHours(-1)))).Record;
			var oldest = (await records.InsertIfAbsentAsync(Submission("submission:r2-old", 0, 0, now.AddDays(-4)))).Record;
			var asked = (await records.InsertIfAbsentAsync(Submission("submission:r2-info", 1, 0, now.AddDays(-2)))).Record;
			await records.InsertIfAbsentAsync(Submission("submission:r2-self", 4, 1, now.AddDays(-3)));

			var awaiting = await records.QueryAsync(new LearningRecordQuery
			{
				DepartmentId = Department, SourceKinds = new() { 3 }, Statuses = new() { 0 }, ReviewStates = new() { 0 }, OldestSubmittedFirst = true
			});
			awaiting.Select(r => r.SourceRefId).Should().ContainInOrder("submission:r2-old", "submission:r2-new");
			awaiting.Select(r => r.SourceRefId).Should().Contain("submission:a", "a T1 submission without a state waits for review").And.NotContain("submission:r2-info");
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = Department, ReviewStates = new() { 1 } })).Select(r => r.LearningRecordId).Should().Equal(asked.LearningRecordId);
			(await records.GetDepartmentIdsAwaitingReviewAsync(100)).Should().Contain(Department).And.NotContain(OtherDepartment);

			newest.ReviewState = (int)TrainingSubmissionReviewStates.InfoRequested;
			newest.CeHours = 1.5m;
			newest.EvidenceFileId = 77;
			newest.SubmittedOn = now;
			await records.UpdateColumnsAsync(newest, new[] { "ReviewState", "CeHours", "EvidenceFileId", "SubmittedOn" });
			var read = await records.GetByIdForDepartmentAsync(Department, newest.LearningRecordId);
			read.ReviewState.Should().Be((int)TrainingSubmissionReviewStates.InfoRequested);
			read.CeHours.Should().Be(1.5m);
			read.EvidenceFileId.Should().Be(77);
			oldest.LearningRecordId.Should().BePositive();
		}

		[Test, Order(3)]
		public async Task Evidence_lists_never_carry_the_bytes_link_once_and_read_a_member_case_insensitively()
		{
			var files = Repository((c, s, u, q) => new LearningRecordFilesRepository(c, s, u, q));
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var record = (await records.InsertIfAbsentAsync(new LearningRecord
			{
				DepartmentId = Department, UserId = Member, SourceKind = 3, SourceRefId = "submission:r3", Title = "Evidence", CompletedOn = DateTime.UtcNow, Status = 0, ReviewState = 0,
				SubmittedOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow
			})).Record;

			LearningRecordFile New(string name) => new LearningRecordFile
			{
				DepartmentId = Department, UserId = Member, FileName = name, ContentType = "image/jpeg", Data = new byte[] { 9, 9, 9 }, UploadedOn = DateTime.UtcNow, UploadedByUserId = Member,
				SizeBytes = 3, Sha256 = "abc", EvidenceKind = (int)TrainingEvidenceKinds.Photo, ScanState = (int)RmsAttachmentScanState.Clean
			};
			var a = await files.InsertAsync(New("a.jpg"), default, true);
			var b = await files.InsertAsync(New("b.jpg"), default, true);
			(await files.LinkToRecordAsync(Department, new[] { a.LearningRecordFileId, b.LearningRecordFileId }, record.LearningRecordId)).Should().Be(2);
			(await files.LinkToRecordAsync(Department, new[] { a.LearningRecordFileId }, record.LearningRecordId + 1000)).Should().Be(0, "a linked file never moves");
			(await files.LinkToRecordAsync(OtherDepartment, new[] { b.LearningRecordFileId }, 1)).Should().Be(0);

			var listed = await files.GetForRecordsAsync(Department, new[] { record.LearningRecordId });
			listed.Select(f => f.FileName).Should().Equal("a.jpg", "b.jpg");
			listed.Should().OnlyContain(f => f.Data == null && f.SizeBytes == 3 && f.EvidenceKind == (int)TrainingEvidenceKinds.Photo && f.ScanState == (int)RmsAttachmentScanState.Clean);
			(await files.GetForUserAsync(Department, Member.ToUpperInvariant())).Should().Contain(f => f.FileName == "a.jpg").And.OnlyContain(f => f.Data == null);
			(await files.GetWithDataAsync(Department, a.LearningRecordFileId)).Data.Should().Equal(9, 9, 9);
			(await files.GetForRecordsAsync(OtherDepartment, new[] { record.LearningRecordId })).Should().BeEmpty();
		}

		[Test, Order(4)]
		public async Task Review_history_reads_case_insensitively_and_a_digest_is_claimed_once_a_day()
		{
			var history = Repository((c, s, u, q) => new LearningRecordReviewEventsRepository(c, s, u, q));
			await history.InsertAsync(new LearningRecordReviewEvent { DepartmentId = Department, LearningRecordId = 501, UserId = Member, Kind = 0, ActorUserId = Member, CreatedOn = DateTime.UtcNow.AddMinutes(-5), Hours = 2m, FileCount = 2 }, default, true);
			await history.InsertAsync(new LearningRecordReviewEvent { DepartmentId = Department, LearningRecordId = 501, UserId = Member, Kind = 4, ActorUserId = Officer, Note = "Add page 2", CreatedOn = DateTime.UtcNow }, default, true);
			await history.InsertAsync(new LearningRecordReviewEvent { DepartmentId = OtherDepartment, LearningRecordId = 501, UserId = Member, Kind = 0, CreatedOn = DateTime.UtcNow }, default, true);

			(await history.GetForRecordAsync(Department, 501)).Select(e => e.Kind).Should().Equal(0, 4);
			(await history.GetForUserAsync(Department, Member.ToUpperInvariant())).Should().HaveCount(2).And.Contain(e => e.Note == "Add page 2" && e.ActorUserId == Officer);

			var digests = Repository((c, s, u, q) => new TrainingReviewDigestsRepository(c, s, u, q));
			var day = new DateTime(2026, 11, 25);
			(await digests.TryClaimAsync(Department, day, DateTime.UtcNow)).Should().BeTrue();
			(await digests.TryClaimAsync(Department, day.AddHours(13), DateTime.UtcNow)).Should().BeFalse("one digest per department and day");
			(await digests.TryClaimAsync(Department, day.AddDays(1), DateTime.UtcNow)).Should().BeTrue();
			(await digests.TryClaimAsync(OtherDepartment, day, DateTime.UtcNow)).Should().BeTrue();
			(await digests.SaveCountsAsync(Department, day, 3, 5)).Should().Be(1);
			(await CountAsync($"SELECT {T("Submissions")} FROM {T("TrainingReviewDigests")} WHERE {T("DepartmentId")} = @D AND {T("Reviewers")} = 3", new { D = Department })).Should().Be(5);
		}

		[Test, Order(5)]
		public async Task Department_cleanup_removes_the_new_tables_for_that_department_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables.Concat(new[] { "LearningRecords", "LearningRecordFiles" }))
				(await CountAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await CountAsync($"SELECT COUNT(*) FROM {T("LearningRecordReviewEvents")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
			(await CountAsync($"SELECT COUNT(*) FROM {T("TrainingReviewDigests")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
