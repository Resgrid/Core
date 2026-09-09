using System;
using System.Collections.Generic;
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
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ChecklistDatabaseTests
	{
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;
		public ChecklistDatabaseTests(DatabaseTypes type) { _type = type; }
		private DbConnection Connect(string connection) => _type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		[OneTimeSetUp]
		public async Task CreateIsolatedDatabaseAndMigrate()
		{
			_master = Environment.GetEnvironmentVariable(_type == DatabaseTypes.Postgres ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set the checklist test connection for " + _type + " to run real database verification.");
			_previous = DataConfig.DatabaseType; DataConfig.DatabaseType = _type;
			_database = "checklist_verification_" + Guid.NewGuid().ToString("N");
			await using var master = Connect(_master); await master.ExecuteAsync("CREATE DATABASE " + _database);
			if (_type == DatabaseTypes.Postgres) { var builder = new NpgsqlConnectionStringBuilder(_master) { Database = _database }; _connection = builder.ConnectionString; }
			else { var builder = new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }; _connection = builder.ConnectionString; }
			await using var db = Connect(_connection);
			await db.ExecuteAsync(_type == DatabaseTypes.Postgres ? "CREATE TABLE departments(departmentid integer PRIMARY KEY); INSERT INTO departments VALUES(77),(88);" : "CREATE TABLE Departments(DepartmentId int PRIMARY KEY); INSERT INTO Departments VALUES(77),(88);");
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { _type == DatabaseTypes.Postgres ? new M0191_AddChecklistWorkflowPg() : new M0191_AddChecklistWorkflow() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (_type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}
		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_database == null) return;
			_runner?.Dispose(); DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith("checklist_verification_", StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(23), "N", out _)) throw new InvalidOperationException("Unexpected test database name.");
			if (_type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(_type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)" : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}
		private IConnectionProvider Connections()
		{ var provider = new Mock<IConnectionProvider>(); provider.Setup(p => p.Create()).Returns(() => Connect(_connection)); return provider.Object; }
		private SqlConfiguration Configuration() => _type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private ChecklistRepository Repository(IConnectionProvider provider, IUnitOfWork uow) => new ChecklistRepository(provider, Configuration(), uow, new Mock<IQueryFactory>().Object);
		private static T Row<T>(string parent = null) where T : ChecklistRow, new() => new T { DepartmentId = 77, ParentId = parent, CreatedBy = "author", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, Content = "{}" };
		[Test, Order(0)]
		public void Migration_can_roll_back_and_reapply_in_an_empty_isolated_database()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			runner.MigrateDown(0); runner.MigrateUp(); runner.MigrateUp();
		}
		[Test]
		public async Task Round_trip_versions_occurrences_answers_and_files_and_enforce_tenant_foreign_keys()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			var completion = Row<ChecklistCompletion>(definition.Id); completion.VersionId = version.Id; completion.TargetId = "77";
			var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.VersionId = version.Id; occurrence.CompletionId = completion.Id; occurrence.TargetId = "77"; await store.WriteAsync(occurrence, true);
			completion.OccurrenceId = occurrence.Id; await store.WriteAsync(completion, true);
			var answer = Row<ChecklistCompletionItem>(completion.Id); answer.ItemId = Guid.NewGuid().ToString(); await store.ReplaceAnswersAsync(77, completion.Id, new[] { answer });
			var file = Row<ChecklistCompletionFile>(completion.Id); file.ItemId = answer.ItemId; file.ContentType = "image/png"; file.Data = new byte[] { 1, 2, 3 }; file.Size = 3; file.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file.Data)); file.ScanState = 1; await store.WriteAsync(file, true);
			uow.CommitChanges();
			(await store.GetAsync<ChecklistCompletion>(88, completion.Id)).Should().BeNull();
			(await store.GetAsync<ChecklistCompletion>(77, completion.Id)).VersionId.Should().Be(version.Id);
			(await store.ListAsync<ChecklistCompletionItem>(77, completion.Id)).Should().ContainSingle();
			(await store.ListAsync<ChecklistCompletionFile>(77, completion.Id)).Single().Data.Should().BeNull("list reads must never fetch blobs");
			(await store.GetAsync<ChecklistCompletionFile>(77, file.Id)).Data.Should().Equal(1, 2, 3);
			await uow.CreateOrGetConnectionAsync(); var foreign = Row<ChecklistDefinitionVersion>(definition.Id); foreign.DepartmentId = 88; foreign.Version = 2;
			Func<Task> insert = () => store.WriteAsync(foreign, true); await insert.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
		}
		[Test]
		public async Task Rollback_removes_partial_data_and_duplicate_version_is_rejected()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var row = Row<ChecklistDefinition>(); await store.WriteAsync(row, true); uow.DiscardChanges(); (await store.GetAsync<ChecklistDefinition>(77, row.Id)).Should().BeNull();
			await uow.CreateOrGetConnectionAsync(); await store.WriteAsync(row, true);
			var first = Row<ChecklistDefinitionVersion>(row.Id); first.Version = 1; await store.WriteAsync(first, true); uow.CommitChanges();
			await uow.CreateOrGetConnectionAsync(); var duplicate = Row<ChecklistDefinitionVersion>(row.Id); duplicate.Version = 1;
			Func<Task> insert = () => store.WriteAsync(duplicate, true); await insert.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
		}
		[Test]
		public async Task Department_lock_serializes_two_writers_until_commit()
		{
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			await first.CreateOrGetConnectionAsync(); await second.CreateOrGetConnectionAsync();
			await Repository(connections, first).LockDepartmentAsync(77);
			var waiting = Repository(connections, second).LockDepartmentAsync(77);
			(await Task.WhenAny(waiting, Task.Delay(200))).Should().NotBe(waiting);
			first.CommitChanges(); await waiting.WaitAsync(TimeSpan.FromSeconds(10)); second.CommitChanges();
		}
	}
}
