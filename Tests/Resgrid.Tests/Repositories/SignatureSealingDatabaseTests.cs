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
	/// Real-database proof for e-Signature S1/S2 (M0356–M0359) on both engines: the migrations re-run as no-ops, roll back and
	/// apply again; the repositories' SQL (due-work queries, conditional claims, the single-use link consumption, the webhook
	/// hint's insert-if-absent, the IN-list on PostgreSQL); an external signature with no member (SignerUserId nullable); and the
	/// department cleanup of the new tables. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SignatureSealingDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "esign_s1s2_";
		private const int Dept = 4101;
		private const int OtherDept = 4102;

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

		private async Task<bool> TableExistsAsync(string table) =>
			await ScalarAsync(IsPostgres ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @T" : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @T",
				new { T = IsPostgres ? table.ToLowerInvariant() : table }) == 1;

		[Test, Order(1)]
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 356");

			using var runner = Runner();
			using var scope = runner.CreateScope();
			var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

			// Up a second time over the existing schema: every guard holds.
			migrations.MigrateUp();
			foreach (var table in new[] { "SealedDocuments", "SealedDocumentInputs", "SignatureRequests", "SignatureRequestSigners", "SignatureCaptures", "ESignProviderAccounts", "ESignWebhookEvents" })
				(await TableExistsAsync(table)).Should().BeTrue(table);

			// An external signature (no member) exists before the rollback; Down removes it with the columns.
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"INSERT INTO {T("SignatureRecords")} ({T("DepartmentId")}, {T("SubjectKind")}, {T("SubjectId")}, {T("SignerUserId")}, {T("ExternalSignerName")}, {T("Intent")}, {T("Level")}, {T("Method")}, {T("ArtifactSha256")}, {T("SignedOn")}, {T("CreatedOn")}) " +
					$"VALUES (@D, 6, 'dtr-x', NULL, 'Casey', 0, 4, 3, @H, @N, @N)", new { D = Dept, H = new string('a', 64), N = DateTime.UtcNow });

			migrations.MigrateDown(355);
			(await TableExistsAsync("SealedDocuments")).Should().BeFalse();
			(await TableExistsAsync("ESignWebhookEvents")).Should().BeFalse();
			(await ScalarAsync(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'signaturerecords' AND column_name = 'externalsignername'"
				: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SignatureRecords') AND name = 'ExternalSignerName'")).Should().Be(0);
			(await TableExistsAsync("SignatureRecords")).Should().BeTrue("S0's table stays");

			migrations.MigrateUp();
			(await TableExistsAsync("SignatureCaptures")).Should().BeTrue();
		}

		[Test, Order(2)]
		public async Task Sealed_documents_are_found_by_subject_and_bytes_and_claimed_once()
		{
			var repository = Repository((c, s, u, q) => new SealedDocumentsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var pending = await repository.InsertAsync(new SealedDocument
			{
				DepartmentId = Dept, SubjectKind = 4, SubjectId = "LR-42", RevisionSha256 = new string('a', 64), Status = (int)SealedDocumentStatuses.Pending,
				NextAttemptOn = now.AddMinutes(-1), CreatedOn = now
			}, default, true);
			var sealedDoc = await repository.InsertAsync(new SealedDocument
			{
				DepartmentId = Dept, SubjectKind = 4, SubjectId = "LR-42", RevisionSha256 = new string('b', 64), Status = (int)SealedDocumentStatuses.Sealed, Sha256 = new string('c', 64),
				PadesLevel = (int)PadesLevels.BaselineLTA, RetimestampDueOn = now.AddMinutes(-5), CreatedOn = now
			}, default, true);
			pending.SealedDocumentId.Should().BeGreaterThan(0);

			(await repository.GetForSubjectAsync(Dept, 4, "lr-42", 0, null)).Should().HaveCount(2, "the subject id compares case-insensitively");
			(await repository.GetForSubjectAsync(Dept, 4, "LR-42", 0, new string('b', 64))).Single().SealedDocumentId.Should().Be(sealedDoc.SealedDocumentId);
			(await repository.GetForSubjectAsync(OtherDept, 4, "LR-42", 0, null)).Should().BeEmpty();
			(await repository.GetBySha256Async(new string('C', 64))).Single().SealedDocumentId.Should().Be(sealedDoc.SealedDocumentId);
			(await repository.GetByIdForDepartmentAsync(OtherDept, sealedDoc.SealedDocumentId)).Should().BeNull();

			(await repository.GetDuePendingAsync(now, now.AddMinutes(-15), 10)).Select(d => d.SealedDocumentId).Should().Equal(pending.SealedDocumentId);
			(await repository.ClaimAsync(pending.SealedDocumentId, now.AddMinutes(-15), now)).Should().BeTrue();
			(await repository.ClaimAsync(pending.SealedDocumentId, now.AddMinutes(-15), now)).Should().BeFalse("a fresh claim is held");
			(await repository.GetDuePendingAsync(now, now.AddMinutes(-15), 10)).Should().BeEmpty();
			(await repository.ClaimAsync(pending.SealedDocumentId, now.AddMinutes(1), now.AddMinutes(1))).Should().BeTrue("a stale claim is taken over");
			await repository.ReleaseAsync(pending.SealedDocumentId);
			(await repository.GetDuePendingAsync(now, now.AddMinutes(-15), 10)).Should().HaveCount(1);

			(await repository.GetDueRetimestampAsync(now, now.AddMinutes(-15), 10)).Select(d => d.SealedDocumentId).Should().Equal(sealedDoc.SealedDocumentId);

			var inputs = Repository((c, s, u, q) => new SealedDocumentInputsRepository(c, s, u, q));
			await inputs.InsertAsync(new SealedDocumentInput { SealedDocumentId = pending.SealedDocumentId, DepartmentId = Dept, Html = "enc2:abc", PageSize = "Letter", CreatedOn = now }, default, true);
			(await inputs.GetForDocumentAsync(Dept, pending.SealedDocumentId)).Html.Should().Be("enc2:abc");
			(await inputs.GetForDocumentAsync(OtherDept, pending.SealedDocumentId)).Should().BeNull();
			(await inputs.DeleteForDocumentAsync(Dept, pending.SealedDocumentId)).Should().Be(1);
		}

		[Test, Order(3)]
		public async Task Requests_and_signers_follow_the_link_session_and_due_work_rules()
		{
			var requests = Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q));
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var open = await requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = Dept, SubjectKind = 6, SubjectId = "DTR-1", ArtifactSha256 = new string('a', 64), RequiredLevel = 4, Status = (int)SignatureRequestStatuses.Sent,
				Channel = (int)SignatureRequestChannels.Remote, ExpiresOn = now.AddMinutes(-1), ProviderKind = 1, ProviderEnvelopeId = "envelope_Ab", ReconcileAfter = now.AddMinutes(-1), CreatedOn = now
			}, default, true);
			var done = await requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = Dept, SubjectKind = 6, SubjectId = "dtr-1", ArtifactSha256 = new string('a', 64), RequiredLevel = 4, Status = (int)SignatureRequestStatuses.Completed,
				Channel = (int)SignatureRequestChannels.InPerson, ExpiresOn = now.AddMinutes(-1), CreatedOn = now
			}, default, true);

			(await requests.GetForSubjectAsync(Dept, 6, "dtr-1")).Should().HaveCount(2);
			(await requests.GetByEnvelopeAsync(1, "envelope_Ab")).SignatureRequestId.Should().Be(open.SignatureRequestId);
			if (IsPostgres) // varchar on PostgreSQL; SQL Server follows its (case-insensitive) column collation
				(await requests.GetByEnvelopeAsync(1, "envelope_ab")).Should().BeNull("a provider id is compared exactly");
			(await requests.GetDueReconcileAsync(now, now.AddMinutes(-10), 10)).Select(r => r.SignatureRequestId).Should().Equal(open.SignatureRequestId);
			(await requests.GetDueExpiryAsync(now, 10)).Select(r => r.SignatureRequestId).Should().Equal(open.SignatureRequestId);
			(await requests.ClaimAsync(open.SignatureRequestId, now.AddMinutes(-10), now)).Should().BeTrue();
			(await requests.GetDueReconcileAsync(now, now.AddMinutes(-10), 10)).Should().BeEmpty();
			await requests.ReleaseAsync(open.SignatureRequestId);

			var link = new string('1', 64);
			var signer = await signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = open.SignatureRequestId, DepartmentId = Dept, SignerOrder = 1, Role = 0, ExternalName = "Casey", ExternalEmailEncrypted = "enc2:x",
				ContactMasked = "c***@example.com", AuthMethod = 3, Status = 0, LinkTokenHash = link, LinkTokenExpiresOn = now.AddDays(1), CreatedOn = now
			}, default, true);
			await signers.InsertAsync(new SignatureRequestSigner { SignatureRequestId = done.SignatureRequestId, DepartmentId = Dept, Role = 0, AuthMethod = 5, Status = 3, CreatedOn = now }, default, true);

			(await signers.GetForRequestsAsync(Dept, new[] { open.SignatureRequestId, done.SignatureRequestId })).Should().HaveCount(2, "the IN-list works on both engines");
			(await signers.GetByLinkTokenHashAsync(link)).SignatureRequestSignerId.Should().Be(signer.SignatureRequestSignerId);

			var session = new string('2', 64);
			(await signers.ConsumeLinkTokenAsync(signer.SignatureRequestSignerId, link, session, now.AddHours(1), now)).Should().BeTrue();
			(await signers.ConsumeLinkTokenAsync(signer.SignatureRequestSignerId, link, new string('3', 64), now.AddHours(1), now)).Should().BeFalse("the link is single use");
			var opened = await signers.GetBySessionHashAsync(session);
			opened.LinkTokenUsedOn.Should().NotBeNull();
			opened.Status.Should().Be((int)SignatureSignerStatuses.Opened);

			var expired = await signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = open.SignatureRequestId, DepartmentId = Dept, Role = 0, AuthMethod = 3, Status = 0, LinkTokenHash = new string('4', 64), LinkTokenExpiresOn = now.AddMinutes(-1), CreatedOn = now
			}, default, true);
			(await signers.ConsumeLinkTokenAsync(expired.SignatureRequestSignerId, new string('4', 64), new string('5', 64), now.AddHours(1), now)).Should().BeFalse("an expired link is refused");
		}

		[Test, Order(4)]
		public async Task An_external_signature_has_no_member_and_captures_accounts_and_hints_persist()
		{
			var records = Repository((c, s, u, q) => new SignatureRecordsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var record = await records.InsertAsync(new SignatureRecord
			{
				DepartmentId = Dept, SubjectKind = 9, SubjectId = "INSP-1", SignerUserId = null, ExternalSignerName = "Olive Occupant", SignerNameSnapshot = "Olive Occupant",
				Intent = 0, Level = 4, Method = 3, ArtifactSha256 = new string('d', 64), SignedOn = now, SignatureRequestId = 1, CapturedByUserId = "member", CreatedOn = now
			}, default, true);
			(await records.GetForSubjectAsync(Dept, 9, "insp-1")).Single().ExternalSignerName.Should().Be("Olive Occupant");
			record.SignatureRecordId.Should().BeGreaterThan(0);

			var captures = Repository((c, s, u, q) => new SignatureCapturesRepository(c, s, u, q));
			await captures.InsertAsync(new SignatureCapture
			{
				DepartmentId = Dept, SignatureRequestId = 77, SignatureRequestSignerId = 5, Method = 3, BoundRevisionSha256 = new string('d', 64), SignatureImageEncrypted = "enc2:img",
				CapturedOn = now, ReceivedOn = now
			}, default, true);
			(await captures.GetForRequestAsync(Dept, 77)).Should().HaveCount(1);

			var accounts = Repository((c, s, u, q) => new ESignProviderAccountsRepository(c, s, u, q));
			await accounts.InsertAsync(new ESignProviderAccount { DepartmentId = Dept, ProviderKind = 1, TeamId = "team_1", ApiTokenEncrypted = "enc2:tok", TokenHint = "1234", IsActive = true, CreatedOn = now }, default, true);
			(await accounts.GetForDepartmentAsync(Dept, 1)).TeamId.Should().Be("team_1");
			FluentActions.Awaiting(() => accounts.InsertAsync(new ESignProviderAccount { DepartmentId = Dept, ProviderKind = 1, IsActive = true, CreatedOn = now }, default, true))
				.Should().ThrowAsync<Exception>("one account per department and provider");

			var hints = Repository((c, s, u, q) => new ESignWebhookEventsRepository(c, s, u, q));
			var hint = new ESignWebhookEvent { ESignWebhookEventId = "1:evt-1", ProviderKind = 1, ProviderEventId = "evt-1", EventType = "DOCUMENT_SIGNED", EnvelopeId = "envelope_1", State = 0, ReceivedOn = now.AddDays(-8) };
			(await hints.TryInsertAsync(hint)).Should().BeTrue();
			(await hints.TryInsertAsync(hint)).Should().BeFalse("a redelivered hint is recorded once");
			(await hints.GetDueAsync(now.AddMinutes(-5), 10, 10)).Should().HaveCount(1);
			(await hints.ClaimAsync("1:evt-1", now.AddMinutes(-5), now)).Should().BeTrue();
			(await hints.ClaimAsync("1:evt-1", now.AddMinutes(-5), now)).Should().BeFalse();
			await hints.MarkAsync("1:evt-1", ESignWebhookEventStates.Done, now, null);
			(await hints.GetDueAsync(now.AddMinutes(10), 10, 10)).Should().BeEmpty();
			(await hints.PurgeReceivedBeforeAsync(now.AddDays(-7))).Should().Be(1);
		}

		[Test, Order(5)]
		public async Task Department_cleanup_removes_the_new_tables_for_that_department_only()
		{
			var now = DateTime.UtcNow;
			var documents = Repository((c, s, u, q) => new SealedDocumentsRepository(c, s, u, q));
			await documents.InsertAsync(new SealedDocument { DepartmentId = OtherDept, SubjectKind = 4, SubjectId = "x", RevisionSha256 = new string('a', 64), Status = 0, CreatedOn = now }, default, true);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Dept, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "SignatureCaptures", "SignatureRequestSigners", "SignatureRequests", "SealedDocumentInputs", "SealedDocuments", "ESignProviderAccounts", "SignatureRecords" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Dept })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("SealedDocuments")} WHERE {T("DepartmentId")} = @D", new { D = OtherDept })).Should().Be(1, "another department's rows are untouched");
		}

		#region Security review M4 (2026-11-25): the one-time-code attempt cap under parallel posts

		private const string Code = "246810";

		/// <summary>An open remote request and a consented signer holding <see cref="Code"/> (unexpired, no attempts) and a signing session.</summary>
		private async Task<(SignatureRequestSigner Signer, string Session)> SignerWithCodeAsync()
		{
			var requests = Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q));
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var request = await requests.InsertAsync(new SignatureRequestRecord
			{
				DepartmentId = OtherDept, SubjectKind = 6, SubjectId = "DTR-OTP", ArtifactSha256 = new string('a', 64), RequiredLevel = 4, Status = (int)SignatureRequestStatuses.Sent,
				Channel = (int)SignatureRequestChannels.Remote, ExpiresOn = now.AddDays(1), ProviderKind = 1, CreatedOn = now
			}, default, true);
			var session = SignatureTokens.NewToken();
			var signer = await signers.InsertAsync(new SignatureRequestSigner
			{
				SignatureRequestId = request.SignatureRequestId, DepartmentId = OtherDept, SignerOrder = 1, Role = 0, ExternalName = "Casey", AuthMethod = (int)SignatureSignerAuthMethods.EmailOtp,
				Status = (int)SignatureSignerStatuses.Opened, SessionHash = SignatureTokens.Hash(session), SessionExpiresOn = now.AddHours(1), ConsentedOn = now,
				ConsentTextVersion = SignatureTokens.ConsentTextVersion, OtpExpiresOn = now.AddMinutes(10), OtpAttempts = 0, OtpSentOn = now, CreatedOn = now
			}, default, true);
			signer.OtpHash = SignatureTokens.CodeHash(signer.SignatureRequestSignerId, Code);
			await signers.UpdateAsync(signer, default, true);
			return (signer, session);
		}

		private Resgrid.Services.Signatures.SignatureRequestService SigningPage() => new Resgrid.Services.Signatures.SignatureRequestService(
			Repository((c, s, u, q) => new SignatureRequestsRepository(c, s, u, q)), Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q)),
			Mock.Of<Resgrid.Model.Repositories.ISignatureCapturesRepository>(), Mock.Of<Resgrid.Model.Repositories.ISignatureRecordsRepository>(),
			Mock.Of<Resgrid.Model.Repositories.IESignWebhookEventsRepository>(), Mock.Of<Resgrid.Model.Repositories.IRmsSignaturesRepository>(),
			Array.Empty<Resgrid.Model.Services.ISignatureSubjectSource>(), Mock.Of<Resgrid.Model.Providers.IESignProvider>(), Mock.Of<Resgrid.Model.Services.IESignAccountService>(),
			Mock.Of<Resgrid.Model.Services.IDocumentSealingService>(), Mock.Of<Resgrid.Model.Providers.IPdfProvider>(), Mock.Of<Resgrid.Model.Services.IEncryptionService>(),
			Mock.Of<Resgrid.Model.Providers.IEmailSender>(), Mock.Of<Resgrid.Model.Providers.ITextMessageProvider>(), Mock.Of<Resgrid.Model.Services.IDepartmentsService>(),
			Mock.Of<Resgrid.Model.Services.IDepartmentSettingsService>(), Mock.Of<Resgrid.Model.Services.IBusinessOperationsAccessService>(),
			Mock.Of<Resgrid.Model.Services.IReadinessAccessService>(), Mock.Of<Resgrid.Model.Services.IIncidentBackOfficeAccessService>(), Mock.Of<Resgrid.Model.Services.ILmsAccessService>(),
			Mock.Of<Resgrid.Model.Providers.ICacheProvider>(), Mock.Of<Resgrid.Model.Providers.IEventAggregator>(), Mock.Of<Resgrid.Model.Services.ISignatureService>(),
			Mock.Of<Resgrid.Model.Services.ISignerEligibilityService>(), Mock.Of<Resgrid.Model.Services.ISignatureNotificationService>(),
			Mock.Of<Resgrid.Model.Services.IDepartmentDataProtectionService>(), new Lazy<Resgrid.Model.Services.IProtectedWriteService>(() => Mock.Of<Resgrid.Model.Services.IProtectedWriteService>()));

		private async Task<SignatureRequestSigner> StoredAsync(string session) =>
			await Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q)).GetBySessionHashAsync(SignatureTokens.Hash(session));

		[Test, Order(6)]
		public async Task Parallel_attempts_are_counted_atomically_and_never_pass_the_cap()
		{
			var (signer, _) = await SignerWithCodeAsync();
			var signers = Repository((c, s, u, q) => new SignatureRequestSignersRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var taken = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => signers.TryCountOtpAttemptAsync(signer.SignatureRequestSignerId, signer.OtpHash, 5, now))));

			taken.Count(t => t).Should().Be(5, "twenty parallel posts get exactly the five attempts");
			(await ScalarAsync($"SELECT {T("OtpAttempts")} FROM {T("SignatureRequestSigners")} WHERE {T("SignatureRequestSignerId")} = @Id", new { Id = signer.SignatureRequestSignerId })).Should().Be(5);
			(await signers.TryCountOtpAttemptAsync(signer.SignatureRequestSignerId, new string('f', 64), 5, now)).Should().BeFalse("another code never counts");
			await signers.WipeExhaustedOtpAsync(signer.SignatureRequestSignerId, signer.OtpHash, 5);
			(await signers.MarkOtpVerifiedAsync(signer.SignatureRequestSignerId, signer.OtpHash, now)).Should().BeFalse("a wiped code cannot be verified");
			(await signers.GetByIdAsync(signer.SignatureRequestSignerId)).OtpHash.Should().BeNull();

			var fresh = new string('d', 64);
			(await signers.IssueOtpAsync(signer.SignatureRequestSignerId, fresh, now.AddMinutes(10), now, (int)SignatureSignerAuthMethods.EmailOtp)).Should().BeTrue("a new code starts over");
			var reissued = await signers.GetByIdAsync(signer.SignatureRequestSignerId);
			(reissued.OtpHash, reissued.OtpAttempts).Should().Be((fresh, 0));
			(await signers.MarkOtpVerifiedAsync(signer.SignatureRequestSignerId, fresh, now)).Should().BeTrue();
			(await signers.MarkOtpVerifiedAsync(signer.SignatureRequestSignerId, fresh, now)).Should().BeFalse("recorded once");
			(await signers.IssueOtpAsync(signer.SignatureRequestSignerId, new string('e', 64), now.AddMinutes(10), now, (int)SignatureSignerAuthMethods.EmailOtp))
				.Should().BeFalse("a code sent after the verification never undoes it");
			var verified = await signers.GetByIdAsync(signer.SignatureRequestSignerId);
			verified.OtpVerifiedOn.Should().NotBeNull();
			verified.Status.Should().Be((int)SignatureSignerStatuses.Verified);
			verified.OtpHash.Should().BeNull();
		}

		[Test, Order(7)]
		public async Task Parallel_wrong_codes_on_the_signing_page_stop_at_the_cap_and_wipe_the_code()
		{
			var (signer, session) = await SignerWithCodeAsync();
			var page = SigningPage();

			var states = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => page.VerifyCodeAsync(session, (100000 + i).ToString()))));

			states.Should().OnlyContain(s => s.Step == SigningPageSteps.Code && s.EmbedUrl == null);
			var stored = await StoredAsync(session);
			stored.OtpAttempts.Should().Be(ESignConfig.MaxCodeAttempts, "no parallel post compares a code past the cap");
			stored.OtpHash.Should().BeNull("the exhausted code is wiped");
			stored.OtpVerifiedOn.Should().BeNull();
			(await page.VerifyCodeAsync(session, Code)).Step.Should().Be(SigningPageSteps.Code, "the right code no longer works once the attempts are used");
			signer.SignatureRequestSignerId.Should().Be(stored.SignatureRequestSignerId);
		}

		[Test, Order(8)]
		public async Task A_wrong_code_posted_alongside_the_right_one_never_undoes_the_verification()
		{
			for (var round = 0; round < 5; round++)
			{
				var (_, session) = await SignerWithCodeAsync();
				var page = SigningPage();
				var posts = new[] { "111111", Code, "222222", "333333" };

				await Task.WhenAll(posts.Select(code => Task.Run(() => page.VerifyCodeAsync(session, code))));

				var stored = await StoredAsync(session);
				stored.OtpVerifiedOn.Should().NotBeNull("round " + round + ": four posts fit in the cap, so the right one is compared and recorded");
				stored.Status.Should().Be((int)SignatureSignerStatuses.Verified);
				stored.OtpHash.Should().BeNull();
				stored.OtpAttempts.Should().BeLessThanOrEqualTo(ESignConfig.MaxCodeAttempts);
			}
		}

		#endregion
	}
}
