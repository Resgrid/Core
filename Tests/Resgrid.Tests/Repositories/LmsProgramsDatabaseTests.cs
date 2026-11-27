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
	/// Real-database proof for Training T3 part 2 (M0367–M0370) on both engines: the migrations re-run as no-ops, roll back and apply
	/// again; plans, frameworks, tags, requests (the conditional transition, the null-step path), saved reports (the due-work query and the
	/// run claim), cycle rules, alert claims, worker state and the Records familiarization reads all work; department cleanup removes the
	/// new tables for that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level
	/// connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsProgramsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_programs_";
		private const string UserA = "5c2e7a11-1f3b-4d2c-9a6e-0b1c2d3e4f51";
		private const string UserB = "6d3f8b22-2a4c-4e3d-8b7f-1c2d3e4f5a62";
		private const int Department = 4301;
		private const int OtherDepartment = 4302;

		private static readonly string[] Tables =
		{
			"LmsTrainingPlans", "LmsTrainingPlanItems", "LmsCompetencyFrameworks", "LmsCompetencyItems", "LmsCompetencyAlignments", "LmsTrainingRequests",
			"LmsTrainingRequestDecisions", "LmsReportFolders", "LmsReportDefinitions", "LmsReportRuns", "LmsCertificationCycleRules", "LmsCertificationAlerts", "LmsProgramStates"
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
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 367");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");

				migrations.MigrateDown(366);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task Plans_and_their_lines_round_trip()
		{
			var plans = Repository((c, s, u, q) => new LmsTrainingPlansRepository(c, s, u, q));
			var items = Repository((c, s, u, q) => new LmsTrainingPlanItemsRepository(c, s, u, q));

			var plan = await plans.SaveOrUpdateAsync(new LmsTrainingPlan { DepartmentId = Department, Year = 2027, Name = "Company training", ScopeKind = 1, ScopeId = 12, CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA }, default, true);
			await plans.SaveOrUpdateAsync(new LmsTrainingPlan { DepartmentId = Department, Year = 2026, Name = "Old", IsDeleted = true, CreatedOn = DateTime.UtcNow }, default, true);
			await plans.SaveOrUpdateAsync(new LmsTrainingPlan { DepartmentId = OtherDepartment, Year = 2027, Name = "Theirs", CreatedOn = DateTime.UtcNow }, default, true);
			await items.SaveOrUpdateAsync(new LmsTrainingPlanItem { LmsTrainingPlanId = plan.LmsTrainingPlanId, DepartmentId = Department, Month = 6, CategoryId = 3, Title = "Ventilation", PlannedHours = 2.75m, PlannedSessions = 4, PlannedOn = new DateTime(2027, 6, 14), CreatedOn = DateTime.UtcNow }, default, true);
			await items.SaveOrUpdateAsync(new LmsTrainingPlanItem { LmsTrainingPlanId = plan.LmsTrainingPlanId, DepartmentId = Department, Month = 2, Title = "Hose", PlannedHours = 1m, CreatedOn = DateTime.UtcNow }, default, true);

			(await plans.GetForDepartmentAsync(Department)).Select(p => p.Name).Should().Equal("Company training");
			(await plans.GetForDepartmentAsync(Department, true)).Should().HaveCount(2);
			(await plans.GetByIdForDepartmentAsync(OtherDepartment, plan.LmsTrainingPlanId)).Should().BeNull("department scoped");
			var lines = await items.GetForPlanAsync(Department, plan.LmsTrainingPlanId);
			lines.Select(l => l.Month).Should().Equal(2, 6);
			lines[1].PlannedHours.Should().Be(2.75m);
			lines[1].PlannedSessions.Should().Be(4);
			lines[1].PlannedHoursTotal.Should().Be(11m);
			lines[0].PlannedSessions.Should().Be(1, "the default");
		}

		[Test, Order(3)]
		public async Task Frameworks_items_and_tags_keep_one_tag_per_subject()
		{
			var frameworks = Repository((c, s, u, q) => new LmsCompetencyFrameworksRepository(c, s, u, q));
			var items = Repository((c, s, u, q) => new LmsCompetencyItemsRepository(c, s, u, q));
			var alignments = Repository((c, s, u, q) => new LmsCompetencyAlignmentsRepository(c, s, u, q));

			var framework = await frameworks.SaveOrUpdateAsync(new LmsCompetencyFramework { DepartmentId = Department, Name = "State FF", Source = 1, CaseDocumentId = "ABC-def", CreatedOn = DateTime.UtcNow }, default, true);
			(await frameworks.GetByCaseDocumentIdAsync(Department, "abc-DEF")).Should().NotBeNull("CASE identifiers compare case-insensitively");
			(await frameworks.GetByCaseDocumentIdAsync(OtherDepartment, "ABC-def")).Should().BeNull();

			var parent = await items.SaveOrUpdateAsync(new LmsCompetencyItem { LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId, DepartmentId = Department, Identifier = "i-1", Label = "Level", SortOrder = 1, CreatedOn = DateTime.UtcNow }, default, true);
			var child = await items.SaveOrUpdateAsync(new LmsCompetencyItem { LmsCompetencyFrameworkId = framework.LmsCompetencyFrameworkId, DepartmentId = Department, Identifier = "i-2", HumanCodingScheme = "4.3.6", Label = "Ladders", SortOrder = 2, CreatedOn = DateTime.UtcNow }, default, true);
			await items.SetParentAsync(Department, child.LmsCompetencyItemId, parent.LmsCompetencyItemId);
			(await items.GetByIdForDepartmentAsync(Department, child.LmsCompetencyItemId)).ParentItemId.Should().Be(parent.LmsCompetencyItemId);
			await items.SetParentAsync(Department, child.LmsCompetencyItemId, null);
			(await items.GetByIdForDepartmentAsync(Department, child.LmsCompetencyItemId)).ParentItemId.Should().BeNull();

			var tag = new LmsCompetencyAlignment { DepartmentId = Department, LmsCompetencyItemId = child.LmsCompetencyItemId, SubjectKind = 1, SubjectId = "33", CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA };
			(await alignments.AddAsync(tag)).Should().BeTrue();
			(await alignments.AddAsync(tag)).Should().BeFalse("one tag per item and subject");
			(await alignments.AddAsync(new LmsCompetencyAlignment { DepartmentId = Department, LmsCompetencyItemId = parent.LmsCompetencyItemId, SubjectKind = 1, SubjectId = "33", CreatedOn = DateTime.UtcNow })).Should().BeTrue();
			(await alignments.GetForSubjectAsync(Department, 1, "33")).Should().HaveCount(2);
			var forItems = await alignments.GetForItemsAsync(Department, new[] { child.LmsCompetencyItemId });
			forItems.Should().ContainSingle();
			(await alignments.RemoveAsync(Department, forItems[0].LmsCompetencyAlignmentId)).Should().Be(1);
			(await alignments.GetForDepartmentAsync(Department)).Should().ContainSingle();
		}

		[Test, Order(4)]
		public async Task A_request_moves_only_from_the_state_its_decider_saw()
		{
			var requests = Repository((c, s, u, q) => new LmsTrainingRequestsRepository(c, s, u, q));
			var decisions = Repository((c, s, u, q) => new LmsTrainingRequestDecisionsRepository(c, s, u, q));
			var submitted = new DateTime(2027, 3, 1, 9, 0, 0, DateTimeKind.Utc);

			var request = await requests.SaveOrUpdateAsync(new LmsTrainingRequest
			{
				DepartmentId = Department, UserId = UserA, Title = "Rope rescue", Hours = 16m, CostTuition = 400.5m, Currency = "USD", Status = 0, CurrentStep = 0, Steps = "0,1",
				SubmittedOn = submitted
			}, default, true);
			await requests.SaveOrUpdateAsync(new LmsTrainingRequest { DepartmentId = Department, UserId = UserB, Title = "Old", Status = 2, SubmittedOn = submitted.AddYears(-1) }, default, true);

			(await requests.GetForUserAsync(Department, UserA.ToUpperInvariant())).Should().ContainSingle("member ids compare case-insensitively");
			(await requests.GetOpenAsync(Department)).Should().ContainSingle();
			(await requests.GetForDepartmentAsync(Department, submitted.AddDays(-1), submitted.AddDays(1))).Should().ContainSingle();
			(await requests.GetForDepartmentAsync(Department, null, null)).Should().HaveCount(2);

			request.CurrentStep = 1;
			request.UpdatedOn = DateTime.UtcNow;
			(await requests.TryTransitionAsync(request, 0, 0)).Should().BeTrue();
			(await requests.TryTransitionAsync(request, 0, 0)).Should().BeFalse("the supervisor step was decided already");

			request.Status = 1;
			request.CurrentStep = null;
			request.DecidedOn = DateTime.UtcNow;
			request.DecidedByUserId = UserB;
			(await requests.TryTransitionAsync(request, 0, 1)).Should().BeTrue();

			// From Approved, with no step waiting: the "IS NULL" shape is built in C# (pg-untyped-null-param-is-null).
			request.Status = 4;
			request.LearningRecordId = 88;
			request.CompletedOn = DateTime.UtcNow;
			request.ActualCost = 380.25m;
			(await requests.TryTransitionAsync(request, 1, null)).Should().BeTrue();
			var stored = await requests.GetByIdForDepartmentAsync(Department, request.LmsTrainingRequestId);
			stored.Status.Should().Be(4);
			stored.LearningRecordId.Should().Be(88);
			stored.ActualCost.Should().Be(380.25m);
			stored.CostTuition.Should().Be(400.5m);
			stored.StepList().Should().Equal(LmsTrainingRequestSteps.Supervisor, LmsTrainingRequestSteps.TrainingOfficer);

			await decisions.SaveOrUpdateAsync(new LmsTrainingRequestDecision { LmsTrainingRequestId = request.LmsTrainingRequestId, DepartmentId = Department, Step = 0, Decision = 0, DecidedByUserId = UserB, DecidedOn = DateTime.UtcNow }, default, true);
			(await decisions.GetForRequestAsync(Department, request.LmsTrainingRequestId)).Should().ContainSingle();
		}

		[Test, Order(5)]
		public async Task Due_reports_are_found_by_the_due_work_query_and_claimed_once()
		{
			var folders = Repository((c, s, u, q) => new LmsReportFoldersRepository(c, s, u, q));
			var definitions = Repository((c, s, u, q) => new LmsReportDefinitionsRepository(c, s, u, q));
			var runs = Repository((c, s, u, q) => new LmsReportRunsRepository(c, s, u, q));
			var now = new DateTime(2027, 5, 17, 8, 0, 0, DateTimeKind.Utc);

			var folder = await folders.SaveOrUpdateAsync(new LmsReportFolder { DepartmentId = Department, Name = "ISO", CreatedOn = DateTime.UtcNow }, default, true);
			var due = await definitions.SaveOrUpdateAsync(new LmsReportDefinition
			{
				DepartmentId = Department, LmsReportFolderId = folder.LmsReportFolderId, ReportKey = "iso-fsrs", Name = "Monthly ISO", ParametersJson = "{}", Frequency = 3, DayOfMonth = 1,
				HourLocal = 6, RecipientUserIds = UserA, NextRunUtc = now.AddMinutes(-1), CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA
			}, default, true);
			await definitions.SaveOrUpdateAsync(new LmsReportDefinition { DepartmentId = Department, ReportKey = "iso-fsrs", Name = "Later", Frequency = 1, NextRunUtc = now.AddHours(1), CreatedOn = DateTime.UtcNow }, default, true);
			await definitions.SaveOrUpdateAsync(new LmsReportDefinition { DepartmentId = Department, ReportKey = "iso-fsrs", Name = "Unscheduled", Frequency = 0, NextRunUtc = now.AddDays(-1), CreatedOn = DateTime.UtcNow }, default, true);
			await definitions.SaveOrUpdateAsync(new LmsReportDefinition { DepartmentId = Department, ReportKey = "iso-fsrs", Name = "Deleted", Frequency = 1, IsDeleted = true, NextRunUtc = now.AddDays(-1), CreatedOn = DateTime.UtcNow }, default, true);

			var found = await definitions.GetDueAsync(now, 50);
			found.Select(d => d.Name).Should().Equal("Monthly ISO");

			(await definitions.TryClaimRunAsync(due.LmsReportDefinitionId, found[0].NextRunUtc.Value, now.AddMonths(1), now)).Should().BeTrue();
			(await definitions.TryClaimRunAsync(due.LmsReportDefinitionId, found[0].NextRunUtc.Value, now.AddMonths(1), now)).Should().BeFalse("claimed once");
			(await definitions.GetDueAsync(now, 50)).Should().BeEmpty();
			await definitions.SetLastRunStatusAsync(due.LmsReportDefinitionId, (int)LmsReportRunStatuses.Sent);
			var stored = await definitions.GetByIdForDepartmentAsync(Department, due.LmsReportDefinitionId);
			stored.LastRunStatus.Should().Be((int)LmsReportRunStatuses.Sent);
			stored.LastRunUtc.Should().Be(now);
			stored.Recipients().Should().Equal(UserA);

			await runs.SaveOrUpdateAsync(new LmsReportRun { DepartmentId = Department, LmsReportDefinitionId = due.LmsReportDefinitionId, RanOn = now, Status = 0, Recipients = 1, RowTotal = 12 }, default, true);
			(await runs.GetForDefinitionAsync(Department, due.LmsReportDefinitionId, 5)).Single().RowTotal.Should().Be(12);

			await definitions.ClearFolderAsync(Department, folder.LmsReportFolderId);
			(await definitions.GetByIdForDepartmentAsync(Department, due.LmsReportDefinitionId)).LmsReportFolderId.Should().BeNull();
			(await definitions.GetForDepartmentAsync(Department)).Should().HaveCount(3, "deleted definitions are not listed");
		}

		[Test, Order(6)]
		public async Task Cycle_rules_alert_claims_and_worker_state_are_once_per_key()
		{
			var rules = Repository((c, s, u, q) => new LmsCertificationCycleRulesRepository(c, s, u, q));
			var alerts = Repository((c, s, u, q) => new LmsCertificationAlertsRepository(c, s, u, q));
			var states = Repository((c, s, u, q) => new LmsProgramStatesRepository(c, s, u, q));

			await rules.SaveOrUpdateAsync(new LmsCertificationCycleRule { DepartmentId = Department, DepartmentCertificationTypeId = 3, AutoRenewMode = 1, NamedRecipientUserIds = UserA, AlertsFrom = DateTime.UtcNow, CreatedOn = DateTime.UtcNow }, default, true);
			Func<Task> duplicate = () => rules.SaveOrUpdateAsync(new LmsCertificationCycleRule { DepartmentId = Department, DepartmentCertificationTypeId = 3, AlertsFrom = DateTime.UtcNow, CreatedOn = DateTime.UtcNow }, default, true);
			await duplicate.Should().ThrowAsync<Exception>("one rule per certification type");
			(await rules.GetForDepartmentAsync(Department)).Single().NamedRecipients().Should().Equal(UserA);
			(await rules.GetDepartmentIdsAsync()).Should().Contain(Department);

			var claim = new LmsCertificationAlert { DepartmentId = Department, PersonnelCertificationId = 55, Kind = 2, AlertKey = "20270531:30", SentOn = DateTime.UtcNow };
			(await alerts.TryClaimAsync(claim)).Should().BeTrue();
			claim.LmsCertificationAlertId.Should().BeGreaterThan(0);
			(await alerts.TryClaimAsync(new LmsCertificationAlert { DepartmentId = Department, PersonnelCertificationId = 55, Kind = 2, AlertKey = "20270531:30", SentOn = DateTime.UtcNow })).Should().BeFalse();
			(await alerts.TryClaimAsync(new LmsCertificationAlert { DepartmentId = Department, PersonnelCertificationId = 55, Kind = 2, AlertKey = "20270531:7", SentOn = DateTime.UtcNow })).Should().BeTrue();
			await alerts.SetRecipientsAsync(claim.LmsCertificationAlertId, 3);
			(await ScalarAsync($"SELECT {T("Recipients")} FROM {T("LmsCertificationAlerts")} WHERE {T("LmsCertificationAlertId")} = @Id", new { Id = claim.LmsCertificationAlertId })).Should().Be(3);

			var day = new DateTime(2027, 3, 1);
			(await states.TryClaimCycleSweepAsync(Department, day)).Should().BeTrue();
			(await states.TryClaimCycleSweepAsync(Department, day)).Should().BeFalse("one sweep per department-local day");
			await states.ReleaseCycleSweepAsync(Department, day);
			(await states.TryClaimCycleSweepAsync(Department, day)).Should().BeTrue("a released day can be claimed again");
			(await states.TryClaimCycleSweepAsync(Department, day.AddDays(1))).Should().BeTrue();
			var through = new DateTime(2027, 3, 2, 4, 5, 6, DateTimeKind.Utc);
			await states.SetFamiliarizationScannedThroughAsync(Department, through);
			(await states.GetForDepartmentAsync(Department)).FamiliarizationScannedThrough.Should().Be(through);

			var anyDepartment = await ScalarAsync($"SELECT COALESCE(MIN({T("DepartmentId")}), 0) FROM {T("Departments")}");
			if (anyDepartment > 0)
			{
				await ExecuteAsync($"INSERT INTO {T("DepartmentSettings")} ({T("DepartmentId")}, {T("SettingType")}, {T("Setting")}) VALUES (@D, @Type, 'x')",
					new { D = anyDepartment, Type = (int)DepartmentSettingTypes.LmsProgramSettings });
				(await states.GetDepartmentsWithProgramSettingsAsync()).Should().Contain(anyDepartment);
			}
		}

		[Test, Order(7)]
		public async Task Familiarization_reads_completed_inspections_and_reviews_in_the_window_only()
		{
			var sources = Repository((c, s, u, q) => new LmsFamiliarizationSourceRepository(c, s, u, q));
			var since = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
			var until = since.AddDays(1);
			var inside = since.AddHours(10);

			async Task Inspection(string id, int department, int state, DateTime modified, DateTime? deleted = null) =>
				await ExecuteAsync($"INSERT INTO {T("RmsInspections")} ({T("RmsInspectionId")}, {T("DepartmentId")}, {T("RmsOccupancyId")}, {T("InspectionNumber")}, {T("State")}, " +
					$"{T("StartedOn")}, {T("CompletedOn")}, {T("InspectorUserId")}, {T("CreatedOn")}, {T("ModifiedOn")}, {T("DeletedOn")}) " +
					"VALUES (@Id, @D, 'occ-1', @Number, @State, @Started, @Completed, @Inspector, @Modified, @Modified, @Deleted)",
					new { Id = id, D = department, Number = "INSP-" + id, State = state, Started = modified.AddHours(-2), Completed = modified.AddHours(-1), Inspector = UserA, Modified = modified, Deleted = deleted });

			await Inspection("a", Department, (int)RmsInspectionState.Completed, inside);
			await Inspection("b", Department, (int)RmsInspectionState.Completed, inside.AddMinutes(1), inside);
			await Inspection("c", Department, (int)RmsInspectionState.Scheduled, inside.AddMinutes(2));
			await Inspection("d", Department, (int)RmsInspectionState.Completed, until.AddHours(1));
			await Inspection("e", OtherDepartment, (int)RmsInspectionState.Completed, inside);

			var inspections = await sources.GetInspectionsChangedAsync(Department, since, until, 100);
			inspections.Select(v => v.SourceRefId).Should().Equal("insp:a", "insp:b", "insp:c");
			inspections[0].Withdrawn.Should().BeFalse();
			inspections[0].UserId.Should().Be(UserA);
			inspections[0].Reference.Should().Be("INSP-a");
			inspections[1].Withdrawn.Should().BeTrue("deleted");
			inspections[2].Withdrawn.Should().BeTrue("not completed");
			(await sources.GetInspectionsChangedAsync(Department, since, until, 2)).Should().HaveCount(2, "a page is capped");

			await ExecuteAsync($"INSERT INTO {T("RmsOccupancies")} ({T("RmsOccupancyId")}, {T("DepartmentId")}, {T("OccupancyNumber")}, {T("Name")}, {T("LastReviewedOn")}, {T("ReviewedByUserId")}, {T("CreatedOn")}, {T("ModifiedOn")}) " +
				"VALUES ('occ-1', @D, 'OCC-2027-0001', 'Warehouse', @Reviewed, @User, @Reviewed, @Reviewed)", new { D = Department, Reviewed = inside, User = UserB });
			var reviews = await sources.GetOccupancyReviewsAsync(Department, since, until, 100);
			reviews.Should().ContainSingle().Which.SourceRefId.Should().Be("occ-review:occ-1:20270301");
			reviews[0].UserId.Should().Be(UserB);

			await ExecuteAsync($"INSERT INTO {T("ContactPreplans")} ({T("ContactPreplanId")}, {T("DepartmentId")}, {T("ContactId")}, {T("LastReviewedOn")}, {T("ReviewedByUserId")}, {T("AddedOn")}) " +
				"VALUES ('pp-1', @D, 'contact-1', @Reviewed, @User, @Reviewed)", new { D = Department, Reviewed = inside, User = UserA });
			(await sources.GetPreplanReviewsAsync(Department, since, until, 100)).Should().ContainSingle().Which.SourceRefId.Should().Be("preplan-review:pp-1:20270301");
			(await sources.GetPreplanReviewsAsync(Department, until, until.AddDays(1), 100)).Should().BeEmpty();
		}

		[Test, Order(8)]
		public async Task Department_cleanup_removes_the_program_tables_for_that_department_only()
		{
			var plans = Repository((c, s, u, q) => new LmsTrainingPlansRepository(c, s, u, q));
			await plans.SaveOrUpdateAsync(new LmsTrainingPlan { DepartmentId = OtherDepartment, Year = 2028, Name = "Kept", CreatedOn = DateTime.UtcNow }, default, true);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsTrainingPlans")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(2);
		}
	}
}
