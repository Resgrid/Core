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
using Resgrid.Model.Academy;
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
	/// Real-database proof for A2/A3 on both engines: M0397 in the full chain, Up again as a no-op, Down and Up; member guidance states (upsert
	/// by department, member and key, member ids without case on PostgreSQL's citext); major lesson updates (recorded once, pending until
	/// completed, the cursor) and the departments that assigned a lesson; and the department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION
	/// / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class AcademyGuidanceDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "academy_a3_";
		private const string UserA = "6a1d0e1c-1a2b-4c3d-8e9f-00000000000a";
		private const string UserB = "6a1d0e1c-1a2b-4c3d-8e9f-00000000000b";

		private DatabaseTypes _previousType;
		private string _previousConnection;
		private string _master, _connection, _database;
		private int _department, _otherDepartment, _course, _version;

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

		private AcademyGuidanceStatesRepository States() => Repository((c, s, u, q) => new AcademyGuidanceStatesRepository(c, s, u, q));

		private AcademyLessonUpdatesRepository Updates() => Repository((c, s, u, q) => new AcademyLessonUpdatesRepository(c, s, u, q));

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

			foreach (var (id, name) in new[] { (UserA, "Ada.Member"), (UserB, "Bo.Member") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "A3 One", Code = "A3A", ManagingUserId = UserA, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "A3 Two", Code = "A3B", ManagingUserId = UserB, ShowWelcome = false });
			_department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'A3A'");
			_otherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'A3B'");

			// A system (Academy) course and version, as the seed writes them (DepartmentId 0).
			await InsertAsync(database, "LmsCourses", new { DepartmentId = 0, Title = "Calls", SystemKey = "academy:05-calls", PublisherKind = 1, Status = 3, CreatedOn = DateTime.UtcNow });
			_course = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsCourseId")} FROM {T("LmsCourses")} WHERE {T("SystemKey")} = 'academy:05-calls'");
			await InsertAsync(database, "LmsCourseVersions", new { DepartmentId = 0, LmsCourseId = _course, Major = 1, Minor = 0, Status = 3, Title = "Calls", CreatedOn = DateTime.UtcNow });
			_version = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsCourseVersionId")} FROM {T("LmsCourseVersions")} WHERE {T("LmsCourseId")} = @C", new { C = _course });
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

		private async Task EnrollAsync(int departmentId, string userId, LmsEnrollmentSources source, bool open)
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await InsertAsync(database, "LmsEnrollments", new
			{
				DepartmentId = departmentId, LmsCourseId = _course, LmsCourseVersionId = _version, UserId = userId, Source = (int)source, AssignedOn = DateTime.UtcNow,
				Status = open ? (int)LmsEnrollmentStatuses.NotStarted : (int)LmsEnrollmentStatuses.Completed, IsOpen = open
			});
		}

		[Test, Order(1)]
		public async Task A_members_guidance_state_is_upserted_per_key_and_read_without_case()
		{
			var states = States();
			await states.UpsertAsync(new AcademyGuidanceState { DepartmentId = _department, UserId = UserA, GuidanceKey = "tour:member", State = 0, Revision = 1, TasksDone = "profile", UpdatedOn = DateTime.UtcNow });
			await states.UpsertAsync(new AcademyGuidanceState { DepartmentId = _department, UserId = UserA.ToUpperInvariant(), GuidanceKey = "tour:member", State = 1, Revision = 1, TasksDone = "apps,profile", UpdatedOn = DateTime.UtcNow });
			await states.UpsertAsync(new AcademyGuidanceState { DepartmentId = _department, UserId = UserA, GuidanceKey = "whatsnew:2026.12", State = 1, Revision = 1, UpdatedOn = DateTime.UtcNow });
			await states.UpsertAsync(new AcademyGuidanceState { DepartmentId = _department, UserId = UserB, GuidanceKey = "tour:member", State = 0, Revision = 1, UpdatedOn = DateTime.UtcNow });

			var mine = await states.GetForUserAsync(_department, UserA.ToUpperInvariant());
			mine.Should().HaveCount(2, "one row per key, whatever the member id's case");
			var tour = mine.Single(s => s.GuidanceKey == "tour:member");
			tour.State.Should().Be(1);
			tour.DoneTasks.Should().BeEquivalentTo(new[] { "apps", "profile" });
			tour.UpdatedOn.Kind.Should().Be(DateTimeKind.Utc);
			(await states.GetForUserAsync(_department, UserB)).Should().ContainSingle();
			(await states.GetForUserAsync(_otherDepartment, UserA)).Should().BeEmpty();
		}

		[Test, Order(2)]
		public async Task A_major_update_is_recorded_once_and_stays_pending_until_completed()
		{
			var updates = Updates();
			var update = new AcademyLessonUpdate { SystemKey = "academy:05-calls", LmsCourseId = _course, LmsCourseVersionId = _version, Revision = 2, Policy = 2, ReassignDueDays = 14, CreatedOn = DateTime.UtcNow };

			(await updates.InsertIfNewAsync(update)).Should().BeTrue();
			(await updates.InsertIfNewAsync(new AcademyLessonUpdate { SystemKey = "academy:05-calls", LmsCourseId = _course, LmsCourseVersionId = _version, Revision = 2, Policy = 1 })).Should().BeFalse();

			var pending = await updates.GetPendingAsync(10);
			pending.Should().ContainSingle().Which.Should().Match<AcademyLessonUpdate>(u => u.Policy == 2 && u.ReassignDueDays == 14 && u.AppliedThroughDepartmentId == null);

			update.AppliedThroughDepartmentId = _department;
			(await updates.SaveProgressAsync(update)).Should().BeTrue();
			(await updates.GetPendingAsync(10)).Single().AppliedThroughDepartmentId.Should().Be(_department);
			update.CompletedOn = DateTime.UtcNow;
			await updates.SaveProgressAsync(update);
			(await updates.GetPendingAsync(10)).Should().BeEmpty();
		}

		private async Task<int> RequirementAsync(int departmentId, LmsRequirementKinds kind, int targetRefId, int policy, bool deleted = false)
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			var name = "Req " + Guid.NewGuid().ToString("N").Substring(0, 8);
			await InsertAsync(database, "LmsRequirements", new
			{
				DepartmentId = departmentId, Name = name, Kind = (int)kind, TargetRefId = targetRefId, Recurrence = 0, DueDays = 21, GraceDays = 0, AtRiskDays = 14,
				IsActive = true, AcademyUpdatePolicy = policy, CreatedOn = DateTime.UtcNow, DeletedOn = deleted ? DateTime.UtcNow : (DateTime?)null
			});
			return await database.ExecuteScalarAsync<int>($"SELECT {T("LmsRequirementId")} FROM {T("LmsRequirements")} WHERE {T("Name")} = @N", new { N = name });
		}

		[Test, Order(3)]
		public async Task Only_departments_that_require_the_lesson_come_back_after_the_cursor()
		{
			// Round 5F: assignments and self-enrolment do not make a lesson required; a live T3 requirement does.
			await EnrollAsync(_department, UserA, LmsEnrollmentSources.RolePath, false);
			await EnrollAsync(_otherDepartment, UserB, LmsEnrollmentSources.Assigned, false);
			var updates = Updates();
			(await updates.GetAssigningDepartmentIdsAsync(_course, 0, 50)).Should().BeEmpty("nobody has a requirement on the lesson yet");

			var direct = await RequirementAsync(_department, LmsRequirementKinds.Course, _course, (int)LmsMajorChangePolicies.Reassign);
			await RequirementAsync(_otherDepartment, LmsRequirementKinds.Course, _course, (int)LmsMajorChangePolicies.Reassign, deleted: true);
			(await updates.GetAssigningDepartmentIdsAsync(_course, 0, 50)).Should().Equal(_department);
			(await updates.GetAssigningDepartmentIdsAsync(_course, _department, 50)).Should().BeEmpty("the cursor skips departments already handled");
			(await updates.GetRequirementsOnLessonAsync(_department, _course)).Should().ContainSingle()
				.Which.Should().Match<AcademyLessonRequirement>(r => r.LmsRequirementId == direct && r.AcademyUpdatePolicy == (int)LmsMajorChangePolicies.Reassign && r.DueDays == 21);

			// A path requirement covers the lessons on its path.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await InsertAsync(database, "LmsPaths", new { DepartmentId = _otherDepartment, Name = "Onboarding path", IsDeleted = false, CreatedOn = DateTime.UtcNow });
				var path = await database.ExecuteScalarAsync<int>($"SELECT {T("LmsPathId")} FROM {T("LmsPaths")} WHERE {T("Name")} = 'Onboarding path'");
				await InsertAsync(database, "LmsPathSteps", new { LmsPathId = path, DepartmentId = _otherDepartment, SortOrder = 1, Kind = (int)LmsPathStepKinds.Course, TargetRefId = _course });
				await RequirementAsync(_otherDepartment, LmsRequirementKinds.Path, path, (int)LmsMajorChangePolicies.Notify);
			}

			(await updates.GetAssigningDepartmentIdsAsync(_course, 0, 50)).Should().Equal(new[] { _department, _otherDepartment }.OrderBy(d => d));
			(await updates.GetAssigningDepartmentIdsAsync(_course, 0, 1)).Should().HaveCount(1);
			(await updates.GetRequirementsOnLessonAsync(_otherDepartment, _course)).Should().ContainSingle()
				.Which.AcademyUpdatePolicy.Should().Be((int)LmsMajorChangePolicies.Notify);
		}

		[Test, Order(4)]
		public async Task The_department_cleanup_clears_its_guidance_and_keeps_the_system_updates()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("AcademyGuidanceStates")} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("AcademyLessonUpdates")}")).Should().Be(1, "lesson updates belong to no department");
		}

		[Test, Order(5)]
		public async Task M0397_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 397");
			var tables = IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('academyguidancestates', 'academylessonupdates')"
				: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('AcademyGuidanceStates', 'AcademyLessonUpdates')";

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(tables)).Should().Be(2);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("AcademyLessonUpdates")}")).Should().Be(1, "the second Up keeps the rows");

				migrations.MigrateDown(396);
				(await ScalarAsync(tables)).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(tables)).Should().Be(2);
		}
	}
}
