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
	/// <see cref="RmsHydrantsRepository.GetByNumbersAsync"/> against a real server: every requested number resolves to exactly what
	/// <see cref="RmsHydrantsRepository.GetByNumberAsync"/> returns, whatever the column collation decides about case. The
	/// numbers span more than one lookup chunk (SQL Server's 2,100-parameter ceiling). Disposable database, skipped unless the
	/// checklist test connection for the dialect is set (the <c>InventoryDatabaseTests</c> convention).
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class HydrantNumberLookupDatabaseTests
	{
		private const string DatabasePrefix = "hydrant_lookup_verification_";
		private const int Dept = 77;
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private bool _created, _configured;

		public HydrantNumberLookupDatabaseTests(DatabaseTypes type) { _type = type; }

		private DbConnection Connect(string connection) => _type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => _type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private RmsHydrantsRepository Repository(IUnitOfWork unitOfWork = null)
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => Connect(_connection));
			return new RmsHydrantsRepository(provider.Object, Configuration(), unitOfWork ?? new UnitOfWork(provider.Object), Mock.Of<IQueryFactory>());
		}

		[OneTimeSetUp]
		public async Task CreateOnlyAnIsolatedDatabase()
		{
			var configured = Environment.GetEnvironmentVariable(_type == DatabaseTypes.Postgres ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Set the checklist test connection for " + _type + " to enable the disposable hydrant lookup database tests.");
			if (_type == DatabaseTypes.Postgres)
			{
				var builder = new NpgsqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.Database) && builder.Database != "postgres" && builder.Database != "template1") throw new InvalidOperationException("Hydrant lookup tests require a PostgreSQL administrative database connection.");
				builder.Database = "postgres"; builder.IncludeErrorDetail = false; _master = builder.ConnectionString;
			}
			else
			{
				var builder = new SqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hydrant lookup tests require a SQL Server master connection.");
				builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			}
			_previous = DataConfig.DatabaseType; _configured = true; DataConfig.DatabaseType = _type;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = _type == DatabaseTypes.Postgres ? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// The M0186 column types for the columns the lookup touches: AsString(64) is nvarchar(64) / varchar(64) with the
			// database's default collation.
			await using var db = Connect(_connection);
			await db.ExecuteAsync(_type == DatabaseTypes.Postgres
				? "CREATE TABLE public.rmshydrants (rmshydrantid varchar(128) NOT NULL PRIMARY KEY, departmentid integer NOT NULL, hydrantnumber varchar(64) NOT NULL, latitude decimal(9,6) NOT NULL, longitude decimal(9,6) NOT NULL, rowversion bigint NOT NULL, deletedon timestamp NULL)"
				: "CREATE TABLE [dbo].[RmsHydrants] ([RmsHydrantId] nvarchar(128) NOT NULL PRIMARY KEY, [DepartmentId] int NOT NULL, [HydrantNumber] nvarchar(64) NOT NULL, [Latitude] decimal(9,6) NOT NULL, [Longitude] decimal(9,6) NOT NULL, [RowVersion] bigint NOT NULL, [DeletedOn] datetime2 NULL)");
			var rows = new List<object>
			{
				new { Id = "live-h2", Dept, Number = "H-2", Deleted = (DateTime?)null },
				new { Id = "deleted-h3", Dept, Number = "H-3", Deleted = (DateTime?)DateTime.UtcNow },
				new { Id = "other-dept-h4", Dept = Dept + 1, Number = "H-4", Deleted = (DateTime?)null }
			};
			rows.AddRange(Enumerable.Range(1, 2500).Select(i => (object)new { Id = "bulk-" + i, Dept, Number = "B" + i, Deleted = (DateTime?)null }));
			var table = _type == DatabaseTypes.Postgres ? "public.rmshydrants (rmshydrantid, departmentid, hydrantnumber, latitude, longitude, rowversion, deletedon)"
				: "[dbo].[RmsHydrants] ([RmsHydrantId], [DepartmentId], [HydrantNumber], [Latitude], [Longitude], [RowVersion], [DeletedOn])";
			await db.ExecuteAsync($"INSERT INTO {table} VALUES (@Id, @Dept, @Number, 1, 2, 1, @Deleted)", rows);
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_configured) DataConfig.DatabaseType = _previous;
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _)) throw new InvalidOperationException("Unexpected hydrant lookup fixture database name.");
			if (_type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(_type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[Test]
		public async Task Batch_lookup_returns_exactly_what_the_single_lookup_returns_for_every_number()
		{
			var repository = Repository();
			var requested = new List<string> { "H-2", "h-2", "H-3", "H-4", "missing" };
			requested.AddRange(Enumerable.Range(1, 2500).Select(i => "B" + i));

			var batch = await repository.GetByNumbersAsync(Dept, requested);

			foreach (var number in requested)
			{
				var single = await repository.GetByNumberAsync(Dept, number);
				(batch.TryGetValue(number, out var found) ? found?.RmsHydrantId : null).Should().Be(single?.RmsHydrantId, $"'{number}' must resolve as GetByNumberAsync does");
			}
			batch["H-2"].HydrantNumber.Should().Be("H-2");
			batch.Should().NotContainKeys("H-3", "H-4", "missing");
			batch.Keys.Count(k => k.StartsWith("B", StringComparison.Ordinal)).Should().Be(2500, "every chunk is queried");
		}

		[Test]
		public async Task Batch_lookup_runs_inside_the_unit_of_work_transaction()
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => Connect(_connection));
			using var unitOfWork = new UnitOfWork(provider.Object);
			var repository = Repository(unitOfWork);
			await unitOfWork.CreateOrGetConnectionAsync();
			try
			{
				await unitOfWork.Connection.ExecuteAsync(_type == DatabaseTypes.Postgres
					? "INSERT INTO public.rmshydrants (rmshydrantid, departmentid, hydrantnumber, latitude, longitude, rowversion) VALUES ('uncommitted', 77, 'T-1', 1, 2, 1)"
					: "INSERT INTO [dbo].[RmsHydrants] ([RmsHydrantId], [DepartmentId], [HydrantNumber], [Latitude], [Longitude], [RowVersion]) VALUES ('uncommitted', 77, 'T-1', 1, 2, 1)",
					transaction: unitOfWork.Transaction);
				(await repository.GetByNumbersAsync(Dept, new[] { "T-1" })).Should().ContainKey("T-1", "the lookup reads on the transaction's connection");
			}
			finally { unitOfWork.DiscardChanges(); }
			(await Repository().GetByNumbersAsync(Dept, new[] { "T-1" })).Should().BeEmpty("the rolled-back row is gone");
		}
	}
}
