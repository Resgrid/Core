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
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof, on both engines, that the list number filters find department-formatted numbers however they are
	/// typed (IdentifierSearchKeys.ContainsParts as LIKE / ILIKE): invoices and bids (DisplayNumber, else the plain number),
	/// records requests, inspections, permits and investigation evidence. Minimal stand-ins hold just the columns each list query
	/// reads; their PostgreSQL types match the shipped ones (citext for M0268 and request numbers, varchar for prevention).
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class NumberFilterDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "number_filter_";
		private const int Dept = 31;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			return connections.Object;
		}

		private SqlConfiguration Configuration() => IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private T Repository<T>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T> create) => create(Connections(), Configuration(), Mock.Of<IUnitOfWork>(), Mock.Of<IQueryFactory>());

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

			await using var database = Connect(_connection);
			await database.OpenAsync();
			await database.ExecuteAsync(IsPostgres
				? @"CREATE EXTENSION IF NOT EXISTS citext;
					CREATE TABLE invoices (invoiceid varchar(36) PRIMARY KEY, departmentid int NOT NULL, invoicenumber int NOT NULL, displaynumber citext NULL, status int NOT NULL, isdeleted boolean NOT NULL);
					CREATE TABLE bids (bidid varchar(36) PRIMARY KEY, departmentid int NOT NULL, bidnumber int NOT NULL, displaynumber citext NULL, status int NOT NULL, isdeleted boolean NOT NULL);
					CREATE TABLE rmsdisclosurerequests (rmsdisclosurerequestid varchar(36) PRIMARY KEY, departmentid int NOT NULL, requestnumber citext NULL, state int NOT NULL, receivedon timestamp NOT NULL, statutorydueon timestamp NULL, deletedon timestamp NULL);
					CREATE TABLE rmsinspections (rmsinspectionid varchar(36) PRIMARY KEY, departmentid int NOT NULL, inspectionnumber varchar(32) NULL, state int NOT NULL, scheduledon timestamp NULL, createdon timestamp NOT NULL, deletedon timestamp NULL);
					CREATE TABLE rmspermits (rmspermitid varchar(36) PRIMARY KEY, departmentid int NOT NULL, permitnumber varchar(32) NULL, state int NOT NULL, appliedon timestamp NOT NULL, deletedon timestamp NULL);
					CREATE TABLE rmsinvestigationevidence (rmsinvestigationevidenceid varchar(36) PRIMARY KEY, departmentid int NOT NULL, rmsinvestigationcaseid varchar(36) NOT NULL, evidencenumber varchar(32) NULL, deletedon timestamp NULL);"
				: @"CREATE TABLE Invoices (InvoiceId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, InvoiceNumber int NOT NULL, DisplayNumber nvarchar(50) NULL, Status int NOT NULL, IsDeleted bit NOT NULL);
					CREATE TABLE Bids (BidId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, BidNumber int NOT NULL, DisplayNumber nvarchar(50) NULL, Status int NOT NULL, IsDeleted bit NOT NULL);
					CREATE TABLE RmsDisclosureRequests (RmsDisclosureRequestId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, RequestNumber nvarchar(50) NULL, State int NOT NULL, ReceivedOn datetime2 NOT NULL, StatutoryDueOn datetime2 NULL, DeletedOn datetime2 NULL);
					CREATE TABLE RmsInspections (RmsInspectionId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, InspectionNumber nvarchar(32) NULL, State int NOT NULL, ScheduledOn datetime2 NULL, CreatedOn datetime2 NOT NULL, DeletedOn datetime2 NULL);
					CREATE TABLE RmsPermits (RmsPermitId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, PermitNumber nvarchar(32) NULL, State int NOT NULL, AppliedOn datetime2 NOT NULL, DeletedOn datetime2 NULL);
					CREATE TABLE RmsInvestigationEvidence (RmsInvestigationEvidenceId varchar(36) PRIMARY KEY, DepartmentId int NOT NULL, RmsInvestigationCaseId varchar(36) NOT NULL, EvidenceNumber nvarchar(32) NULL, DeletedOn datetime2 NULL);");
			if (database is NpgsqlConnection npgsql)
				await npgsql.ReloadTypesAsync();

			var now = new DateTime(2026, 10, 1);
			// INV2026.0042 and BID_26_0007 are one word to a word analyzer; a pre-M0268 row (no DisplayNumber) reads as its plain number.
			await database.ExecuteAsync(("INSERT INTO Invoices (InvoiceId, DepartmentId, InvoiceNumber, DisplayNumber, Status, IsDeleted) VALUES (@Id, @Dept, @Seq, @Number, 0, @Deleted)"), new[]
			{
				new { Id = "inv-1", Dept, Seq = 42, Number = "INV2026.0042", Deleted = false },
				new { Id = "inv-2", Dept, Seq = 43, Number = "INV2026.0043", Deleted = false },
				new { Id = "inv-3", Dept, Seq = 1042, Number = (string)null, Deleted = false },
				new { Id = "inv-4", Dept, Seq = 44, Number = "INV2026.0044", Deleted = true },
				new { Id = "inv-5", Dept = Dept + 1, Seq = 42, Number = "INV2026.0042", Deleted = false }
			});
			await database.ExecuteAsync(("INSERT INTO Bids (BidId, DepartmentId, BidNumber, DisplayNumber, Status, IsDeleted) VALUES (@Id, @Dept, @Seq, @Number, 0, @Deleted)"), new[]
			{
				new { Id = "bid-1", Dept, Seq = 7, Number = "BID_26_0007", Deleted = false },
				new { Id = "bid-2", Dept, Seq = 8, Number = "BID_26_0008", Deleted = false }
			});
			await database.ExecuteAsync(("INSERT INTO RmsDisclosureRequests (RmsDisclosureRequestId, DepartmentId, RequestNumber, State, ReceivedOn) VALUES (@Id, @Dept, @Number, 0, @Now)"), new[]
			{
				new { Id = "prr-1", Dept, Number = "PRR27-0003", Now = now },
				new { Id = "prr-2", Dept, Number = "PRR27-0013", Now = now }
			});
			await database.ExecuteAsync(("INSERT INTO RmsInspections (RmsInspectionId, DepartmentId, InspectionNumber, State, CreatedOn) VALUES (@Id, @Dept, @Number, 0, @Now)"), new[]
			{
				new { Id = "insp-1", Dept, Number = "INSP-2026-0001", Now = now },
				new { Id = "insp-2", Dept, Number = "FM/2026/0002", Now = now }
			});
			await database.ExecuteAsync(("INSERT INTO RmsPermits (RmsPermitId, DepartmentId, PermitNumber, State, AppliedOn) VALUES (@Id, @Dept, @Number, 0, @Now)"), new[]
			{
				new { Id = "prm-1", Dept, Number = "prm-2026-0009", Now = now },
				new { Id = "prm-2", Dept, Number = "PRM-2026-0010", Now = now }
			});
			await database.ExecuteAsync(("INSERT INTO RmsInvestigationEvidence (RmsInvestigationEvidenceId, DepartmentId, RmsInvestigationCaseId, EvidenceNumber, DeletedOn) VALUES (@Id, @Dept, @Case, @Number, @Deleted)"), new[]
			{
				new { Id = "ev-1", Dept, Case = "case-1", Number = "EV26-0042", Deleted = (DateTime?)null },
				new { Id = "ev-2", Dept, Case = "case-2", Number = "EV26-0043", Deleted = (DateTime?)null },
				new { Id = "ev-3", Dept, Case = "case-3", Number = "EV26-0042", Deleted = (DateTime?)now }
			});
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[Test]
		public async Task Invoices_filter_by_the_issued_number_however_typed()
		{
			var invoices = Repository((c, s, u, q) => new InvoiceRepository(c, s, u, q));
			async Task<List<string>> Find(string number) => (await invoices.GetForDepartmentAsync(Dept, new InvoiceListFilter { Number = number })).Select(i => i.InvoiceId).ToList();

			(await Find("INV2026.0042")).Should().Equal("inv-1");
			(await Find("inv-2026-0042")).Should().Equal(new[] { "inv-1" }, "separators are formatting");
			(await Find("2026 0043")).Should().Equal("inv-2");
			(await Find("1042")).Should().Equal(new[] { "inv-3" }, "a row with no DisplayNumber reads as its plain number");
			(await Find("inv2026")).Should().Equal(new[] { "inv-2", "inv-1" }, "newest number first; the deleted one and the other department's are left out");
			(await Find("2027")).Should().BeEmpty();
			(await invoices.CountForDepartmentAsync(Dept, new InvoiceListFilter { Number = "INV" })).Should().Be(2);
		}

		[Test]
		public async Task Bids_filter_by_the_issued_number_and_underscore_is_not_a_wildcard()
		{
			var bids = Repository((c, s, u, q) => new BidRepository(c, s, u, q));

			(await bids.GetForDepartmentAsync(Dept, null, "bid-26-0007", 0, 50)).Select(b => b.BidId).Should().Equal("bid-1");
			(await bids.GetForDepartmentAsync(Dept, null, "26_0008", 0, 50)).Select(b => b.BidId).Should().Equal("bid-2");
			(await bids.GetForDepartmentAsync(Dept, null, null, 0, 50)).Should().HaveCount(2);
			(await bids.CountForDepartmentAsync(Dept, null, "BID")).Should().Be(2);
			(await bids.CountForDepartmentAsync(Dept, 1, "BID")).Should().Be(0, "the status filter still applies");
		}

		[Test]
		public async Task Records_requests_inspections_and_permits_filter_by_the_issued_number()
		{
			var requests = Repository((c, s, u, q) => new RmsDisclosureRequestsRepository(c, s, u, q));
			var inspections = Repository((c, s, u, q) => new RmsInspectionsRepository(c, s, u, q));
			var permits = Repository((c, s, u, q) => new RmsPermitsRepository(c, s, u, q));

			(await requests.GetForDepartmentAsync(Dept, null, "prr-27-0003", 0, 50)).Select(r => r.RmsDisclosureRequestId).Should().Equal("prr-1");
			(await requests.GetForDepartmentAsync(Dept, null, "27-3", 0, 50)).Select(r => r.RmsDisclosureRequestId).Should().BeEquivalentTo(new[] { "prr-1", "prr-2" }, "an unpadded sequence matches every number holding it");

			(await inspections.QueryAsync(Dept, new RmsInspectionQuery { Number = "FM 2026 0002" })).Select(i => i.RmsInspectionId).Should().Equal("insp-2");
			(await inspections.CountAsync(Dept, new RmsInspectionQuery { Number = "2026" })).Should().Be(2);

			(await permits.QueryAsync(Dept, new RmsPermitQuery { Number = "PRM-2026-0009" })).Select(p => p.RmsPermitId).Should().Equal(new[] { "prm-1" }, "case is ignored on both engines");
			(await permits.CountAsync(Dept, new RmsPermitQuery { Number = "0010" })).Should().Be(1);
		}

		[Test]
		public async Task Investigation_cases_are_found_by_their_live_evidence_numbers()
		{
			var evidence = Repository((c, s, u, q) => new RmsInvestigationEvidenceRepository(c, s, u, q));

			(await evidence.GetCaseIdsWithEvidenceNumberAsync(Dept, new[] { "case-1", "case-2", "case-3" }, "ev26-0042")).Should().Equal(new[] { "case-1" }, "deleted evidence does not find its case");
			(await evidence.GetCaseIdsWithEvidenceNumberAsync(Dept, new[] { "case-2" }, "0042")).Should().BeEmpty("only the listed cases are searched");
			(await evidence.GetCaseIdsWithEvidenceNumberAsync(Dept, new[] { "case-1" }, " - ")).Should().BeEmpty("a search with no letters or digits finds nothing");
		}
	}
}
