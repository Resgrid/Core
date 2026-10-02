using System;
using System.Data.Common;
using System.Linq;
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
	/// Real-database proof for the M0244 tables on both engines: a challenge is spent by exactly one of 16 concurrent
	/// consumers and never revived, and evidence lookups honour session, kind, generation, revocation and expiry.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MfaEvidenceDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_evidence_";
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

		private AuthenticationChallengeRepository Challenges() => new(Connections(), Configuration());
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
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[]
			{
				type == DatabaseTypes.Postgres ? new M0244_AddMfaEvidenceAndChallengesPg() : new M0244_AddMfaEvidenceAndChallenges()
			});
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		private static AuthenticationChallenge NewChallenge(string user, DateTime now, int lifetimeSeconds = 120, int maxAttempts = 5) => new()
		{
			AuthenticationChallengeId = Guid.NewGuid().ToString(),
			UserId = user,
			Purpose = (int)AuthenticationChallengePurpose.LoginSecondFactor,
			ClientApplication = (int)UserSessionClientApplication.Responder,
			RpId = "responder.resgrid.com",
			ParentKind = (int)AuthenticationChallengeParentKind.LoginTransaction,
			ParentId = Guid.NewGuid().ToString(),
			DepartmentId = 42,
			AuthenticationGeneration = 7,
			OptionsJson = "{\"challenge\":\"x\"}",
			CreatedOnUtc = now,
			ExpiresOnUtc = now.AddSeconds(lifetimeSeconds),
			MaxAttempts = maxAttempts
		};

		[Test]
		public async Task A_challenge_round_trips_with_its_binding()
		{
			var now = Now;
			var challenge = NewChallenge(Guid.NewGuid().ToString(), now);
			await Challenges().InsertAsync(challenge);

			var stored = await Challenges().GetAsync(challenge.AuthenticationChallengeId);

			stored.Should().BeEquivalentTo(challenge, o => o.Excluding(c => c.CreatedOnUtc).Excluding(c => c.ExpiresOnUtc)
				.Excluding(c => c.ChallengePurpose).Excluding(c => c.ChallengeState));
			stored.ExpiresOnUtc.Should().BeCloseTo(challenge.ExpiresOnUtc, TimeSpan.FromMilliseconds(5));
			stored.ChallengeState.Should().Be(AuthenticationChallengeState.Pending);
		}

		[Test]
		public async Task Concurrent_consumers_of_one_challenge_have_exactly_one_winner()
		{
			var now = Now;
			var challenge = NewChallenge(Guid.NewGuid().ToString(), now);
			await Challenges().InsertAsync(challenge);

			var winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Challenges().TryConsumeAsync(challenge.AuthenticationChallengeId, now)));

			winners.Count(w => w).Should().Be(1);
			var stored = await Challenges().GetAsync(challenge.AuthenticationChallengeId);
			stored.ChallengeState.Should().Be(AuthenticationChallengeState.Consumed);
			stored.ConsumedOnUtc.Should().NotBeNull();
		}

		[Test]
		public async Task An_expired_challenge_cannot_be_consumed()
		{
			var now = Now;
			var challenge = NewChallenge(Guid.NewGuid().ToString(), now, lifetimeSeconds: 120);
			await Challenges().InsertAsync(challenge);

			(await Challenges().TryConsumeAsync(challenge.AuthenticationChallengeId, now.AddSeconds(120))).Should().BeFalse();
			(await Challenges().GetAsync(challenge.AuthenticationChallengeId)).ChallengeState.Should().Be(AuthenticationChallengeState.Pending);
		}

		[Test]
		public async Task Failed_attempts_exhaust_the_challenge_at_its_limit_even_under_concurrency()
		{
			var now = Now;
			var challenge = NewChallenge(Guid.NewGuid().ToString(), now, maxAttempts: 3);
			await Challenges().InsertAsync(challenge);

			await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Challenges().RecordFailedAttemptAsync(challenge.AuthenticationChallengeId)));

			var stored = await Challenges().GetAsync(challenge.AuthenticationChallengeId);
			stored.ChallengeState.Should().Be(AuthenticationChallengeState.Exhausted);
			stored.Attempts.Should().Be(3, "once exhausted, later failures no longer match the pending guard");
			(await Challenges().TryConsumeAsync(challenge.AuthenticationChallengeId, now)).Should().BeFalse();
		}

		[Test]
		public async Task Canceling_spends_only_that_users_pending_challenges()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var pending = NewChallenge(user, now);
			var consumed = NewChallenge(user, now);
			var otherUser = NewChallenge(Guid.NewGuid().ToString(), now);
			foreach (var c in new[] { pending, consumed, otherUser })
				await Challenges().InsertAsync(c);
			await Challenges().TryConsumeAsync(consumed.AuthenticationChallengeId, now);

			(await Challenges().CountPendingForUserAsync(user, now)).Should().Be(1);
			(await Challenges().CancelPendingForUserAsync(user)).Should().Be(1);

			(await Challenges().GetAsync(pending.AuthenticationChallengeId)).ChallengeState.Should().Be(AuthenticationChallengeState.Canceled);
			(await Challenges().GetAsync(consumed.AuthenticationChallengeId)).ChallengeState.Should().Be(AuthenticationChallengeState.Consumed);
			(await Challenges().GetAsync(otherUser.AuthenticationChallengeId)).ChallengeState.Should().Be(AuthenticationChallengeState.Pending);
			(await Challenges().CountPendingForUserAsync(user, now)).Should().Be(0);
			(await Challenges().TryConsumeAsync(pending.AuthenticationChallengeId, now)).Should().BeFalse();
		}

		[Test]
		public async Task Expired_challenges_do_not_count_toward_the_outstanding_limit_and_are_purged()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var expired = NewChallenge(user, now.AddMinutes(-10), lifetimeSeconds: 60);
			var live = NewChallenge(user, now);
			await Challenges().InsertAsync(expired);
			await Challenges().InsertAsync(live);

			(await Challenges().CountPendingForUserAsync(user, now)).Should().Be(1);
			(await Challenges().PurgeExpiredBeforeAsync(now.AddMinutes(-1))).Should().BeGreaterThanOrEqualTo(1);

			(await Challenges().GetAsync(expired.AuthenticationChallengeId)).Should().BeNull();
			(await Challenges().GetAsync(live.AuthenticationChallengeId)).Should().NotBeNull();
		}

		private static MfaEvidence NewEvidence(string user, string session, DateTime verifiedOn, MfaEvidenceKind kind = MfaEvidenceKind.FirstFactor,
			long generation = 7, int retentionHours = 24) => new()
		{
			MfaEvidenceId = Guid.NewGuid().ToString(),
			UserId = user,
			SessionKey = session,
			ClientApplication = (int)UserSessionClientApplication.Web,
			Kind = (int)kind,
			Method = (int)(kind == MfaEvidenceKind.FirstFactor ? MfaEvidenceMethod.Password : MfaEvidenceMethod.Totp),
			Purpose = (int)MfaEvidencePurpose.Login,
			VerifiedOnUtc = verifiedOn,
			ExpiresOnUtc = verifiedOn.AddHours(retentionHours),
			AuthenticationGeneration = generation
		};

		[Test]
		public async Task The_latest_evidence_is_read_per_session_kind_and_generation()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var session = "sid:" + Guid.NewGuid();
			await Evidence().InsertAsync(NewEvidence(user, session, now.AddMinutes(-30)));
			await Evidence().InsertAsync(NewEvidence(user, session, now.AddMinutes(-2)));
			await Evidence().InsertAsync(NewEvidence(user, session, now.AddMinutes(-1), MfaEvidenceKind.SecondFactor));
			await Evidence().InsertAsync(NewEvidence(user, "sid:other", now));
			await Evidence().InsertAsync(NewEvidence(user, session, now, generation: 6));

			var latest = await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.FirstFactor, 7, now);

			latest.Should().NotBeNull();
			latest.VerifiedOnUtc.Should().BeCloseTo(now.AddMinutes(-2), TimeSpan.FromMilliseconds(5));
			(await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.FirstFactor, 8, now)).Should().BeNull();
			(await Evidence().GetLatestAsync(Guid.NewGuid().ToString(), session, MfaEvidenceKind.FirstFactor, 7, now)).Should().BeNull();
		}

		[Test]
		public async Task A_changed_provider_mapping_retires_its_evidence_for_every_user_and_nothing_else()
		{
			var now = Now;
			var retired = "federated:cfg-" + Guid.NewGuid() + ":3";
			var first = NewEvidence(Guid.NewGuid().ToString(), "sid:a", now, MfaEvidenceKind.SecondFactor);
			var second = NewEvidence(Guid.NewGuid().ToString(), "sid:b", now, MfaEvidenceKind.SecondFactor);
			var alreadyRevoked = NewEvidence(Guid.NewGuid().ToString(), "sid:c", now, MfaEvidenceKind.SecondFactor);
			var current = NewEvidence(first.UserId, "sid:a", now.AddSeconds(-1), MfaEvidenceKind.SecondFactor);
			foreach (var evidence in new[] { first, second, alreadyRevoked })
			{
				evidence.Method = (int)MfaEvidenceMethod.Federated;
				evidence.FactorReference = retired;
			}
			current.Method = (int)MfaEvidenceMethod.Federated;
			current.FactorReference = retired[..^1] + "4";
			alreadyRevoked.RevokedOnUtc = now.AddMinutes(-5);
			foreach (var evidence in new[] { first, second, alreadyRevoked, current })
				await Evidence().InsertAsync(evidence);

			(await Evidence().RevokeByFactorReferenceAsync(retired, now)).Should().Be(2, "both users' live rows, not the one already revoked");
			(await Evidence().GetLatestAsync(first.UserId, "sid:a", MfaEvidenceKind.SecondFactor, 7, now)).FactorReference
				.Should().Be(current.FactorReference, "the new version's evidence is untouched");
			(await Evidence().GetLatestAsync(second.UserId, "sid:b", MfaEvidenceKind.SecondFactor, 7, now)).Should().BeNull();
		}

		[Test]
		public async Task A_purpose_filtered_read_skips_a_newer_sign_in()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var session = "sid:" + Guid.NewGuid();
			var stepUp = NewEvidence(user, session, now.AddMinutes(-3), MfaEvidenceKind.SecondFactor);
			stepUp.Purpose = (int)MfaEvidencePurpose.StepUp;
			await Evidence().InsertAsync(stepUp);
			await Evidence().InsertAsync(NewEvidence(user, session, now.AddMinutes(-1), MfaEvidenceKind.SecondFactor));

			(await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.SecondFactor, 7, now)).Purpose.Should().Be((int)MfaEvidencePurpose.Login);
			var latestStepUp = await Evidence().GetLatestForPurposeAsync(user, session, MfaEvidenceKind.SecondFactor, MfaEvidencePurpose.StepUp, 7, now);
			latestStepUp.VerifiedOnUtc.Should().BeCloseTo(now.AddMinutes(-3), TimeSpan.FromMilliseconds(5));
		}

		[Test]
		public async Task Revoked_and_expired_evidence_is_never_returned()
		{
			var now = Now;
			var user = Guid.NewGuid().ToString();
			var session = "sid:" + Guid.NewGuid();
			await Evidence().InsertAsync(NewEvidence(user, session, now.AddHours(-2), retentionHours: 1));
			(await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.FirstFactor, 7, now)).Should().BeNull("it expired an hour ago");

			await Evidence().InsertAsync(NewEvidence(user, session, now));
			(await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.FirstFactor, 7, now)).Should().NotBeNull();

			(await Evidence().RevokeForUserAsync(user, now)).Should().Be(2);
			(await Evidence().GetLatestAsync(user, session, MfaEvidenceKind.FirstFactor, 7, now)).Should().BeNull();

			(await Evidence().PurgeExpiredBeforeAsync(now)).Should().BeGreaterThanOrEqualTo(1);
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
