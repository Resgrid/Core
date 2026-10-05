using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 (Business Operations search entitlement): an invoice, rate card, bid or contract hit is shown only
	/// while the family's flags are on for the department — the Business.Operations master flag and the family's capability
	/// flag, the same flags its pages and the system action catalog evaluate — on top of the view claim and module switch.
	/// </summary>
	[TestFixture]
	public class BusinessOpsSearchEntitlementTests
	{
		private const int Dept = 91;

		private Mock<IFeatureToggleService> _flags;
		private Mock<IInvoicingService> _invoicing;
		private Mock<IBidsService> _bids;
		private UnifiedSearchService _service;

		[SetUp]
		public void SetUp()
		{
			_flags = new Mock<IFeatureToggleService>();
			_invoicing = new Mock<IInvoicingService>();
			_invoicing.Setup(x => x.GetInvoiceByIdAsync("inv-1", Dept)).ReturnsAsync(new Invoice { InvoiceId = "inv-1", DepartmentId = Dept });
			_bids = new Mock<IBidsService>();
			_bids.Setup(x => x.GetBidByIdAsync("bid-1", Dept)).ReturnsAsync(new Bid { BidId = "bid-1", DepartmentId = Dept });

			var constructor = typeof(UnifiedSearchService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			_service = (UnifiedSearchService)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(IFeatureToggleService) ? _flags.Object :
				p.ParameterType == typeof(Lazy<IInvoicingService>) ? new Lazy<IInvoicingService>(() => _invoicing.Object) :
				p.ParameterType == typeof(Lazy<IBidsService>) ? new Lazy<IBidsService>(() => _bids.Object) :
				p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : (object)null).ToArray());
		}

		private void Flags(bool master, bool invoicing, bool contractor)
		{
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.BusinessOperations, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(master);
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(invoicing);
			_flags.Setup(x => x.IsEnabledAsync(FeatureFlagKeys.ContractorBilling, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(contractor);
		}

		/// <summary>Runs the per-hit authorization for a department admin (every claim, every module), so only the entitlement varies.</summary>
		private async Task<bool> AuthorizeAsync(string entityType, string entityId)
		{
			var accessType = typeof(UnifiedSearchService).GetNestedType("SearchAccess", BindingFlags.NonPublic);
			var access = Activator.CreateInstance(accessType, nonPublic: true);
			accessType.GetField("Principal").SetValue(access, new SearchPrincipal { DepartmentId = Dept, UserId = "admin", IsDepartmentAdmin = true, HasClaim = (r, a) => true, IsModuleEnabled = m => true });
			var method = typeof(UnifiedSearchService).GetMethod("AuthorizeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
			return await (Task<bool>)method.Invoke(_service, new[] { new GlobalSearchHit { DepartmentId = Dept, EntityType = entityType, EntityId = entityId }, access });
		}

		[Test]
		public async Task Invoice_hits_need_the_customer_invoicing_flags()
		{
			Flags(master: true, invoicing: false, contractor: true);
			(await AuthorizeAsync(SearchEntityTypes.Invoice, "inv-1")).Should().BeFalse();

			Flags(master: false, invoicing: true, contractor: true);
			(await AuthorizeAsync(SearchEntityTypes.Invoice, "inv-1")).Should().BeFalse("the master flag is the kill switch");

			Flags(master: true, invoicing: true, contractor: false);
			(await AuthorizeAsync(SearchEntityTypes.Invoice, "inv-1")).Should().BeTrue();
		}

		[Test]
		public async Task Bid_hits_need_the_contractor_billing_flags()
		{
			Flags(master: true, invoicing: true, contractor: false);
			(await AuthorizeAsync(SearchEntityTypes.Bid, "bid-1")).Should().BeFalse();

			Flags(master: true, invoicing: false, contractor: true);
			(await AuthorizeAsync(SearchEntityTypes.Bid, "bid-1")).Should().BeTrue();
		}
	}
}
