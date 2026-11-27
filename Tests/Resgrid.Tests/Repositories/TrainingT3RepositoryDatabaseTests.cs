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
	/// Real-database proof for Training T3 (learning requirements, enforcement, learning paths) on both engines: M0364-M0366 re-run as
	/// no-ops, rolled back and applied again; the one-open-row keys (requirement members, enforcement actions), the notice and assignment
	/// claims, the per-day claim of worker 93 (insert and update paths, release), the due-work union, the fact projections over LMS
	/// enrollments and the T1 ledger (training references, category hours, case-insensitive skill codes), system paths and step replacement,
	/// the A1 link and the completion stamp, and the department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class TrainingT3RepositoryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "training_t3_";
		private const string UserA = "7b2e0c74-8f5a-4f7d-9e3c-3d4e5f6a7b82";
		private const string UserB = "8c3f1d85-9a6b-4a8e-8f4d-4e5f6a7b8c93";
		private const int Department = 4301;
		private const int OtherDepartment = 4302;

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;
		private LmsRequirement _requirement;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepo Repository<TRepo>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepo> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
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

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private static readonly string[] T3Tables =
		{
			"LmsRequirements", "LmsRequirementAudiences", "LmsRequirementMembers", "LmsPaths", "LmsPathSteps", "LmsRequirementEnforcements", "LmsRequirementDepartmentStates"
		};

		private Task<int> CountT3TablesAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(@Names)"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN @Names",
			new { Names = IsPostgres ? T3Tables.Select(t => t.ToLowerInvariant()).ToArray() : T3Tables });

		private Task<int> CountPathColumnsAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'lmspathassignments' AND column_name = ANY(@Names)"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LmsPathAssignments' AND COLUMN_NAME IN @Names",
			new { Names = IsPostgres ? new[] { "lmspathid", "completedon", "personnelcertificationid", "lmsrequirementid" } : new[] { "LmsPathId", "CompletedOn", "PersonnelCertificationId", "LmsRequirementId" } });

		[Test, Order(1)]
		public async Task T3_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			(await CountT3TablesAsync()).Should().Be(T3Tables.Length);
			(await CountPathColumnsAsync()).Should().Be(4);
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 364 AND {versionColumn} <= 366");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await CountT3TablesAsync()).Should().Be(T3Tables.Length, "Up over the existing schema is a no-op");

				migrations.MigrateDown(363);
				(await CountT3TablesAsync()).Should().Be(0, "Down removes every T3 table");
				(await CountPathColumnsAsync()).Should().Be(0, "and the path assignment columns");

				migrations.MigrateUp();
			}

			(await CountT3TablesAsync()).Should().Be(T3Tables.Length);
			(await CountPathColumnsAsync()).Should().Be(4);
		}

		[Test, Order(2)]
		public async Task Requirements_audiences_and_one_open_member_row_per_requirement()
		{
			var requirements = Repository((c, s, u, q) => new LmsRequirementsRepository(c, s, u, q));
			var audiences = Repository((c, s, u, q) => new LmsRequirementAudiencesRepository(c, s, u, q));
			var members = Repository((c, s, u, q) => new LmsRequirementMembersRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			_requirement = await requirements.SaveOrUpdateAsync(new LmsRequirement
			{
				DepartmentId = Department, Name = "SCBA refresher", Kind = (int)LmsRequirementKinds.Course, TargetRefId = 31, Recurrence = (int)LmsRequirementRecurrences.RollingMonths,
				IntervalMonths = 12, RequiredHours = 1.25m, CreatedOn = now, CreatedByUserId = UserA
			}, default, true);
			await requirements.SaveOrUpdateAsync(new LmsRequirement { DepartmentId = OtherDepartment, Name = "Theirs", CreatedOn = now, IsActive = false }, default, true);
			var stored = await requirements.GetByIdForDepartmentAsync(Department, _requirement.LmsRequirementId);
			stored.RequiredHours.Should().Be(1.25m);
			stored.IntervalMonths.Should().Be(12);
			(await requirements.GetForDepartmentAsync(Department)).Should().ContainSingle();
			(await requirements.HasLiveAsync(Department)).Should().BeTrue();
			(await requirements.HasLiveAsync(OtherDepartment)).Should().BeFalse("an inactive requirement is no due work");

			await audiences.ReplaceAsync(Department, _requirement.LmsRequirementId, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "5"), new ContentAudienceRule(ContentAudienceKinds.Shift, "9") });
			await audiences.ReplaceAsync(Department, _requirement.LmsRequirementId, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "6") });
			(await audiences.GetForDepartmentAsync(Department)).Select(a => a.RefId).Should().Equal("6");

			var (row, created) = await members.InsertOpenAsync(new LmsRequirementMember { DepartmentId = Department, LmsRequirementId = _requirement.LmsRequirementId, UserId = UserA, EnteredOn = now });
			created.Should().BeTrue();
			var (twin, again) = await members.InsertOpenAsync(new LmsRequirementMember { DepartmentId = Department, LmsRequirementId = _requirement.LmsRequirementId, UserId = UserA.ToUpperInvariant(), EnteredOn = now });
			again.Should().BeFalse("one open row per member and requirement, whatever the case of the id");
			twin.LmsRequirementMemberId.Should().Be(row.LmsRequirementMemberId);

			row.State = (int)LmsRequirementStates.AtRisk;
			row.StateChangedOn = now;
			row.DueOn = now.AddDays(5);
			await members.SaveStateAsync(row);
			(await members.GetOpenForUserAsync(Department, UserA.ToUpperInvariant())).Single().State.Should().Be((int)LmsRequirementStates.AtRisk);

			var due = now.AddDays(5);
			(await members.TryClaimNoticeAsync(Department, row.LmsRequirementMemberId, false, due)).Should().BeTrue();
			(await members.TryClaimNoticeAsync(Department, row.LmsRequirementMemberId, false, due)).Should().BeFalse("one at-risk notice per due date");
			(await members.TryClaimNoticeAsync(Department, row.LmsRequirementMemberId, true, due)).Should().BeTrue("the overdue notice is claimed apart");
			(await members.TryClaimNoticeAsync(Department, row.LmsRequirementMemberId, false, due.AddMonths(12))).Should().BeTrue("a new due date notifies again");
			(await members.TryClaimAssignmentAsync(Department, row.LmsRequirementMemberId, due)).Should().BeTrue();
			(await members.TryClaimAssignmentAsync(Department, row.LmsRequirementMemberId, due)).Should().BeFalse();

			row.ExemptOn = now;
			row.ExemptReason = "Military leave";
			await members.SaveExemptionAsync(row);
			(await members.GetByIdForDepartmentAsync(Department, row.LmsRequirementMemberId)).ExemptReason.Should().Be("Military leave");

			(await members.MarkLeftAsync(Department, new[] { row.LmsRequirementMemberId }, now)).Should().Be(1);
			(await members.GetOpenForDepartmentAsync(Department)).Should().BeEmpty();
			var (back, reentered) = await members.InsertOpenAsync(new LmsRequirementMember { DepartmentId = Department, LmsRequirementId = _requirement.LmsRequirementId, UserId = UserA, EnteredOn = now.AddDays(1) });
			reentered.Should().BeTrue("re-entering the audience starts a new row");
			back.LmsRequirementMemberId.Should().NotBe(row.LmsRequirementMemberId);
		}

		[Test, Order(3)]
		public async Task One_open_enforcement_action_per_requirement_and_member_undone_once()
		{
			var enforcements = Repository((c, s, u, q) => new LmsRequirementEnforcementsRepository(c, s, u, q));
			var requirements = Repository((c, s, u, q) => new LmsRequirementsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			LmsRequirementEnforcement Action() => new LmsRequirementEnforcement
			{
				DepartmentId = Department, LmsRequirementId = _requirement.LmsRequirementId, LmsRequirementMemberId = 1, UserId = UserB, PersonnelCertificationId = 77,
				DepartmentCertificationTypeId = 3, PreviousStatus = 0, PreviousExpiresOn = now.AddMonths(4), DueOn = now.AddDays(-20), ActedOn = now, RolesJson = "[{\"PersonnelRoleId\":5}]"
			};

			var first = Action();
			(await enforcements.TryInsertOpenAsync(first)).Should().BeTrue();
			(await enforcements.TryInsertOpenAsync(Action())).Should().BeFalse("the filtered unique index keeps one open action");
			(await enforcements.GetDepartmentIdsWithOpenAsync()).Should().Contain(Department);
			(await requirements.GetDepartmentIdsWithDueWorkAsync()).Should().Contain(Department).And.NotContain(OtherDepartment);

			(await enforcements.TryMarkUndoneAsync(Department, first.LmsRequirementEnforcementId, LmsEnforcementUndoReasons.Completed, "[5]", now)).Should().BeTrue();
			(await enforcements.TryMarkUndoneAsync(Department, first.LmsRequirementEnforcementId, LmsEnforcementUndoReasons.Manual, "[]", now)).Should().BeFalse("undone once");
			(await enforcements.GetOpenForDepartmentAsync(Department)).Should().BeEmpty();
			var history = await enforcements.GetRecentForDepartmentAsync(Department, 10);
			history.Single().UndoReason.Should().Be((int)LmsEnforcementUndoReasons.Completed);
			history.Single().RestoredRolesJson.Should().Be("[5]");
			(await enforcements.TryInsertOpenAsync(Action())).Should().BeTrue("after the undo a new action may open");
		}

		[Test, Order(4)]
		public async Task Worker_93_claims_a_local_day_once_and_can_give_it_back()
		{
			var states = Repository((c, s, u, q) => new LmsRequirementDepartmentStatesRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var today = now.Date;

			(await states.TryClaimDayAsync(Department, today, now)).Should().BeTrue("the first pass inserts the claimed row");
			(await states.TryClaimDayAsync(Department, today, now)).Should().BeFalse();
			await states.ReleaseDayAsync(Department, today);
			(await states.TryClaimDayAsync(Department, today, now)).Should().BeTrue("a released day is claimed again");
			(await states.TryClaimDayAsync(Department, today.AddDays(1), now)).Should().BeTrue("the next day is claimed by update");
			await states.StampRefusedAsync(Department, now);
			(await states.GetForDepartmentAsync(Department)).LastRefusedOn.Should().NotBeNull();
		}

		[Test, Order(5)]
		public async Task Facts_come_from_enrollments_and_the_ledger()
		{
			var enrollments = Repository((c, s, u, q) => new LmsEnrollmentsRepository(c, s, u, q));
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var facts = Repository((c, s, u, q) => new LmsRequirementFactsRepository(c, s, u, q));
			var courses = Repository((c, s, u, q) => new LmsCoursesRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsCourseVersionsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var course = await courses.InsertAsync(new LmsCourse { DepartmentId = Department, Title = "SCBA refresher", Status = (int)ProtocolStatuses.Published, CreatedOn = now }, default, true);
			var version = await versions.InsertAsync(new LmsCourseVersion
			{
				DepartmentId = Department, LmsCourseId = course.LmsCourseId, Major = 1, Status = (int)ProtocolVersionStatuses.Published, Title = "SCBA refresher", CreatedOn = now
			}, default, true);
			await enrollments.InsertAsync(new LmsEnrollment
			{
				DepartmentId = Department, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = version.LmsCourseVersionId, UserId = UserA, AssignedOn = now.AddDays(-3),
				Status = (int)LmsEnrollmentStatuses.Completed, IsOpen = false, CompletedOn = now.AddDays(-1), LearningRecordId = 900
			}, default, true);
			await enrollments.InsertAsync(new LmsEnrollment
			{
				DepartmentId = Department, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = version.LmsCourseVersionId, UserId = UserB, AssignedOn = now, IsOpen = true
			}, default, true);

			LearningRecord Record(int kind, string sourceRef, string code = null, int? category = null, decimal hours = 0m, DateTime? on = null) => new LearningRecord
			{
				DepartmentId = Department, UserId = UserA, SourceKind = kind, SourceRefId = sourceRef, Title = "x", CourseCode = code, CategoryId = category, Hours = hours,
				CompletedOn = on ?? now.AddDays(-2), Status = (int)LearningRecordStatuses.Verified
			};
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.CourseCompletion, "training:12:0"));
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.CourseCompletion, "training:13:1"));
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.TrainingRecord, "log:12", category: 4, hours: 2.5m));
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.TrainingRecord, "log:13", category: 4, hours: 3m, on: now.AddYears(-2)));
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.SkillEvaluation, "eval:1", code: "INT-1"));
			await records.InsertIfAbsentAsync(Record((int)LearningRecordSourceKinds.SkillEvaluation, "eval:2", code: "int-1"));
			var rejected = Record((int)LearningRecordSourceKinds.SkillEvaluation, "eval:3", code: "INT-1");
			rejected.Status = (int)LearningRecordStatuses.Rejected;
			await records.InsertIfAbsentAsync(rejected);

			var completions = await facts.GetCourseCompletionsAsync(Department, new[] { course.LmsCourseId });
			completions.Should().ContainSingle().Which.LearningRecordId.Should().Be(900, "only completed enrollments count");
			completions.Single().RefId.Should().Be(course.LmsCourseId);
			(await facts.GetCourseCompletionsAsync(OtherDepartment, new[] { course.LmsCourseId })).Should().BeEmpty();

			var trainings = await facts.GetTrainingCompletionsAsync(Department, new[] { 12 });
			trainings.Should().ContainSingle().Which.RefId.Should().Be(12);

			var hours = await facts.GetCategoryHoursAsync(Department, new[] { 4 }, now.AddYears(-1));
			hours.Should().ContainSingle().Which.Amount.Should().Be(2.5m);

			var evaluations = await facts.GetEvaluationsAsync(Department, new[] { "Int-1" });
			evaluations.Should().HaveCount(2, "skill codes match without regard to case; a rejected evaluation never counts");
		}

		[Test, Order(6)]
		public async Task System_paths_are_unique_by_key_steps_replace_and_A1_assignments_link_and_complete()
		{
			var paths = Repository((c, s, u, q) => new LmsPathsRepository(c, s, u, q));
			var steps = Repository((c, s, u, q) => new LmsPathStepsRepository(c, s, u, q));
			var assignments = Repository((c, s, u, q) => new LmsPathAssignmentsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var system = await paths.InsertAsync(new LmsPath { DepartmentId = 0, SystemKey = "academy:path:dispatcher", Name = "dispatcher", CreatedOn = now }, default, true);
			Func<Task> duplicate = () => paths.InsertAsync(new LmsPath { DepartmentId = 0, SystemKey = "academy:path:dispatcher", Name = "again", CreatedOn = now }, default, true);
			await duplicate.Should().ThrowAsync<DbException>("the Academy paths are seeded by key");
			var mine = await paths.InsertAsync(new LmsPath { DepartmentId = Department, Name = "Engineer onboarding", CreatedOn = now }, default, true);
			await paths.InsertAsync(new LmsPath { DepartmentId = OtherDepartment, Name = "Theirs", CreatedOn = now }, default, true);

			(await paths.GetVisibleAsync(Department)).Select(p => p.LmsPathId).Should().BeEquivalentTo(new[] { system.LmsPathId, mine.LmsPathId });
			(await paths.GetBySystemKeyAsync("ACADEMY:PATH:DISPATCHER")).LmsPathId.Should().Be(system.LmsPathId);

			await steps.ReplaceAsync(Department, mine.LmsPathId, new[]
			{
				new LmsPathStep { SortOrder = 1, Kind = 0, TargetRefId = 31 }, new LmsPathStep { SortOrder = 2, Kind = 1, TargetRefId = 12, PrerequisiteOrders = "1", ElectiveGroup = 1, ElectiveRequired = 1 }
			});
			await steps.ReplaceAsync(Department, mine.LmsPathId, new[] { new LmsPathStep { SortOrder = 1, Kind = 1, TargetRefId = 12 } });
			(await steps.GetForPathsAsync(new[] { mine.LmsPathId })).Should().ContainSingle().Which.Kind.Should().Be(1);

			// A1: an ordered-list assignment (PathKey only) in two departments, and one already linked.
			await assignments.InsertAsync(new LmsPathAssignment { DepartmentId = Department, PathKey = "dispatcher", UserId = UserA, AssignedOn = now }, default, true);
			await assignments.InsertAsync(new LmsPathAssignment { DepartmentId = OtherDepartment, PathKey = "DISPATCHER", UserId = UserB, AssignedOn = now }, default, true);
			(await assignments.LinkSystemPathAsync("dispatcher", system.LmsPathId)).Should().Be(2);
			(await assignments.LinkSystemPathAsync("dispatcher", system.LmsPathId)).Should().Be(0, "linking is idempotent");
			var linked = (await assignments.GetForUserAsync(Department, UserA)).Single();
			linked.LmsPathId.Should().Be(system.LmsPathId);

			(await assignments.TryCompleteAsync(Department, linked.LmsPathAssignmentId, now, 55)).Should().BeTrue();
			(await assignments.TryCompleteAsync(Department, linked.LmsPathAssignmentId, now, 56)).Should().BeFalse("a finished path is stamped once");
			(await assignments.GetForUserAsync(Department, UserA)).Single().PersonnelCertificationId.Should().Be(55);
		}

		[Test, Order(7)]
		public async Task Department_cleanup_removes_that_departments_T3_rows_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in T3Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsPaths")} WHERE {T("DepartmentId")} = 0")).Should().Be(1, "the Academy paths are not the department's to delete");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsPaths")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsRequirements")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
