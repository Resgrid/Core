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
using Resgrid.Model.Search;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// The set-based statements behind bulk map-data removal, against a real server: <see cref="PoisRepository.DeleteForTypeAsync"/>
	/// (id chunks past SQL Server's parameter ceiling, other types untouched, a caller's transaction decides),
	/// <see cref="IndoorMapZonesRepository.SoftDeleteForFloorAsync"/> and <see cref="SearchProjectionsRepository.SoftDeleteManyAsync"/>.
	/// Disposable database, skipped unless the checklist test connection for the dialect is set.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class MapDataBulkRemovalDatabaseTests
	{
		private const string DatabasePrefix = "map_bulk_removal_verification_";
		private const int TypeA = 40, TypeB = 41, ManyPois = 1500, Dept = 7;
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private bool _created, _configured;

		public MapDataBulkRemovalDatabaseTests(DatabaseTypes type) { _type = type; }

		private bool Pg => _type == DatabaseTypes.Postgres;
		private DbConnection Connect(string connection) => Pg ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private SqlConfiguration Configuration() => Pg ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private string T(string name) => Pg ? "public." + name.ToLowerInvariant() : "[dbo].[" + name + "]";

		private Mock<IConnectionProvider> Provider()
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => Connect(_connection));
			return provider;
		}

		private PoisRepository Pois(UnitOfWork unitOfWork = null) { var p = Provider(); return new PoisRepository(p.Object, Configuration(), unitOfWork ?? new UnitOfWork(p.Object), Mock.Of<IQueryFactory>()); }
		private IndoorMapZonesRepository Zones() { var p = Provider(); return new IndoorMapZonesRepository(p.Object, Configuration(), new UnitOfWork(p.Object), Mock.Of<IQueryFactory>()); }
		private SearchProjectionsRepository Projections() { var p = Provider(); return new SearchProjectionsRepository(p.Object, Configuration(), new UnitOfWork(p.Object), Mock.Of<IQueryFactory>()); }

		private async Task<T> Scalar<T>(string sql) { await using var db = Connect(_connection); return await db.ExecuteScalarAsync<T>(sql); }

		[OneTimeSetUp]
		public async Task CreateOnlyAnIsolatedDatabase()
		{
			var configured = Environment.GetEnvironmentVariable(Pg ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Set the checklist test connection for " + _type + " to enable the disposable map bulk-removal database tests.");
			if (Pg)
			{
				var builder = new NpgsqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.Database) && builder.Database != "postgres" && builder.Database != "template1") throw new InvalidOperationException("Map bulk-removal tests require a PostgreSQL administrative database connection.");
				builder.Database = "postgres"; builder.IncludeErrorDetail = false; _master = builder.ConnectionString;
			}
			else
			{
				var builder = new SqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Map bulk-removal tests require a SQL Server master connection.");
				builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			}
			_previous = DataConfig.DatabaseType; _configured = true; DataConfig.DatabaseType = _type;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = Pg ? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString : new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// The columns the three statements read and write, with the production types.
			await using var db = Connect(_connection);
			await db.ExecuteAsync(Pg
				? "CREATE TABLE public.pois (poiid serial PRIMARY KEY, poitypeid integer NOT NULL, longitude double precision NOT NULL, latitude double precision NOT NULL, name text NULL);" +
				  "CREATE TABLE public.indoormapzones (indoormapzoneid varchar(128) PRIMARY KEY, indoormapfloorid varchar(128) NOT NULL, name text NULL, isdeleted boolean NOT NULL);" +
				  "CREATE TABLE public.searchprojections (searchprojectionid varchar(128) PRIMARY KEY, departmentid integer NOT NULL, entitytype varchar(64) NOT NULL, entityid varchar(128) NOT NULL, modifiedon timestamp NOT NULL, rowversion bigint NOT NULL, deletedon timestamp NULL);"
				: "CREATE TABLE [dbo].[Pois] ([PoiId] int IDENTITY(1,1) PRIMARY KEY, [PoiTypeId] int NOT NULL, [Longitude] float NOT NULL, [Latitude] float NOT NULL, [Name] nvarchar(250) NULL);" +
				  "CREATE TABLE [dbo].[IndoorMapZones] ([IndoorMapZoneId] nvarchar(128) PRIMARY KEY, [IndoorMapFloorId] nvarchar(128) NOT NULL, [Name] nvarchar(250) NULL, [IsDeleted] bit NOT NULL);" +
				  "CREATE TABLE [dbo].[SearchProjections] ([SearchProjectionId] nvarchar(128) PRIMARY KEY, [DepartmentId] int NOT NULL, [EntityType] nvarchar(64) NOT NULL, [EntityId] nvarchar(128) NOT NULL, [ModifiedOn] datetime2 NOT NULL, [RowVersion] bigint NOT NULL, [DeletedOn] datetime2 NULL);");
		}

		[SetUp]
		public async Task Seed()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"DELETE FROM {T("Pois")}; DELETE FROM {T("IndoorMapZones")}; DELETE FROM {T("SearchProjections")};");
			var poiColumns = Pg ? "(poitypeid, longitude, latitude, name)" : "([PoiTypeId], [Longitude], [Latitude], [Name])";
			await db.ExecuteAsync($"INSERT INTO {T("Pois")} {poiColumns} VALUES (@Type, 1, 2, @Name)",
				Enumerable.Range(1, ManyPois).Select(i => new { Type = TypeA, Name = "A" + i }).Concat(Enumerable.Range(1, 5).Select(i => new { Type = TypeB, Name = "B" + i })));
			var zoneColumns = Pg ? "(indoormapzoneid, indoormapfloorid, name, isdeleted)" : "([IndoorMapZoneId], [IndoorMapFloorId], [Name], [IsDeleted])";
			await db.ExecuteAsync($"INSERT INTO {T("IndoorMapZones")} {zoneColumns} VALUES (@Id, @Floor, @Id, @Deleted)", new[]
			{
				new { Id = "a1", Floor = "floor-a", Deleted = false }, new { Id = "a2", Floor = "floor-a", Deleted = false }, new { Id = "a3", Floor = "floor-a", Deleted = false },
				new { Id = "a-gone", Floor = "floor-a", Deleted = true }, new { Id = "b1", Floor = "floor-b", Deleted = false }, new { Id = "b2", Floor = "floor-b", Deleted = false }
			});
			var projectionColumns = Pg ? "(searchprojectionid, departmentid, entitytype, entityid, modifiedon, rowversion, deletedon)" : "([SearchProjectionId], [DepartmentId], [EntityType], [EntityId], [ModifiedOn], [RowVersion], [DeletedOn])";
			var projections = Enumerable.Range(1, ManyPois).Select(i => new { Id = "p" + i, Dept, Type = SearchEntityTypes.Poi, Entity = i.ToString() }).ToList();
			projections.Add(new { Id = "other-dept", Dept = Dept + 1, Type = SearchEntityTypes.Poi, Entity = "1" });
			projections.Add(new { Id = "other-type", Dept, Type = SearchEntityTypes.Unit, Entity = "1" });
			await db.ExecuteAsync($"INSERT INTO {T("SearchProjections")} {projectionColumns} VALUES (@Id, @Dept, @Type, @Entity, '2026-01-01', 1, NULL)", projections);
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_configured) DataConfig.DatabaseType = _previous;
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _)) throw new InvalidOperationException("Unexpected map bulk-removal fixture database name.");
			if (Pg) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(Pg ? "DROP DATABASE " + _database + " WITH (FORCE)" : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		private async Task<List<int>> PoiIds(int type)
		{
			await using var db = Connect(_connection);
			return (await db.QueryAsync<int>(Pg ? $"SELECT poiid FROM public.pois WHERE poitypeid = {type}" : $"SELECT [PoiId] FROM [dbo].[Pois] WHERE [PoiTypeId] = {type}")).ToList();
		}

		[Test]
		public async Task Poi_delete_spans_id_chunks_and_never_touches_another_type()
		{
			var typeA = await PoiIds(TypeA);
			var typeB = await PoiIds(TypeB);
			typeA.Count.Should().BeGreaterThan(PoisRepository.DeleteIdChunkSize);

			var deleted = await Pois().DeleteForTypeAsync(TypeA, typeA.Concat(typeB).ToList());

			deleted.Should().Be(ManyPois);
			(await PoiIds(TypeA)).Should().BeEmpty();
			(await PoiIds(TypeB)).Should().BeEquivalentTo(typeB, "ids of another type are ignored");
		}

		[Test]
		public async Task Poi_delete_inside_a_callers_transaction_rolls_back_with_it()
		{
			var typeA = await PoiIds(TypeA);
			var provider = Provider();
			using var unitOfWork = new UnitOfWork(provider.Object);
			await unitOfWork.CreateOrGetConnectionAsync();
			try
			{
				(await Pois(unitOfWork).DeleteForTypeAsync(TypeA, typeA)).Should().Be(ManyPois);
				unitOfWork.Transaction.Should().NotBeNull("the repository leaves a caller's transaction open");
			}
			finally { unitOfWork.DiscardChanges(); }
			(await PoiIds(TypeA)).Should().HaveCount(ManyPois);
		}

		[Test]
		public async Task Region_clear_soft_deletes_only_the_layers_live_regions()
		{
			(await Zones().SoftDeleteForFloorAsync("floor-a")).Should().Be(3);

			(await Scalar<int>(Pg ? "SELECT COUNT(1) FROM public.indoormapzones WHERE indoormapfloorid = 'floor-a' AND isdeleted = false" : "SELECT COUNT(1) FROM [dbo].[IndoorMapZones] WHERE [IndoorMapFloorId] = 'floor-a' AND [IsDeleted] = 0")).Should().Be(0);
			(await Scalar<int>(Pg ? "SELECT COUNT(1) FROM public.indoormapzones WHERE indoormapfloorid = 'floor-b' AND isdeleted = false" : "SELECT COUNT(1) FROM [dbo].[IndoorMapZones] WHERE [IndoorMapFloorId] = 'floor-b' AND [IsDeleted] = 0")).Should().Be(2);
			(await Zones().SoftDeleteForFloorAsync("floor-a")).Should().Be(0, "a second clear finds nothing live");
		}

		[Test]
		public async Task Projection_bulk_removal_is_scoped_to_department_and_family()
		{
			var ids = Enumerable.Range(1, ManyPois).Select(i => i.ToString()).Append("missing").ToList();

			(await Projections().SoftDeleteManyAsync(Dept, SearchEntityTypes.Poi, ids)).Should().Be(ManyPois);

			(await Scalar<int>(Pg ? "SELECT COUNT(1) FROM public.searchprojections WHERE deletedon IS NULL" : "SELECT COUNT(1) FROM [dbo].[SearchProjections] WHERE [DeletedOn] IS NULL")).Should().Be(2, "the other department's POI and the department's unit stay");
			(await Scalar<long>(Pg ? "SELECT MAX(rowversion) FROM public.searchprojections" : "SELECT MAX([RowVersion]) FROM [dbo].[SearchProjections]")).Should().Be(2);
		}
	}
}
