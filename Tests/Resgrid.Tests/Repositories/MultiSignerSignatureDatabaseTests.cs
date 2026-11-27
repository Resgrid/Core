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
	/// Real-database proof for e-Signature S3 (M0371) on both engines: the migration re-runs as a no-op, rolls back (removing the
	/// requests that hold member steps first) and applies again; the step columns round-trip; and the new queries — a member's
	/// pending steps (case-insensitive user id, open and unexpired requests of the department only), due reminders and the
	/// conditional reminder claim, requests by id with the IN-list on PostgreSQL. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MultiSignerSignatureDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "esign_s3_";
		private const int Dept = 4111;
		private const int OtherDept = 4112;
		private const string Member = "6A1D2C3B-0000-4000-8000-0000000000AA";

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

		private Task<int> ColumnCountAsync(string column) => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'signaturerequestsigners' AND column_name = @C"
			: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SignatureRequestSigners') AND name = @C", new { C = IsPostgres ? column.ToLowerInvariant() : column });

		private async Task<(SignatureRequestRecord Request, SignatureRequestSigner Member)> MemberRequestAsync(int department, DateTime expiresOn, int status = (int)SignatureRequestStatuses.Sent,
			int signerStatus = (int)SignatureSignerStatuses.Pending, string userId = Member, DateTime? nextReminderOn = null)
		{
			var requests = Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q));
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var request = await requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = department, SubjectKind = (int)SignatureSubjectKinds.SkillEvaluation, SubjectId = "EVAL-" + Guid.NewGuid().ToString("N").Substring(0, 6),
				Title = "Ladder raise", ArtifactSha256 = new string('a', 64), RequiredLevel = 2, Ordered = true, Status = status, Channel = (int)SignatureRequestChannels.InPerson,
				ExpiresOn = expiresOn, StatementVersion = "skill-evaluation/1", CreatedByUserId = "requester", CreatedOn = now
			}, default, true);
			var member = await signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId, DepartmentId = department, SignerOrder = 1, Role = (int)SignatureSignerRoles.Evaluator,
				SignerKind = (int)SignatureSignerKinds.Member, UserId = userId, RequiredLevel = 2, EligibilityJson = new SignerEligibility { CertificationTypeIds = { 7 } }.ToJson(),
				StepLabel = "Company officer", AuthMethod = (int)SignatureSignerAuthMethods.StepUp, Status = signerStatus, TurnStartedOn = now, NextReminderOn = nextReminderOn, CreatedOn = now
			}, default, true);
			await signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId, DepartmentId = department, SignerOrder = 2, Role = (int)SignatureSignerRoles.Vendor,
				SignerKind = (int)SignatureSignerKinds.External, ExternalName = "Acme Pumps", RequiredLevel = 4, AuthMethod = (int)SignatureSignerAuthMethods.InPerson, Status = 0, CreatedOn = now
			}, default, true);
			return (request, member);
		}

		[Test, Order(1)]
		public async Task The_migration_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 371");

			using var runner = Runner();
			using var scope = runner.CreateScope();
			var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

			// Up a second time over the existing schema: every guard holds.
			migrations.MigrateUp();
			foreach (var column in new[] { "SignerKind", "RequiredLevel", "EligibilityJson", "StepLabel", "DeclineReason", "TurnStartedOn", "NextReminderOn", "LastRemindedOn", "ReminderCount" })
				(await ColumnCountAsync(column)).Should().Be(1, column);

			// A request holding a member step and an S2 one-signer request exist before the rollback.
			var (memberRequest, _) = await MemberRequestAsync(Dept, DateTime.UtcNow.AddDays(1));
			var requests = Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q));
			var s2 = await requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = Dept, SubjectKind = 6, SubjectId = "DTR-9", ArtifactSha256 = new string('b', 64), RequiredLevel = 4, Status = (int)SignatureRequestStatuses.Sent,
				Channel = (int)SignatureRequestChannels.InPerson, ExpiresOn = DateTime.UtcNow.AddDays(1), CreatedOn = DateTime.UtcNow
			}, default, true);
			await Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q)).InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = s2.SignatureRequestId, DepartmentId = Dept, Role = 0, ExternalName = "Casey", AuthMethod = 5, Status = 0, CreatedOn = DateTime.UtcNow
			}, default, true);

			migrations.MigrateDown(370);
			(await ColumnCountAsync("SignerKind")).Should().Be(0);
			(await ColumnCountAsync("NextReminderOn")).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("SignatureRequests")} WHERE {T("SignatureRequestId")} = @Id", new { Id = memberRequest.SignatureRequestId }))
				.Should().Be(0, "the S2 shape cannot hold a member step, so Down removes its request");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("SignatureRequestSigners")} WHERE {T("SignatureRequestId")} = @Id", new { Id = memberRequest.SignatureRequestId })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("SignatureRequestSigners")} WHERE {T("SignatureRequestId")} = @Id", new { Id = s2.SignatureRequestId })).Should().Be(1, "S2 rows stay");

			migrations.MigrateUp();
			(await ColumnCountAsync("SignerKind")).Should().Be(1);
			(await ScalarAsync($"SELECT {T("SignerKind")} FROM {T("SignatureRequestSigners")} WHERE {T("SignatureRequestId")} = @Id", new { Id = s2.SignatureRequestId }))
				.Should().Be((int)SignatureSignerKinds.External, "an S2 signer is external");

			await using var reloaded = Connect(_connection);
			await reloaded.OpenAsync();
			if (reloaded is NpgsqlConnection postgres)
				await postgres.ReloadTypesAsync();
		}

		[Test, Order(2)]
		public async Task Step_columns_round_trip_and_a_members_pending_steps_come_from_open_requests_only()
		{
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var requests = Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var (open, step) = await MemberRequestAsync(Dept, now.AddDays(3));
			await MemberRequestAsync(Dept, now.AddDays(-1));                                                    // expired
			await MemberRequestAsync(Dept, now.AddDays(3), (int)SignatureRequestStatuses.Completed);           // closed
			await MemberRequestAsync(Dept, now.AddDays(3), signerStatus: (int)SignatureSignerStatuses.Signed); // already signed
			await MemberRequestAsync(OtherDept, now.AddDays(3));                                               // another department
			await MemberRequestAsync(Dept, now.AddDays(3), userId: "someone-else");

			var stored = (await signers.GetForRequestAsync(Dept, open.SignatureRequestId)).First(s => s.SignatureRequestSignerId == step.SignatureRequestSignerId);
			stored.SignerKind.Should().Be((int)SignatureSignerKinds.Member);
			stored.RequiredLevel.Should().Be(2);
			stored.StepLabel.Should().Be("Company officer");
			SignerEligibility.Parse(stored.EligibilityJson).CertificationTypeIds.Should().Equal(7);
			stored.IsMember.Should().BeTrue();

			var pending = await signers.GetPendingMemberStepsAsync(Dept, Member.ToLowerInvariant(), now, 50);
			pending.Select(s => s.SignatureRequestId).Should().Equal(new[] { open.SignatureRequestId }, "the user id compares case-insensitively; expired, closed, signed and other members' steps stay out");
			(await signers.GetPendingMemberStepsAsync(OtherDept + 1, Member, now, 50)).Should().BeEmpty();

			var byIds = await requests.GetByIdsAsync(Dept, new[] { open.SignatureRequestId, step.SignatureRequestId + 100000 });
			byIds.Select(r => r.SignatureRequestId).Should().Equal(open.SignatureRequestId);
			(await requests.GetByIdsAsync(OtherDept, new[] { open.SignatureRequestId })).Should().BeEmpty();
		}

		[Test, Order(3)]
		public async Task Due_reminders_come_from_open_requests_and_are_claimed_once()
		{
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var (_, due) = await MemberRequestAsync(Dept, now.AddDays(2), nextReminderOn: now.AddMinutes(-5));
			await MemberRequestAsync(Dept, now.AddDays(2), nextReminderOn: now.AddHours(5));                                                 // not yet
			await MemberRequestAsync(Dept, now.AddDays(-1), nextReminderOn: now.AddMinutes(-5));                                             // expired request
			await MemberRequestAsync(Dept, now.AddDays(2), (int)SignatureRequestStatuses.Voided, nextReminderOn: now.AddMinutes(-5));        // closed request
			await MemberRequestAsync(Dept, now.AddDays(2), signerStatus: (int)SignatureSignerStatuses.Signed, nextReminderOn: now.AddMinutes(-5));

			var found = await signers.GetDueRemindersAsync(now, 50);
			found.Select(s => s.SignatureRequestSignerId).Should().Contain(due.SignatureRequestSignerId);
			found.Should().OnlyContain(s => s.NextReminderOn <= now && s.Status != (int)SignatureSignerStatuses.Signed);
			found.Where(s => s.SignatureRequestSignerId != due.SignatureRequestSignerId).Should().BeEmpty("only the open, unexpired, unsigned and due step is found");

			var next = now.AddHours(48);
			(await signers.ClaimReminderAsync(due.SignatureRequestSignerId, 0, next, now)).Should().BeTrue();
			(await signers.ClaimReminderAsync(due.SignatureRequestSignerId, 0, next, now)).Should().BeFalse("a second host sees the count moved on");
			var claimed = (await signers.GetForRequestAsync(Dept, due.SignatureRequestId)).First(s => s.SignatureRequestSignerId == due.SignatureRequestSignerId);
			claimed.ReminderCount.Should().Be(1);
			claimed.LastRemindedOn.Should().NotBeNull();
			claimed.NextReminderOn.Should().BeCloseTo(next, TimeSpan.FromSeconds(1));
			(await signers.GetDueRemindersAsync(now, 50)).Should().NotContain(s => s.SignatureRequestSignerId == due.SignatureRequestSignerId);

			// The last reminder clears the schedule (a null timestamp parameter on both engines).
			(await signers.ClaimReminderAsync(due.SignatureRequestSignerId, 1, null, now)).Should().BeTrue();
			(await signers.GetForRequestAsync(Dept, due.SignatureRequestId)).First(s => s.SignatureRequestSignerId == due.SignatureRequestSignerId).NextReminderOn.Should().BeNull();
			(await signers.ClaimReminderAsync(due.SignatureRequestSignerId, 2, now, now)).Should().BeFalse("nothing is scheduled any more");
		}
	}
}
