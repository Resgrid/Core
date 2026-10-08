using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading;
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
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for M0267 on both engines: requests insert and read back, the claim hands each waiting request
	/// to exactly one worker (also under concurrent claimers) oldest first, heartbeats and outcomes only land on a running
	/// request, only a waiting request can be cancelled, and a running request without a recent heartbeat is failed as
	/// abandoned. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level
	/// connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class SystemOperationRequestsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "system_operation_requests_";
		private static readonly DateTime Base = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private string Table => IsPostgres ? "systemoperationrequests" : "SystemOperationRequests";

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private SystemOperationRequestsRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new SystemOperationRequestsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_configured = true;
			var database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + database);
			_database = database;
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			if (IsPostgres)
			{
				await using var setup = Connect(_connection);
				await setup.OpenAsync();
				await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");

				// The data source loaded its types before citext existed; without a reload a citext column cannot be read back.
				await ((NpgsqlConnection)setup).ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0267_AddSystemOperationRequestsPg() }
				: new IMigration[] { new M0267_AddSystemOperationRequests() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			_runner?.Dispose();
			if (_configured) DataConfig.DatabaseType = _previous;
			// Only a database this fixture created is ever dropped.
			if (_database == null) return;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[SetUp]
		public async Task Empty_the_table()
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync("DELETE FROM " + Table);
		}

		private async Task<SystemOperationRequest> QueueAsync(SystemOperationTypes type, int? departmentId, DateTime requestedOn)
		{
			return await Repository().InsertAsync(new SystemOperationRequest
			{
				SystemOperationRequestId = Guid.NewGuid().ToString(),
				OperationType = (int)type,
				TargetDepartmentId = departmentId,
				Status = (int)SystemOperationStatuses.Pending,
				Source = (int)SystemOperationSources.BackOffice,
				RequestedBy = "ops@resgrid.com",
				Reason = "database test",
				RequestedOn = IsPostgres ? DateTime.SpecifyKind(requestedOn, DateTimeKind.Unspecified) : requestedOn
			}, CancellationToken.None);
		}

		[Test]
		public async Task A_queued_request_reads_back_and_is_found_as_the_waiting_one_for_its_target()
		{
			var all = await QueueAsync(SystemOperationTypes.RebuildSecurityMatrices, null, Base);
			var one = await QueueAsync(SystemOperationTypes.RebuildSecurityMatrices, 12, Base.AddMinutes(1));
			var repository = Repository();

			var read = await repository.GetByIdAsync(all.SystemOperationRequestId);
			read.Should().NotBeNull();
			read.Reason.Should().Be("database test");
			read.TargetDepartmentId.Should().BeNull();

			(await repository.GetPendingAsync((int)SystemOperationTypes.RebuildSecurityMatrices, null)).SystemOperationRequestId.Should().Be(all.SystemOperationRequestId);
			(await repository.GetPendingAsync((int)SystemOperationTypes.RebuildSecurityMatrices, 12)).SystemOperationRequestId.Should().Be(one.SystemOperationRequestId);
			(await repository.GetPendingAsync((int)SystemOperationTypes.RebuildSecurityMatrices, 13)).Should().BeNull();
			(await repository.GetPendingAsync((int)SystemOperationTypes.ReportingRollup, null)).Should().BeNull();
		}

		[Test]
		public async Task Recent_requests_come_newest_first_and_honour_the_limit()
		{
			await QueueAsync(SystemOperationTypes.ReportingRollup, null, Base);
			var newest = await QueueAsync(SystemOperationTypes.ChatRetention, null, Base.AddMinutes(2));
			await QueueAsync(SystemOperationTypes.BidExpiration, null, Base.AddMinutes(1));

			var recent = await Repository().GetRecentAsync(2);

			recent.Should().HaveCount(2);
			recent[0].SystemOperationRequestId.Should().Be(newest.SystemOperationRequestId);
		}

		[Test]
		public async Task Claims_take_the_oldest_waiting_request_and_mark_it_running()
		{
			var second = await QueueAsync(SystemOperationTypes.ReportingRollup, null, Base.AddMinutes(1));
			var first = await QueueAsync(SystemOperationTypes.ChatRetention, null, Base);
			var repository = Repository();

			var claimed = await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(5));

			claimed.SystemOperationRequestId.Should().Be(first.SystemOperationRequestId);
			claimed.Status.Should().Be((int)SystemOperationStatuses.Running);
			claimed.WorkerName.Should().Be("worker-a");
			claimed.StartedOn.Should().Be(Base.AddMinutes(5));
			claimed.HeartbeatOn.Should().Be(Base.AddMinutes(5));

			(await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(6))).SystemOperationRequestId.Should().Be(second.SystemOperationRequestId);
			(await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(7))).Should().BeNull();
		}

		[Test]
		public async Task Concurrent_claimers_never_take_the_same_request()
		{
			for (var i = 0; i < 4; i++)
				await QueueAsync(SystemOperationTypes.ReportingRollup, null, Base.AddSeconds(i));

			var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
				Task.Run(() => Repository().ClaimNextPendingAsync($"worker-{i}", Base.AddMinutes(5)))));

			var won = claims.Where(c => c != null).Select(c => c.SystemOperationRequestId).ToList();
			won.Should().OnlyHaveUniqueItems();
			won.Should().HaveCount(4);
		}

		[Test]
		public async Task Heartbeats_and_outcomes_only_land_on_a_running_request()
		{
			var request = await QueueAsync(SystemOperationTypes.RebuildSecurityMatrices, null, Base);
			var repository = Repository();

			(await repository.HeartbeatAsync(request.SystemOperationRequestId, "too early", Base.AddMinutes(1))).Should().BeFalse("it is still waiting");
			(await repository.FinishAsync(request.SystemOperationRequestId, (int)SystemOperationStatuses.Completed, "too early", Base.AddMinutes(1))).Should().BeFalse();

			await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(2));
			(await repository.HeartbeatAsync(request.SystemOperationRequestId, "Rebuilt 10 of 40 departments.", Base.AddMinutes(3))).Should().BeTrue();
			(await repository.HeartbeatAsync(request.SystemOperationRequestId, null, Base.AddMinutes(4))).Should().BeTrue();

			var running = await repository.GetByIdAsync(request.SystemOperationRequestId);
			running.Progress.Should().Be("Rebuilt 10 of 40 departments.", "a beat without a new line keeps the last one");
			running.HeartbeatOn.Should().Be(Base.AddMinutes(4));

			var longResult = new string('x', SystemOperationRequest.ResultMaxLength + 100);
			(await repository.FinishAsync(request.SystemOperationRequestId, (int)SystemOperationStatuses.Failed, longResult, Base.AddMinutes(5))).Should().BeTrue();
			(await repository.FinishAsync(request.SystemOperationRequestId, (int)SystemOperationStatuses.Completed, "again", Base.AddMinutes(6))).Should().BeFalse("it already finished");

			var finished = await repository.GetByIdAsync(request.SystemOperationRequestId);
			finished.Status.Should().Be((int)SystemOperationStatuses.Failed);
			finished.CompletedOn.Should().Be(Base.AddMinutes(5));
			finished.Result.Length.Should().Be(SystemOperationRequest.ResultMaxLength);
		}

		[Test]
		public async Task Only_a_waiting_request_can_be_cancelled()
		{
			var waiting = await QueueAsync(SystemOperationTypes.ReportingRollup, null, Base);
			var running = await QueueAsync(SystemOperationTypes.ChatRetention, null, Base.AddSeconds(-1));
			var repository = Repository();
			await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(1));

			(await repository.CancelPendingAsync(running.SystemOperationRequestId, "ops", Base.AddMinutes(2))).Should().BeFalse();
			(await repository.CancelPendingAsync(waiting.SystemOperationRequestId, "ops", Base.AddMinutes(2))).Should().BeTrue();

			var cancelled = await repository.GetByIdAsync(waiting.SystemOperationRequestId);
			cancelled.Status.Should().Be((int)SystemOperationStatuses.Cancelled);
			cancelled.CancelledBy.Should().Be("ops");
			(await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(3))).Should().BeNull("a cancelled request is never claimed");
		}

		[Test]
		public async Task A_running_request_without_a_recent_heartbeat_is_failed_as_abandoned()
		{
			var stale = await QueueAsync(SystemOperationTypes.ReportingRollup, null, Base);
			var live = await QueueAsync(SystemOperationTypes.ChatRetention, null, Base.AddSeconds(1));
			var repository = Repository();
			await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(1));
			await repository.ClaimNextPendingAsync("worker-a", Base.AddMinutes(1));
			await repository.HeartbeatAsync(live.SystemOperationRequestId, null, Base.AddMinutes(9));

			var failed = await repository.FailAbandonedAsync(Base.AddMinutes(5), "worker stopped", Base.AddMinutes(10));

			failed.Should().Be(1);
			var abandoned = await repository.GetByIdAsync(stale.SystemOperationRequestId);
			abandoned.Status.Should().Be((int)SystemOperationStatuses.Failed);
			abandoned.Result.Should().Be("worker stopped");
			(await repository.GetByIdAsync(live.SystemOperationRequestId)).Status.Should().Be((int)SystemOperationStatuses.Running);
		}
	}
}
