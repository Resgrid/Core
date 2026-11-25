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
	/// Real-database proof for Training T0 and T1 on both engines: the whole migration chain into an isolated database, the T0
	/// (M0338–M0340) and T1 (M0348–M0350) migrations re-run as no-ops and rolled back and forward again, the learning-record
	/// ledger's idempotency key (voided records free it, components and case-insensitive member ids), filters, groups,
	/// categories, backfill claims, verification codes and derived-credit lookups, T0's repository SQL that had never run
	/// against a database (the assignment insert's ON CONFLICT / UPDLOCK path, UpdateColumnsAsync, the notice-work query), and
	/// the department cleanup of the new tables. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class TrainingT1RepositoryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "training_t1_";
		private const string UserA = "5f0c8a52-6f3e-4d5b-9c1a-1b2c3d4e5f60";
		private const string UserB = "6a1d9b63-7e4f-4e6c-8d2b-2c3d4e5f6a71";

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;
		private int _department, _otherDepartment, _training, _certificationType, _certification;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private T Repository<T>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			// The generic insert, update, select and delete the base repository builds (the production QueryList finds these by reflection).
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteMultipleQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectAllQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByDepartmentIdQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByUserIdQuery) })
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

		[OneTimeSetUp]
		public async Task Migrate_and_seed_an_isolated_database()
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

			foreach (var (id, name) in new[] { (UserA, "Alex.Member"), (UserB, "Blair.Member") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "T1 One", Code = "T1A", ManagingUserId = UserA, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "T1 Two", Code = "T1B", ManagingUserId = UserB, ShowWelcome = false });
			_department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'T1A'");
			_otherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'T1B'");
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

		/// <summary>Inserts a row with the given columns and a placeholder in every other required column without a default.</summary>
		private async Task InsertAsync(DbConnection database, string table, object values)
		{
			var row = values.GetType().GetProperties().ToDictionary(p => IsPostgres ? p.Name.ToLowerInvariant() : p.Name, p => p.GetValue(values), StringComparer.OrdinalIgnoreCase);
			var required = IsPostgres
				? await database.QueryAsync<(string Column, string Type)>(
					@"SELECT column_name, data_type FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @Table AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'",
					new { Table = table.ToLowerInvariant() })
				: await database.QueryAsync<(string Column, string Type)>(
					@"SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @Table AND IS_NULLABLE = 'NO' AND COLUMN_DEFAULT IS NULL
					  AND COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 0",
					new { Table = table });

			var parameters = new DynamicParameters();
			var expressions = new List<(string Column, string Value)>();
			foreach (var (column, value) in row)
			{
				parameters.Add(column, value);
				expressions.Add((column, "@" + column));
			}

			foreach (var (column, kind) in required.Where(r => !row.ContainsKey(r.Column)))
			{
				var placeholder = kind switch
				{
					"boolean" => "false",
					"bit" => "0",
					"smallint" or "integer" or "bigint" or "numeric" or "double precision" or "real" or "int" or "tinyint" or "decimal" or "float" or "money" => "0",
					"timestamp without time zone" or "timestamp with time zone" or "date" or "datetime" or "datetime2" or "datetimeoffset" or "smalldatetime" => IsPostgres ? "TIMESTAMP '2026-10-08'" : "'2026-10-08'",
					"json" or "jsonb" => "'{}'",
					"bytea" => "''::bytea",
					"varbinary" or "binary" or "image" => "0x00",
					"uuid" => "'" + Guid.NewGuid() + "'",
					"uniqueidentifier" => "'" + Guid.NewGuid() + "'",
					_ => "'" + Guid.NewGuid().ToString("N").Substring(0, 12) + "'"
				};
				expressions.Add((column, placeholder));
			}

			var quote = IsPostgres ? (Func<string, string>)(c => "\"" + c + "\"") : c => "[" + c + "]";
			await database.ExecuteAsync(
				$"INSERT INTO {T(table)} ({string.Join(", ", expressions.Select(e => quote(e.Column)))}) VALUES ({string.Join(", ", expressions.Select(e => e.Value))})",
				parameters);
		}

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		[Test, Order(1)]
		public async Task T0_and_T1_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 338");

			// Up a second time over the existing schema: every guard holds.
			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Training.Lms'")).Should().Be(1, "the flag is seeded once");

				migrations.MigrateDown(337);
				(await ScalarAsync(IsPostgres
					? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('learningrecords', 'trainingcategories', 'trainingquizattempts', 'lmscertificatetemplates')"
					: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('LearningRecords', 'TrainingCategories', 'TrainingQuizAttempts', 'LmsCertificateTemplates')")).Should().Be(0);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Training.Lms'")).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Training.Lms' AND {T("IsEnabledGlobally")} = {(IsPostgres ? "false" : "0")}")).Should().Be(1, "Training.Lms is seeded off");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("AddonType")} = 9")).Should().Be(1,
				"M0366 (LMS general availability) seeds the catalog row; general availability is still the operator switching the seeded-off flag on");
		}

		[Test, Order(2)]
		public async Task Seed_training_certification_and_t0_assignment_sql()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
				await InsertAsync(database, "Trainings", new
				{
					DepartmentId = _department, CreatedByUserId = UserA, Name = "Ladder drill", Description = "d", TrainingText = "t", MinimumScore = 0d, CreatedOn = DateTime.UtcNow.AddDays(-3),
					Hours = 2.5m, ClosedToNewAssignees = false, OpenToAllMembers = false, AllowSelfEnroll = false, OneQuestionPerPage = false, SendOverdueNotices = false
				});
				_training = await database.ExecuteScalarAsync<int>($"SELECT MAX({T("TrainingId")}) FROM {T("Trainings")}");
				await InsertAsync(database, "DepartmentCertificationTypes", new { DepartmentId = _department, Type = "EMT", Code = "NREMT-E", IsDeleted = false, IsActive = true });
				_certificationType = await database.ExecuteScalarAsync<int>($"SELECT MAX({T("DepartmentCertificationTypeId")}) FROM {T("DepartmentCertificationTypes")}");
				await InsertAsync(database, "PersonnelCertifications", new
				{
					DepartmentId = _department, UserId = UserA, Name = "EMT", DepartmentCertificationTypeId = _certificationType, Status = 0, IsDeleted = false, IsProtected = false,
					RecievedOn = new DateTime(2026, 1, 1), ExpiresOn = new DateTime(2027, 12, 31)
				});
				_certification = await database.ExecuteScalarAsync<int>($"SELECT MAX({T("PersonnelCertificationId")}) FROM {T("PersonnelCertifications")}");
			}

			var users = Repository((c, s, u, q) => new TrainingUserRepository(c, s, u, q));
			var first = await users.InsertAssignmentAsync(new TrainingUser { TrainingId = _training, UserId = UserA, AssignedOn = DateTime.UtcNow, AssignmentSource = 0 });
			first.Should().NotBeNull();
			var again = await users.InsertAssignmentAsync(new TrainingUser { TrainingId = _training, UserId = UserA.ToUpperInvariant(), AssignedOn = DateTime.UtcNow });
			again.Should().BeNull("one row per member and training, whatever the id's case (ON CONFLICT / UPDLOCK)");

			first.DueOn = DateTime.UtcNow.AddDays(2);
			(await users.UpdateColumnsAsync(first, new[] { "DueOn" })).Should().Be(1);

			var work = await Repository((c, s, u, q) => new TrainingRepository(c, s, u, q)).GetTrainingIdsWithNoticeWorkAsync(DateTime.UtcNow, 7);
			work.Should().Contain(_training, "the assignment was never announced");
		}

		private LearningRecord Record(string userId, string sourceRef, string component = "", int status = (int)LearningRecordStatuses.Verified, decimal hours = 2m, int? category = null) => new LearningRecord
		{
			DepartmentId = _department, UserId = userId, SourceKind = (int)LearningRecordSourceKinds.TrainingRecord, SourceRefId = sourceRef, ComponentKey = component,
			Title = "Hose drill", CourseCode = "HD-1", CompletedOn = new DateTime(2026, 6, 1, 18, 0, 0), StartedOn = new DateTime(2026, 6, 1, 16, 0, 0), EndedOn = new DateTime(2026, 6, 1, 18, 0, 0),
			Hours = hours, Status = status, CategoryId = category, CreatedOn = DateTime.UtcNow
		};

		[Test, Order(3)]
		public async Task The_idempotency_key_admits_one_live_record_per_member_and_component()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));

			var (first, inserted) = await records.InsertIfAbsentAsync(Record(UserA, "log:1"));
			inserted.Should().BeTrue();
			var (same, again) = await records.InsertIfAbsentAsync(Record(UserA.ToUpperInvariant(), "log:1"));
			again.Should().BeFalse("the same member (any case), source and component is the same record (R-T1)");
			same.LearningRecordId.Should().Be(first.LearningRecordId);

			(await records.InsertIfAbsentAsync(Record(UserA, "log:1", "pumps"))).Inserted.Should().BeTrue("a second activity line of one session is its own record");
			(await records.InsertIfAbsentAsync(Record(UserB, "log:1"))).Inserted.Should().BeTrue();

			(await records.VoidAsync(_department, first.LearningRecordId, "source_changed", UserB, DateTime.UtcNow)).Should().BeTrue();
			(await records.VoidAsync(_department, first.LearningRecordId, "again", UserB, DateTime.UtcNow)).Should().BeFalse("a voided record stays voided");
			var replacement = Record(UserA, "log:1", hours: 3m);
			replacement.SupersedesId = first.LearningRecordId;
			var (superseding, written) = await records.InsertIfAbsentAsync(replacement);
			written.Should().BeTrue("a voided record frees the key for the record that supersedes it (R-T4)");

			var chain = await records.GetBySourceAsync(_department, (int)LearningRecordSourceKinds.TrainingRecord, "log:1");
			chain.Should().HaveCount(4);
			chain.Single(r => r.LearningRecordId == superseding.LearningRecordId).SupersedesId.Should().Be(first.LearningRecordId);

			var existing = await records.GetExistingSourceRefIdsAsync(_department, (int)LearningRecordSourceKinds.TrainingRecord, new[] { "log:1", "log:2" });
			existing.Should().BeEquivalentTo(new[] { "log:1" });
			(await records.GetBySourceRefIdsAsync(_department, (int)LearningRecordSourceKinds.TrainingRecord, new[] { "log:1" })).Should().HaveCount(3, "voided records are left out");
		}

		[Test, Order(4)]
		public async Task Queries_groups_categories_duplicates_and_codes_run_on_the_engine()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var categories = Repository((c, s, u, q) => new TrainingCategoriesRepository(c, s, u, q));

			var category = await categories.SaveOrUpdateAsync(new TrainingCategory { DepartmentId = _department, Name = "Company training", Code = "ISO-CO", Kind = 1, TargetHours = 192m, TargetPeriod = 1, CreatedOn = DateTime.UtcNow }, default, true);
			category.TrainingCategoryId.Should().BeGreaterThan(0);
			await categories.SaveOrUpdateAsync(new TrainingCategory { DepartmentId = _otherDepartment, Name = "Theirs", CreatedOn = DateTime.UtcNow }, default, true);
			(await categories.GetForDepartmentAsync(_department, false)).Select(c => c.Code).Should().Equal("ISO-CO");

			var uncategorized = await records.GetUncategorizedGroupsAsync(_department, new DateTime(2026, 1, 1), null);
			uncategorized.Should().ContainSingle(g => g.Title == "Hose drill" && g.CourseCode == "HD-1").Which.Count.Should().Be(3);

			var live = await records.QueryAsync(new LearningRecordQuery { DepartmentId = _department, Search = "hose", UncategorizedOnly = true });
			live.Should().HaveCount(3, "voided records are left out unless asked");
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = _department, Search = "50%_x" })).Should().BeEmpty("wildcards in the search are literal");
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = _department, IncludeVoided = true, UserIds = new List<string> { UserA.ToUpperInvariant() } })).Should().HaveCount(3);
			(await records.CountAsync(new LearningRecordQuery { DepartmentId = _department, CourseCode = "hd-1" })).Should().Be(3);

			(await records.SetCategoryAsync(_department, live.Select(r => r.LearningRecordId), category.TrainingCategoryId)).Should().Be(3);
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = _department, CategoryIds = new List<int> { category.TrainingCategoryId } })).Should().HaveCount(3);

			var flagged = live[0];
			flagged.PossibleDuplicateOfId = live[1].LearningRecordId;
			(await records.UpdateColumnsAsync(flagged, new[] { "PossibleDuplicateOfId" })).Should().Be(1);
			(await records.CountUnresolvedDuplicatesAsync(_department, null, null)).Should().Be(1);
			(await records.GetForMemberInWindowAsync(_department, flagged.UserId.ToUpperInvariant(), new DateTime(2026, 6, 1), new DateTime(2026, 6, 2))).Should().NotBeEmpty();

			flagged.VerificationCode = "ABCDEFGH23";
			await records.UpdateColumnsAsync(flagged, new[] { "VerificationCode" });
			(await records.GetByVerificationCodeAsync("abcdefgh23")).LearningRecordId.Should().Be(flagged.LearningRecordId);
			var other = live[1];
			other.VerificationCode = "ABCDEFGH23";
			Func<Task> duplicateCode = () => records.UpdateColumnsAsync(other, new[] { "VerificationCode" });
			await duplicateCode.Should().ThrowAsync<DbException>("a verification code identifies one certificate");
		}

		[Test, Order(5)]
		public async Task Derived_credits_are_found_by_learning_record_and_files_mappings_and_templates_round_trip()
		{
			var credits = Repository((c, s, u, q) => new PersonnelCertificationCreditsRepository(c, s, u, q));
			await credits.InsertAsync(new PersonnelCertificationCredit { PersonnelCertificationId = _certification, DepartmentId = _department, CreditDate = new DateTime(2026, 6, 1), Hours = 2m, AddedOn = DateTime.UtcNow, AddedByUserId = UserB, LearningRecordId = 4242 }, default, true);
			await credits.InsertAsync(new PersonnelCertificationCredit { PersonnelCertificationId = _certification, DepartmentId = _department, CreditDate = new DateTime(2026, 6, 2), Hours = 1m, AddedOn = DateTime.UtcNow, AddedByUserId = UserB }, default, true);
			var derived = (await credits.GetByLearningRecordIdsAsync(_department, new[] { 4242 })).ToList();
			derived.Should().ContainSingle().Which.IsDerived.Should().BeTrue();
			(await credits.GetByCertificationIdAsync(_certification)).Should().HaveCount(2);

			var files = Repository((c, s, u, q) => new LearningRecordFilesRepository(c, s, u, q));
			var file = await files.InsertAsync(new LearningRecordFile { DepartmentId = _department, UserId = UserA, FileName = "cert.pdf", ContentType = "application/pdf", Data = new byte[] { 1, 2, 3 }, UploadedOn = DateTime.UtcNow }, default, true);
			(await files.GetWithDataAsync(_department, file.LearningRecordFileId)).Data.Should().Equal(1, 2, 3);
			(await files.GetWithDataAsync(_otherDepartment, file.LearningRecordFileId)).Should().BeNull("evidence is read inside its department only");

			var category = (await Repository((c, s, u, q) => new TrainingCategoriesRepository(c, s, u, q)).GetForDepartmentAsync(_department, false)).Single();
			var mappings = Repository((c, s, u, q) => new CategoryCreditMappingsRepository(c, s, u, q));
			await mappings.SaveOrUpdateAsync(new CategoryCreditMapping { DepartmentId = _department, DepartmentCertificationTypeId = _certificationType, CategoryId = category.TrainingCategoryId, IsEnabled = true, EnabledOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow }, default, true);
			(await mappings.GetForCertificationTypeAsync(_department, _certificationType)).Should().ContainSingle();

			var templates = Repository((c, s, u, q) => new LmsCertificateTemplatesRepository(c, s, u, q));
			await templates.SaveOrUpdateAsync(new LmsCertificateTemplate { DepartmentId = _department, Name = "Gold", Body = "<p>{{member}}</p>", IsDefault = true, CreatedOn = DateTime.UtcNow }, default, true);
			(await templates.GetForDepartmentAsync(_department)).Should().ContainSingle(t => t.IsDefault);
		}

		[Test, Order(6)]
		public async Task A_backfill_job_is_claimed_once_resumes_and_a_stale_run_is_reclaimed()
		{
			var backfills = Repository((c, s, u, q) => new LearningRecordBackfillsRepository(c, s, u, q));
			var job = await backfills.InsertAsync(new LearningRecordBackfill { DepartmentId = _department, State = (int)LearningRecordBackfillStates.Queued, RequestedOn = DateTime.UtcNow }, default, true);

			var now = DateTime.UtcNow;
			(await backfills.GetRunnableAsync(now.AddMinutes(-30), 5)).Should().Contain(j => j.LearningRecordBackfillId == job.LearningRecordBackfillId);
			(await backfills.ClaimAsync(job.LearningRecordBackfillId, (int)LearningRecordBackfillStates.Queued, null, now)).Should().BeTrue();
			(await backfills.ClaimAsync(job.LearningRecordBackfillId, (int)LearningRecordBackfillStates.Queued, null, now)).Should().BeFalse("a second worker never runs the same job");
			(await backfills.GetRunnableAsync(now.AddMinutes(-30), 5)).Should().NotContain(j => j.LearningRecordBackfillId == job.LearningRecordBackfillId, "a fresh run is not stale");

			var running = await backfills.GetLatestForDepartmentAsync(_department);
			running.State.Should().Be((int)LearningRecordBackfillStates.Running);
			running.Attempts.Should().Be(1);
			running.ResumeAfter = "logs:17";
			running.RecordsWritten = 5;
			(await backfills.SaveProgressAsync(running)).Should().Be(1);
			(await backfills.GetRunnableAsync(now.AddMinutes(30), 5)).Should().Contain(j => j.LearningRecordBackfillId == job.LearningRecordBackfillId && j.ResumeAfter == "logs:17",
				"a run whose worker stopped is picked up again once it is stale");
			(await backfills.ClaimAsync(job.LearningRecordBackfillId, (int)LearningRecordBackfillStates.Running, running.StartedOn, now.AddMinutes(31))).Should().BeTrue();
		}

		[Test, Order(7)]
		public async Task Department_cleanup_removes_the_new_tables_for_that_department_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync($"UPDATE {T("Trainings")} SET {T("CategoryId")} = (SELECT MIN({T("TrainingCategoryId")}) FROM {T("TrainingCategories")} WHERE {T("DepartmentId")} = @D) WHERE {T("TrainingId")} = @Id",
					new { D = _department, Id = _training });
				var other = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
				var theirs = Record(UserB, "log:99");
				theirs.DepartmentId = _otherDepartment;
				await other.InsertIfAbsentAsync(theirs);

				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "LearningRecords", "LearningRecordFiles", "LearningRecordBackfills", "CategoryCreditMappings", "LmsCertificateTemplates", "TrainingCategories" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0, table);

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LearningRecords")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1, "another department's ledger is untouched");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("TrainingCategories")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("Trainings")} WHERE {T("TrainingId")} = @Id AND {T("CategoryId")} IS NULL", new { Id = _training })).Should().Be(1,
				"removing a category sets the training's category to null (M0349)");
		}
	}
}
