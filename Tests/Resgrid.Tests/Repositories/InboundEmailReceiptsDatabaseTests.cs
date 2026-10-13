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
	/// Real-database proof for M0271 on both engines: a provider message id is inserted once (also under concurrent deliveries,
	/// and without the expected collision ever raising), it is found regardless of case, a claim is only taken over once its lease
	/// has passed or it was given back and by one taker only, the state only moves forward, and old receipts are purged. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class InboundEmailReceiptsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "inbound_email_receipts_";
		private static readonly DateTime Base = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private string Table => IsPostgres ? "inboundemailreceipts" : "InboundEmailReceipts";

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private DateTime At(DateTime value) => IsPostgres ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified) : value;

		private InboundEmailReceiptsRepository Repository()
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

			return new InboundEmailReceiptsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
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
				? new IMigration[] { new M0271_AddInboundEmailReceiptsPg() }
				: new IMigration[] { new M0271_AddInboundEmailReceipts() });
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

		private InboundEmailReceipt NewReceipt(string providerMessageId, DateTime receivedOn) => new InboundEmailReceipt
		{
			InboundEmailReceiptId = Guid.NewGuid().ToString(),
			ProviderMessageId = providerMessageId,
			Kind = (int)InboundEmailKinds.Dispatch,
			State = (int)InboundEmailReceiptStates.Processing,
			PendingSteps = 0,
			Deliveries = 1,
			ReceivedOn = receivedOn,
			ClaimedOn = receivedOn
		};

		private async Task<InboundEmailReceipt> ClaimedAsync(string providerMessageId, DateTime receivedOn)
		{
			var receipt = NewReceipt(providerMessageId, receivedOn);
			(await Repository().TryInsertAsync(receipt)).Should().BeTrue();
			return receipt;
		}

		[Test]
		public async Task A_provider_message_id_is_inserted_once_and_found_regardless_of_case()
		{
			var repository = Repository();
			var first = await ClaimedAsync("a1b2c3d4-0000-4000-8000-00000000000a", Base);

			(await repository.TryInsertAsync(NewReceipt("A1B2C3D4-0000-4000-8000-00000000000A", Base.AddMinutes(1)))).Should().BeFalse();

			var read = await repository.GetByProviderMessageIdAsync("A1B2C3D4-0000-4000-8000-00000000000A");
			read.Should().NotBeNull();
			read.InboundEmailReceiptId.Should().Be(first.InboundEmailReceiptId);
			read.State.Should().Be((int)InboundEmailReceiptStates.Processing);
			read.ClaimedOn.Should().Be(At(Base));
			read.Deliveries.Should().Be(1);
			(await repository.GetByProviderMessageIdAsync("missing")).Should().BeNull();
		}

		[Test]
		public async Task Concurrent_deliveries_of_one_email_insert_one_receipt()
		{
			var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
				Task.Run(() => Repository().TryInsertAsync(NewReceipt("same-email", Base)))));

			outcomes.Count(won => won).Should().Be(1);
			await using var database = Connect(_connection);
			(await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM " + Table)).Should().Be(1);
		}

		[Test]
		public async Task A_claim_is_only_taken_over_after_its_lease_or_once_given_back()
		{
			var receipt = await ClaimedAsync("pm-lease", Base);
			var repository = Repository();
			var processing = (int)InboundEmailReceiptStates.Processing;

			(await repository.TryReclaimAsync(receipt.InboundEmailReceiptId, processing, Base.AddMinutes(1), Base.AddMinutes(-4))).Should().BeFalse("the lease still runs");
			(await repository.TryReclaimAsync(receipt.InboundEmailReceiptId, (int)InboundEmailReceiptStates.Saved, Base.AddMinutes(6), Base.AddMinutes(1))).Should().BeFalse("wrong state");
			(await repository.TryReclaimAsync(receipt.InboundEmailReceiptId, processing, Base.AddMinutes(6), Base.AddMinutes(1))).Should().BeTrue();
			(await repository.TryReclaimAsync(receipt.InboundEmailReceiptId, processing, Base.AddMinutes(6), Base.AddMinutes(1))).Should().BeFalse("the claim was just renewed");

			(await repository.ReleaseAsync(receipt.InboundEmailReceiptId)).Should().BeTrue();
			(await repository.TryReclaimAsync(receipt.InboundEmailReceiptId, processing, Base.AddMinutes(7), Base.AddMinutes(2))).Should().BeTrue("a given-back claim is free at once");

			var read = await repository.GetByIdAsync(receipt.InboundEmailReceiptId);
			read.ClaimedOn.Should().Be(At(Base.AddMinutes(7)));
			read.Deliveries.Should().Be(3);
		}

		[Test]
		public async Task Concurrent_takeovers_have_one_winner()
		{
			var receipt = await ClaimedAsync("pm-race", Base);
			await Repository().ReleaseAsync(receipt.InboundEmailReceiptId);

			var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
				Task.Run(() => Repository().TryReclaimAsync(receipt.InboundEmailReceiptId, (int)InboundEmailReceiptStates.Processing, Base.AddMinutes(10), Base.AddMinutes(5)))));

			outcomes.Count(won => won).Should().Be(1);
		}

		[Test]
		public async Task The_state_only_moves_forward()
		{
			var receipt = await ClaimedAsync("pm-state", Base);
			var repository = Repository();
			var steps = (int)(InboundEmailPendingSteps.CallBroadcast | InboundEmailPendingSteps.AiEnrichment);

			(await repository.MarkSavedAsync(receipt.InboundEmailReceiptId, 271737, steps, Base.AddMinutes(1))).Should().BeTrue();
			(await repository.MarkSavedAsync(receipt.InboundEmailReceiptId, 999, 0, Base.AddMinutes(2))).Should().BeFalse("only a Processing receipt is marked saved");

			var saved = await repository.GetByIdAsync(receipt.InboundEmailReceiptId);
			saved.State.Should().Be((int)InboundEmailReceiptStates.Saved);
			saved.EntityId.Should().Be(271737);
			saved.PendingSteps.Should().Be(steps);
			saved.SavedOn.Should().Be(At(Base.AddMinutes(1)));
			saved.ClaimedOn.Should().Be(At(Base.AddMinutes(1)), "saving renews the claim");

			(await repository.MarkCompletedAsync(receipt.InboundEmailReceiptId, Base.AddMinutes(3))).Should().BeTrue();
			(await repository.MarkCompletedAsync(receipt.InboundEmailReceiptId, Base.AddMinutes(4))).Should().BeFalse();
			(await repository.ReleaseAsync(receipt.InboundEmailReceiptId)).Should().BeFalse("a completed receipt holds no claim");

			(await repository.RecordDeliveryAsync(receipt.InboundEmailReceiptId)).Should().BeTrue();
			var completed = await repository.GetByIdAsync(receipt.InboundEmailReceiptId);
			completed.State.Should().Be((int)InboundEmailReceiptStates.Completed);
			completed.PendingSteps.Should().Be(0);
			completed.CompletedOn.Should().Be(At(Base.AddMinutes(3)));
			completed.EntityId.Should().Be(271737, "the call stays recorded for investigation");
			completed.Deliveries.Should().Be(2);
		}

		[Test]
		public async Task Finished_steps_are_cleared_only_from_a_saved_receipt()
		{
			var receipt = await ClaimedAsync("pm-steps", Base);
			var repository = Repository();
			var steps = (int)(InboundEmailPendingSteps.CallAddedEvent | InboundEmailPendingSteps.DispatchStatuses | InboundEmailPendingSteps.CallBroadcast);

			(await repository.CompleteStepsAsync(receipt.InboundEmailReceiptId, (int)InboundEmailPendingSteps.CallAddedEvent)).Should().BeFalse("nothing is saved yet");

			await repository.MarkSavedAsync(receipt.InboundEmailReceiptId, 700, steps, Base.AddMinutes(1));
			(await repository.CompleteStepsAsync(receipt.InboundEmailReceiptId, (int)InboundEmailPendingSteps.CallAddedEvent)).Should().BeTrue();
			(await repository.CompleteStepsAsync(receipt.InboundEmailReceiptId, (int)InboundEmailPendingSteps.CallAddedEvent)).Should().BeTrue("clearing twice is harmless");
			(await repository.CompleteStepsAsync(receipt.InboundEmailReceiptId, (int)InboundEmailPendingSteps.DispatchStatuses)).Should().BeTrue();

			(await repository.GetByIdAsync(receipt.InboundEmailReceiptId)).PendingSteps.Should().Be((int)InboundEmailPendingSteps.CallBroadcast);
		}

		[Test]
		public async Task Receipts_received_before_the_cutoff_are_purged()
		{
			var old = await ClaimedAsync("pm-old", Base.AddDays(-8));
			var young = await ClaimedAsync("pm-young", Base.AddHours(-11));

			(await Repository().PurgeReceivedBeforeAsync(Base.AddDays(-7))).Should().Be(1);

			(await Repository().GetByIdAsync(old.InboundEmailReceiptId)).Should().BeNull();
			(await Repository().GetByIdAsync(young.InboundEmailReceiptId)).Should().NotBeNull();
		}
	}
}
