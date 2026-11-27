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
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for round 5F on both engines: M0387 in the full chain, Up again as a no-op, Down and Up; the protocol learning
	/// settings (unique per protocol), worker 86's micro-learning due-work query and weekly clock claim, the weekly questions (one per member
	/// and week, member ids without case on PostgreSQL's citext, answered once), quiz attempts written against their RowVersion, the course
	/// attempts read for the GDPR export without their snapshot, LmsRequirements.AcademyUpdatePolicy's default, and the department cleanup.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsProtocolLearningDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_r5f_";
		private const string UserA = "7b2e0f1c-1a2b-4c3d-8e9f-00000000000a";
		private const string UserB = "7b2e0f1c-1a2b-4c3d-8e9f-00000000000b";

		private DatabaseTypes _previousType;
		private string _previousConnection;
		private string _master, _connection, _database;
		private int _department, _otherDepartment;

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
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery) })
				queries[query] = (IQuery)Activator.CreateInstance(query, configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, new UnitOfWork(connections.Object), new QueryFactory(list.Object));
		}

		private LmsProtocolLearningsRepository Learnings() => Repository((c, s, u, q) => new LmsProtocolLearningsRepository(c, s, u, q));
		private LmsProtocolQuizAttemptsRepository QuizAttempts() => Repository((c, s, u, q) => new LmsProtocolQuizAttemptsRepository(c, s, u, q));
		private LmsMicroLearningRepository Micro() => Repository((c, s, u, q) => new LmsMicroLearningRepository(c, s, u, q));

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

			foreach (var (id, name) in new[] { (UserA, "Ann.Member"), (UserB, "Ben.Member") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "R5F One", Code = "R5FA", ManagingUserId = UserA, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "R5F Two", Code = "R5FB", ManagingUserId = UserB, ShowWelcome = false });
			_department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R5FA'");
			_otherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R5FB'");
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
				parameters.Add(column, value is DateTime d && IsPostgres ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : value);
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
					"bytea" => "'\\x00'::bytea",
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
		public async Task Protocol_learning_settings_are_unique_per_protocol_and_feed_the_micro_learning_due_query()
		{
			var learnings = Learnings();
			var now = DateTime.UtcNow;
			await learnings.SaveOrUpdateAsync(new LmsProtocolLearning
			{
				DepartmentId = _department, DispatchProtocolId = 501, LmsQuestionBankId = 9, QuizRequired = true, QuestionCount = 4, PassMark = 75, Tags = "mayday",
				MicroLearning = true, CreatedOn = now, CreatedByUserId = UserA
			}, default, true);
			await learnings.SaveOrUpdateAsync(new LmsProtocolLearning { DepartmentId = _otherDepartment, DispatchProtocolId = 601, MicroLearning = false, CreatedOn = now }, default, true);

			var stored = await learnings.GetForProtocolAsync(_department, 501);
			stored.Should().Match<LmsProtocolLearning>(l => l.QuizRequired && l.QuestionCount == 4 && l.PassMark == 75 && l.LmsQuestionBankId == 9 && l.GatesAcknowledgment);
			(await learnings.GetForDepartmentAsync(_department)).Should().ContainSingle();

			Func<Task> duplicate = () => learnings.InsertAsync(new LmsProtocolLearning { DepartmentId = _department, DispatchProtocolId = 501, CreatedOn = now }, default, true);
			await duplicate.Should().ThrowAsync<Exception>("one settings row per department and protocol");

			// Due work only: the department with a micro-learning protocol and no clock yet; never the one without micro-learning.
			(await learnings.GetMicroLearningDueDepartmentIdsAsync(now, 50)).Should().Equal(_department);
		}

		[Test, Order(2)]
		public async Task The_weekly_clock_is_claimed_once_and_is_not_due_again_until_it_runs_out()
		{
			var micro = Micro();
			var learnings = Learnings();
			var now = DateTime.UtcNow;
			var next = now.AddDays(5);

			(await micro.TryClaimRunAsync(_department, now, next, "2026-W48")).Should().BeTrue("the first run starts the clock");
			(await micro.TryClaimRunAsync(_department, now, next, "2026-W48")).Should().BeFalse("claimed: not due again this week");
			(await learnings.GetMicroLearningDueDepartmentIdsAsync(now, 50)).Should().BeEmpty();

			var run = await micro.GetRunAsync(_department);
			run.LastWeekKey.Should().Be("2026-W48");
			run.NextRunOn.Should().BeCloseTo(next, TimeSpan.FromSeconds(1));
			run.NextRunOn.Kind.Should().Be(DateTimeKind.Utc);

			var later = next.AddMinutes(1);
			(await learnings.GetMicroLearningDueDepartmentIdsAsync(later, 50)).Should().Equal(_department);
			(await micro.TryClaimRunAsync(_department, later, later.AddDays(7), "2026-W49")).Should().BeTrue();
			(await micro.TryClaimRunAsync(_department, later, later.AddDays(7), "2026-W49")).Should().BeFalse();
		}

		[Test, Order(3)]
		public async Task A_member_gets_one_question_a_week_and_answers_it_once()
		{
			var micro = Micro();
			var item = new LmsMicroLearningItem
			{
				DepartmentId = _department, UserId = UserA, DispatchProtocolId = 501, LmsQuestionId = 41, WeekKey = "2026-W48", SnapshotJson = "{\"PassMark\":100}", SentOn = DateTime.UtcNow
			};
			(await micro.TryInsertItemAsync(item)).Should().BeTrue();
			item.LmsMicroLearningItemId.Should().BeGreaterThan(0);
			(await micro.TryInsertItemAsync(new LmsMicroLearningItem
			{
				DepartmentId = _department, UserId = UserA.ToUpperInvariant(), DispatchProtocolId = 501, LmsQuestionId = 42, WeekKey = "2026-W48", SentOn = DateTime.UtcNow
			})).Should().BeFalse("one question per member and week, whatever the case of the member id");
			(await micro.TryInsertItemAsync(new LmsMicroLearningItem
			{
				DepartmentId = _department, UserId = UserA, DispatchProtocolId = 501, LmsQuestionId = 43, WeekKey = "2026-W49", SentOn = DateTime.UtcNow
			})).Should().BeTrue();

			(await micro.GetForUserAsync(_department, UserA.ToUpperInvariant(), 0)).Should().HaveCount(2);
			(await micro.GetForUserAsync(_department, UserA, 1)).Should().ContainSingle().Which.WeekKey.Should().Be("2026-W49");
			(await micro.GetRecentForDepartmentAsync(_department, DateTime.UtcNow.AddDays(-1))).Select(i => i.LmsQuestionId).Should().BeEquivalentTo(new[] { 41, 43 });

			item.AnsweredOn = DateTime.UtcNow;
			item.AnswersJson = "[\"b\"]";
			item.Correct = true;
			(await micro.TryAnswerAsync(item)).Should().BeTrue();
			item.Correct = false;
			(await micro.TryAnswerAsync(item)).Should().BeFalse("answered once");
			(await micro.GetItemAsync(_department, item.LmsMicroLearningItemId)).Should().Match<LmsMicroLearningItem>(i => i.Correct == true && i.AnsweredOn.HasValue);
			(await micro.GetItemAsync(_otherDepartment, item.LmsMicroLearningItemId)).Should().BeNull();
		}

		[Test, Order(4)]
		public async Task Quiz_attempts_are_read_without_case_and_written_against_their_row_version()
		{
			var attempts = QuizAttempts();
			var attempt = new LmsProtocolQuizAttempt
			{
				DepartmentId = _department, DispatchProtocolId = 501, ProtocolVersionId = 5010, UserId = UserA, LmsQuestionBankId = 9, AttemptNumber = 1,
				Status = (int)LmsProtocolQuizAttemptStatuses.InProgress, StartedOn = DateTime.UtcNow, PassMark = 80, RevisionHash = new string('a', 64), SnapshotJson = "{}"
			};
			await attempts.InsertAsync(attempt, default, true);

			var mine = await attempts.GetForUserVersionAsync(_department, UserA.ToUpperInvariant(), 5010);
			mine.Should().ContainSingle();
			mine[0].StartedOn.Kind.Should().Be(DateTimeKind.Utc);

			var loaded = await attempts.GetByIdForDepartmentAsync(_department, attempt.LmsProtocolQuizAttemptId);
			loaded.Status = (int)LmsProtocolQuizAttemptStatuses.Scored;
			loaded.Percent = 80m;
			loaded.Passed = true;
			loaded.SubmittedOn = DateTime.UtcNow;
			(await attempts.TryUpdateAsync(loaded, loaded.RowVersion)).Should().BeTrue();
			var stale = await attempts.GetByIdForDepartmentAsync(_department, attempt.LmsProtocolQuizAttemptId);
			(await attempts.TryUpdateAsync(attempt, 0)).Should().BeFalse("someone else's write moved the row version");
			stale.Passed.Should().BeTrue();
			stale.Percent.Should().Be(80m);
			(await attempts.GetForUserAsync(_department, UserA)).Should().ContainSingle();
			(await attempts.GetForUserAsync(_otherDepartment, UserA)).Should().BeEmpty();
		}

		[Test, Order(5)]
		public async Task Course_attempts_reach_the_gdpr_export_without_their_snapshot_and_requirements_default_to_notify()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await InsertAsync(database, "LmsCourses", new { DepartmentId = _department, Title = "Hazmat", PublisherKind = 0, Status = 3, CreatedOn = DateTime.UtcNow, SystemKey = "r5f-hazmat" });
				var course = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsCourseId")} FROM {T("LmsCourses")} WHERE {T("SystemKey")} = 'r5f-hazmat'");
				await InsertAsync(database, "LmsCourseVersions", new { DepartmentId = _department, LmsCourseId = course, Major = 1, Minor = 0, Status = 3, Title = "Hazmat", CreatedOn = DateTime.UtcNow });
				var version = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsCourseVersionId")} FROM {T("LmsCourseVersions")} WHERE {T("LmsCourseId")} = @C", new { C = course });
				await InsertAsync(database, "LmsCourseModules", new { DepartmentId = _department, LmsCourseVersionId = version, Title = "Module" });
				var module = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsCourseModuleId")} FROM {T("LmsCourseModules")} WHERE {T("LmsCourseVersionId")} = @V", new { V = version });
				await InsertAsync(database, "LmsLessons", new { DepartmentId = _department, LmsCourseVersionId = version, LmsCourseModuleId = module, Kind = 2, Title = "Check" });
				var lesson = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsLessonId")} FROM {T("LmsLessons")} WHERE {T("LmsCourseModuleId")} = @M", new { M = module });
				await InsertAsync(database, "LmsEnrollments", new { DepartmentId = _department, LmsCourseId = course, LmsCourseVersionId = version, UserId = UserA, AssignedOn = DateTime.UtcNow });
				var enrollment = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsEnrollmentId")} FROM {T("LmsEnrollments")} WHERE {T("LmsCourseId")} = @C", new { C = course });
				await InsertAsync(database, "LmsAssessmentAttempts", new
				{
					DepartmentId = _department, LmsCourseVersionId = version, LmsLessonId = lesson, LmsEnrollmentId = enrollment, UserId = UserA, AttemptNumber = 1, Status = 1,
					StartedOn = DateTime.UtcNow, Percent = 90m, Passed = true, PassMark = 80, SnapshotJson = "{\"Questions\":[{\"CorrectKeys\":[\"SECRET\"]}]}", AnswersJson = "[]", ResultsJson = "[]"
				});
				await InsertAsync(database, "LmsRequirements", new { DepartmentId = _department, Name = "R5F default", Kind = 0, TargetRefId = course, IsActive = true, CreatedOn = DateTime.UtcNow });
			}

			var attempts = Repository((c, s, u, q) => new LmsAssessmentAttemptsRepository(c, s, u, q));
			var exported = await attempts.GetForUserWithoutSnapshotAsync(_department, UserA.ToUpperInvariant());
			exported.Should().ContainSingle();
			exported[0].SnapshotJson.Should().BeNull("the export never reads the drawn questions' key");
			exported[0].Percent.Should().Be(90m);
			(await ScalarAsync($"SELECT {T("AcademyUpdatePolicy")} FROM {T("LmsRequirements")} WHERE {T("Name")} = 'R5F default'")).Should().Be(1, "notify unless the department chose otherwise");
		}

		[Test, Order(6)]
		public async Task The_department_cleanup_clears_the_round_5F_tables_of_that_department_only()
		{
			await Learnings().SaveOrUpdateAsync(new LmsProtocolLearning { DepartmentId = _otherDepartment, DispatchProtocolId = 602, CreatedOn = DateTime.UtcNow }, default, true);
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "LmsProtocolLearnings", "LmsProtocolQuizAttempts", "LmsMicroLearningItems", "LmsMicroLearningRuns" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsProtocolLearnings")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(2);
		}

		[Test, Order(7)]
		public async Task M0387_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 387");
			var tables = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('lmsprotocollearnings', 'lmsprotocolquizattempts', 'lmsmicrolearningitems', 'lmsmicrolearningruns')"
				: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('LmsProtocolLearnings', 'LmsProtocolQuizAttempts', 'LmsMicroLearningItems', 'LmsMicroLearningRuns')";
			var column = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsrequirements' AND column_name = 'academyupdatepolicy'"
				: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LmsRequirements' AND COLUMN_NAME = 'AcademyUpdatePolicy'";

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(tables)).Should().Be(4);
				(await ScalarAsync(column)).Should().Be(1);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsProtocolLearnings")}")).Should().Be(2, "the second Up keeps the rows");

				migrations.MigrateDown(386);
				(await ScalarAsync(tables)).Should().Be(0);
				(await ScalarAsync(column)).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(tables)).Should().Be(4);
			(await ScalarAsync(column)).Should().Be(1);
		}
	}
}
