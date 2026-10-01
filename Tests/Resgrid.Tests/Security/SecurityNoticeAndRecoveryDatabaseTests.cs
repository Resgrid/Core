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
	/// Real-database proof for M0253 on both engines: a security notice is claimed by one sender however many race, a batch
	/// claim never hands the same notice to two senders, and only the lease holder records a result; a factor recovery
	/// completes once, stops at its attempt limit, and cancels once. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SecurityNoticeAndRecoveryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_notice_";
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

		private SecurityNoticeRepository Notices() => new(Connections(), Configuration());
		private FactorRecoveryTransactionRepository Recoveries() => new(Connections(), Configuration());

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
				type == DatabaseTypes.Postgres ? new M0253_AddSecurityNoticesAndFactorRecoveryPg() : new M0253_AddSecurityNoticesAndFactorRecovery()
			});
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Drop_isolated_database()
		{
			if (string.IsNullOrWhiteSpace(_master)) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			NpgsqlConnection.ClearAllPools();
			SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private static SecurityNotice NewNotice(DateTime now, string userId = null, SecurityNoticeKind kind = SecurityNoticeKind.PasskeyRemoved) => new()
		{
			SecurityNoticeId = Guid.NewGuid().ToString(),
			UserId = userId ?? Guid.NewGuid().ToString(),
			Kind = (int)kind,
			OccurredOnUtc = now,
			ClientApplication = (int)UserSessionClientApplication.Unit,
			InstallationLabel = "Engine 7 tablet",
			Region = "Ontario, Canada",
			NextAttemptOnUtc = now,
			CreatedOnUtc = now
		};

		[Test]
		public async Task A_notice_round_trips_and_is_claimed_by_exactly_one_sender()
		{
			var now = Now;
			var notice = NewNotice(now);
			await Notices().InsertAsync(notice);
			var stored = await Notices().GetAsync(notice.SecurityNoticeId);
			stored.Should().BeEquivalentTo(notice, o => o.Excluding(n => n.NoticeKind).Excluding(n => n.NoticeState));

			var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
				Task.Run(() => Notices().TryClaimAsync(notice.SecurityNoticeId, "sender-" + i, now, now.AddMinutes(5)))));
			claims.Count(won => won).Should().Be(1);

			var owner = (await Notices().GetAsync(notice.SecurityNoticeId)).LeaseOwner;
			(await Notices().MarkSentAsync(notice.SecurityNoticeId, "not-the-owner", now)).Should().BeFalse("only the lease holder records a result");
			(await Notices().MarkSentAsync(notice.SecurityNoticeId, owner, now)).Should().BeTrue();
			(await Notices().GetAsync(notice.SecurityNoticeId)).NoticeState.Should().Be(SecurityNoticeState.Sent);
			(await Notices().TryClaimAsync(notice.SecurityNoticeId, "late", now.AddHours(1), now.AddHours(2))).Should().BeFalse("a sent notice is done");
		}

		[Test]
		public async Task Concurrent_batch_claims_never_hand_a_notice_to_two_senders()
		{
			var now = Now;
			var userId = Guid.NewGuid().ToString();
			for (var i = 0; i < 40; i++)
				await Notices().InsertAsync(NewNotice(now, userId));

			var batches = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
				Task.Run(() => Notices().ClaimDueAsync("sender-" + i, now, now.AddMinutes(5), 25))));

			var claimed = batches.SelectMany(batch => batch.Where(n => n.UserId == userId).Select(n => n.SecurityNoticeId)).ToList();
			claimed.Should().OnlyHaveUniqueItems();
			claimed.Should().HaveCount(40);
		}

		[Test]
		public async Task A_retry_releases_the_lease_until_it_is_due_and_a_failure_is_final()
		{
			var now = Now;
			var notice = NewNotice(now);
			await Notices().InsertAsync(notice);
			await Notices().TryClaimAsync(notice.SecurityNoticeId, "sender", now, now.AddMinutes(5));

			(await Notices().MarkRetryAsync(notice.SecurityNoticeId, "sender", now.AddMinutes(4), "delivery_failed")).Should().BeTrue();
			var retrying = await Notices().GetAsync(notice.SecurityNoticeId);
			retrying.Attempts.Should().Be(1);
			retrying.LeaseOwner.Should().BeNull();
			(await Notices().TryClaimAsync(notice.SecurityNoticeId, "sender", now.AddMinutes(1), now.AddMinutes(6))).Should().BeFalse("not due yet");
			(await Notices().TryClaimAsync(notice.SecurityNoticeId, "sender", now.AddMinutes(4), now.AddMinutes(9))).Should().BeTrue();

			(await Notices().MarkFailedAsync(notice.SecurityNoticeId, "sender", "no_destination")).Should().BeTrue();
			var failed = await Notices().GetAsync(notice.SecurityNoticeId);
			failed.NoticeState.Should().Be(SecurityNoticeState.Failed);
			failed.Attempts.Should().Be(2);
			failed.LastFailure.Should().Be("no_destination");
		}

		[Test]
		public async Task Earlier_notices_purge_and_account_deletion_are_scoped()
		{
			var now = Now;
			var userId = Guid.NewGuid().ToString();
			var old = NewNotice(now.AddDays(-100), userId, SecurityNoticeKind.ApprovalSuspended);
			var pendingOld = NewNotice(now.AddDays(-100), userId);
			var recent = NewNotice(now, userId, SecurityNoticeKind.ApprovalSuspended);
			foreach (var notice in new[] { old, pendingOld, recent })
				await Notices().InsertAsync(notice);
			await Notices().TryClaimAsync(old.SecurityNoticeId, "sender", now, now.AddMinutes(5));
			await Notices().MarkSentAsync(old.SecurityNoticeId, "sender", now);

			(await Notices().ExistsSinceAsync(userId, SecurityNoticeKind.ApprovalSuspended, now.AddMinutes(-15))).Should().BeTrue();
			(await Notices().ExistsSinceAsync(userId, SecurityNoticeKind.TotpReplaced, now.AddMinutes(-15))).Should().BeFalse();

			(await Notices().PurgeFinishedBeforeAsync(now.AddDays(-90))).Should().BeGreaterThanOrEqualTo(1);
			(await Notices().GetAsync(old.SecurityNoticeId)).Should().BeNull();
			(await Notices().GetAsync(pendingOld.SecurityNoticeId)).Should().NotBeNull("a notice still to be sent is never purged");

			(await Notices().DeleteForUserAsync(userId)).Should().Be(2);
		}

		private static FactorRecoveryTransaction NewRecovery(DateTime now, int lifetimeMinutes = 10, int maxAttempts = 5) => new()
		{
			FactorRecoveryTransactionId = Guid.NewGuid().ToString(),
			SecretHash = RandomNumberGenerator.GetBytes(32),
			UserId = Guid.NewGuid().ToString(),
			ClientApplication = (int)UserSessionClientApplication.Unit,
			FirstFactorMethod = (int)MfaEvidenceMethod.Password,
			FirstFactorVerifiedOnUtc = now,
			DepartmentId = 42,
			AuthenticationGeneration = 4,
			CreatedOnUtc = now,
			ExpiresOnUtc = now.AddMinutes(lifetimeMinutes),
			MaxAttempts = maxAttempts,
			State = (int)FactorRecoveryState.Pending
		};

		[Test]
		public async Task A_recovery_round_trips_by_its_hash_and_completes_once()
		{
			var now = Now;
			var recovery = NewRecovery(now);
			await Recoveries().InsertAsync(recovery);
			(await Recoveries().GetBySecretHashAsync(recovery.SecretHash)).Should().BeEquivalentTo(recovery, o => o.Excluding(r => r.RecoveryState));

			var completed = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Recoveries().TryCompleteAsync(recovery.FactorRecoveryTransactionId, now))));
			completed.Count(won => won).Should().Be(1);
			(await Recoveries().TryCancelAsync(recovery.FactorRecoveryTransactionId)).Should().BeFalse("a completed recovery is final");
		}

		[Test]
		public async Task An_expired_exhausted_or_canceled_recovery_never_completes()
		{
			var now = Now;
			var expired = NewRecovery(now, lifetimeMinutes: 0);
			var exhausted = NewRecovery(now, maxAttempts: 3);
			var canceled = NewRecovery(now);
			foreach (var recovery in new[] { expired, exhausted, canceled })
				await Recoveries().InsertAsync(recovery);

			await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Recoveries().RecordFailedAttemptAsync(exhausted.FactorRecoveryTransactionId))));
			var stored = await Recoveries().GetBySecretHashAsync(exhausted.SecretHash);
			stored.RecoveryState.Should().Be(FactorRecoveryState.Exhausted);
			stored.Attempts.Should().Be(3, "attempts stop counting once the recovery is spent");
			(await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Recoveries().TryCancelAsync(canceled.FactorRecoveryTransactionId)))))
				.Count(won => won).Should().Be(1);

			foreach (var recovery in new[] { expired, exhausted, canceled })
				(await Recoveries().TryCompleteAsync(recovery.FactorRecoveryTransactionId, now)).Should().BeFalse();
		}

		[Test]
		public async Task Purge_and_account_deletion_remove_recoveries()
		{
			var now = Now;
			var old = NewRecovery(now.AddHours(-3));
			var current = NewRecovery(now);
			await Recoveries().InsertAsync(old);
			await Recoveries().InsertAsync(current);

			(await Recoveries().PurgeExpiredBeforeAsync(now.AddHours(-1))).Should().BeGreaterThanOrEqualTo(1);
			(await Recoveries().GetBySecretHashAsync(old.SecretHash)).Should().BeNull();
			(await Recoveries().DeleteForUserAsync(current.UserId)).Should().Be(1);
			(await Recoveries().GetBySecretHashAsync(current.SecretHash)).Should().BeNull();
		}
	}
}
