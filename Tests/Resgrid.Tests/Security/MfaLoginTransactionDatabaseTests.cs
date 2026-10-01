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
	/// Real-database proof for M0249 on both engines: a login transaction completes once and its code redeems once however
	/// many nodes race, failed attempts exhaust it in one statement, and the session's second-factor columns exist. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MfaLoginTransactionDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_login_tx_";
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

		private MfaLoginTransactionRepository Transactions() => new(Connections(), Configuration());

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

			// M0249 adds two columns to UserSessions; a minimal table stands in for M0121's.
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(type == DatabaseTypes.Postgres
					? "CREATE TABLE usersessions (usersessionid varchar(128) PRIMARY KEY)"
					: "CREATE TABLE UserSessions (UserSessionId nvarchar(128) PRIMARY KEY)");

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(type == DatabaseTypes.Postgres
				? new IMigration[] { new M0249_AddMfaLoginTransactionsPg(), new M0256_AddLoginTransactionInstallationPg() }
				: new IMigration[] { new M0249_AddMfaLoginTransactions(), new M0256_AddLoginTransactionInstallation() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		private static MfaLoginTransaction NewTransaction(DateTime now, int lifetimeSeconds = 300, int maxAttempts = 5) => new()
		{
			MfaLoginTransactionId = Guid.NewGuid().ToString(),
			SecretHash = RandomNumberGenerator.GetBytes(32),
			UserId = Guid.NewGuid().ToString(),
			DepartmentId = 42,
			ClientApplication = (int)UserSessionClientApplication.Unit,
			ClientId = "unit-app",
			FirstFactorMethod = (int)MfaEvidenceMethod.Password,
			FirstFactorVerifiedOnUtc = now.AddSeconds(-1),
			AuthenticationGeneration = 4,
			MfaPolicyVersion = 3,
			Scopes = "openid offline_access",
			CreatedOnUtc = now,
			ExpiresOnUtc = now.AddSeconds(lifetimeSeconds),
			MaxAttempts = maxAttempts,
			State = (int)MfaLoginTransactionState.Pending,
			SharedMode = true,
			InstallationLabel = "Engine 7 tablet"
		};

		[Test]
		public async Task A_transaction_round_trips_by_its_secret_hash()
		{
			var transaction = NewTransaction(Now);
			await Transactions().InsertAsync(transaction);

			var stored = await Transactions().GetBySecretHashAsync(transaction.SecretHash);

			stored.Should().BeEquivalentTo(transaction, o => o.Excluding(t => t.CreatedOnUtc).Excluding(t => t.ExpiresOnUtc)
				.Excluding(t => t.FirstFactorVerifiedOnUtc).Excluding(t => t.TransactionState));
			stored.FirstFactorVerifiedOnUtc.Should().BeCloseTo(transaction.FirstFactorVerifiedOnUtc, TimeSpan.FromMilliseconds(5));
			stored.TransactionState.Should().Be(MfaLoginTransactionState.Pending);
			(await Transactions().GetBySecretHashAsync(RandomNumberGenerator.GetBytes(32))).Should().BeNull();
		}

		[Test]
		public async Task Concurrent_completions_have_exactly_one_winner()
		{
			var now = Now;
			var transaction = NewTransaction(now);
			await Transactions().InsertAsync(transaction);

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Transactions().TryCompleteAsync(transaction.MfaLoginTransactionId,
				(int)MfaEvidenceMethod.Passkey, "passkey:" + i, now, false, RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now)));

			winners.Count(w => w).Should().Be(1);
			var stored = await Transactions().GetBySecretHashAsync(transaction.SecretHash);
			stored.TransactionState.Should().Be(MfaLoginTransactionState.Completed);
			stored.CompletionMethod.Should().Be((int)MfaEvidenceMethod.Passkey);
			stored.CompletionCodeHash.Should().HaveCount(32);
		}

		[Test]
		public async Task Concurrent_redemptions_have_exactly_one_winner_and_only_with_the_right_code()
		{
			var now = Now;
			var transaction = NewTransaction(now);
			var code = RandomNumberGenerator.GetBytes(32);
			await Transactions().InsertAsync(transaction);
			(await Transactions().TryRedeemAsync(transaction.MfaLoginTransactionId, code, now)).Should().BeFalse("nothing is redeemable before completion");
			await Transactions().TryCompleteAsync(transaction.MfaLoginTransactionId, (int)MfaEvidenceMethod.Totp, null, now, false, code, now.AddSeconds(60), now);

			(await Transactions().TryRedeemAsync(transaction.MfaLoginTransactionId, RandomNumberGenerator.GetBytes(32), now)).Should().BeFalse();
			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Transactions().TryRedeemAsync(transaction.MfaLoginTransactionId, code, now)));

			winners.Count(w => w).Should().Be(1);
			var stored = await Transactions().GetBySecretHashAsync(transaction.SecretHash);
			stored.TransactionState.Should().Be(MfaLoginTransactionState.Redeemed);
			stored.RedeemedOnUtc.Should().NotBeNull();
		}

		[Test]
		public async Task An_expired_transaction_or_code_is_refused()
		{
			var now = Now;
			var expired = NewTransaction(now, lifetimeSeconds: -1);
			await Transactions().InsertAsync(expired);
			(await Transactions().TryCompleteAsync(expired.MfaLoginTransactionId, (int)MfaEvidenceMethod.Totp, null, now, false,
				RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now)).Should().BeFalse();

			var lateCode = NewTransaction(now);
			var code = RandomNumberGenerator.GetBytes(32);
			await Transactions().InsertAsync(lateCode);
			await Transactions().TryCompleteAsync(lateCode.MfaLoginTransactionId, (int)MfaEvidenceMethod.Totp, null, now, false, code, now.AddSeconds(60), now);
			(await Transactions().TryRedeemAsync(lateCode.MfaLoginTransactionId, code, now.AddSeconds(61))).Should().BeFalse();
		}

		[Test]
		public async Task Failed_attempts_exhaust_the_transaction_under_concurrency()
		{
			var now = Now;
			var transaction = NewTransaction(now, maxAttempts: 5);
			await Transactions().InsertAsync(transaction);

			await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Transactions().RecordFailedAttemptAsync(transaction.MfaLoginTransactionId)));

			var stored = await Transactions().GetBySecretHashAsync(transaction.SecretHash);
			stored.TransactionState.Should().Be(MfaLoginTransactionState.Exhausted);
			stored.Attempts.Should().Be(5, "attempts stop counting once the transaction is spent");
			(await Transactions().TryCompleteAsync(transaction.MfaLoginTransactionId, (int)MfaEvidenceMethod.Totp, null, now, false,
				RandomNumberGenerator.GetBytes(32), now.AddSeconds(60), now)).Should().BeFalse();
		}

		[Test]
		public async Task A_not_me_denial_ends_a_pending_or_unredeemed_sign_in_once()
		{
			var now = Now;
			var pending = NewTransaction(now);
			var completed = NewTransaction(now);
			await Transactions().InsertAsync(pending);
			await Transactions().InsertAsync(completed);
			var code = RandomNumberGenerator.GetBytes(32);
			(await Transactions().TryCompleteAsync(completed.MfaLoginTransactionId, (int)MfaEvidenceMethod.Totp, null, now, false, code, now.AddSeconds(60), now))
				.Should().BeTrue();

			(await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Transactions().TryAbandonAsync(pending.MfaLoginTransactionId))))
				.Count(ended => ended).Should().Be(1);
			(await Transactions().TryAbandonAsync(completed.MfaLoginTransactionId)).Should().BeTrue();

			(await Transactions().GetBySecretHashAsync(pending.SecretHash)).TransactionState.Should().Be(MfaLoginTransactionState.Exhausted);
			(await Transactions().TryRedeemAsync(completed.MfaLoginTransactionId, code, now)).Should().BeFalse("nothing is issued from an abandoned sign-in");
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

			(await Transactions().GetBySecretHashAsync(old.SecretHash)).Should().BeNull();
			(await Transactions().GetBySecretHashAsync(current.SecretHash)).Should().NotBeNull();
		}

		[Test]
		public async Task Sessions_gain_their_second_factor_columns()
		{
			await using var connection = Connect(_connection);
			await connection.ExecuteAsync(type == DatabaseTypes.Postgres
				? "INSERT INTO usersessions (usersessionid, loginmfamethod, loginmfafactorreference) VALUES ('s1', 11, 'passkey:pk-1')"
				: "INSERT INTO UserSessions (UserSessionId, LoginMfaMethod, LoginMfaFactorReference) VALUES ('s1', 11, 'passkey:pk-1')");
			(await connection.ExecuteScalarAsync<string>(type == DatabaseTypes.Postgres
				? "SELECT loginmfafactorreference FROM usersessions WHERE loginmfamethod = 11"
				: "SELECT LoginMfaFactorReference FROM UserSessions WHERE LoginMfaMethod = 11")).Should().Be("passkey:pk-1");
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
	}
}
