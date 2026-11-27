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
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for round 7A on both engines: M0399 (ProtocolFlashcards) Up, Up again as a no-op, Down and Up; then the repository on
	/// rows read back from the database (department-scoped reads in order, the supersede hook's one update, flagging named cards, an edit and a
	/// delete) and the protocol department cleanup taking the department's cards only.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolFlashcardsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_cards_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private ProtocolFlashcardsRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			queries[typeof(DeleteQuery)] = new DeleteQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return new ProtocolFlashcardsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private IMigration[] Migrations() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0399_AddProtocolFlashcardsPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0399_AddProtocolFlashcards() };

		private void Migrate(Action<IMigrationRunner> run)
		{
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(Migrations());
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			run(provider.GetRequiredService<IMigrationRunner>());
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				// The protocol cleanup runs only where the protocol tree exists: its root table is enough here.
				await database.ExecuteAsync(IsPostgres
					? "CREATE EXTENSION IF NOT EXISTS citext; CREATE TABLE dispatchprotocols (dispatchprotocolid serial PRIMARY KEY, departmentid integer NOT NULL, name citext NOT NULL);"
					: "CREATE TABLE DispatchProtocols (DispatchProtocolId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(100) NOT NULL);");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
				await database.ExecuteAsync($"INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}) VALUES (1, 'Arrest'), (2, 'Theirs')");
			}

			Migrate(r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Drop_isolated_database()
		{
			DataConfig.DatabaseType = _previous;
			if (string.IsNullOrWhiteSpace(_master) || _database == null)
				return;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres
				? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)"
				: $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
		}

		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'protocolflashcards') + " +
				  "(SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_protocolflashcards_protocol')"
				: "SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'ProtocolFlashcards') + " +
				  "(SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_ProtocolFlashcards_Protocol')");
		}

		[Test, Order(1)]
		public async Task Up_creates_the_table_and_index_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(2);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" = 399" : "DELETE FROM [VersionInfo] WHERE [Version] = 399");

			Migrate(r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(2);
		}

		[Test, Order(2)]
		public async Task Down_removes_it_and_up_rebuilds_it()
		{
			Migrate(r => r.MigrateDown(398));
			(await CountSchemaAsync()).Should().Be(0);

			Migrate(r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(2);
		}

		private static ProtocolFlashcard Card(int departmentId, int protocolId, int versionId, string front, int order) => new ProtocolFlashcard
		{
			DepartmentId = departmentId, DispatchProtocolId = protocolId, ProtocolVersionId = versionId, SourceVersionLabel = "1.0", SourceAnchor = "b-abc123",
			SourceHeading = "Compressions", Front = front, Back = "100 to 120 per minute.", Status = (int)ProtocolFlashcardStatuses.Active, SortOrder = order,
			ExchangeId = Guid.NewGuid().ToString("D"), CreatedOn = DateTime.UtcNow, CreatedByUserId = "Author-1"
		};

		[Test, Order(3)]
		public async Task Cards_are_read_by_department_and_protocol_and_flagged_in_one_update()
		{
			var repository = Repository();
			var first = await repository.InsertAsync(Card(1, 1, 401, "Rate?", 2), CancellationToken.None, true);
			var second = await repository.InsertAsync(Card(1, 1, 401, "Depth?", 1), CancellationToken.None, true);
			var current = await repository.InsertAsync(Card(1, 1, 402, "Already current?", 3), CancellationToken.None, true);
			var theirs = await repository.InsertAsync(Card(2, 2, 401, "Theirs?", 1), CancellationToken.None, true);

			var cards = await repository.GetForProtocolAsync(1, 1);
			cards.Select(c => c.Front).Should().Equal("Depth?", "Rate?", "Already current?");
			cards.Should().OnlyContain(c => c.CreatedOn.Kind == DateTimeKind.Utc);
			(await repository.GetByIdForDepartmentAsync(2, first.ProtocolFlashcardId)).Should().BeNull("another department's card");
			(await repository.GetByIdForDepartmentAsync(1, first.ProtocolFlashcardId)).Front.Should().Be("Rate?");

			var now = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
			(await repository.FlagOtherVersionsAsync(1, 1, 402, (int)ProtocolFlashcardFlagReasons.SourceSuperseded, now)).Should().Be(2);
			(await repository.FlagOtherVersionsAsync(1, 1, 402, (int)ProtocolFlashcardFlagReasons.SourceSuperseded, now.AddHours(1))).Should().Be(0, "already flagged cards keep their first flag");
			var flagged = await repository.GetForProtocolAsync(1, 1);
			flagged.Where(c => c.ProtocolVersionId == 401).Should().OnlyContain(c => c.Status == (int)ProtocolFlashcardStatuses.NeedsReview && c.FlaggedOn == now);
			flagged.Single(c => c.ProtocolFlashcardId == current.ProtocolFlashcardId).IsActive.Should().BeTrue();
			(await repository.GetByIdForDepartmentAsync(2, theirs.ProtocolFlashcardId)).IsActive.Should().BeTrue("another department is untouched");

			(await repository.FlagCardsAsync(1, new[] { current.ProtocolFlashcardId, theirs.ProtocolFlashcardId }, (int)ProtocolFlashcardFlagReasons.SourceUnavailable, now)).Should().Be(1);
			(await repository.GetByIdForDepartmentAsync(1, current.ProtocolFlashcardId)).FlagReason.Should().Be((int)ProtocolFlashcardFlagReasons.SourceUnavailable);

			// A review writes the card back; a removal deletes it.
			var reviewed = await repository.GetByIdForDepartmentAsync(1, second.ProtocolFlashcardId);
			reviewed.Status = (int)ProtocolFlashcardStatuses.Active;
			reviewed.ProtocolVersionId = 402;
			reviewed.ReviewedOn = now;
			reviewed.ReviewedByUserId = "Author-1";
			await repository.UpdateAsync(reviewed, CancellationToken.None, true);
			(await repository.GetByIdForDepartmentAsync(1, second.ProtocolFlashcardId)).Should().Match<ProtocolFlashcard>(c => c.IsActive && c.ProtocolVersionId == 402 && c.ReviewedOn == now);
			(await repository.DeleteAsync(reviewed, CancellationToken.None)).Should().BeTrue();
			(await repository.GetForProtocolAsync(1, 1)).Should().HaveCount(2);
		}

		[Test, Order(4)]
		public async Task The_protocol_department_cleanup_takes_the_departments_cards_only()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolFlashcards")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolFlashcards")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
