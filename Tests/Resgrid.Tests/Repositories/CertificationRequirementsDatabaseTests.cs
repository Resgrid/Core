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
using Resgrid.Model.Certifications;
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
	/// Real-database proof for round 9A (M0404-M0407) on both engines: the migrations re-run as no-ops, roll back exactly what they added and
	/// apply again; certification types keep their validity rule; requirement sets are one per type with their items; the issuance ledger's
	/// live and per-record reads (case-insensitive member ids) and its state update with null dates; the requirement-set sweep claims a local
	/// day once; course completion facts find the ledger record of an enrolment not stamped yet (never a voided one); department cleanup
	/// removes the new tables for that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CertificationRequirementsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "r9a_cr_";
		private const string Member = "7d4e9b22-2a3c-4e5d-8f6a-1b2c3d4e5f81";
		private const string Officer = "7d4e9b22-2a3c-4e5d-8f6a-1b2c3d4e5f82";
		private int Department, OtherDepartment;

		private static readonly string[] Tables = { "LmsCertificationRequirementSets", "LmsCertificationRequirementItems", "LmsCertificationIssuances" };

		private static readonly (string Table, string Column)[] Columns =
		{
			("DepartmentCertificationTypes", "ValidityRule"), ("DepartmentCertificationTypes", "ValidityYearEndMonth"), ("DepartmentCertificationTypes", "ValidityYearEndDay"),
			("DepartmentCertificationTypes", "ValidityFixedDate"), ("DepartmentCertificationTypes", "RenewalGraceDays"), ("DepartmentCertificationTypes", "RenewalWindowMonths"),
			("LmsProgramStates", "RequirementSetSweepLocalDate"), ("Trainings", "RequiresAcknowledgment")
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

		private string TablesExist(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

		private string ColumnsExist() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND (" +
			  string.Join(" OR ", Columns.Select(c => $"(table_name = '{c.Table.ToLowerInvariant()}' AND column_name = '{c.Column.ToLowerInvariant()}')")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE " + string.Join(" OR ", Columns.Select(c => $"(TABLE_NAME = '{c.Table}' AND COLUMN_NAME = '{c.Column}')"));

		/// <summary>Inserts a row, filling the other NOT NULL columns without a default with a neutral value (the TrainingT1 fixture's helper).</summary>
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

			foreach (var (id, name) in new[] { (Member, "Morgan.Member9a"), (Officer, "Olive.Officer9a") })
				await InsertAsync(database, "AspNetUsers", new
				{
					Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name + "@example.com", NormalizedEmail = (name + "@example.com").ToUpperInvariant(),
					EmailConfirmed = true, PhoneNumberConfirmed = false, TwoFactorEnabled = false, LockoutEnabled = false, AccessFailedCount = 0
				});
			await InsertAsync(database, "Departments", new { Name = "R9A One", Code = "R9A1", ManagingUserId = Officer, ShowWelcome = false });
			await InsertAsync(database, "Departments", new { Name = "R9A Two", Code = "R9A2", ManagingUserId = Officer, ShowWelcome = false });
			Department = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R9A1'");
			OtherDepartment = await database.ExecuteScalarAsync<int>($"SELECT {T("DepartmentId")} FROM {T("Departments")} WHERE {T("Code")} = 'R9A2'");
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
		public async Task The_migrations_rerun_as_no_ops_roll_back_exactly_what_they_added_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 404 AND {versionColumn} <= 407");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(TablesExist(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");
				(await ScalarAsync(ColumnsExist())).Should().Be(Columns.Length);

				migrations.MigrateDown(403);
				(await ScalarAsync(TablesExist(Tables))).Should().Be(0);
				(await ScalarAsync(ColumnsExist())).Should().Be(0, "Down removes exactly the columns Up added");
				(await ScalarAsync(TablesExist("DepartmentCertificationTypes", "LmsProgramStates", "Trainings", "PersonnelCertifications"))).Should().Be(4, "and nothing else");

				migrations.MigrateUp();
			}

			(await ScalarAsync(TablesExist(Tables))).Should().Be(Tables.Length);
			(await ScalarAsync(ColumnsExist())).Should().Be(Columns.Length);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("DepartmentCertificationTypes")} WHERE {T("ValidityRule")} <> 0")).Should().Be(0, "existing types read as rolling months");
		}

		[Test, Order(2)]
		public async Task A_certification_type_keeps_its_validity_rule_and_a_training_its_confirmation_flag()
		{
			var types = Repository((c, s, u, q) => new DepartmentCertificationTypeRepository(c, s, u, q));
			var saved = await types.SaveOrUpdateAsync(new DepartmentCertificationType
			{
				DepartmentId = Department, Type = "Forklift operator", Code = "FORKLIFT-" + Guid.NewGuid().ToString("N").Substring(0, 6), ValidityRule = (int)CertificationValidityRules.FiscalYearEnd,
				ValidityYearEndMonth = 3, ValidityYearEndDay = 31, RenewalGraceDays = 30, RenewalWindowMonths = 3, IsActive = true, AddedOn = DateTime.UtcNow
			}, default);
			var read = await types.GetByIdAsync(saved.DepartmentCertificationTypeId);
			read.ValidityRule.Should().Be((int)CertificationValidityRules.FiscalYearEnd);
			read.ValidityYearEndMonth.Should().Be(3);
			read.ValidityYearEndDay.Should().Be(31);
			read.RenewalGraceDays.Should().Be(30);
			read.RenewalWindowMonths.Should().Be(3);
			read.ValidityFixedDate.Should().BeNull();

			read.ValidityRule = (int)CertificationValidityRules.FixedDate;
			read.ValidityFixedDate = new DateTime(2027, 6, 30);
			await types.SaveOrUpdateAsync(read, default);
			(await types.GetByIdAsync(saved.DepartmentCertificationTypeId)).ValidityFixedDate.Should().Be(new DateTime(2027, 6, 30));

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await InsertAsync(database, "Trainings", new
				{
					DepartmentId = Department, CreatedByUserId = Officer, Name = "Employee handbook", Description = "d", TrainingText = "t", MinimumScore = 0d, CreatedOn = DateTime.UtcNow,
					RequiresAcknowledgment = true
				});
			}
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("Trainings")} WHERE {T("DepartmentId")} = @D AND {T("RequiresAcknowledgment")} = @Ack", new { D = Department, Ack = true })).Should().Be(1);
		}

		[Test, Order(3)]
		public async Task A_requirement_set_is_one_per_type_with_its_items()
		{
			var sets = Repository((c, s, u, q) => new LmsCertificationRequirementSetsRepository(c, s, u, q));
			var items = Repository((c, s, u, q) => new LmsCertificationRequirementItemsRepository(c, s, u, q));
			var set = await sets.InsertAsync(new LmsCertificationRequirementSet { DepartmentId = Department, DepartmentCertificationTypeId = 5, CreatedOn = DateTime.UtcNow, CreatedByUserId = Officer }, default, true);
			set.LmsCertificationRequirementSetId.Should().BePositive();
			Func<Task> duplicate = () => sets.InsertAsync(new LmsCertificationRequirementSet { DepartmentId = Department, DepartmentCertificationTypeId = 5, CreatedOn = DateTime.UtcNow }, default, true);
			await duplicate.Should().ThrowAsync<Exception>("one set per certification type");
			await sets.InsertAsync(new LmsCertificationRequirementSet { DepartmentId = OtherDepartment, DepartmentCertificationTypeId = 5, IsActive = false, CreatedOn = DateTime.UtcNow }, default, true);

			await items.InsertAsync(new LmsCertificationRequirementItem { DepartmentId = Department, LmsCertificationRequirementSetId = set.LmsCertificationRequirementSetId, Kind = (int)LmsRequirementKinds.Training, TargetRefId = 40, RequireRecordedByInstructor = true, Label = "Slow speed course", SortOrder = 2 }, default, true);
			await items.InsertAsync(new LmsCertificationRequirementItem { DepartmentId = Department, LmsCertificationRequirementSetId = set.LmsCertificationRequirementSetId, Kind = (int)LmsRequirementKinds.CategoryHours, TargetRefId = 7, RequiredHours = 8.5m, SortOrder = 1 }, default, true);

			var stored = await items.GetForSetsAsync(Department, new[] { set.LmsCertificationRequirementSetId });
			stored.Select(i => i.SortOrder).Should().Equal(1, 2);
			stored[0].RequiredHours.Should().Be(8.5m);
			stored[1].RequireRecordedByInstructor.Should().BeTrue();
			stored[1].Label.Should().Be("Slow speed course");
			(await items.GetForSetsAsync(OtherDepartment, new[] { set.LmsCertificationRequirementSetId })).Should().BeEmpty("every read starts at the department");

			(await sets.GetForTypeAsync(Department, 5)).LmsCertificationRequirementSetId.Should().Be(set.LmsCertificationRequirementSetId);
			(await sets.HasActiveSetsAsync(Department)).Should().BeTrue();
			(await sets.HasActiveSetsAsync(OtherDepartment)).Should().BeFalse("its only set is inactive");
			(await sets.GetDepartmentIdsWithActiveSetsAsync()).Should().Contain(Department).And.NotContain(OtherDepartment);

			(await items.DeleteForSetAsync(Department, set.LmsCertificationRequirementSetId)).Should().Be(2);
			(await items.GetForSetsAsync(Department, new[] { set.LmsCertificationRequirementSetId })).Should().BeEmpty();
		}

		[Test, Order(4)]
		public async Task The_issuance_ledger_reads_live_rows_case_insensitively_and_updates_state_with_null_dates()
		{
			var ledger = Repository((c, s, u, q) => new LmsCertificationIssuancesRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var issued = await ledger.InsertAsync(new LmsCertificationIssuance
			{
				DepartmentId = Department, UserId = Member, PersonnelCertificationId = 301, DepartmentCertificationTypeId = 5, Source = (int)LmsCertificationIssueSources.CourseCompletion,
				SourceRefId = "lms-enrollment:77", Action = (int)LmsCertificationIssueActions.Issued, NewStatus = 0, NewExpiresOn = new DateTime(2027, 12, 31), BasisRecordIds = "1001,1003",
				CompletedOn = now.AddMinutes(-5), IssuedOn = now, IssuedByUserId = Officer, State = (int)LmsCertificationIssuanceStates.Live
			}, default, true);
			var renewed = await ledger.InsertAsync(new LmsCertificationIssuance
			{
				DepartmentId = Department, UserId = Member, PersonnelCertificationId = 301, DepartmentCertificationTypeId = 5, Source = (int)LmsCertificationIssueSources.RequirementSet,
				SourceRefId = "lms-cert-set:1", Action = (int)LmsCertificationIssueActions.Renewed, PreviousStatus = 0, PreviousExpiresOn = new DateTime(2027, 12, 31),
				PreviousRecievedOn = new DateTime(2026, 11, 25), NewStatus = 0, NewExpiresOn = new DateTime(2028, 12, 31), CompletedOn = now, IssuedOn = now.AddSeconds(1),
				State = (int)LmsCertificationIssuanceStates.Live
			}, default, true);
			await ledger.InsertAsync(new LmsCertificationIssuance
			{
				DepartmentId = OtherDepartment, UserId = Member, PersonnelCertificationId = 302, DepartmentCertificationTypeId = 5, Source = 0, Action = 0, NewStatus = 0, CompletedOn = now,
				IssuedOn = now, State = 0
			}, default, true);

			var live = await ledger.GetLiveForUserAsync(Department, Member.ToUpperInvariant());
			live.Select(i => i.LmsCertificationIssuanceId).Should().Equal(issued.LmsCertificationIssuanceId, renewed.LmsCertificationIssuanceId);
			live[0].Basis.Should().BeEquivalentTo(new[] { 1001, 1003 });
			live[1].PreviousRecievedOn.Should().Be(new DateTime(2026, 11, 25));

			// Unwound: the later row inherits a null previous state (the earlier row created the record).
			renewed.PreviousStatus = null;
			renewed.PreviousExpiresOn = null;
			renewed.PreviousRecievedOn = null;
			renewed.Action = (int)LmsCertificationIssueActions.Issued;
			(await ledger.UpdateStateAsync(renewed)).Should().BeTrue();
			issued.State = (int)LmsCertificationIssuanceStates.Unwound;
			issued.RolledBackOn = now;
			issued.RolledBackByUserId = Officer;
			issued.RollbackReason = "training_completion_voided";
			issued.BasisRecordIds = null;
			(await ledger.UpdateStateAsync(issued)).Should().BeTrue();

			(await ledger.GetLiveForUserAsync(Department, Member)).Should().ContainSingle().Which.PreviousStatus.Should().BeNull();
			var history = await ledger.GetForCertificationAsync(Department, 301);
			history.Should().HaveCount(2);
			history[0].State.Should().Be((int)LmsCertificationIssuanceStates.Unwound);
			history[0].RollbackReason.Should().Be("training_completion_voided");
			(await ledger.GetRecentForUserAsync(Department, Member, 1)).Single().LmsCertificationIssuanceId.Should().Be(renewed.LmsCertificationIssuanceId);
			(await ledger.GetForCertificationAsync(OtherDepartment, 301)).Should().BeEmpty();
		}

		[Test, Order(5)]
		public async Task The_requirement_set_sweep_claims_a_local_day_once()
		{
			var states = Repository((c, s, u, q) => new LmsProgramStatesRepository(c, s, u, q));
			var day = new DateTime(2026, 11, 25);
			(await states.TryClaimRequirementSetSweepAsync(Department, day)).Should().BeTrue();
			(await states.TryClaimRequirementSetSweepAsync(Department, day)).Should().BeFalse();
			(await states.TryClaimCycleSweepAsync(Department, day)).Should().BeTrue("the cycle sweep has its own claim");
			await states.ReleaseRequirementSetSweepAsync(Department, day);
			(await states.TryClaimRequirementSetSweepAsync(Department, day)).Should().BeTrue("a released day is claimed again");
			(await states.TryClaimRequirementSetSweepAsync(Department, day.AddDays(1))).Should().BeTrue();
		}

		[Test, Order(6)]
		public async Task Course_completion_facts_find_the_ledger_record_of_an_enrolment_not_stamped_yet_never_a_voided_one()
		{
			var courses = Repository((c, s, u, q) => new LmsCoursesRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsCourseVersionsRepository(c, s, u, q));
			var enrollments = Repository((c, s, u, q) => new LmsEnrollmentsRepository(c, s, u, q));
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var facts = Repository((c, s, u, q) => new LmsRequirementFactsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var course = await courses.InsertAsync(new LmsCourse { DepartmentId = Department, Title = "Forklift", Status = (int)ProtocolStatuses.Published, CreatedOn = now }, default, true);
			var version = await versions.InsertAsync(new LmsCourseVersion
			{
				DepartmentId = Department, LmsCourseId = course.LmsCourseId, Major = 1, Status = (int)ProtocolVersionStatuses.Published, Title = "Forklift", Hours = 1m, CreatedOn = now
			}, default, true);
			var (enrollment, _) = await enrollments.InsertOpenAsync(new LmsEnrollment
			{
				DepartmentId = Department, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = version.LmsCourseVersionId, UserId = Member, AssignedOn = now.AddDays(-1),
				Status = (int)LmsEnrollmentStatuses.InProgress, IsOpen = true
			});
			await ExecuteAsync($"UPDATE {T("LmsEnrollments")} SET {T("Status")} = @S, {T("IsOpen")} = @Open, {T("CompletedOn")} = @On WHERE {T("LmsEnrollmentId")} = @Id",
				new { S = (int)LmsEnrollmentStatuses.Completed, Open = false, On = DateTime.UtcNow.AddMinutes(-10), Id = enrollment.LmsEnrollmentId });
			var (record, inserted) = await records.InsertIfAbsentAsync(new LearningRecord
			{
				DepartmentId = Department, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.LmsCourseCompletion, SourceRefId = "lms-enrollment:" + enrollment.LmsEnrollmentId,
				Title = "Forklift", CompletedOn = DateTime.UtcNow.AddMinutes(-10), Hours = 1m, Status = (int)LearningRecordStatuses.Verified
			});
			inserted.Should().BeTrue();

			var fact = (await facts.GetCourseCompletionsWithRecordsAsync(Department, new[] { course.LmsCourseId })).Should().ContainSingle().Subject;
			fact.LearningRecordId.Should().Be(record.LearningRecordId);
			fact.UserId.Should().BeEquivalentTo(Member);
			(await facts.GetCourseCompletionsAsync(Department, new[] { course.LmsCourseId })).Single().LearningRecordId.Should().BeNull("the plain projection reads the enrolment only");

			await records.VoidAsync(Department, record.LearningRecordId, "recorded_completion_voided", Officer, DateTime.UtcNow);
			(await facts.GetCourseCompletionsWithRecordsAsync(Department, new[] { course.LmsCourseId })).Single().LearningRecordId.Should().BeNull("a voided record is never a basis");
		}

		[Test, Order(7)]
		public async Task Department_cleanup_removes_the_new_tables_for_that_department_only()
		{
			var ledger = Repository((c, s, u, q) => new LmsCertificationIssuancesRepository(c, s, u, q));
			await ledger.InsertAsync(new LmsCertificationIssuance { DepartmentId = OtherDepartment, UserId = Officer, PersonnelCertificationId = 9, DepartmentCertificationTypeId = 5, CompletedOn = DateTime.UtcNow, IssuedOn = DateTime.UtcNow }, default, true);
			var items = Repository((c, s, u, q) => new LmsCertificationRequirementItemsRepository(c, s, u, q));
			await items.InsertAsync(new LmsCertificationRequirementItem { DepartmentId = Department, LmsCertificationRequirementSetId = 1, Kind = 0, TargetRefId = 31 }, default, true);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsCertificationIssuances")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().BeGreaterThan(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsCertificationRequirementSets")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
