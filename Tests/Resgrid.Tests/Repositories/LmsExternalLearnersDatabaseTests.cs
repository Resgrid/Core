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
	/// Real-database proof for Training T5 external learners (M0396–M0397) on both engines: the migrations re-run as no-ops, roll back and
	/// apply again; one learner per department and e-mail (case-insensitive); the fair-use count; tokens are single use; the portal key
	/// lookup; a fee is claimed by one invoice run only; department-wide learning-record queries leave external learners out; the GDPR purge
	/// touches only the learner's rows; department cleanup removes the new tables for that department only. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsExternalLearnersDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_xl_";
		private const string Member = "5c2e7a11-1f3b-4d2c-9a6e-0b1c2d3e4f51";
		private const int Department = 4501;
		private const int OtherDepartment = 4502;

		private static readonly string[] Tables =
		{
			"LmsLearnerPortals", "LmsExternalLearners", "LmsExternalLearnerTokens", "LmsPublicClasses", "LmsClassFees", "LmsExternalCourseAccess"
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

		private static LmsExternalLearner NewLearner(int department, string email, LmsExternalLearnerStatuses status) => new LmsExternalLearner
		{
			DepartmentId = department,
			LearnerUserId = LmsExternalLearnerIds.New(),
			Email = LmsExternalLearnerRules.NormalizeEmail(email),
			FirstName = "Casey",
			LastName = "Rivera",
			Organization = "County CERT",
			Status = (int)status,
			SecurityStamp = LmsExternalLearnerRules.NewToken(),
			CreatedOn = DateTime.UtcNow
		};

		[Test, Order(1)]
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 396 AND {versionColumn} <= 397");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");

				migrations.MigrateDown(395);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task One_learner_per_department_and_email_and_the_fair_use_count()
		{
			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var active = await learners.InsertAsync(NewLearner(Department, "casey@example.org", LmsExternalLearnerStatuses.Active), default, true);
			await learners.InsertAsync(NewLearner(Department, "invited@example.org", LmsExternalLearnerStatuses.Invited), default, true);
			await learners.InsertAsync(NewLearner(Department, "pending@example.org", LmsExternalLearnerStatuses.Pending), default, true);
			await learners.InsertAsync(NewLearner(Department, "suspended@example.org", LmsExternalLearnerStatuses.Suspended), default, true);
			await learners.InsertAsync(NewLearner(OtherDepartment, "casey@example.org", LmsExternalLearnerStatuses.Active), default, true);

			var same = NewLearner(Department, "casey@example.org", LmsExternalLearnerStatuses.Active);
			same.Email = "CASEY@example.org";
			Func<Task> duplicate = () => learners.InsertAsync(same, default, true);
			await duplicate.Should().ThrowAsync<Exception>("one learner per department and address, whatever the case");

			(await learners.GetByEmailAsync(Department, "Casey@Example.org")).LmsExternalLearnerId.Should().Be(active.LmsExternalLearnerId);
			(await learners.GetByLearnerUserIdAsync(Department, active.LearnerUserId.ToUpperInvariant())).Should().NotBeNull();
			(await learners.GetByLearnerUserIdAsync(OtherDepartment, active.LearnerUserId)).Should().BeNull("department scoped");
			(await learners.CountCountedAsync(Department)).Should().Be(2, "invited and active count; pending and suspended do not");
			(await learners.GetByLearnerUserIdsAsync(Department, new[] { active.LearnerUserId, Member })).Should().ContainSingle("member ids are never looked up");
			(await learners.GetForDepartmentAsync(Department)).Should().HaveCount(4);

			// An erased learner's address is gone, so the address can register again.
			active.Email = null;
			active.Status = (int)LmsExternalLearnerStatuses.Erased;
			await learners.SaveOrUpdateAsync(active, default, true);
			await learners.InsertAsync(NewLearner(Department, "casey@example.org", LmsExternalLearnerStatuses.Pending), default, true);
		}

		[Test, Order(3)]
		public async Task Tokens_are_single_use_and_expire()
		{
			var tokens = Repository((c, s, u, q) => new LmsExternalLearnerTokensRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var good = await tokens.InsertAsync(new LmsExternalLearnerToken
			{
				DepartmentId = Department, LmsExternalLearnerId = 1, Purpose = (int)LmsExternalLearnerTokenPurposes.Invite, TokenHash = LmsExternalLearnerRules.HashToken("good"),
				LmsClassSessionId = 9, ExpiresOn = now.AddDays(1), CreatedOn = now
			}, default, true);
			var old = await tokens.InsertAsync(new LmsExternalLearnerToken
			{
				DepartmentId = Department, LmsExternalLearnerId = 1, Purpose = (int)LmsExternalLearnerTokenPurposes.ResetPassword, TokenHash = LmsExternalLearnerRules.HashToken("old"),
				ExpiresOn = now.AddMinutes(-1), CreatedOn = now.AddHours(-3)
			}, default, true);

			(await tokens.GetByHashAsync(LmsExternalLearnerRules.HashToken("good"))).LmsClassSessionId.Should().Be(9);
			(await tokens.ClaimAsync(good.LmsExternalLearnerTokenId, now)).Should().BeTrue();
			(await tokens.ClaimAsync(good.LmsExternalLearnerTokenId, now)).Should().BeFalse("a token is used once");
			(await tokens.ClaimAsync(old.LmsExternalLearnerTokenId, now)).Should().BeFalse("an expired token is refused");
			(await tokens.DeleteForLearnerAsync(Department, 1)).Should().Be(2);
		}

		[Test, Order(4)]
		public async Task The_portal_is_found_by_its_key_and_is_one_per_department()
		{
			var portals = Repository((c, s, u, q) => new LmsLearnerPortalsRepository(c, s, u, q));
			var key = LmsExternalLearnerRules.NewPortalKey();
			await portals.InsertAsync(new LmsLearnerPortal { DepartmentId = Department, PortalKey = key, IsEnabled = true, AccentColor = "#aa0000", CreatedOn = DateTime.UtcNow }, default, true);

			(await portals.GetByKeyAsync(key)).DepartmentId.Should().Be(Department);
			(await portals.GetByKeyAsync(key.ToUpperInvariant())).Should().NotBeNull();
			(await portals.GetByDepartmentAsync(Department)).AccentColor.Should().Be("#aa0000");
			Func<Task> second = () => portals.InsertAsync(new LmsLearnerPortal { DepartmentId = Department, PortalKey = LmsExternalLearnerRules.NewPortalKey(), CreatedOn = DateTime.UtcNow }, default, true);
			await second.Should().ThrowAsync<Exception>("one portal per department");
		}

		[Test, Order(5)]
		public async Task A_fee_is_claimed_by_one_invoice_run_only()
		{
			var fees = Repository((c, s, u, q) => new LmsClassFeesRepository(c, s, u, q));
			var publicClasses = Repository((c, s, u, q) => new LmsPublicClassesRepository(c, s, u, q));
			await publicClasses.InsertAsync(new LmsPublicClass { DepartmentId = Department, LmsClassSessionId = 70, IsPublic = true, FeeAmount = 45.5m, Currency = "USD", CreatedOn = DateTime.UtcNow }, default, true);
			(await publicClasses.GetBySessionAsync(Department, 70)).FeeAmount.Should().Be(45.5m);

			var learner = LmsExternalLearnerIds.New();
			var a = await fees.InsertAsync(new LmsClassFee { DepartmentId = Department, LmsClassSessionId = 70, LmsClassEnrollmentId = 1, LearnerUserId = learner, Amount = 45.5m, Currency = "USD", CreatedOn = DateTime.UtcNow }, default, true);
			var b = await fees.InsertAsync(new LmsClassFee { DepartmentId = Department, LmsClassSessionId = 70, LmsClassEnrollmentId = 2, LearnerUserId = LmsExternalLearnerIds.New(), Amount = 45.5m, Waived = true, CreatedOn = DateTime.UtcNow }, default, true);
			Func<Task> duplicate = () => fees.InsertAsync(new LmsClassFee { DepartmentId = Department, LmsClassSessionId = 70, LmsClassEnrollmentId = 1, LearnerUserId = learner, CreatedOn = DateTime.UtcNow }, default, true);
			await duplicate.Should().ThrowAsync<Exception>("one fee row per roster place");

			(await fees.ClaimAsync(Department, new[] { a.LmsClassFeeId, b.LmsClassFeeId }, "claim:one")).Should().Be(1, "a waived fee is never claimed");
			(await fees.ClaimAsync(Department, new[] { a.LmsClassFeeId }, "claim:two")).Should().Be(0, "the first run holds it");
			(await fees.ReleaseClaimAsync(Department, "claim:one")).Should().Be(1);
			(await fees.ClaimAsync(Department, new[] { a.LmsClassFeeId }, "claim:two")).Should().Be(1);
			(await fees.CompleteClaimAsync(Department, new[] { a.LmsClassFeeId }, "claim:two", "INV-1", DateTime.UtcNow)).Should().Be(1);
			var stored = await fees.GetByEnrollmentAsync(Department, 1);
			stored.IsInvoiced.Should().BeTrue();
			stored.InvoiceId.Should().Be("INV-1");
			(await fees.GetForLearnerAsync(Department, learner.ToUpperInvariant())).Should().ContainSingle();
			(await fees.GetForSessionAsync(Department, 70)).Should().HaveCount(2);
		}

		[Test, Order(6)]
		public async Task Department_queries_leave_external_learners_out_and_the_purge_touches_only_the_learner()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var enrollments = Repository((c, s, u, q) => new LmsClassEnrollmentsRepository(c, s, u, q));
			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var learner = LmsExternalLearnerIds.New();
			LearningRecord Record(string userId, string source) => new LearningRecord
			{
				DepartmentId = Department, UserId = userId, SourceKind = (int)LearningRecordSourceKinds.ClassSession, SourceRefId = source, Title = "CPR",
				CompletedOn = DateTime.UtcNow.AddDays(-1), Hours = 4m, Status = (int)LearningRecordStatuses.Verified, CreatedOn = DateTime.UtcNow
			};
			await records.InsertAsync(Record(Member, "s-1"), default, true);
			await records.InsertAsync(Record(learner, "s-1"), default, true);
			await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = 80, UserId = learner, MemberName = "Casey Rivera", JobTitle = "County CERT", Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow }, default, true);
			await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = 80, UserId = Member, MemberName = "Pat Doe", Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow }, default, true);

			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = Department })).Select(r => r.UserId).Should().Equal(Member);
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = Department, UserIds = new List<string> { learner } })).Should().ContainSingle();
			(await records.CountAsync(new LearningRecordQuery { DepartmentId = Department })).Should().Be(1);

			(await learners.PurgeLearnerDataAsync(Department, Member)).Should().Be(0, "a member id is refused before any statement runs");
			(await learners.PurgeLearnerDataAsync(Department, learner)).Should().BeGreaterThan(0);
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = Department, UserIds = new List<string> { learner } })).Should().BeEmpty();
			(await records.QueryAsync(new LearningRecordQuery { DepartmentId = Department, UserIds = new List<string> { Member } })).Should().ContainSingle();
			var roster = await enrollments.GetForSessionAsync(Department, 80);
			roster.Single(e => e.UserId == learner).MemberName.Should().BeNull("the roster row stays without the learner's name");
			roster.Single(e => e.UserId == learner).JobTitle.Should().BeNull();
			roster.Single(e => e.UserId == Member).MemberName.Should().Be("Pat Doe");
		}

		[Test, Order(7)]
		public async Task Department_cleanup_removes_the_external_learner_tables_for_that_department_only()
		{
			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var access = Repository((c, s, u, q) => new LmsExternalCourseAccessRepository(c, s, u, q));
			await access.InsertAsync(new LmsExternalCourseAccess { DepartmentId = Department, LmsCourseId = 3, Access = (int)LmsExternalCourseAccessKinds.Catalog }, default, true);
			(await access.GetByCourseAsync(Department, 3)).Access.Should().Be((int)LmsExternalCourseAccessKinds.Catalog);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await learners.GetForDepartmentAsync(OtherDepartment)).Should().ContainSingle();
		}

		private Task<int> StampHashColumnAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsexternallearnertokens' AND column_name = 'stamphash'"
			: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LmsExternalLearnerTokens') AND name = 'StampHash'");

		[Test, Order(8)]
		public async Task Security_review_M0411_the_token_stamp_hash_persists_and_the_migration_reruns_and_rolls_back()
		{
			var tokens = Repository((c, s, u, q) => new LmsExternalLearnerTokensRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var stamp = LmsExternalLearnerRules.StampHash(LmsExternalLearnerRules.NewToken());
			await tokens.InsertAsync(new LmsExternalLearnerToken
			{
				DepartmentId = OtherDepartment, LmsExternalLearnerId = 3, Purpose = (int)LmsExternalLearnerTokenPurposes.VerifyEmail, TokenHash = LmsExternalLearnerRules.HashToken("m0451"),
				StampHash = stamp, ExpiresOn = now.AddDays(1), CreatedOn = now
			}, default, true);
			(await tokens.GetByHashAsync(LmsExternalLearnerRules.HashToken("m0451"))).StampHash.Should().Be(stamp);

			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} = 411");
			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await StampHashColumnAsync()).Should().Be(1, "a second Up over the existing column is a no-op");
				(await tokens.GetByHashAsync(LmsExternalLearnerRules.HashToken("m0451"))).StampHash.Should().Be(stamp, "and keeps the data");

				migrations.MigrateDown(410);
				(await StampHashColumnAsync()).Should().Be(0);
				(await ScalarAsync(Exists("LmsExternalLearnerTokens"))).Should().Be(1, "Down removes only the column");

				migrations.MigrateUp();
			}
			(await StampHashColumnAsync()).Should().Be(1);
		}

		#region Security review follow-up (M0412): a stale whole-row save never reverts a stamp rotation or a password change

		private Task<int> RowVersionColumnAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsexternallearners' AND column_name = 'rowversion'"
			: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LmsExternalLearners') AND name = 'RowVersion'");

		[Test, Order(9)]
		public async Task Security_review_M0412_a_stale_copy_can_never_write_back_an_old_stamp_or_password()
		{
			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var inserted = await learners.InsertAsync(NewLearner(OtherDepartment, "stale@example.org", LmsExternalLearnerStatuses.Active), default, true);
			var stale = await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId);
			var fresh = await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId);
			var (oldStamp, newStamp) = (stale.SecurityStamp, LmsExternalLearnerRules.NewToken());

			fresh.SecurityStamp = newStamp;
			fresh.PasswordHash = "new-hash";
			(await learners.TryUpdateAsync(fresh, fresh.RowVersion)).Should().BeTrue("the sign-out / password change");

			stale.Language = "de";
			(await learners.TryUpdateAsync(stale, stale.RowVersion)).Should().BeFalse("a copy read before the change is refused");
			await FluentActions.Awaiting(() => learners.SaveOrUpdateAsync(stale, default, true)).Should().ThrowAsync<System.Data.DBConcurrencyException>("a whole-row save goes through the same check");
			await FluentActions.Awaiting(() => learners.UpdateAsync(stale, default, true)).Should().ThrowAsync<System.Data.DBConcurrencyException>();
			var stored = await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId);
			(stored.SecurityStamp, stored.PasswordHash, stored.Language).Should().Be((newStamp, "new-hash", (string)null), "nothing of the stale copy was written");
			stored.SecurityStamp.Should().NotBe(oldStamp);

			// The single-column writes bump the version too, so a copy read before them is refused as well.
			var before = await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId);
			var key = await learners.EnsureUnsubscribeKeyAsync(OtherDepartment, inserted.LmsExternalLearnerId, "key-1");
			(await learners.EnsureUnsubscribeKeyAsync(OtherDepartment, inserted.LmsExternalLearnerId, "key-2")).Should().Be(key, "an existing key is kept");
			await learners.SetBillingContactAsync(OtherDepartment, inserted.LmsExternalLearnerId, "contact-9", DateTime.UtcNow);
			(await learners.TryUpdateAsync(before, before.RowVersion)).Should().BeFalse();
			(await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId)).BillingContactId.Should().Be("contact-9");

			// Racing stale saves from copies read at the same version: exactly one wins.
			var copies = new List<LmsExternalLearner>();
			for (var i = 0; i < 8; i++)
				copies.Add(await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId));
			for (var i = 0; i < copies.Count; i++)
				copies[i].SecurityStamp = "race-" + i;
			var won = await Task.WhenAll(copies.Select(c => Task.Run(() => learners.TryUpdateAsync(c, c.RowVersion))));
			won.Count(w => w).Should().Be(1);
			(await learners.GetByIdForDepartmentAsync(OtherDepartment, inserted.LmsExternalLearnerId)).SecurityStamp.Should().Be("race-" + Array.IndexOf(won, true));
		}

		[Test, Order(10)]
		public async Task Security_review_M0412_racing_sign_out_and_preference_saves_through_the_service_both_survive()
		{
			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var service = new Resgrid.Services.LmsExternalLearnerService(Mock.Of<Resgrid.Model.Repositories.ILmsLearnerPortalsRepository>(), learners,
				Mock.Of<Resgrid.Model.Repositories.ILmsExternalLearnerTokensRepository>(), Mock.Of<Resgrid.Model.Repositories.ILmsExternalCourseAccessRepository>(),
				Mock.Of<Resgrid.Model.Repositories.ILmsPublicClassesRepository>(), Mock.Of<Resgrid.Model.Repositories.ILmsClassFeesRepository>(),
				Mock.Of<Resgrid.Model.Repositories.ILmsClassEnrollmentsRepository>(), Mock.Of<Resgrid.Model.Repositories.ILmsCoursesRepository>(),
				Mock.Of<Resgrid.Model.Repositories.ILmsCourseVersionsRepository>(), Mock.Of<Resgrid.Model.Repositories.ILmsEnrollmentsRepository>(),
				Mock.Of<Resgrid.Model.Repositories.ILearningRecordsRepository>(), Mock.Of<Resgrid.Model.Services.ILmsClassService>(), Mock.Of<Resgrid.Model.Services.ILmsLearnerService>(),
				Mock.Of<Resgrid.Model.Services.ILmsPublicClassService>(), Mock.Of<Resgrid.Model.Services.ILmsGateService>(), Mock.Of<Resgrid.Model.Services.ILmsSeatService>(),
				Mock.Of<Resgrid.Model.Services.IDepartmentsService>(), Mock.Of<Resgrid.Model.Services.IDepartmentProfileMediaService>(), Mock.Of<Resgrid.Model.Providers.IEmailSender>(),
				Mock.Of<Resgrid.Model.Providers.IEventAggregator>(), Mock.Of<Resgrid.Model.Repositories.ILmsLearnerNoticesRepository>(),
				Mock.Of<Resgrid.Model.Services.IUserProfileService>());
			var learner = await learners.InsertAsync(NewLearner(OtherDepartment, "racer@example.org", LmsExternalLearnerStatuses.Active), default, true);

			foreach (var language in new[] { "de", "fr", "es", "it", "sv" })
			{
				var copied = (await learners.GetByIdForDepartmentAsync(OtherDepartment, learner.LmsExternalLearnerId)).SecurityStamp;
				var stale = await learners.GetByIdForDepartmentAsync(OtherDepartment, learner.LmsExternalLearnerId);

				await Task.WhenAll(
					Task.Run(() => service.SignOutAsync(OtherDepartment, learner.LmsExternalLearnerId)),
					Task.Run(() => service.SetPreferencesAsync(OtherDepartment, stale, language, true)));

				var stored = await learners.GetByIdForDepartmentAsync(OtherDepartment, learner.LmsExternalLearnerId);
				stored.SecurityStamp.Should().NotBe(copied, "the sign-out is never undone by the preferences save racing it (" + language + ")");
				stored.Language.Should().Be(language, "and the preferences save is re-applied to the fresh row, not lost");
				(await service.ValidateSessionAsync(OtherDepartment, learner.LmsExternalLearnerId, copied)).Should().BeNull("a copied cookie stays dead");
			}
		}

		[Test, Order(11)]
		public async Task Security_review_M0412_the_migration_reruns_as_a_no_op_and_rolls_back()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} = 412");
			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await RowVersionColumnAsync()).Should().Be(1, "a second Up over the existing column is a no-op");
				migrations.MigrateDown(411);
				(await RowVersionColumnAsync()).Should().Be(0);
				(await ScalarAsync(Exists("LmsExternalLearners"))).Should().Be(1, "Down removes only the column");
				migrations.MigrateUp();
			}
			(await RowVersionColumnAsync()).Should().Be(1);
		}

		#endregion
	}
}
