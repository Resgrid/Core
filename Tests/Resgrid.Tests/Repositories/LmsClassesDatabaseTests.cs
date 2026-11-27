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
	/// Real-database proof for Training T4 part 1 (M0378–M0380) on both engines: the migrations re-run as no-ops, roll back and apply again;
	/// sessions, rosters (one row per member, case-insensitive), the reminder claim and the waitlist promotion run once; the worker 95
	/// due-work queries; the purge refuses a roster inside its retention (the guard is in the statement) and removes the children after it;
	/// resource bookings never overlap; lesson-plan versions move only from the expected status; department cleanup removes the new tables
	/// for that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level
	/// connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsClassesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_classes_";
		private const string UserA = "5c2e7a11-1f3b-4d2c-9a6e-0b1c2d3e4f51";
		private const string UserB = "6d3f8b22-2a4c-4e3d-8b7f-1c2d3e4f5a62";
		private const int Department = 4401;
		private const int OtherDepartment = 4402;

		private static readonly string[] Tables =
		{
			"LmsClassSessions", "LmsClassEnrollments", "LmsClassInstructors", "LmsLessonPlans", "LmsLessonPlanVersions", "LmsClassCostLines", "LmsTrainingResources",
			"LmsClassResourceBookings"
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

		private static LmsClassSession NewSession(int department, DateTime start, string title = "BBP refresher") => new LmsClassSession
		{
			DepartmentId = department, CalendarItemId = 77, Title = title, StartsOn = start, EndsOn = start.AddHours(2), Capacity = 2, WaitlistEnabled = true, EnrollmentOpen = true,
			ReminderHours = 24, QrEnabled = true, QrSecret = LmsCrewCodes.NewSecret(), GpsLatitude = 39.739236m, GpsLongitude = -104.990251m, CreatedOn = DateTime.UtcNow
		};

		[Test, Order(1)]
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 378 AND {versionColumn} <= 380");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");

				migrations.MigrateDown(377);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task Sessions_round_trip_and_the_roster_keeps_one_row_per_member()
		{
			var sessions = Repository((c, s, u, q) => new LmsClassSessionsRepository(c, s, u, q));
			var enrollments = Repository((c, s, u, q) => new LmsClassEnrollmentsRepository(c, s, u, q));
			var instructors = Repository((c, s, u, q) => new LmsClassInstructorsRepository(c, s, u, q));
			var start = new DateTime(2027, 6, 1, 9, 0, 0);

			var session = await sessions.InsertAsync(NewSession(Department, start), default, true);
			session.SeriesKey = "Series-A";
			session.Tags = "osha, bbp";
			await sessions.SaveOrUpdateAsync(session, default, true);
			await sessions.InsertAsync(NewSession(Department, start.AddDays(7)), default, true);
			await sessions.InsertAsync(NewSession(OtherDepartment, start), default, true);

			var stored = await sessions.GetByIdForDepartmentAsync(Department, session.LmsClassSessionId);
			stored.GpsLatitude.Should().Be(39.739236m);
			stored.Tags.Should().Be("osha, bbp");
			(await sessions.GetByIdForDepartmentAsync(OtherDepartment, session.LmsClassSessionId)).Should().BeNull("department scoped");
			(await sessions.GetForDepartmentAsync(Department, start.AddDays(-1), start.AddDays(1))).Should().ContainSingle();
			(await sessions.GetForDepartmentAsync(Department, null, null)).Should().HaveCount(2);
			(await sessions.GetBySeriesAsync(Department, "series-a")).Should().ContainSingle("series keys compare case-insensitively");
			(await sessions.GetByCalendarItemAsync(Department, 77)).Should().NotBeNull();

			var a = await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, UserId = UserA, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow }, default, true);
			var b = await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, UserId = UserB, Status = (int)LmsClassEnrollmentStatuses.Waitlisted, WaitlistPosition = 1, EnrolledOn = DateTime.UtcNow }, default, true);
			Func<Task> duplicate = () => enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, UserId = UserA.ToUpperInvariant(), EnrolledOn = DateTime.UtcNow }, default, true);
			if (IsPostgres)
				await duplicate.Should().ThrowAsync<Exception>("one roster row per member and session, whatever the case");
			else
				await duplicate.Should().ThrowAsync<Exception>("one roster row per member and session");

			(await enrollments.GetForMemberAsync(Department, session.LmsClassSessionId, UserA.ToUpperInvariant())).Should().NotBeNull("member ids compare case-insensitively");
			(await enrollments.GetForUserAsync(Department, UserB.ToUpperInvariant())).Should().ContainSingle();
			(await enrollments.GetForSessionsAsync(Department, new[] { session.LmsClassSessionId, 999 })).Should().HaveCount(2);

			(await enrollments.ClaimReminderAsync(Department, a.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeTrue();
			(await enrollments.ClaimReminderAsync(Department, a.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeFalse("a reminder is claimed once");
			(await enrollments.PromoteAsync(Department, a.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeFalse("only a waitlisted row is promoted");
			(await enrollments.PromoteAsync(Department, b.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeTrue();
			(await enrollments.PromoteAsync(Department, b.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeFalse("promoted once");
			var promoted = await enrollments.GetByIdForDepartmentAsync(Department, b.LmsClassEnrollmentId);
			promoted.Status.Should().Be((int)LmsClassEnrollmentStatuses.Enrolled);
			promoted.WaitlistPosition.Should().BeNull();

			await instructors.InsertAsync(new LmsClassInstructor { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, UserId = UserA, Name = "Lead", HoursTaught = 1.5m, CreatedOn = DateTime.UtcNow }, default, true);
			await instructors.InsertAsync(new LmsClassInstructor { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, Name = "Outside RN", Qualifications = "RN", SortOrder = 1, CreatedOn = DateTime.UtcNow }, default, true);
			(await instructors.GetForSessionAsync(Department, session.LmsClassSessionId)).Select(i => i.Name).Should().Equal("Lead", "Outside RN");
			(await instructors.GetForUserAsync(Department, UserA.ToUpperInvariant())).Single().HoursTaught.Should().Be(1.5m);
		}

		[Test, Order(3)]
		public async Task Worker_95_finds_due_reminders_waitlists_and_purges_only()
		{
			var sessions = Repository((c, s, u, q) => new LmsClassSessionsRepository(c, s, u, q));
			var enrollments = Repository((c, s, u, q) => new LmsClassEnrollmentsRepository(c, s, u, q));
			var now = new DateTime(2030, 1, 10, 12, 0, 0, DateTimeKind.Utc);

			var soon = await sessions.InsertAsync(NewSession(Department, now.AddHours(10), "Soon"), default, true);
			var far = await sessions.InsertAsync(NewSession(Department, now.AddDays(30), "Far"), default, true);
			var past = await sessions.InsertAsync(NewSession(Department, now.AddHours(-5), "Past"), default, true);
			foreach (var s in new[] { soon, far, past })
				await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = s.LmsClassSessionId, UserId = UserA, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = now }, default, true);
			await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = far.LmsClassSessionId, UserId = UserB, Status = (int)LmsClassEnrollmentStatuses.Waitlisted, WaitlistPosition = 1, EnrolledOn = now }, default, true);

			(await sessions.GetReminderCandidatesAsync(now, now.AddDays(14), 100)).Select(s => s.Title).Should().Equal("Soon");
			(await sessions.GetWaitlistCandidatesAsync(now, 100)).Select(s => s.Title).Should().Equal("Far");

			var deleted = NewSession(Department, now.AddYears(-5), "Old");
			deleted.IsDeleted = true;
			deleted.DeletedOn = now.AddYears(-4);
			deleted.RetainUntil = now.AddYears(-2);
			await sessions.InsertAsync(deleted, default, true);
			var held = NewSession(Department, now.AddYears(-1), "Held");
			held.IsDeleted = true;
			held.DeletedOn = now.AddMonths(-6);
			held.RetainUntil = now.AddYears(2);
			await sessions.InsertAsync(held, default, true);
			(await sessions.GetPurgeCandidatesAsync(now, now.AddDays(-30), 100)).Select(s => s.Title).Should().Equal("Old");
		}

		[Test, Order(4)]
		public async Task The_purge_refuses_a_roster_inside_its_retention_and_removes_the_children_after_it()
		{
			var sessions = Repository((c, s, u, q) => new LmsClassSessionsRepository(c, s, u, q));
			var enrollments = Repository((c, s, u, q) => new LmsClassEnrollmentsRepository(c, s, u, q));
			var costs = Repository((c, s, u, q) => new LmsClassCostLinesRepository(c, s, u, q));
			var now = new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			var session = NewSession(Department, now.AddYears(-1), "Retained");
			session.Status = (int)LmsClassSessionStatuses.SignedOff;
			session.IsDeleted = true;
			session.DeletedOn = now.AddMonths(-11);
			session.RetainUntil = now.AddYears(2);
			session = await sessions.InsertAsync(session, default, true);
			await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, UserId = UserA, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = now }, default, true);
			await costs.InsertAsync(new LmsClassCostLine { DepartmentId = Department, LmsClassSessionId = session.LmsClassSessionId, Kind = 0, Amount = 125.50m, Currency = "USD", CreatedOn = now }, default, true);
			(await costs.GetForSessionsAsync(Department, new[] { session.LmsClassSessionId })).Single().Amount.Should().Be(125.50m);

			(await sessions.PurgeAsync(Department, session.LmsClassSessionId, now)).Should().BeFalse("the retention holds");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsClassEnrollments")} WHERE {T("LmsClassSessionId")} = @S", new { S = session.LmsClassSessionId })).Should().Be(1, "nothing is removed while held");

			(await sessions.PurgeAsync(Department, session.LmsClassSessionId, now.AddYears(3))).Should().BeTrue();
			(await sessions.GetByIdForDepartmentAsync(Department, session.LmsClassSessionId)).Should().BeNull();
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsClassEnrollments")} WHERE {T("LmsClassSessionId")} = @S", new { S = session.LmsClassSessionId })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsClassCostLines")} WHERE {T("LmsClassSessionId")} = @S", new { S = session.LmsClassSessionId })).Should().Be(0);

			var live = await sessions.InsertAsync(NewSession(Department, now, "Live"), default, true);
			(await sessions.PurgeAsync(Department, live.LmsClassSessionId, now.AddYears(10))).Should().BeFalse("a session that was not deleted is never purged");
		}

		[Test, Order(5)]
		public async Task Bookings_never_overlap_and_lesson_plan_versions_move_only_from_the_expected_status()
		{
			var resources = Repository((c, s, u, q) => new LmsTrainingResourcesRepository(c, s, u, q));
			var bookings = Repository((c, s, u, q) => new LmsClassResourceBookingsRepository(c, s, u, q));
			var plans = Repository((c, s, u, q) => new LmsLessonPlansRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsLessonPlanVersionsRepository(c, s, u, q));
			var start = new DateTime(2027, 9, 4, 8, 0, 0);

			var tower = await resources.InsertAsync(new LmsTrainingResource { DepartmentId = Department, Name = "Training tower", Kind = (int)LmsTrainingResourceKinds.Facility, Capacity = 12, CreatedOn = DateTime.UtcNow }, default, true);
			await resources.InsertAsync(new LmsTrainingResource { DepartmentId = Department, Name = "Gone", IsDeleted = true, CreatedOn = DateTime.UtcNow }, default, true);
			(await resources.GetForDepartmentAsync(Department)).Select(r => r.Name).Should().Equal("Training tower");

			await bookings.InsertAsync(new LmsClassResourceBooking { DepartmentId = Department, LmsTrainingResourceId = tower.LmsTrainingResourceId, LmsClassSessionId = 1, StartsOn = start, EndsOn = start.AddHours(3), CreatedOn = DateTime.UtcNow }, default, true);
			(await bookings.HasOverlapAsync(Department, tower.LmsTrainingResourceId, start.AddHours(2), start.AddHours(4), 2)).Should().BeTrue();
			(await bookings.HasOverlapAsync(Department, tower.LmsTrainingResourceId, start.AddHours(3), start.AddHours(4), 2)).Should().BeFalse("back to back is fine");
			(await bookings.HasOverlapAsync(Department, tower.LmsTrainingResourceId, start, start.AddHours(1), 1)).Should().BeFalse("the session's own booking is not a conflict");
			(await bookings.GetForResourceAsync(Department, tower.LmsTrainingResourceId, start.AddDays(-1), start.AddDays(1))).Should().ContainSingle();
			(await bookings.DeleteForSessionAsync(Department, 1)).Should().Be(1);

			var plan = await plans.InsertAsync(new LmsLessonPlan { DepartmentId = Department, Title = "Live fire", StarterKey = "live-fire", CreatedOn = DateTime.UtcNow }, default, true);
			var content = new LmsLessonPlanContent { Objectives = { "Advance a line" } }.ToJson();
			var version = await versions.InsertAsync(new LmsLessonPlanVersion { LmsLessonPlanId = plan.LmsLessonPlanId, DepartmentId = Department, Title = "Live fire", ContentJson = content, CreatedOn = DateTime.UtcNow }, default, true);
			(await versions.MoveAsync(Department, version.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.Draft, (int)ProtocolVersionStatuses.InReview)).Should().BeTrue();
			(await versions.MoveAsync(Department, version.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.Draft, (int)ProtocolVersionStatuses.InReview)).Should().BeFalse("it moved already");
			var stored = (await versions.GetForPlanAsync(Department, plan.LmsLessonPlanId)).Single();
			stored.Status.Should().Be((int)ProtocolVersionStatuses.InReview);
			stored.Content().Objectives.Should().Equal("Advance a line");
			(await plans.GetForDepartmentAsync(Department)).Should().ContainSingle();
		}

		[Test, Order(6)]
		public async Task Department_cleanup_removes_the_class_tables_for_that_department_only()
		{
			var sessions = Repository((c, s, u, q) => new LmsClassSessionsRepository(c, s, u, q));
			await sessions.InsertAsync(NewSession(OtherDepartment, DateTime.UtcNow, "Kept"), default, true);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsClassSessions")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(2);
		}
	}
}
