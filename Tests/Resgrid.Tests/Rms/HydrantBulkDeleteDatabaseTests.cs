using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// <see cref="RmsHydrantsRepository.CountForBulkDeleteAsync"/> and <see cref="RmsHydrantsRepository.SoftDeleteBulkAsync"/> against a
	/// real server: each scope removes exactly the live rows of one department, a selection spans more than one id chunk (SQL Server's
	/// 2,100-parameter ceiling), and a caller's transaction decides whether the removal sticks. Disposable database, skipped unless the
	/// checklist test connection for the dialect is set (the <c>HydrantNumberLookupDatabaseTests</c> convention).
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class HydrantBulkDeleteDatabaseTests
	{
		private const string DatabasePrefix = "hydrant_bulk_delete_verification_";
		private const int Dept = 77;
		private const int CsvRows = 1500;
		private static readonly DateTime EarlierRemoval = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private bool _created, _configured;

		public HydrantBulkDeleteDatabaseTests(DatabaseTypes type) { _type = type; }

		private DbConnection Connect(string connection) => _type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => _type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private string Table => _type == DatabaseTypes.Postgres ? "public.rmshydrants" : "[dbo].[RmsHydrants]";

		private Mock<IConnectionProvider> Provider()
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => Connect(_connection));
			return provider;
		}

		private RmsHydrantsRepository Repository(IUnitOfWork unitOfWork = null)
		{
			var provider = Provider();
			return new RmsHydrantsRepository(provider.Object, Configuration(), unitOfWork ?? new UnitOfWork(provider.Object), Mock.Of<IQueryFactory>());
		}

		private sealed class Row
		{
			public string RmsHydrantId { get; set; }
			public int DepartmentId { get; set; }
			public long RowVersion { get; set; }
			public DateTime ModifiedOn { get; set; }
			public DateTime? DeletedOn { get; set; }
		}

		private async Task<Dictionary<string, Row>> RowsAsync()
		{
			await using var db = Connect(_connection);
			var sql = _type == DatabaseTypes.Postgres
				? $"SELECT rmshydrantid AS RmsHydrantId, departmentid AS DepartmentId, rowversion AS RowVersion, modifiedon AS ModifiedOn, deletedon AS DeletedOn FROM {Table}"
				: $"SELECT [RmsHydrantId], [DepartmentId], [RowVersion], [ModifiedOn], [DeletedOn] FROM {Table}";
			return (await db.QueryAsync<Row>(sql)).ToDictionary(r => r.RmsHydrantId);
		}

		[OneTimeSetUp]
		public async Task CreateOnlyAnIsolatedDatabase()
		{
			var configured = Environment.GetEnvironmentVariable(_type == DatabaseTypes.Postgres ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Set the checklist test connection for " + _type + " to enable the disposable hydrant bulk-delete database tests.");
			if (_type == DatabaseTypes.Postgres)
			{
				var builder = new NpgsqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.Database) && builder.Database != "postgres" && builder.Database != "template1") throw new InvalidOperationException("Hydrant bulk-delete tests require a PostgreSQL administrative database connection.");
				builder.Database = "postgres"; builder.IncludeErrorDetail = false; _master = builder.ConnectionString;
			}
			else
			{
				var builder = new SqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hydrant bulk-delete tests require a SQL Server master connection.");
				builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			}
			_previous = DataConfig.DatabaseType; _configured = true; DataConfig.DatabaseType = _type;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = _type == DatabaseTypes.Postgres ? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// The M0186 column types for the columns the bulk removal reads and writes.
			await using var db = Connect(_connection);
			await db.ExecuteAsync(_type == DatabaseTypes.Postgres
				? "CREATE TABLE public.rmshydrants (rmshydrantid varchar(128) NOT NULL PRIMARY KEY, departmentid integer NOT NULL, hydrantnumber varchar(64) NOT NULL, source varchar(32) NULL, modifiedon timestamp NOT NULL, rowversion bigint NOT NULL, deletedon timestamp NULL)"
				: "CREATE TABLE [dbo].[RmsHydrants] ([RmsHydrantId] nvarchar(128) NOT NULL PRIMARY KEY, [DepartmentId] int NOT NULL, [HydrantNumber] nvarchar(64) NOT NULL, [Source] nvarchar(32) NULL, [ModifiedOn] datetime2 NOT NULL, [RowVersion] bigint NOT NULL, [DeletedOn] datetime2 NULL)");
		}

		[SetUp]
		public async Task Seed()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync("DELETE FROM " + Table);
			var columns = _type == DatabaseTypes.Postgres ? "(rmshydrantid, departmentid, hydrantnumber, source, modifiedon, rowversion, deletedon)"
				: "([RmsHydrantId], [DepartmentId], [HydrantNumber], [Source], [ModifiedOn], [RowVersion], [DeletedOn])";
			var rows = new List<object>
			{
				new { Id = "manual-1", Dept, Number = "M-1", Source = "manual", Deleted = (DateTime?)null },
				new { Id = "json-1", Dept, Number = "J-1", Source = "json-import", Deleted = (DateTime?)null },
				new { Id = "null-source", Dept, Number = "N-1", Source = (string)null, Deleted = (DateTime?)null },
				new { Id = "gone-1", Dept, Number = "G-1", Source = "csv-import", Deleted = (DateTime?)EarlierRemoval },
				new { Id = "other-1", Dept = Dept + 1, Number = "O-1", Source = "csv-import", Deleted = (DateTime?)null }
			};
			rows.AddRange(Enumerable.Range(1, CsvRows).Select(i => (object)new { Id = "csv-" + i, Dept, Number = "C" + i, Source = "csv-import", Deleted = (DateTime?)null }));
			await db.ExecuteAsync($"INSERT INTO {Table} {columns} VALUES (@Id, @Dept, @Number, @Source, '2026-01-01', 1, @Deleted)", rows);
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_configured) DataConfig.DatabaseType = _previous;
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _)) throw new InvalidOperationException("Unexpected hydrant bulk-delete fixture database name.");
			if (_type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(_type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private void UntouchedExceptRemoved(Dictionary<string, Row> before, Dictionary<string, Row> after, ISet<string> removed, DateTime now)
		{
			foreach (var (id, row) in after)
			{
				if (removed.Contains(id))
				{
					row.DeletedOn.Should().BeCloseTo(now, TimeSpan.FromSeconds(1), id);
					row.ModifiedOn.Should().Be(row.DeletedOn.Value, id);
					row.RowVersion.Should().Be(before[id].RowVersion + 1, id);
				}
				else
				{
					row.DeletedOn.Should().Be(before[id].DeletedOn, id);
					row.RowVersion.Should().Be(before[id].RowVersion, id);
				}
			}
		}

		[Test]
		public async Task Imported_removes_the_live_imported_rows_of_the_department_only()
		{
			var repository = Repository();
			var before = await RowsAsync();
			var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

			(await repository.CountForBulkDeleteAsync(Dept, HydrantBulkDeleteScope.Imported, null)).Should().Be(CsvRows + 1);
			(await repository.SoftDeleteBulkAsync(Dept, HydrantBulkDeleteScope.Imported, null, now)).Should().Be(CsvRows + 1);

			var expected = Enumerable.Range(1, CsvRows).Select(i => "csv-" + i).Append("json-1").ToHashSet();
			UntouchedExceptRemoved(before, await RowsAsync(), expected, now);
			(await repository.CountForBulkDeleteAsync(Dept, HydrantBulkDeleteScope.Imported, null)).Should().Be(0);
		}

		[Test]
		public async Task Selected_spans_id_chunks_and_ignores_foreign_and_removed_ids()
		{
			var repository = Repository();
			var before = await RowsAsync();
			var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
			var ids = Enumerable.Range(1, CsvRows).Select(i => "csv-" + i).Concat(new[] { "manual-1", "other-1", "gone-1", "missing", "csv-1" }).ToList();
			ids.Count.Should().BeGreaterThan(RmsHydrantsRepository.BulkDeleteIdChunkSize);

			(await repository.CountForBulkDeleteAsync(Dept, HydrantBulkDeleteScope.Selected, ids)).Should().Be(CsvRows + 1);
			(await repository.SoftDeleteBulkAsync(Dept, HydrantBulkDeleteScope.Selected, ids, now)).Should().Be(CsvRows + 1);

			var expected = Enumerable.Range(1, CsvRows).Select(i => "csv-" + i).Append("manual-1").ToHashSet();
			UntouchedExceptRemoved(before, await RowsAsync(), expected, now);
		}

		[Test]
		public async Task All_removes_every_live_row_of_the_department()
		{
			var repository = Repository();
			var before = await RowsAsync();
			var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

			(await repository.CountForBulkDeleteAsync(Dept, HydrantBulkDeleteScope.All, null)).Should().Be(CsvRows + 3);
			(await repository.SoftDeleteBulkAsync(Dept, HydrantBulkDeleteScope.All, null, now)).Should().Be(CsvRows + 3);

			var expected = Enumerable.Range(1, CsvRows).Select(i => "csv-" + i).Concat(new[] { "manual-1", "json-1", "null-source" }).ToHashSet();
			UntouchedExceptRemoved(before, await RowsAsync(), expected, now);
			(await repository.CountForBulkDeleteAsync(Dept + 1, HydrantBulkDeleteScope.All, null)).Should().Be(1);
		}

		[Test]
		public async Task A_callers_transaction_decides_whether_the_removal_sticks()
		{
			var provider = Provider();
			using var unitOfWork = new UnitOfWork(provider.Object);
			var repository = Repository(unitOfWork);
			await unitOfWork.CreateOrGetConnectionAsync();
			try
			{
				(await repository.SoftDeleteBulkAsync(Dept, HydrantBulkDeleteScope.All, null, DateTime.UtcNow)).Should().Be(CsvRows + 3);
				unitOfWork.Transaction.Should().NotBeNull("the repository leaves a caller's transaction open");
			}
			finally { unitOfWork.DiscardChanges(); }
			(await Repository().CountForBulkDeleteAsync(Dept, HydrantBulkDeleteScope.All, null)).Should().Be(CsvRows + 3, "the rolled-back removal is gone");
		}
	}
}
