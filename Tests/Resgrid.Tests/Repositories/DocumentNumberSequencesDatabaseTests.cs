using System;
using System.Collections.Concurrent;
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
	/// Real-database proof for M0268 on both engines: the DisplayNumber columns are added and backfilled with the numbers each
	/// document always showed, the counter upsert issues each sequence once (also under concurrent callers), a raised next
	/// number only ever rises, and the seed scan reads the highest sequence already written into a kind's number column. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class DocumentNumberSequencesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "document_number_sequences_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private DocumentNumberSequencesRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new DocumentNumberSequencesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
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

			// Minimal stand-ins for the tables M0268 alters and the number columns the seed scan reads, with rows written before it.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? @"CREATE EXTENSION IF NOT EXISTS citext;
						CREATE TABLE workorders (id serial PRIMARY KEY, departmentid int NOT NULL, numberyear int NOT NULL, numbersequence int NOT NULL);
						CREATE TABLE invoices (id serial PRIMARY KEY, departmentid int NOT NULL, invoicenumber int NOT NULL);
						CREATE TABLE bids (id serial PRIMARY KEY, departmentid int NOT NULL, bidnumber int NOT NULL);
						CREATE TABLE deploymenttimereports (id serial PRIMARY KEY, departmentid int NOT NULL, reportnumber int NOT NULL);
						CREATE TABLE rmsdisclosurerequests (id serial PRIMARY KEY, departmentid int NOT NULL, requestnumber citext NULL);
						INSERT INTO workorders (departmentid, numberyear, numbersequence) VALUES (1, 2026, 19), (1, 2026, 1234567);
						INSERT INTO invoices (departmentid, invoicenumber) VALUES (1, 1042);
						INSERT INTO bids (departmentid, bidnumber) VALUES (1, 7);
						INSERT INTO deploymenttimereports (departmentid, reportnumber) VALUES (1, 3);"
					: @"CREATE TABLE WorkOrders (Id int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, NumberYear int NOT NULL, NumberSequence int NOT NULL);
						CREATE TABLE Invoices (Id int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, InvoiceNumber int NOT NULL);
						CREATE TABLE Bids (Id int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, BidNumber int NOT NULL);
						CREATE TABLE DeploymentTimeReports (Id int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, ReportNumber int NOT NULL);
						CREATE TABLE RmsDisclosureRequests (Id int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, RequestNumber nvarchar(50) NULL);
						INSERT INTO WorkOrders (DepartmentId, NumberYear, NumberSequence) VALUES (1, 2026, 19), (1, 2026, 1234567);
						INSERT INTO Invoices (DepartmentId, InvoiceNumber) VALUES (1, 1042);
						INSERT INTO Bids (DepartmentId, BidNumber) VALUES (1, 7);
						INSERT INTO DeploymentTimeReports (DepartmentId, ReportNumber) VALUES (1, 3);");

				if (database is NpgsqlConnection npgsql)
					await npgsql.ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0268_AddDocumentNumberingPg() }
				: new IMigration[] { new M0268_AddDocumentNumbering() });
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
			if (_database == null) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task<string[]> NumbersAsync(string table)
		{
			await using var database = Connect(_connection);
			return (await database.QueryAsync<string>(IsPostgres ? $"SELECT displaynumber FROM {table.ToLowerInvariant()} ORDER BY id" : $"SELECT DisplayNumber FROM {table} ORDER BY Id")).ToArray();
		}

		[Test]
		public async Task Existing_documents_are_backfilled_with_the_numbers_they_always_showed()
		{
			(await NumbersAsync("WorkOrders")).Should().Equal("WO-2026-000019", "WO-2026-1234567");
			(await NumbersAsync("Invoices")).Should().Equal("1042");
			(await NumbersAsync("Bids")).Should().Equal("7");
			(await NumbersAsync("DeploymentTimeReports")).Should().Equal("3");
		}

		[Test]
		public async Task Postgres_text_columns_are_citext()
		{
			if (!IsPostgres) Assert.Ignore("citext is the PostgreSQL convention; SQL Server compares case-insensitively by collation.");

			await using var database = Connect(_connection);
			var types = (await database.QueryAsync<(string Table, string Column, string Type)>(
				@"SELECT table_name, column_name, udt_name FROM information_schema.columns
				  WHERE (table_name = 'documentnumbersequences' AND column_name IN ('kind', 'scopekey', 'floorsetbyuserid'))
				     OR (table_name IN ('workorders', 'invoices', 'bids', 'deploymenttimereports') AND column_name = 'displaynumber')")).ToList();

			types.Should().HaveCount(7);
			types.Should().OnlyContain(t => t.Type == "citext", string.Join(", ", types.Select(t => t.Table + "." + t.Column + "=" + t.Type)));
		}

		[Test]
		public async Task A_new_scope_starts_above_its_seed_and_then_counts_up_per_kind()
		{
			var repository = Repository();

			(await repository.GetSequenceAsync(1, DocumentNumberKinds.Invoice, "INV-2026-#")).Should().BeNull();
			(await repository.TakeNextAsync(1, DocumentNumberKinds.Invoice, "INV-2026-#", 41)).Should().Be(42);
			(await repository.TakeNextAsync(1, DocumentNumberKinds.Invoice, "INV-2026-#", 41)).Should().Be(43, "the seed only applies to the first number of a scope");
			(await repository.TakeNextAsync(1, DocumentNumberKinds.Bid, "INV-2026-#", 0)).Should().Be(1, "kinds never share a sequence");
			(await repository.TakeNextAsync(2, DocumentNumberKinds.Invoice, "INV-2026-#", 0)).Should().Be(1, "departments never share a sequence");
		}

		[Test]
		public async Task Concurrent_documents_never_share_a_sequence()
		{
			var issued = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() => Repository().TakeNextAsync(3, DocumentNumberKinds.WorkOrder, "MNT-27-#", 0))));

			issued.Should().OnlyHaveUniqueItems();
			issued.OrderBy(i => i).Should().Equal(Enumerable.Range(1, 25));
		}

		[Test]
		public async Task A_raised_next_number_only_ever_rises()
		{
			var repository = Repository();
			await repository.TakeNextAsync(4, DocumentNumberKinds.TimeReport, "DTR-#", 10);

			await repository.RaiseFloorAsync(4, DocumentNumberKinds.TimeReport, "DTR-#", 500, "admin", new DateTime(2026, 10, 8, 12, 0, 0));
			await repository.RaiseFloorAsync(4, DocumentNumberKinds.TimeReport, "DTR-#", 200, "other", new DateTime(2026, 10, 8, 13, 0, 0));
			(await repository.TakeNextAsync(4, DocumentNumberKinds.TimeReport, "DTR-#", 0)).Should().Be(500);
			(await repository.GetSequenceAsync(4, DocumentNumberKinds.TimeReport, "DTR-#")).FloorSequence.Should().Be(500);
		}

		[Test]
		public async Task The_seed_scan_reads_the_kinds_own_number_column()
		{
			var repository = Repository();
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres
					? "INSERT INTO rmsdisclosurerequests (departmentid, requestnumber) VALUES (1, 'PRR-2026-0009'), (1, 'prr-2026-0012'), (1, 'PRR-2025-0040'), (2, 'PRR-2026-0090')"
					: "INSERT INTO RmsDisclosureRequests (DepartmentId, RequestNumber) VALUES (1, 'PRR-2026-0009'), (1, 'prr-2026-0012'), (1, 'PRR-2025-0040'), (2, 'PRR-2026-0090')");

			(await repository.GetHighestIssuedAsync(1, DocumentNumberKinds.RecordsRequest, "PRR-2026-", "")).Should().Be(12, "the number columns compare case-insensitively");
			(await repository.GetHighestIssuedAsync(1, DocumentNumberKinds.WorkOrder, "WO-2026-", "")).Should().Be(1234567, "the backfilled built-in numbers seed a pattern that reads like them");
			(await repository.GetHighestIssuedAsync(1, DocumentNumberKinds.Invoice, "", "")).Should().Be(1042);
			(await repository.GetHighestIssuedAsync(1, "unknown-kind", "", "")).Should().Be(0);
		}
	}
}
