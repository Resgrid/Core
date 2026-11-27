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
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for member wellness WL1 (M0372-M0374) on both engines: the four tables, the queries worker 91 relies on (overdue
	/// Ryan White answers and due return-to-duty reviews, each claimed once, the retention candidates), the case-insensitive member lookup,
	/// case numbers unique per department and year, the duty-period IN list, then Up again after the version rows are gone changes nothing,
	/// and Down removes the tables and Up rebuilds them.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MemberHealthRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "wellness_wl1_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private T2 Repository<T2>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T2> build)
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
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private void Migrate(Action<IMigrationRunner> run)
		{
			IMigration[] migrations = IsPostgres
				? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0372_AddExposureIncidentsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0373_AddMemberInjuriesPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0374_AddMedicalClearancesPg() }
				: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0372_AddExposureIncidents(), new Resgrid.Providers.Migrations.Migrations.M0373_AddMemberInjuries(), new Resgrid.Providers.Migrations.Migrations.M0374_AddMedicalClearances() };
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(migrations);
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			run(provider.GetRequiredService<IMigrationRunner>());
		}

		private RmsExposureIncidentsRepository Incidents() => Repository((c, s, u, q) => new RmsExposureIncidentsRepository(c, s, u, q));
		private RmsMemberInjuriesRepository Injuries() => Repository((c, s, u, q) => new RmsMemberInjuriesRepository(c, s, u, q));
		private RmsInjuryDutyPeriodsRepository Periods() => Repository((c, s, u, q) => new RmsInjuryDutyPeriodsRepository(c, s, u, q));
		private RmsMedicalClearancesRepository Clearances() => Repository((c, s, u, q) => new RmsMedicalClearancesRepository(c, s, u, q));

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

			if (IsPostgres)
			{
				await using var database = (NpgsqlConnection)Connect(_connection);
				await database.OpenAsync();
				await database.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
				await database.ReloadTypesAsync();
			}

			Migrate(r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private static RmsExposureIncident Case(int departmentId, string userId, ExposureIncidentStages stage, DateTime occurredOn, string exposureId = null) => new RmsExposureIncident
		{
			RmsExposureIncidentId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			UserId = userId,
			ResponderExposureId = exposureId,
			Route = (int)ExposureIncidentRoutes.Percutaneous,
			OccurredOn = occurredOn,
			ReportedOn = occurredOn,
			Stage = (int)stage,
			MedicalNotes = "Baseline drawn",
			CreatedOn = DateTime.UtcNow,
			ModifiedOn = DateTime.UtcNow,
			RowVersion = 1
		};

		private static RmsMemberInjury Injury(int departmentId, string userId, int year, int number, DateTime occurredOn) => new RmsMemberInjury
		{
			RmsMemberInjuryId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			UserId = userId,
			OccurredOn = occurredOn,
			ReportedOn = occurredOn,
			OshaYear = year,
			OshaCaseNumber = number,
			OshaInjuryType = (int)OshaInjuryTypes.Injury,
			Description = "Fell",
			CreatedOn = DateTime.UtcNow,
			ModifiedOn = DateTime.UtcNow,
			RowVersion = 1
		};

		[Test, Order(1)]
		public async Task Exposure_cases_read_by_member_and_the_overdue_query_claims_once()
		{
			var repository = Incidents();
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

			var overdue = Case(1, "MiXeD-User", ExposureIncidentStages.NotificationRequested, now.AddDays(-4), "exp-1");
			overdue.NotificationRequestedOn = now.AddDays(-3);
			overdue.NotificationDueOn = now.AddDays(-1);
			var notDue = Case(1, "other", ExposureIncidentStages.NotificationRequested, now.AddDays(-1));
			notDue.NotificationDueOn = now.AddHours(10);
			var closed = Case(1, "mixed-user", ExposureIncidentStages.Closed, now.AddDays(-40));
			var ancient = Case(2, "old", ExposureIncidentStages.Closed, now.AddYears(-31));
			foreach (var row in new[] { overdue, notDue, closed, ancient })
				await repository.InsertAsync(row, default, true);

			(await repository.GetForUserAsync(1, "MIXED-USER")).Select(r => r.RmsExposureIncidentId).Should().BeEquivalentTo(new[] { overdue.RmsExposureIncidentId, closed.RmsExposureIncidentId });
			(await repository.GetForDepartmentAsync(1, false)).Should().HaveCount(2, "open only");
			(await repository.GetForDepartmentAsync(1, true)).Should().HaveCount(3);
			(await repository.GetForExposureAsync(1, "exp-1")).Should().ContainSingle();
			(await repository.GetByIdForDepartmentAsync(1, overdue.RmsExposureIncidentId)).MedicalNotes.Should().Be("Baseline drawn");
			(await repository.GetByIdForDepartmentAsync(2, overdue.RmsExposureIncidentId)).Should().BeNull("an id from another department is never read");

			(await repository.GetOverdueNotificationsAsync(now, 50)).Select(r => r.RmsExposureIncidentId).Should().BeEquivalentTo(new[] { overdue.RmsExposureIncidentId });
			(await repository.ClaimOverdueNoticeAsync(1, overdue.RmsExposureIncidentId, now)).Should().BeTrue();
			(await repository.ClaimOverdueNoticeAsync(1, overdue.RmsExposureIncidentId, now)).Should().BeFalse("the claim wins once");
			(await repository.GetOverdueNotificationsAsync(now, 50)).Should().BeEmpty();

			(await repository.GetDepartmentsWithCasesBeforeAsync(now.AddYears(-30), 50)).Should().BeEquivalentTo(new[] { 2 });
			(await repository.GetBeforeAsync(2, now.AddYears(-30), 50)).Should().ContainSingle();
			(await repository.DeleteForDepartmentAsync(2, ancient.RmsExposureIncidentId)).Should().Be(1);
		}

		[Test, Order(2)]
		public async Task Injuries_number_per_year_the_review_query_claims_once_and_periods_read_by_list()
		{
			var injuries = Injuries();
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			var first = Injury(1, "MiXeD-User", 2026, 1, now.AddDays(-20));
			first.ExpectedReturnOn = now.Date.AddDays(-1);
			var second = Injury(1, "other", 2026, 2, now.AddDays(-5));
			second.ExpectedReturnOn = now.Date.AddDays(10);
			var cleared = Injury(1, "other", 2026, 3, now.AddDays(-50));
			cleared.State = (int)MemberInjuryStates.Cleared;
			cleared.ExpectedReturnOn = now.Date.AddDays(-30);
			var old = Injury(3, "old", 2019, 1, new DateTime(2019, 5, 1));
			foreach (var row in new[] { first, second, cleared, old })
				await injuries.InsertAsync(row, default, true);

			var duplicate = Injury(1, "dupe", 2026, 2, now);
			await FluentActions.Awaiting(() => injuries.InsertAsync(duplicate, default, true)).Should().ThrowAsync<Exception>("one case number per department and year");
			(await injuries.GetMaxCaseNumberAsync(1, 2026)).Should().Be(3);
			(await injuries.GetMaxCaseNumberAsync(1, 2025)).Should().Be(0);
			(await injuries.GetForUserAsync(1, "mixed-user")).Should().ContainSingle();
			(await injuries.GetOpenForDepartmentAsync(1)).Should().HaveCount(2);
			(await injuries.GetForYearAsync(1, 2026)).Select(i => i.OshaCaseNumber).Should().Equal(1, 2, 3);

			(await injuries.GetDueReturnReviewsAsync(now, 50)).Select(i => i.RmsMemberInjuryId).Should().BeEquivalentTo(new[] { first.RmsMemberInjuryId }, "open, and its expected return passed");
			(await injuries.ClaimReturnReviewAsync(1, first.RmsMemberInjuryId, null, now)).Should().BeTrue();
			(await injuries.ClaimReturnReviewAsync(1, first.RmsMemberInjuryId, null, now)).Should().BeFalse("the claim wins once");
			(await injuries.GetDueReturnReviewsAsync(now, 50)).Should().BeEmpty();
			await injuries.UpdateAsync(Update(await injuries.GetByIdForDepartmentAsync(1, first.RmsMemberInjuryId), i => i.ExpectedReturnOn = now.AddHours(1)), default, true);
			(await injuries.GetDueReturnReviewsAsync(now.AddHours(2), 50)).Should().ContainSingle("a later expected return asks again");
			(await injuries.ClaimReturnReviewAsync(1, first.RmsMemberInjuryId, now, now.AddHours(2))).Should().BeTrue();

			(await injuries.GetDepartmentsWithInjuriesBeforeYearAsync(2021, 50)).Should().BeEquivalentTo(new[] { 3 });
			(await injuries.GetBeforeYearAsync(3, 2021, 50)).Should().ContainSingle();

			var periods = Periods();
			await periods.InsertAsync(new RmsInjuryDutyPeriod { RmsInjuryDutyPeriodId = Guid.NewGuid().ToString(), DepartmentId = 1, InjuryId = first.RmsMemberInjuryId, UserId = "MiXeD-User",
				DutyStatus = (int)MemberDutyStatuses.OffDuty, StartsOn = now.Date.AddDays(-19), EndsOn = now.Date.AddDays(-10), CreatedOn = now }, default, true);
			await periods.InsertAsync(new RmsInjuryDutyPeriod { RmsInjuryDutyPeriodId = Guid.NewGuid().ToString(), DepartmentId = 1, InjuryId = first.RmsMemberInjuryId, UserId = "MiXeD-User",
				DutyStatus = (int)MemberDutyStatuses.RestrictedDuty, StartsOn = now.Date.AddDays(-10), CreatedOn = now }, default, true);
			await periods.InsertAsync(new RmsInjuryDutyPeriod { RmsInjuryDutyPeriodId = Guid.NewGuid().ToString(), DepartmentId = 1, InjuryId = second.RmsMemberInjuryId, UserId = "other",
				DutyStatus = (int)MemberDutyStatuses.OffDuty, StartsOn = now.Date.AddDays(-4), CreatedOn = now }, default, true);
			(await periods.GetForInjuryAsync(1, first.RmsMemberInjuryId)).Select(p => p.DutyStatus).Should().Equal((int)MemberDutyStatuses.OffDuty, (int)MemberDutyStatuses.RestrictedDuty);
			(await periods.GetForInjuriesAsync(1, new[] { first.RmsMemberInjuryId, second.RmsMemberInjuryId })).Should().HaveCount(3, "the IN list binds on both engines");
			(await periods.GetForInjuriesAsync(1, Array.Empty<string>())).Should().BeEmpty();
			(await periods.DeleteForInjuryAsync(1, first.RmsMemberInjuryId)).Should().Be(2);
			(await injuries.DeleteForDepartmentAsync(3, old.RmsMemberInjuryId)).Should().Be(1);
		}

		private static RmsMemberInjury Update(RmsMemberInjury row, Action<RmsMemberInjury> change)
		{
			change(row);
			return row;
		}

		[Test, Order(3)]
		public async Task Clearances_read_the_active_ones_and_the_retention_candidates()
		{
			var repository = Clearances();
			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			RmsMedicalClearance Clearance(int department, string user, DateTime evaluated, bool voided) => new RmsMedicalClearance
			{
				RmsMedicalClearanceId = Guid.NewGuid().ToString(), DepartmentId = department, UserId = user, Status = (int)MedicalClearanceStatuses.ClearedWithRestrictions,
				EvaluatedOn = evaluated, ValidUntil = evaluated.AddYears(1), RestrictionDetail = "No SCBA", VoidedOn = voided ? now : (DateTime?)null,
				CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
			var current = Clearance(1, "MiXeD-User", now.Date.AddDays(-10), false);
			var voided = Clearance(1, "mixed-user", now.Date.AddDays(-5), true);
			var ancient = Clearance(4, "old", now.AddYears(-31), false);
			foreach (var row in new[] { current, voided, ancient })
				await repository.InsertAsync(row, default, true);

			(await repository.GetActiveForDepartmentAsync(1)).Select(c => c.RmsMedicalClearanceId).Should().BeEquivalentTo(new[] { current.RmsMedicalClearanceId });
			(await repository.GetForUserAsync(1, "MIXED-USER")).Should().HaveCount(2);
			(await repository.GetDepartmentsWithClearancesBeforeAsync(now.AddYears(-30), 50)).Should().BeEquivalentTo(new[] { 4 });
			(await repository.GetBeforeAsync(4, now.AddYears(-30), 50)).Should().ContainSingle();
			(await repository.DeleteForDepartmentAsync(4, ancient.RmsMedicalClearanceId)).Should().Be(1);
		}

		private const string VersionDelete = "DELETE FROM {0} WHERE {1} IN (394, 395, 396)";

		[Test, Order(4)]
		public async Task Up_again_after_the_version_rows_are_gone_changes_nothing()
		{
			int before;
			await using (var database = Connect(_connection))
			{
				before = await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM rmsmemberinjuries" : "SELECT COUNT(*) FROM [RmsMemberInjuries]");
				// FluentMigrator's version table keeps its quoted mixed-case name on PostgreSQL.
				await database.ExecuteAsync(IsPostgres ? string.Format(VersionDelete, "\"VersionInfo\"", "\"Version\"") : string.Format(VersionDelete, "[VersionInfo]", "[Version]"));
			}

			Migrate(r => r.MigrateUp());

			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(IsPostgres ? "SELECT COUNT(*) FROM rmsmemberinjuries" : "SELECT COUNT(*) FROM [RmsMemberInjuries]")).Should().Be(before);
		}

		[Test, Order(5)]
		public async Task Down_removes_the_tables_and_up_rebuilds_them()
		{
			var exists = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('rmsexposureincidents', 'rmsmemberinjuries', 'rmsinjurydutyperiods', 'rmsmedicalclearances')"
				: "SELECT COUNT(*) FROM sys.tables WHERE name IN ('RmsExposureIncidents', 'RmsMemberInjuries', 'RmsInjuryDutyPeriods', 'RmsMedicalClearances')";

			Migrate(r => r.MigrateDown(371));
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(exists)).Should().Be(0);

			Migrate(r => r.MigrateUp());
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(exists)).Should().Be(4);
		}
	}
}
