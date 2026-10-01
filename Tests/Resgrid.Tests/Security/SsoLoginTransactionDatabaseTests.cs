using System;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
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
using Resgrid.Model.Security;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Real-database proof for M0250, M0251 and M0257 on both engines: an IdP result is accepted once and a brokered code redeems
	/// once however many nodes race, a failed transaction stays failed, and the SAML IdP SSO URL column exists; a provider
	/// step-up keeps its binding and matched value, and the mapping version advances once per change while a test records
	/// only against the version it tested. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SsoLoginTransactionDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "sso_login_tx_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		// Whole seconds: SQL Server datetime rounds to 1/300 s, and the assertions compare stored instants.
		private static DateTime Now
		{
			get
			{
				var now = DateTime.UtcNow;
				return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
			}
		}

		private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return connections.Object;
		}

		private SsoLoginTransactionRepository Transactions() => new(Connections(), Configuration());

		private DepartmentSsoConfigRepository Configs() => new(Connections(), Configuration(),
			Mock.Of<Resgrid.Model.Repositories.Queries.IUnitOfWork>(), Mock.Of<Resgrid.Model.Repositories.Queries.IQueryFactory>());

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(type == DatabaseTypes.Postgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database concurrency checks.");
			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = type == DatabaseTypes.Postgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// M0250 and M0251 add columns to DepartmentSsoConfigs; a minimal table stands in for M0046's.
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(type == DatabaseTypes.Postgres
					? "CREATE TABLE departmentssoconfigs (departmentssoconfigid varchar(128) PRIMARY KEY)"
					: "CREATE TABLE DepartmentSsoConfigs (DepartmentSsoConfigId nvarchar(128) PRIMARY KEY)");

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(type == DatabaseTypes.Postgres
				? new IMigration[] { new M0250_AddBrokeredSsoPg(), new M0251_AddFederatedMfaMappingPg(), new M0257_AddSsoTransactionSharedInstallationPg() }
				: new IMigration[] { new M0250_AddBrokeredSso(), new M0251_AddFederatedMfaMapping(), new M0257_AddSsoTransactionSharedInstallation() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		private static SsoLoginTransaction NewTransaction(DateTime now, int lifetimeSeconds = 600) => new()
		{
			SsoLoginTransactionId = Guid.NewGuid().ToString(),
			StateHash = RandomNumberGenerator.GetBytes(32),
			Purpose = (int)SsoTransactionPurpose.Login,
			DepartmentId = 42,
			DepartmentSsoConfigId = "oidc-config",
			ProviderType = (int)SsoProviderType.Oidc,
			ClientApplication = (int)UserSessionClientApplication.Unit,
			Platform = "ios",
			ReturnTarget = "resgridunit://sso-return",
			ClientState = "app-csrf",
			CodeChallenge = new string('a', 43),
			NonceHash = RandomNumberGenerator.GetBytes(32),
			EncryptedIdpCodeVerifier = "encrypted-verifier",
			CreatedOnUtc = now,
			ExpiresOnUtc = now.AddSeconds(lifetimeSeconds),
			State = (int)SsoLoginTransactionState.Pending
		};

		[Test]
		public async Task A_transaction_round_trips_by_id_and_state_hash()
		{
			var transaction = NewTransaction(Now);
			await Transactions().InsertAsync(transaction);

			var byState = await Transactions().GetByStateHashAsync(transaction.StateHash);
			byState.Should().BeEquivalentTo(transaction, o => o.Excluding(t => t.CreatedOnUtc).Excluding(t => t.ExpiresOnUtc)
				.Excluding(t => t.TransactionPurpose).Excluding(t => t.TransactionState));
			(await Transactions().GetAsync(transaction.SsoLoginTransactionId)).SsoLoginTransactionId.Should().Be(transaction.SsoLoginTransactionId);
			(await Transactions().GetByStateHashAsync(RandomNumberGenerator.GetBytes(32))).Should().BeNull();

			// M0257: a shared installation's round trip says so, for its callback's freshness check.
			var shared = NewTransaction(Now);
			shared.SharedInstallation = true;
			await Transactions().InsertAsync(shared);
			(await Transactions().GetAsync(shared.SsoLoginTransactionId)).SharedInstallation.Should().BeTrue();
			(await Transactions().GetAsync(transaction.SsoLoginTransactionId)).SharedInstallation.Should().BeFalse();
		}

		[Test]
		public async Task Concurrent_callbacks_have_exactly_one_winner()
		{
			var now = Now;
			var transaction = NewTransaction(now);
			await Transactions().InsertAsync(transaction);

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Transactions().TryAuthenticateAsync(transaction.SsoLoginTransactionId,
				"user-" + i, now, "amr:mfa", RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now)));

			winners.Count(w => w).Should().Be(1);
			var stored = await Transactions().GetAsync(transaction.SsoLoginTransactionId);
			stored.TransactionState.Should().Be(SsoLoginTransactionState.Authenticated);
			stored.UserId.Should().StartWith("user-");
			stored.FederatedMfaValue.Should().Be("amr:mfa");
			(await Transactions().TryFailAsync(transaction.SsoLoginTransactionId, "late")).Should().BeFalse("an accepted result is never failed afterwards");
		}

		[Test]
		public async Task Concurrent_redemptions_have_exactly_one_winner_and_only_with_the_right_code()
		{
			var now = Now;
			var transaction = NewTransaction(now);
			var code = RandomNumberGenerator.GetBytes(32);
			await Transactions().InsertAsync(transaction);
			(await Transactions().TryRedeemAsync(transaction.SsoLoginTransactionId, code, now)).Should().BeFalse("nothing is redeemable before the callback");
			await Transactions().TryAuthenticateAsync(transaction.SsoLoginTransactionId, "user-1", now, null, code, now.AddSeconds(60), now);

			(await Transactions().TryRedeemAsync(transaction.SsoLoginTransactionId, RandomNumberGenerator.GetBytes(32), now)).Should().BeFalse();
			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Transactions().TryRedeemAsync(transaction.SsoLoginTransactionId, code, now)));

			winners.Count(w => w).Should().Be(1);
			(await Transactions().GetAsync(transaction.SsoLoginTransactionId)).TransactionState.Should().Be(SsoLoginTransactionState.Redeemed);
		}

		[Test]
		public async Task Expired_or_failed_transactions_accept_nothing()
		{
			var now = Now;
			var expired = NewTransaction(now, lifetimeSeconds: -1);
			await Transactions().InsertAsync(expired);
			(await Transactions().TryAuthenticateAsync(expired.SsoLoginTransactionId, "user-1", now, null, RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now))
				.Should().BeFalse();

			var failed = NewTransaction(now);
			await Transactions().InsertAsync(failed);
			(await Transactions().TryFailAsync(failed.SsoLoginTransactionId, "id_token_invalid")).Should().BeTrue();
			(await Transactions().TryFailAsync(failed.SsoLoginTransactionId, "again")).Should().BeFalse();
			(await Transactions().TryAuthenticateAsync(failed.SsoLoginTransactionId, "user-1", now, null, RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now))
				.Should().BeFalse();
			(await Transactions().GetAsync(failed.SsoLoginTransactionId)).FailureCode.Should().Be("id_token_invalid");

			var lateCode = NewTransaction(now);
			var code = RandomNumberGenerator.GetBytes(32);
			await Transactions().InsertAsync(lateCode);
			await Transactions().TryAuthenticateAsync(lateCode.SsoLoginTransactionId, "user-1", now, null, code, now.AddSeconds(60), now);
			(await Transactions().TryRedeemAsync(lateCode.SsoLoginTransactionId, code, now.AddSeconds(61))).Should().BeFalse();
		}

		[Test]
		public async Task Purge_removes_only_long_expired_transactions()
		{
			var now = Now;
			var old = NewTransaction(now.AddHours(-3));
			var current = NewTransaction(now);
			await Transactions().InsertAsync(old);
			await Transactions().InsertAsync(current);

			(await Transactions().PurgeExpiredBeforeAsync(now.AddHours(-1))).Should().BeGreaterThanOrEqualTo(1);

			(await Transactions().GetAsync(old.SsoLoginTransactionId)).Should().BeNull();
			(await Transactions().GetAsync(current.SsoLoginTransactionId)).Should().NotBeNull();
		}

		[Test]
		public async Task Sso_configs_gain_the_idp_sso_url_column()
		{
			await using var connection = Connect(_connection);
			await connection.ExecuteAsync(type == DatabaseTypes.Postgres
				? "INSERT INTO departmentssoconfigs (departmentssoconfigid, idpssourl) VALUES ('c1', 'https://idp.example.test/saml2/sso')"
				: "INSERT INTO DepartmentSsoConfigs (DepartmentSsoConfigId, IdpSsoUrl) VALUES ('c1', 'https://idp.example.test/saml2/sso')");
			(await connection.ExecuteScalarAsync<string>(type == DatabaseTypes.Postgres
				? "SELECT idpssourl FROM departmentssoconfigs WHERE departmentssoconfigid = 'c1'"
				: "SELECT IdpSsoUrl FROM DepartmentSsoConfigs WHERE DepartmentSsoConfigId = 'c1'")).Should().Be("https://idp.example.test/saml2/sso");
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		// ---- Provider step-up (M0251) ----------------------------------------------------------------------------------

		[Test]
		public async Task A_step_up_keeps_its_binding_and_the_value_the_mapping_matched()
		{
			var now = Now;
			var transaction = NewTransaction(now);
			transaction.Purpose = (int)SsoTransactionPurpose.StepUp;
			transaction.Operation = MfaStepUpOperations.SecurityChange;
			transaction.LoginTransactionId = Guid.NewGuid().ToString();
			transaction.FederatedMappingVersion = 7;
			transaction.SessionId = "session-1";
			transaction.ExpectedUserId = "user-1";
			transaction.AuthenticationGeneration = 4;
			await Transactions().InsertAsync(transaction);

			var matched = "authncontext:" + new string('x', 256);
			(await Transactions().TryAuthenticateAsync(transaction.SsoLoginTransactionId, "user-1", now, matched, RandomNumberGenerator.GetBytes(32),
				now.AddMinutes(1), now)).Should().BeTrue();

			var stored = await Transactions().GetAsync(transaction.SsoLoginTransactionId);
			stored.Operation.Should().Be(MfaStepUpOperations.SecurityChange);
			stored.LoginTransactionId.Should().Be(transaction.LoginTransactionId);
			stored.FederatedMappingVersion.Should().Be(7);
			stored.FederatedMfaValue.Should().Be(matched, "the longest value a mapping accepts fits with its kind prefix");
		}

		private async Task<string> NewConfig()
		{
			var id = Guid.NewGuid().ToString();
			await using var connection = Connect(_connection);
			await connection.ExecuteAsync(type == DatabaseTypes.Postgres
				? "INSERT INTO departmentssoconfigs (departmentssoconfigid) VALUES (@Id)"
				: "INSERT INTO DepartmentSsoConfigs (DepartmentSsoConfigId) VALUES (@Id)", new { Id = id });
			return id;
		}

		private async Task<(long Version, long? Tested, string TestedBy)> MappingState(string id)
		{
			await using var connection = Connect(_connection);
			return await connection.QuerySingleAsync<(long, long?, string)>(type == DatabaseTypes.Postgres
				? "SELECT federatedmfamappingversion, federatedmfatestedversion, federatedmfatestedbyuserid FROM departmentssoconfigs WHERE departmentssoconfigid = @Id"
				: "SELECT FederatedMfaMappingVersion, FederatedMfaTestedVersion, FederatedMfaTestedByUserId FROM DepartmentSsoConfigs WHERE DepartmentSsoConfigId = @Id",
				new { Id = id });
		}

		[Test]
		public async Task A_test_records_only_against_the_version_it_tested_and_a_change_clears_it()
		{
			var id = await NewConfig();
			(await MappingState(id)).Should().Be((0L, (long?)null, (string)null), "existing rows start with no mapping");

			(await Configs().AdvanceFederatedMfaMappingVersionAsync(id)).Should().Be(1);
			(await Configs().TryRecordFederatedMfaTestAsync(id, 0, "manager", Now)).Should().BeFalse("version 0 is not the mapping now");
			(await Configs().TryRecordFederatedMfaTestAsync(id, 1, "manager", Now)).Should().BeTrue();
			(await MappingState(id)).Should().Be((1L, (long?)1, "manager"));

			(await Configs().AdvanceFederatedMfaMappingVersionAsync(id)).Should().Be(2);
			(await MappingState(id)).Should().Be((2L, (long?)null, (string)null), "advancing clears the test in the same statement");
			(await Configs().TryRecordFederatedMfaTestAsync(id, 1, "manager", Now)).Should().BeFalse("a test of the old mapping arrives too late");
			(await MappingState(id)).Tested.Should().BeNull();
		}

		[Test]
		public async Task Concurrent_changes_each_get_their_own_version_and_a_racing_test_never_marks_the_new_one_tested()
		{
			var id = await NewConfig();
			var versions = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Configs().AdvanceFederatedMfaMappingVersionAsync(id))));
			versions.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enumerable.Range(1, 16).Select(v => (long)v));

			for (var round = 0; round < 8; round++)
			{
				var tested = (await MappingState(id)).Version;
				await Task.WhenAll(
					Task.Run(() => Configs().TryRecordFederatedMfaTestAsync(id, tested, "manager", Now)),
					Task.Run(() => Configs().AdvanceFederatedMfaMappingVersionAsync(id)));

				var state = await MappingState(id);
				state.Version.Should().Be(tested + 1);
				state.Tested.Should().BeNull("either the test landed first and the change cleared it, or the change landed first and the test was refused");
			}
		}
	}
}
