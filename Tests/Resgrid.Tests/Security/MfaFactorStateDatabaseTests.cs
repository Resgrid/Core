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
using Resgrid.Model.Repositories.Connection;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Real-database proof of the workbook section 8.3 rules on both engines: 16 concurrent consumers, exactly one winner.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MfaFactorStateDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_verification_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private UserMfaStateRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return new UserMfaStateRepository(connections.Object, Configuration());
		}

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

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(type == DatabaseTypes.Postgres
				? new IMigration[] { new M0243_AddMfaFactorStatePg(), new M0246_AddUserMfaPreferencesPg(), new M0254_AddSharedSessionsPg(),
					new M0255_AddMfaActivityAndApprovalInstallationsPg() }
				: new IMigration[] { new M0243_AddMfaFactorState(), new M0246_AddUserMfaPreferences(), new M0254_AddSharedSessions(),
					new M0255_AddMfaActivityAndApprovalInstallations() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();

			// Only the legacy-token shape the repository touches; the real table comes from M0041/M0043.
			await using var db = Connect(_connection);
			await db.ExecuteAsync(type == DatabaseTypes.Postgres
				? "CREATE TABLE aspnetusertokens (userid varchar(128) NOT NULL, loginprovider varchar(128) NOT NULL, name varchar(128) NOT NULL, value text NULL, PRIMARY KEY (userid, loginprovider, name))"
				: "CREATE TABLE AspNetUserTokens (UserId nvarchar(128) NOT NULL, LoginProvider nvarchar(128) NOT NULL, Name nvarchar(128) NOT NULL, Value nvarchar(max) NULL, PRIMARY KEY (UserId, LoginProvider, Name))");
		}

		[Test]
		public async Task Concurrent_consumers_of_one_totp_step_have_exactly_one_winner()
		{
			var user = Guid.NewGuid().ToString();
			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Repository().TryConsumeTotpTimeStepAsync(user, 1000, DateTime.UtcNow)));

			winners.Count(w => w).Should().Be(1);
			(await Repository().GetTotpStateAsync(user)).LastAcceptedTimeStep.Should().Be(1000);
		}

		[Test]
		public async Task Totp_steps_only_move_forward()
		{
			var user = Guid.NewGuid().ToString();
			var repository = Repository();

			(await repository.TryConsumeTotpTimeStepAsync(user, 1000, DateTime.UtcNow)).Should().BeTrue();
			(await repository.TryConsumeTotpTimeStepAsync(user, 999, DateTime.UtcNow)).Should().BeFalse();
			(await repository.TryConsumeTotpTimeStepAsync(user, 1000, DateTime.UtcNow)).Should().BeFalse();
			(await repository.TryConsumeTotpTimeStepAsync(user, 1001, DateTime.UtcNow)).Should().BeTrue();

			await repository.RecordTotpEnrollmentAsync(user, DateTime.UtcNow, new Resgrid.Model.Repositories.TotpEnrollmentContext(true, (int)Resgrid.Model.UserSessionClientApplication.Unit, "Engine 7 tablet"));
			var state = await repository.GetTotpStateAsync(user);
			state.LastAcceptedTimeStep.Should().Be(1001);
			state.EnrolledOnUtc.Should().NotBeNull();
			state.EnrolledInSharedMode.Should().BeTrue();
			state.EnrolledClientApplication.Should().Be((int)Resgrid.Model.UserSessionClientApplication.Unit);
			state.EnrolledInstallation.Should().Be("Engine 7 tablet");

			await repository.RecordTotpEnrollmentAsync(user, DateTime.UtcNow, new Resgrid.Model.Repositories.TotpEnrollmentContext(false));
			var replaced = await repository.GetTotpStateAsync(user);
			replaced.EnrolledInSharedMode.Should().BeFalse("a replacement on a personal installation clears the flag");
			replaced.EnrolledInstallation.Should().BeNull();
		}

		[Test]
		public async Task Concurrent_redemptions_of_one_recovery_code_have_exactly_one_winner()
		{
			var user = Guid.NewGuid().ToString();
			var hash = RandomNumberGenerator.GetBytes(32);
			await Repository().ReplaceRecoveryCodesAsync(user, new[] { hash, RandomNumberGenerator.GetBytes(32) }, 1, DateTime.UtcNow);

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Repository().TryRedeemRecoveryCodeAsync(user, hash, DateTime.UtcNow)));

			winners.Count(w => w).Should().Be(1);
			(await Repository().CountUnusedRecoveryCodesAsync(user)).Should().Be(1);
		}

		[Test]
		public async Task Hashes_compare_as_exact_bytes()
		{
			var user = Guid.NewGuid().ToString();
			var hash = Enumerable.Repeat((byte)0xAB, 32).ToArray();
			await Repository().ReplaceRecoveryCodesAsync(user, new[] { hash }, 1, DateTime.UtcNow);

			(await Repository().TryRedeemRecoveryCodeAsync(user, Enumerable.Repeat((byte)0xAA, 32).ToArray(), DateTime.UtcNow)).Should().BeFalse();
			(await Repository().TryRedeemRecoveryCodeAsync(user, hash, DateTime.UtcNow)).Should().BeTrue();
		}

		[Test]
		public async Task Concurrent_legacy_imports_migrate_once_and_delete_the_plaintext_token()
		{
			var user = Guid.NewGuid().ToString();
			const string legacy = "AAAAA-11111;BBBBB-22222";
			await InsertLegacyTokenAsync(user, legacy);
			var hashes = new[] { RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32) };

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Repository().ImportLegacyRecoveryCodesAsync(user, legacy, hashes, 1, DateTime.UtcNow)));

			winners.Count(w => w).Should().Be(1);
			(await Repository().CountUnusedRecoveryCodesAsync(user)).Should().Be(2);
			(await LegacyTokenAsync(user)).Should().BeNull();
		}

		[Test]
		public async Task A_changed_legacy_token_is_not_imported()
		{
			var user = Guid.NewGuid().ToString();
			await InsertLegacyTokenAsync(user, "AAAAA-11111;BBBBB-22222");

			(await Repository().ImportLegacyRecoveryCodesAsync(user, "AAAAA-11111", new[] { RandomNumberGenerator.GetBytes(32) }, 1, DateTime.UtcNow)).Should().BeFalse();
			(await Repository().CountUnusedRecoveryCodesAsync(user)).Should().Be(0);
			(await LegacyTokenAsync(user)).Should().NotBeNull();
		}

		[Test]
		public async Task Regeneration_racing_a_legacy_import_never_leaves_old_codes()
		{
			for (var round = 0; round < 8; round++)
			{
				var user = Guid.NewGuid().ToString();
				const string legacy = "OLDOL-00001";
				await InsertLegacyTokenAsync(user, legacy);
				var oldHash = RandomNumberGenerator.GetBytes(32);
				var newHash = RandomNumberGenerator.GetBytes(32);

				await Task.WhenAll(
					Repository().ImportLegacyRecoveryCodesAsync(user, legacy, new[] { oldHash }, 1, DateTime.UtcNow),
					Repository().ReplaceRecoveryCodesAsync(user, new[] { newHash }, 1, DateTime.UtcNow));

				// Whichever ran first, the regeneration's code set is the only one left.
				(await Repository().TryRedeemRecoveryCodeAsync(user, oldHash, DateTime.UtcNow)).Should().BeFalse();
				(await Repository().TryRedeemRecoveryCodeAsync(user, newHash, DateTime.UtcNow)).Should().BeTrue();
				(await LegacyTokenAsync(user)).Should().BeNull();
			}
		}

		[Test]
		public async Task The_preferred_method_is_inserted_then_replaced()
		{
			var user = Guid.NewGuid().ToString();

			(await Repository().GetPreferredMethodAsync(user)).Should().BeNull();
			await Repository().SetPreferredMethodAsync(user, 10, DateTime.UtcNow);
			(await Repository().GetPreferredMethodAsync(user)).Should().Be(10);
			await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Repository().SetPreferredMethodAsync(user, 11, DateTime.UtcNow)));
			(await Repository().GetPreferredMethodAsync(user)).Should().Be(11, "concurrent upserts never collide on the key");
		}

		private async Task InsertLegacyTokenAsync(string user, string value)
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync("INSERT INTO AspNetUserTokens (UserId, LoginProvider, Name, Value) VALUES (@UserId, '[AspNetUserStore]', 'RecoveryCodes', @Value)",
				new { UserId = user, Value = value });
		}

		private async Task<string> LegacyTokenAsync(string user)
		{
			await using var db = Connect(_connection);
			return await db.QuerySingleOrDefaultAsync<string>(
				"SELECT Value FROM AspNetUserTokens WHERE UserId = @UserId AND LoginProvider = '[AspNetUserStore]' AND Name = 'RecoveryCodes'",
				new { UserId = user });
		}

		// ---- Authenticator seeds at rest (slice 14) ---------------------------------------------------------------------

		private IdentityUserRepository Users()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return new IdentityUserRepository(connections.Object, Configuration(), Mock.Of<Resgrid.Model.Repositories.IIdentityRoleRepository>(),
				new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(connections.Object), Mock.Of<Resgrid.Model.Repositories.Queries.IQueryFactory>());
		}

		[Test]
		public async Task A_seed_is_replaced_only_while_it_is_exactly_what_was_read()
		{
			var user = Guid.NewGuid().ToString();
			await Users().SetTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", "tseed1:m1:AbCd", default);

			(await Users().TryReplaceTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", "tseed1:m1:abcd", "tseed1:m1:new", default))
				.Should().BeFalse("values that differ only in case are different ciphertexts");
			(await Users().TryReplaceTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes", "tseed1:m1:AbCd", "tseed1:m1:new", default)).Should().BeFalse();

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
				Users().TryReplaceTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey", "tseed1:m1:AbCd", "tseed1:m1:w" + i, default)));
			winners.Count(w => w).Should().Be(1, "a concurrent re-encryption and replacement never both win");
			(await Users().GetTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey")).Should().StartWith("tseed1:m1:w");
		}

		[Test]
		public async Task Seed_pages_follow_user_id_order_without_gaps_or_repeats()
		{
			var provider = "[SeedPaging" + Guid.NewGuid().ToString("N").Substring(0, 8) + "]";
			var ids = Enumerable.Range(0, 7).Select(i => $"page-user-{i}").ToList();
			foreach (var id in ids)
				await Users().SetTokenAsync(id, provider, "AuthenticatorKey", "seed-" + id, default);
			await Users().SetTokenAsync("page-user-3", provider, "Other", "ignored", default);

			var seen = new System.Collections.Generic.List<string>();
			string after = null;
			while (true)
			{
				var page = await Users().GetTokensPageAsync(provider, "AuthenticatorKey", after, 3, default);
				seen.AddRange(page.Select(p => p.UserId));
				page.Should().OnlyContain(p => p.Value == "seed-" + p.UserId);
				if (page.Count < 3) break;
				after = page[^1].UserId;
			}

			seen.Should().Equal(ids);
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
