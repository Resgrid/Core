using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// A full rebuild of the invoice, bid and deployment families walks every page the repositories serve (their reads clamp
	/// a page at 500 rows) before the family's stale rows are retired; one capped read used to retire everything past it.
	/// </summary>
	[TestFixture]
	public class SearchIndexMaintenancePagingTests
	{
		private const int Dept = 5;
		private Mock<ISearchProjectionsRepository> _projections;
		private Mock<ISearchProjectionService> _projectionService;
		private Mock<IInvoicingService> _invoicing;
		private Mock<IBidsService> _bids;
		private Mock<IDeploymentService> _deployments;
		private List<(string Type, string Id)> _upserted;
		private List<string> _retired;
		private SearchIndexMaintenanceService _service;

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
			_upserted = new List<(string, string)>();
			_retired = new List<string>();
			_projections = new Mock<ISearchProjectionsRepository>();
			_projections.Setup(p => p.SoftDeleteStaleAsync(Dept, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback((int _, string type, DateTime __, CancellationToken ___) => _retired.Add(type)).ReturnsAsync(0);
			_projections.Setup(p => p.GetLivePageAsync(Dept, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<SearchProjection>());
			_projectionService = new Mock<ISearchProjectionService>();
			_projectionService.Setup(p => p.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SearchProjection p, CancellationToken _) => { _upserted.Add((p.EntityType, p.EntityId)); return p; });
			_projectionService.Setup(p => p.BuildInvoiceAsync(It.IsAny<Invoice>())).ReturnsAsync((Invoice i) => Row(SearchEntityTypes.Invoice, i.InvoiceId));
			_projectionService.Setup(p => p.BuildBidAsync(It.IsAny<Bid>())).ReturnsAsync((Bid b) => Row(SearchEntityTypes.Bid, b.BidId));
			_projectionService.Setup(p => p.BuildDeploymentAsync(It.IsAny<Deployment>())).ReturnsAsync((Deployment d) => Row(SearchEntityTypes.Deployment, d.DeploymentId));
			var states = new Mock<ISearchIndexStatesRepository>();
			states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((SearchIndexState s, CancellationToken _, bool __) => s);
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(28);

			_invoicing = new Mock<IInvoicingService>();
			_bids = new Mock<IBidsService>();
			_deployments = new Mock<IDeploymentService>();

			_service = new SearchIndexMaintenanceService(states.Object, _projections.Object, Mock.Of<IGlobalSearchIndexer>(), protection.Object,
				Mock.Of<IFeatureToggleService>(), _projectionService.Object, Mock.Of<ICallsService>(), Mock.Of<IUnitsService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IContactsService>(), Mock.Of<IMessageService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(),
				new Lazy<IInvoicingService>(() => _invoicing.Object), new Lazy<IBidsService>(() => _bids.Object), null,
				new Lazy<IDeploymentService>(() => _deployments.Object), null);
		}

		[TearDown]
		public void TearDown() => SearchConfig.Enabled = false;

		private static SearchProjection Row(string type, string id) => new SearchProjection { DepartmentId = Dept, EntityType = type, EntityId = id, Title = id };

		/// <summary>Serves <paramref name="total"/> rows in repository-sized pages (500), as the clamped reads do.</summary>
		private static List<T> Page<T>(int total, int skip, int take, Func<int, T> make) =>
			Enumerable.Range(skip, Math.Max(0, Math.Min(take, total - skip))).Select(make).ToList();

		[Test]
		public async Task Rebuild_walks_every_page_of_the_business_operations_families_before_retiring_stale_rows()
		{
			var invoiceSkips = new List<int>();
			_invoicing.Setup(i => i.GetInvoicesForDepartmentAsync(Dept, It.IsAny<InvoiceListFilter>()))
				.ReturnsAsync((int _, InvoiceListFilter f) => { invoiceSkips.Add(f.Skip); return Page(1203, f.Skip, Math.Min(f.Take, 500), n => new Invoice { InvoiceId = "inv-" + n, DepartmentId = Dept }); });
			_invoicing.Setup(i => i.GetRateCardsForDepartmentAsync(Dept)).ReturnsAsync(new List<RateCard>());
			_bids.Setup(b => b.GetBidsForDepartmentAsync(Dept, null, It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int _, BidStatuses? __, int skip, int take) => Page(500, skip, Math.Min(take, 500), n => new Bid { BidId = "bid-" + n, DepartmentId = Dept }));
			_deployments.Setup(d => d.GetDeploymentsForDepartmentAsync(Dept, false, It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int _, bool __, int skip, int take) => Page(7, skip, Math.Min(take, 500), n => new Deployment { DeploymentId = "dep-" + n, DepartmentId = Dept }));

			var result = await _service.RebuildDepartmentAsync(Dept);

			invoiceSkips.Should().Equal(new[] { 0, 500, 1000 }, "every page is read until a short one ends the walk");
			_upserted.Count(u => u.Type == SearchEntityTypes.Invoice).Should().Be(1203);
			_upserted.Count(u => u.Type == SearchEntityTypes.Bid).Should().Be(500, "a full last page reads once more and stops on the empty one");
			_upserted.Count(u => u.Type == SearchEntityTypes.Deployment).Should().Be(7);
			_retired.Should().Contain(new[] { SearchEntityTypes.Invoice, SearchEntityTypes.Bid, SearchEntityTypes.Deployment });
			result.ProjectionsRebuilt.Should().Be(1203 + 500 + 7);
		}

		/// <summary>The department's call years as the dialects return them: SQL Server YEAR() as an integer, Postgres extract() as numeric.</summary>
		private Mock<ICallsService> CallsInYears(params string[] years)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(new List<Call>
			{
				new Call { CallId = 1, DepartmentId = Dept },
				new Call { CallId = 2, DepartmentId = Dept, IsDeleted = true }
			});
			calls.Setup(c => c.GetCallYearsByDeptartmentAsync(Dept)).ReturnsAsync(years.ToList());
			calls.Setup(c => c.GetClosedCallsByDepartmentYearAsync(Dept, It.IsAny<string>()))
				.ReturnsAsync((int _, string year) => new List<Call>
				{
					new Call { CallId = int.Parse(year) * 10, DepartmentId = Dept, State = 1 },
					new Call { CallId = 1, DepartmentId = Dept, State = 1 }
				});
			_projectionService.Setup(p => p.BuildCallAsync(It.IsAny<Call>())).ReturnsAsync((Call c) => Row(SearchEntityTypes.Call, c.CallId.ToString()));
			return calls;
		}

		private SearchIndexMaintenanceService WithCalls(Mock<ICallsService> calls)
		{
			var states = new Mock<ISearchIndexStatesRepository>();
			states.Setup(s => s.SaveOrUpdateAsync(It.IsAny<SearchIndexState>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((SearchIndexState s, CancellationToken _, bool __) => s);
			return new SearchIndexMaintenanceService(states.Object, _projections.Object, Mock.Of<IGlobalSearchIndexer>(), Mock.Of<IDepartmentDataProtectionService>(),
				Mock.Of<IFeatureToggleService>(), _projectionService.Object, calls.Object, Mock.Of<IUnitsService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IContactsService>(), Mock.Of<IMessageService>(),
				Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), null, null, null, null, null);
		}

		[Test]
		public async Task Rebuild_projects_closed_calls_from_every_year_the_department_has_calls_in()
		{
			var calls = CallsInYears("2026", "2014.0", " 2009 ", "not-a-year");

			await WithCalls(calls).RebuildDepartmentAsync(Dept);

			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, "2026"), Times.Once);
			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, "2014"), Times.Once);
			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, "2009"), Times.Once);
			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, It.IsAny<string>()), Times.Exactly(3));
			_upserted.Where(u => u.Type == SearchEntityTypes.Call).Select(u => u.Id).Should()
				.BeEquivalentTo(new[] { "1", "20260", "20140", "20090" }, "deleted calls are skipped and a call seen twice is projected once");
			_retired.Should().Contain(SearchEntityTypes.Call);
		}

		[Test]
		public async Task CallRebuildYears_still_caps_the_walk_when_an_operator_sets_it()
		{
			var thisYear = DateTime.UtcNow.Year;
			var calls = CallsInYears(thisYear.ToString(), (thisYear - 1).ToString(), (thisYear - 5).ToString());
			var previous = SearchConfig.CallRebuildYears;
			SearchConfig.CallRebuildYears = 2;
			try
			{
				await WithCalls(calls).RebuildDepartmentAsync(Dept);
			}
			finally
			{
				SearchConfig.CallRebuildYears = previous;
			}

			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, (thisYear - 5).ToString()), Times.Never);
			calls.Verify(c => c.GetClosedCallsByDepartmentYearAsync(Dept, It.IsAny<string>()), Times.Exactly(2));
		}
	}
}
