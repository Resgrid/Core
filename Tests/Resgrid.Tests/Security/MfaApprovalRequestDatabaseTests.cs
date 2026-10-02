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
	/// Real-database proof for M0252 on both engines: one pending approval request per user however many nodes race, a
	/// request is approved once and used once by its requester, wrong numbers stop at the limit, and approval never revives
	/// an expired, denied or canceled request. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MfaApprovalRequestDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "mfa_approval_";
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

		private MfaApprovalRequestRepository Requests()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return new MfaApprovalRequestRepository(connections.Object, Configuration());
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
			source.Setup(s => s.GetMigrations()).Returns(new IMigration[]
			{
				type == DatabaseTypes.Postgres ? new M0252_AddMfaApprovalRequestsPg() : new M0252_AddMfaApprovalRequests()
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

		private static MfaApprovalRequest NewRequest(DateTime now, string userId = null, string requesterId = "unit-session", int lifetimeSeconds = 120)
		{
			var id = Guid.NewGuid().ToString();
			return new MfaApprovalRequest
			{
				MfaApprovalRequestId = id,
				UserId = userId ?? Guid.NewGuid().ToString(),
				RequesterKind = (int)MfaApprovalRequesterKind.Session,
				RequesterId = requesterId,
				ClientApplication = (int)UserSessionClientApplication.Unit,
				InstallationLabel = "Engine 7 tablet",
				DepartmentId = 42,
				Purpose = (int)MfaApprovalPurpose.StepUp,
				Operation = MfaStepUpOperations.ChatExport,
				AuthenticationGeneration = 4,
				MatchNumberHash = MfaApprovalRequest.HashMatchNumber(id, "57"),
				OriginRegion = "Ontario, Canada",
				MaxAttempts = 3,
				CreatedOnUtc = now,
				ExpiresOnUtc = now.AddSeconds(lifetimeSeconds)
			};
		}

		[Test]
		public async Task A_request_round_trips_with_its_requester_and_number_hash()
		{
			var now = Now;
			var request = NewRequest(now);
			(await Requests().TryInsertPendingAsync(request, now)).Should().BeTrue();

			var stored = await Requests().GetAsync(request.MfaApprovalRequestId);
			stored.Should().BeEquivalentTo(request, o => o.Excluding(r => r.Version).Excluding(r => r.RequestState).Excluding(r => r.RequestPurpose)
				.Excluding(r => r.Requester));
			stored.RequestState.Should().Be(MfaApprovalRequestState.Pending);
			stored.Version.Should().Be(1);
			(await Requests().GetPendingForUserAsync(request.UserId, now)).MfaApprovalRequestId.Should().Be(request.MfaApprovalRequestId);
			(await Requests().GetPendingForUserAsync(request.UserId, request.ExpiresOnUtc)).Should().BeNull();
		}

		[Test]
		public async Task Concurrent_requests_for_one_user_leave_exactly_one_pending()
		{
			var now = Now;
			var userId = Guid.NewGuid().ToString();

			var inserted = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Requests().TryInsertPendingAsync(NewRequest(now, userId), now))));

			inserted.Count(won => won).Should().Be(1);
			(await Requests().CountCreatedSinceAsync(userId, now.AddMinutes(-1))).Should().Be(1);
		}

		[Test]
		public async Task An_expired_or_canceled_pending_request_never_blocks_a_new_one()
		{
			var now = Now;
			var userId = Guid.NewGuid().ToString();
			var stale = NewRequest(now.AddMinutes(-5), userId);
			(await Requests().TryInsertPendingAsync(stale, now.AddMinutes(-5))).Should().BeTrue();

			(await Requests().TryInsertPendingAsync(NewRequest(now, userId), now)).Should().BeTrue("the stale one is closed as expired first");
			(await Requests().GetAsync(stale.MfaApprovalRequestId)).RequestState.Should().Be(MfaApprovalRequestState.Expired);

			(await Requests().CancelPendingForUserAsync(userId, MfaApprovalEndReason.Superseded, now)).Should().Be(1);
			(await Requests().TryInsertPendingAsync(NewRequest(now, userId), now)).Should().BeTrue();
			(await Requests().GetRecentForUserAsync(userId, 10)).Select(r => r.RequestState)
				.Should().BeEquivalentTo(new[] { MfaApprovalRequestState.Pending, MfaApprovalRequestState.Canceled, MfaApprovalRequestState.Expired });
		}

		[Test]
		public async Task A_request_is_approved_once_however_many_approvers_race()
		{
			var now = Now;
			var request = NewRequest(now);
			await Requests().TryInsertPendingAsync(request, now);

			var approved = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
				Task.Run(() => Requests().TryApproveAsync(request.MfaApprovalRequestId, "responder-" + i, "pk-" + i, now))));

			approved.Count(won => won).Should().Be(1);
			var stored = await Requests().GetAsync(request.MfaApprovalRequestId);
			stored.RequestState.Should().Be(MfaApprovalRequestState.Approved);
			stored.ApproverSessionId.Should().StartWith("responder-");
			stored.ApproverPasskeyId.Should().Be("pk-" + stored.ApproverSessionId["responder-".Length..]);
			stored.DecidedOnUtc.Should().Be(now);
		}

		[Test]
		public async Task Approval_never_revives_an_expired_denied_or_canceled_request()
		{
			var now = Now;
			var expired = NewRequest(now, lifetimeSeconds: 0);
			var denied = NewRequest(now);
			var canceled = NewRequest(now);
			foreach (var request in new[] { expired, denied, canceled })
				await Requests().TryInsertPendingAsync(request, now);
			(await Requests().TryDenyAsync(denied.MfaApprovalRequestId, MfaApprovalEndReason.NotMe, now)).Should().BeTrue();
			(await Requests().TryCancelAsync(canceled.MfaApprovalRequestId, MfaApprovalRequesterKind.Session, "someone-else", now)).Should().BeFalse(
				"only its requester cancels it");
			(await Requests().TryCancelAsync(canceled.MfaApprovalRequestId, MfaApprovalRequesterKind.Session, "unit-session", now)).Should().BeTrue();

			foreach (var request in new[] { expired, denied, canceled })
				(await Requests().TryApproveAsync(request.MfaApprovalRequestId, "responder-1", "pk-1", now)).Should().BeFalse();
			(await Requests().TryDenyAsync(denied.MfaApprovalRequestId, MfaApprovalEndReason.Declined, now)).Should().BeFalse("a denial is terminal");
			(await Requests().GetAsync(denied.MfaApprovalRequestId)).EndReason.Should().Be((int)MfaApprovalEndReason.NotMe);
		}

		[Test]
		public async Task Wrong_numbers_stop_at_the_limit_under_concurrency()
		{
			var now = Now;
			var request = NewRequest(now);
			await Requests().TryInsertPendingAsync(request, now);

			await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Requests().RecordWrongNumberAsync(request.MfaApprovalRequestId, now))));

			var stored = await Requests().GetAsync(request.MfaApprovalRequestId);
			stored.RequestState.Should().Be(MfaApprovalRequestState.Denied);
			stored.EndReason.Should().Be((int)MfaApprovalEndReason.TooManyAttempts);
			stored.Attempts.Should().Be(3, "attempts stop counting once the request is denied");
			(await Requests().TryApproveAsync(request.MfaApprovalRequestId, "responder-1", "pk-1", now)).Should().BeFalse();
		}

		[Test]
		public async Task An_approval_is_used_once_by_its_requester_within_the_grace()
		{
			var now = Now;
			var request = NewRequest(now);
			var late = NewRequest(now.AddMinutes(-3));
			await Requests().TryInsertPendingAsync(request, now);
			await Requests().TryInsertPendingAsync(late, now.AddMinutes(-3));
			await Requests().TryApproveAsync(request.MfaApprovalRequestId, "responder-1", "pk-1", now);
			await Requests().TryApproveAsync(late.MfaApprovalRequestId, "responder-1", "pk-1", now.AddMinutes(-3));

			(await Requests().TryConsumeAsync(request.MfaApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, "unit-session", now, TimeSpan.FromSeconds(30)))
				.Should().BeFalse("another kind of requester");
			(await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
					Requests().TryConsumeAsync(request.MfaApprovalRequestId, MfaApprovalRequesterKind.Session, "unit-session", now, TimeSpan.FromSeconds(30))))))
				.Count(used => used).Should().Be(1);
			(await Requests().TryConsumeAsync(late.MfaApprovalRequestId, MfaApprovalRequesterKind.Session, "unit-session", now, TimeSpan.FromSeconds(30)))
				.Should().BeFalse("its request expired more than the grace ago");
		}

		[Test]
		public async Task Purge_removes_requests_created_before_the_cutoff()
		{
			var now = Now;
			var old = NewRequest(now.AddDays(-2));
			var current = NewRequest(now);
			await Requests().TryInsertPendingAsync(old, now.AddDays(-2));
			await Requests().TryInsertPendingAsync(current, now);

			(await Requests().PurgeCreatedBeforeAsync(now.AddDays(-1))).Should().BeGreaterThanOrEqualTo(1);

			(await Requests().GetAsync(old.MfaApprovalRequestId)).Should().BeNull();
			(await Requests().GetAsync(current.MfaApprovalRequestId)).Should().NotBeNull();
		}
	}
}
