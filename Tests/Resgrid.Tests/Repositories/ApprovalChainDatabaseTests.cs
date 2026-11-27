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
using Resgrid.Model.Approvals;
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
	/// Real-database proof for e-Signature S4 (M0385, M0386) on both engines: the migrations re-run as a no-op, roll back and apply again;
	/// the repositories' department reads (case-insensitive subject ids), the conditional status claim, worker 97's due-work queries
	/// (unannounced turns joined to the S3 signer, due escalations of pending requests only, pending requests whose signature request
	/// closed) and their one-time claims (with and without an escalation time, the PostgreSQL untyped-null trap), and the department
	/// purge that leaves another department's rows. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ApprovalChainDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "approvals_s4_";
		private const int Dept = 4311;
		private const int OtherDept = 4312;

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

		private Task<int> TableCountAsync(string table) => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @T"
			: "SELECT COUNT(*) FROM sys.tables WHERE name = @T", new { T = IsPostgres ? table.ToLowerInvariant() : table });

		/// <summary>A pending (or other status) approval with one S3 request and one member step, whose turn may have started.</summary>
		private async Task<(ApprovalRequest Approval, ApprovalRequestStep Step, SignatureRequestRecord Request, SignatureRequestSigner Signer)> SeedAsync(int department, string subjectId,
			int approvalStatus = (int)ApprovalRequestStatuses.Pending, int requestStatus = (int)SignatureRequestStatuses.Sent, DateTime? turnStartedOn = null,
			int signerStatus = (int)SignatureSignerStatuses.Pending, DateTime? escalateDueOn = null)
		{
			var now = DateTime.UtcNow;
			var request = await Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q)).InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = department, SubjectKind = (int)SignatureSubjectKinds.InvoiceApproval, SubjectId = subjectId, Title = "Invoice", ArtifactSha256 = new string('a', 64),
				RequiredLevel = 2, Ordered = true, Status = requestStatus, Channel = (int)SignatureRequestChannels.InPerson, ExpiresOn = now.AddDays(14), StatementVersion = "approval/1",
				CreatedByUserId = "submitter", CreatedOn = now
			}, default, true);
			var signer = await Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q)).InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId, DepartmentId = department, SignerOrder = 1, Role = (int)SignatureSignerRoles.Member, SignerKind = (int)SignatureSignerKinds.Member,
				RequiredLevel = 2, EligibilityJson = "{\"c\":[],\"r\":[2],\"x\":[\"submitter\"]}", Status = signerStatus, TurnStartedOn = turnStartedOn, CreatedOn = now
			}, default, true);
			var approval = await Repository((c, s, u, q) => new ApprovalRequestsRepository(c, s, u, q)).InsertAsync(new ApprovalRequest
			{
				DepartmentId = department, SubjectKind = (int)SignatureSubjectKinds.InvoiceApproval, SubjectId = subjectId, ApprovalChainId = 1, SignatureRequestId = request.SignatureRequestId,
				Title = "Invoice INV-1", Amount = 1250.50m, Currency = "USD", ContentSha256 = new string('a', 64), Status = approvalStatus, SubmittedByUserId = "submitter", SubmittedOn = now
			}, default, true);
			var step = await Repository((c, s, u, q) => new ApprovalRequestStepsRepository(c, s, u, q)).InsertAsync(new ApprovalRequestStep
			{
				ApprovalRequestId = approval.ApprovalRequestId, DepartmentId = department, SignatureRequestSignerId = signer.SignatureRequestSignerId, ApprovalChainStepId = 1, StepOrder = 1,
				Label = "Finance", EscalateDueOn = escalateDueOn
			}, default, true);
			return (approval, step, request, signer);
		}

		[Test]
		public async Task The_migrations_rerun_as_a_no_op_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 385");

			using var runner = Runner();
			using var scope = runner.CreateScope();
			var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
			migrations.MigrateUp();
			foreach (var table in new[] { "ApprovalChains", "ApprovalChainSteps", "ApprovalRequests", "ApprovalRequestSteps", "ApprovalDelegates", "MutualAidAgreements" })
				(await TableCountAsync(table)).Should().Be(1, table);

			migrations.MigrateDown(384);
			(await TableCountAsync("ApprovalRequests")).Should().Be(0);
			(await TableCountAsync("MutualAidAgreements")).Should().Be(0);

			migrations.MigrateUp();
			(await TableCountAsync("ApprovalRequestSteps")).Should().Be(1);
			(await TableCountAsync("MutualAidAgreements")).Should().Be(1);
		}

		[Test]
		public async Task Chains_steps_and_delegates_round_trip_and_the_newest_active_chain_wins()
		{
			var chains = Repository((c, s, u, q) => new ApprovalChainsRepository(c, s, u, q));
			var steps = Repository((c, s, u, q) => new ApprovalChainStepsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var old = await chains.InsertAsync(new ApprovalChain { DepartmentId = Dept, SubjectKind = (int)SignatureSubjectKinds.InvoiceApproval, Name = "v1", IsActive = true, ExpiresInDays = 14, CreatedOn = now }, default, true);
			var current = await chains.InsertAsync(new ApprovalChain { DepartmentId = Dept, SubjectKind = (int)SignatureSubjectKinds.InvoiceApproval, Name = "v2", IsActive = true, ExpiresInDays = 7, CreatedOn = now }, default, true);
			await chains.InsertAsync(new ApprovalChain { DepartmentId = OtherDept, SubjectKind = (int)SignatureSubjectKinds.InvoiceApproval, Name = "other", IsActive = true, ExpiresInDays = 7, CreatedOn = now }, default, true);
			await steps.InsertAsync(new ApprovalChainStep { ApprovalChainId = current.ApprovalChainId, DepartmentId = Dept, StepOrder = 2, ApproverKind = (int)ApprovalApproverKinds.DepartmentAdmins, MinimumAmount = 5000.25m }, default, true);
			await steps.InsertAsync(new ApprovalChainStep { ApprovalChainId = current.ApprovalChainId, DepartmentId = Dept, StepOrder = 1, ApproverKind = (int)ApprovalApproverKinds.Role, PersonnelRoleId = 2, EscalateAfterHours = 24 }, default, true);

			(await chains.GetActiveForKindAsync(Dept, (int)SignatureSubjectKinds.InvoiceApproval)).ApprovalChainId.Should().Be(current.ApprovalChainId);
			old.IsActive = false;
			await chains.UpdateAsync(old, default, true);
			(await chains.GetForDepartmentAsync(Dept)).Should().HaveCount(2);
			var stored = await steps.GetForChainAsync(Dept, current.ApprovalChainId);
			stored.Select(s => s.StepOrder).Should().Equal(1, 2);
			stored[1].MinimumAmount.Should().Be(5000.25m);
			stored[0].EscalateAfterHours.Should().Be(24);
			(await steps.DeleteForChainAsync(Dept, current.ApprovalChainId)).Should().Be(2);

			var delegates = Repository((c, s, u, q) => new ApprovalDelegatesRepository(c, s, u, q));
			await delegates.InsertAsync(new ApprovalDelegate { DepartmentId = Dept, UserId = "chief", DelegateUserId = "medic", EndsOn = now.AddDays(3), CreatedOn = now }, default, true);
			(await delegates.GetForDepartmentAsync(Dept)).Should().ContainSingle().Which.EndsOn.Should().NotBeNull();
		}

		[Test]
		public async Task Requests_read_by_subject_case_insensitively_and_the_status_claim_wins_once()
		{
			var requests = Repository((c, s, u, q) => new ApprovalRequestsRepository(c, s, u, q));
			var (approval, _, request, _) = await SeedAsync(Dept, "INV-ABC-" + Guid.NewGuid().ToString("N").Substring(0, 6));

			(await requests.GetForSubjectAsync(Dept, (int)SignatureSubjectKinds.InvoiceApproval, approval.SubjectId.ToLowerInvariant())).Should().ContainSingle();
			(await requests.GetForSubjectAsync(OtherDept, (int)SignatureSubjectKinds.InvoiceApproval, approval.SubjectId)).Should().BeEmpty();
			(await requests.GetBySignatureRequestAsync(Dept, request.SignatureRequestId)).ApprovalRequestId.Should().Be(approval.ApprovalRequestId);
			(await requests.GetForDepartmentAsync(Dept, approval.ApprovalRequestId)).Amount.Should().Be(1250.50m);

			(await requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Approved, DateTime.UtcNow)).Should().BeTrue();
			(await requests.ClaimStatusAsync(approval.ApprovalRequestId, (int)ApprovalRequestStatuses.Pending, (int)ApprovalRequestStatuses.Rejected, DateTime.UtcNow)).Should().BeFalse("one decision wins");
			(await requests.GetByStatusAsync(Dept, (int)ApprovalRequestStatuses.Approved, 10)).Select(r => r.ApprovalRequestId).Should().Contain(approval.ApprovalRequestId);
		}

		[Test]
		public async Task Worker_queries_find_only_due_work_and_each_claim_runs_once()
		{
			var steps = Repository((c, s, u, q) => new ApprovalRequestStepsRepository(c, s, u, q));
			var requests = Repository((c, s, u, q) => new ApprovalRequestsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var turned = await SeedAsync(Dept, "W-TURNED", turnStartedOn: now.AddMinutes(-5));
			var notYet = await SeedAsync(Dept, "W-NOTYET");
			var signed = await SeedAsync(Dept, "W-SIGNED", turnStartedOn: now.AddMinutes(-5), signerStatus: (int)SignatureSignerStatuses.Signed);
			var decided = await SeedAsync(Dept, "W-DECIDED", approvalStatus: (int)ApprovalRequestStatuses.Rejected, turnStartedOn: now.AddMinutes(-5));
			var due = await SeedAsync(Dept, "W-DUE", turnStartedOn: now.AddHours(-30), escalateDueOn: now.AddHours(-6));
			var later = await SeedAsync(Dept, "W-LATER", turnStartedOn: now.AddHours(-1), escalateDueOn: now.AddHours(23));
			var closed = await SeedAsync(Dept, "W-CLOSED", requestStatus: (int)SignatureRequestStatuses.Declined);

			var unannounced = (await steps.GetUnannouncedTurnsAsync(500)).Select(s => s.ApprovalRequestStepId).ToList();
			unannounced.Should().Contain(new[] { turned.Step.ApprovalRequestStepId, due.Step.ApprovalRequestStepId, later.Step.ApprovalRequestStepId });
			unannounced.Should().NotContain(new[] { notYet.Step.ApprovalRequestStepId, signed.Step.ApprovalRequestStepId, decided.Step.ApprovalRequestStepId });

			(await steps.ClaimAnnouncementAsync(turned.Step.ApprovalRequestStepId, now, now.AddHours(24))).Should().BeTrue();
			(await steps.ClaimAnnouncementAsync(turned.Step.ApprovalRequestStepId, now, now.AddHours(24))).Should().BeFalse("announced once");
			(await steps.ClaimAnnouncementAsync(later.Step.ApprovalRequestStepId, now, null)).Should().BeTrue("a step without escalation claims without an escalation time");
			(await steps.GetUnannouncedTurnsAsync(500)).Select(s => s.ApprovalRequestStepId).Should().NotContain(new[] { turned.Step.ApprovalRequestStepId, later.Step.ApprovalRequestStepId });
			(await steps.GetForRequestAsync(Dept, turned.Approval.ApprovalRequestId)).Single().EscalateDueOn.Should().BeCloseTo(now.AddHours(24), TimeSpan.FromSeconds(1));

			var dueNow = (await steps.GetDueEscalationsAsync(now, 500)).Select(s => s.ApprovalRequestStepId).ToList();
			dueNow.Should().Contain(due.Step.ApprovalRequestStepId).And.NotContain(turned.Step.ApprovalRequestStepId);
			(await steps.ClaimEscalationAsync(due.Step.ApprovalRequestStepId, now)).Should().BeTrue();
			(await steps.ClaimEscalationAsync(due.Step.ApprovalRequestStepId, now)).Should().BeFalse("escalated once");
			(await steps.GetDueEscalationsAsync(now.AddHours(25), 500)).Select(s => s.ApprovalRequestStepId).Should().Contain(turned.Step.ApprovalRequestStepId)
				.And.NotContain(due.Step.ApprovalRequestStepId);

			var closedPending = (await requests.GetPendingWithClosedSignatureAsync(500)).Select(r => r.ApprovalRequestId).ToList();
			closedPending.Should().Contain(closed.Approval.ApprovalRequestId).And.NotContain(turned.Approval.ApprovalRequestId);
		}

		[Test]
		public async Task Mutual_aid_agreements_hide_deleted_rows_and_the_purge_removes_only_the_department()
		{
			var agreements = Repository((c, s, u, q) => new MutualAidAgreementsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var kept = await agreements.InsertAsync(new MutualAidAgreement { DepartmentId = Dept, Title = "County", Terms = "30 minutes", Status = 0, CreatedOn = now }, default, true);
			var deleted = await agreements.InsertAsync(new MutualAidAgreement { DepartmentId = Dept, Title = "Old", Status = 0, IsDeleted = true, CreatedOn = now }, default, true);
			await agreements.InsertAsync(new MutualAidAgreement { DepartmentId = OtherDept, Title = "Other", Status = 0, CreatedOn = now }, default, true);
			(await agreements.GetAllForDepartmentAsync(Dept)).Select(a => a.MutualAidAgreementId).Should().Contain(kept.MutualAidAgreementId).And.NotContain(deleted.MutualAidAgreementId);
			(await agreements.GetForDepartmentAsync(Dept, kept.MutualAidAgreementId)).Terms.Should().Be("30 minutes");

			await SeedAsync(Dept, "P-1");
			var other = await SeedAsync(OtherDept, "P-2");
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Dept, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "ApprovalRequestSteps", "ApprovalRequests", "ApprovalChainSteps", "ApprovalChains", "ApprovalDelegates", "MutualAidAgreements" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Dept })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("ApprovalRequests")} WHERE {T("ApprovalRequestId")} = @Id", new { Id = other.Approval.ApprovalRequestId })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("MutualAidAgreements")} WHERE {T("DepartmentId")} = @D", new { D = OtherDept })).Should().Be(1);
		}
	}
}
