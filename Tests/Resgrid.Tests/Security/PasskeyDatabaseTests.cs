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
	/// Real-database proof for M0248 on both engines: one credential can be registered once per RP however many nodes race
	/// for it, revocation and use are owner- and counter-guarded single statements, and removing one app's passkeys
	/// reports exactly the rows it revoked. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class PasskeyDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "passkeys_";
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

		private UserPasskeyRepository Passkeys() => new(Connections(), Configuration());
		private UserSessionMfaEvidenceRepository Evidence() => new(Connections(), Configuration());

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
				? new IMigration[] { new M0244_AddMfaEvidenceAndChallengesPg(), new M0248_AddUserPasskeysPg() }
				: new IMigration[] { new M0244_AddMfaEvidenceAndChallenges(), new M0248_AddUserPasskeys() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		private static UserPasskey NewPasskey(string user, DateTime now, byte[] credentialId = null, UserSessionClientApplication client = UserSessionClientApplication.Unit,
			string rpId = "unit.resgrid.test")
		{
			credentialId ??= RandomNumberGenerator.GetBytes(32);
			return new UserPasskey
			{
				UserPasskeyId = Guid.NewGuid().ToString(),
				UserId = user,
				ClientApplication = (int)client,
				RpId = rpId,
				CredentialId = credentialId,
				CredentialIdHash = SHA256.HashData(credentialId),
				PublicKey = RandomNumberGenerator.GetBytes(77),
				Algorithm = -7,
				UserHandle = RandomNumberGenerator.GetBytes(32),
				SignCount = 0,
				IsBackupEligible = true,
				IsBackedUp = true,
				Transports = "internal,hybrid",
				AttestationFormat = "none",
				DisplayName = "Unit passkey",
				CreatedOnUtc = now,
				RegistrationPlatform = "Android",
				RegistrationInstallation = "Engine 7 tablet",
				RegistrationUserAgentFamily = "Chrome",
				RegistrationAttachment = "platform",
				StateVersion = 1
			};
		}

		[Test]
		public async Task A_passkey_round_trips_with_its_binding_and_context()
		{
			var passkey = NewPasskey(Guid.NewGuid().ToString(), Now);
			(await Passkeys().TryInsertAsync(passkey)).Should().BeTrue();

			var stored = await Passkeys().GetAsync(passkey.UserPasskeyId);

			stored.Should().BeEquivalentTo(passkey, o => o.Excluding(p => p.CreatedOnUtc));
			stored.CreatedOnUtc.Should().BeCloseTo(passkey.CreatedOnUtc, TimeSpan.FromMilliseconds(5));
			(await Passkeys().GetActiveByCredentialAsync(passkey.RpId, passkey.CredentialIdHash)).UserPasskeyId.Should().Be(passkey.UserPasskeyId);
			(await Passkeys().GetActiveByCredentialAsync("responder.resgrid.test", passkey.CredentialIdHash)).Should().BeNull("the id is scoped to its RP");
			(await Passkeys().GetUserHandleAsync(passkey.UserId, passkey.RpId)).Should().Equal(passkey.UserHandle);
		}

		[Test]
		public async Task Concurrent_registrations_of_one_credential_have_exactly_one_winner_across_users()
		{
			var now = Now;
			var credentialId = RandomNumberGenerator.GetBytes(32);

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
				Passkeys().TryInsertAsync(NewPasskey("racer-" + i + "-" + Guid.NewGuid().ToString("N")[..8], now, credentialId))));

			winners.Count(w => w).Should().Be(1);
			var hash = SHA256.HashData(credentialId);
			await using var connection = Connect(_connection);
			(await connection.ExecuteScalarAsync<int>(type == DatabaseTypes.Postgres
				? "SELECT COUNT(*) FROM userpasskeys WHERE credentialidhash = @Hash"
				: "SELECT COUNT(*) FROM UserPasskeys WHERE CredentialIdHash = @Hash", new { Hash = hash })).Should().Be(1);

			(await Passkeys().TryInsertAsync(NewPasskey(Guid.NewGuid().ToString(), now, credentialId, UserSessionClientApplication.Responder,
				"responder.resgrid.test"))).Should().BeTrue("another RP is a different credential namespace");
		}

		[Test]
		public async Task Only_the_owner_can_rename_or_revoke_and_a_revoked_passkey_stays_revoked()
		{
			var now = Now;
			var owner = Guid.NewGuid().ToString();
			var passkey = NewPasskey(owner, now);
			await Passkeys().TryInsertAsync(passkey);

			(await Passkeys().TryRenameAsync(passkey.UserPasskeyId, "someone-else", "Mine")).Should().BeFalse();
			(await Passkeys().TryRevokeAsync(passkey.UserPasskeyId, "someone-else", PasskeyRevocationReason.RemovedByUser, "someone-else", now)).Should().BeFalse();
			(await Passkeys().TryRenameAsync(passkey.UserPasskeyId, owner, "Engine 7")).Should().BeTrue();

			var revocations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
				Passkeys().TryRevokeAsync(passkey.UserPasskeyId, owner, PasskeyRevocationReason.RemovedByUser, owner, now)));
			revocations.Count(r => r).Should().Be(1);

			var stored = await Passkeys().GetAsync(passkey.UserPasskeyId);
			stored.IsActive.Should().BeFalse();
			stored.StateVersion.Should().Be(2);
			stored.DisplayName.Should().Be("Engine 7");
			(await Passkeys().GetActiveByCredentialAsync(passkey.RpId, passkey.CredentialIdHash)).Should().BeNull();
			(await Passkeys().GetActiveForUserAsync(owner)).Should().BeEmpty();
			(await Passkeys().TryRenameAsync(passkey.UserPasskeyId, owner, "Again")).Should().BeFalse();
			(await Passkeys().TrySetApprovalEnabledAsync(passkey.UserPasskeyId, owner, true)).Should().BeFalse();
			(await Passkeys().TryInsertAsync(NewPasskey(owner, now, passkey.CredentialId))).Should().BeFalse("a revoked credential never comes back");
		}

		[Test]
		public async Task A_use_is_recorded_only_against_the_counter_it_was_verified_with()
		{
			var now = Now;
			var passkey = NewPasskey(Guid.NewGuid().ToString(), now);
			await Passkeys().TryInsertAsync(passkey);

			var recorded = await Task.WhenAll(Enumerable.Range(1, 16).Select(i =>
				Passkeys().TryRecordUseAsync(passkey.UserPasskeyId, 0, i, false, (int)UserSessionClientApplication.Unit, "Engine 7 tablet", false, now)));

			recorded.Count(r => r).Should().Be(1);
			var stored = await Passkeys().GetAsync(passkey.UserPasskeyId);
			stored.SignCount.Should().BeGreaterThan(0);
			stored.IsBackedUp.Should().BeFalse();
			stored.LastUsedOnUtc.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(5));
			stored.LastUsedClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			stored.LastUsedInstallation.Should().Be("Engine 7 tablet");

			await Passkeys().TryRevokeAsync(passkey.UserPasskeyId, passkey.UserId, PasskeyRevocationReason.RemovedByUser, passkey.UserId, now);
			(await Passkeys().TryRecordUseAsync(passkey.UserPasskeyId, stored.SignCount, stored.SignCount + 1, false, 3, null, false, now))
				.Should().BeFalse("a revoked passkey records no use");
		}

		[Test]
		public async Task Removing_one_apps_passkeys_reports_exactly_those_rows()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var unitA = NewPasskey(user, now);
			var unitB = NewPasskey(user, now);
			var responder = NewPasskey(user, now, client: UserSessionClientApplication.Responder, rpId: "responder.resgrid.test");
			var stranger = NewPasskey(Guid.NewGuid().ToString(), now);
			foreach (var p in new[] { unitA, unitB, responder, stranger })
				await Passkeys().TryInsertAsync(p);

			var revoked = await Passkeys().RevokeAllForClientAsync(user, (int)UserSessionClientApplication.Unit, PasskeyRevocationReason.RemovedAllForClient, user, now);

			revoked.Should().BeEquivalentTo(new[] { unitA.UserPasskeyId, unitB.UserPasskeyId });
			(await Passkeys().RevokeAllForClientAsync(user, (int)UserSessionClientApplication.Unit, PasskeyRevocationReason.RemovedAllForClient, user, now))
				.Should().BeEmpty();
			(await Passkeys().GetActiveForUserAsync(user)).Select(p => p.UserPasskeyId).Should().Equal(responder.UserPasskeyId);
			(await Passkeys().CountActiveForUserAsync(user)).Should().Be(1);
			(await Passkeys().GetAsync(stranger.UserPasskeyId)).IsActive.Should().BeTrue();
			(await Passkeys().GetAsync(unitA.UserPasskeyId)).RevocationReason.Should().Be((int)PasskeyRevocationReason.RemovedAllForClient);
		}

		[Test]
		public async Task Approval_changes_advance_the_state_version()
		{
			var passkey = NewPasskey(Guid.NewGuid().ToString(), Now, client: UserSessionClientApplication.Responder, rpId: "responder.resgrid.test");
			await Passkeys().TryInsertAsync(passkey);

			(await Passkeys().TrySetApprovalEnabledAsync(passkey.UserPasskeyId, passkey.UserId, true)).Should().BeTrue();

			var stored = await Passkeys().GetAsync(passkey.UserPasskeyId);
			stored.ApprovalEnabled.Should().BeTrue();
			stored.StateVersion.Should().Be(2);

			await Passkeys().TryRevokeAsync(passkey.UserPasskeyId, passkey.UserId, PasskeyRevocationReason.RemovedByUser, passkey.UserId, Now);
			(await Passkeys().GetAsync(passkey.UserPasskeyId)).ApprovalEnabled.Should().BeFalse("a revoked passkey can approve nothing");
		}

		[Test]
		public async Task Revoking_a_factors_evidence_leaves_other_evidence_alone()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			MfaEvidence Row(string reference) => new()
			{
				MfaEvidenceId = Guid.NewGuid().ToString(), UserId = user, SessionKey = "sid:s1", ClientApplication = (int)UserSessionClientApplication.Unit,
				Kind = (int)MfaEvidenceKind.SecondFactor, Method = (int)(reference == null ? MfaEvidenceMethod.Totp : MfaEvidenceMethod.Passkey),
				Purpose = (int)MfaEvidencePurpose.StepUp, VerifiedOnUtc = now, ExpiresOnUtc = now.AddHours(1), AuthenticationGeneration = 1,
				FactorReference = reference
			};
			await Evidence().InsertAsync(Row("passkey:a"));
			await Evidence().InsertAsync(Row("passkey:b"));
			await Evidence().InsertAsync(Row(null));

			(await Evidence().RevokeForFactorAsync(user, "passkey:a", now)).Should().Be(1);
			(await Evidence().RevokeForFactorAsync("someone-else", "passkey:b", now)).Should().Be(0);

			await using var connection = Connect(_connection);
			var active = await connection.QueryAsync<string>(type == DatabaseTypes.Postgres
				? "SELECT factorreference FROM usersessionmfaevidence WHERE userid = @User AND revokedonutc IS NULL"
				: "SELECT FactorReference FROM UserSessionMfaEvidence WHERE UserId = @User AND RevokedOnUtc IS NULL", new { User = user });
			active.Should().BeEquivalentTo(new[] { "passkey:b", null });
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
