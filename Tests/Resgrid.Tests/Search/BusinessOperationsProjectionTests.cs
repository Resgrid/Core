using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Workforce &amp; Business Operations plan decision 41: invoices, rate cards, bids, service contracts, deployments and
	/// certification types project allowlisted identifier / title / status rows only — never an amount, a line, a note,
	/// an e-mail or a person — and a deleted row retires its projection.
	/// </summary>
	[TestFixture]
	public class BusinessOperationsProjectionTests
	{
		private Mock<ISearchProjectionsRepository> _repository;
		private List<SearchProjection> _upserted;
		private List<(string Type, string Id)> _removed;
		private SearchProjectionService _service;

		[SetUp]
		public void SetUp()
		{
			_upserted = new List<SearchProjection>(); _removed = new List<(string, string)>();
			_repository = new Mock<ISearchProjectionsRepository>();
			_repository.Setup(r => r.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>())).ReturnsAsync((SearchProjection p, CancellationToken _) => { _upserted.Add(p); return p; });
			_repository.Setup(r => r.SoftDeleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((int _, string t, string id, CancellationToken __) => { _removed.Add((t, id)); return true; });
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.IsProtectionEnforcedAsync(It.IsAny<int>())).ReturnsAsync(false);
			protection.Setup(p => p.GetPinnedCatalogVersionAsync(It.IsAny<int>())).ReturnsAsync(28);
			_service = new SearchProjectionService(_repository.Object, protection.Object);
		}

		[Test]
		public async Task Invoice_projects_number_status_and_dates_but_never_amounts_lines_or_emails()
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = 5, InvoiceNumber = 1042, Status = (int)InvoiceStatus.Sent, IssuedOn = new DateTime(2026, 9, 1), Currency = "USD", Total = 1234.56m, ContactId = "c-1", DeploymentId = "dep-1", Notes = "Net 30 — call Jane at 555-0100" };
			var p = await _service.BuildInvoiceAsync(invoice);
			p.EntityType.Should().Be(SearchEntityTypes.Invoice);
			p.Title.Should().Be("Invoice #1042");
			p.Summary.Should().Be("Sent · 2026-09-01 · USD");
			p.Keywords.Should().Contain("1042").And.Contain("dep-1");
			p.Url.Should().Be("/User/Invoicing/View?id=inv-1");
			string.Join(" ", p.Title, p.Summary, p.Keywords, p.SearchText, p.MetadataJson).Should().NotContain("1234").And.NotContain("Jane").And.NotContain("555");
			p.IsActive.Should().BeTrue();

			invoice.Status = (int)InvoiceStatus.Void;
			(await _service.BuildInvoiceAsync(invoice)).IsActive.Should().BeFalse();
			invoice.IsDeleted = true;
			await _service.ProjectInvoiceAsync(invoice);
			_removed.Should().Contain((SearchEntityTypes.Invoice, "inv-1"));
			_upserted.Should().BeEmpty();
		}

		[Test]
		public async Task Bid_contract_deployment_rate_card_and_certification_type_project_identifiers_titles_and_statuses_only()
		{
			var bid = await _service.BuildBidAsync(new Bid { BidId = "b-1", DepartmentId = 5, BidNumber = 7, Title = "Type 3 engine, 14 days", Status = (int)BidStatuses.Submitted, IncidentNumber = "CA-LNU-001234", EstimatedTotal = 88000m, Notes = "Customer asked for a discount", SentToEmail = "buyer@example.org" });
			bid.Title.Should().Be("Bid #7 Type 3 engine, 14 days");
			bid.Summary.Should().StartWith("Submitted · CA-LNU-001234");
			string.Join(" ", bid.Summary, bid.Keywords, bid.SearchText, bid.MetadataJson).Should().NotContain("88000").And.NotContain("discount").And.NotContain("buyer@");
			bid.Url.Should().Be("/User/Bids/View?id=b-1");

			var contract = await _service.BuildServiceContractAsync(new ServiceContract { ServiceContractId = "sc-1", DepartmentId = 5, ContractNumber = "2026-014", Name = "County MOU", Status = (int)ServiceContractStatuses.Active, StartOn = new DateTime(2026, 1, 1), InvoiceSubmissionEmail = "ap@county.example" });
			contract.Title.Should().Be("2026-014 County MOU");
			contract.Summary.Should().StartWith("Active · 2026-01-01 – …");
			contract.IsActive.Should().BeTrue();
			string.Join(" ", contract.Summary, contract.Keywords, contract.MetadataJson).Should().NotContain("ap@county");

			var deployment = await _service.BuildDeploymentAsync(new Deployment { DeploymentId = "dep-1", DepartmentId = 5, Name = "LNU Lightning Complex", Status = (int)DeploymentStatuses.Active, IncidentNumber = "CA-LNU-001234", ResourceOrderNumber = "O-1", RequestNumber = "E-12", StartOn = new DateTime(2026, 8, 1), Notes = "rgdp:1:28:protected-envelope", CallId = 99 });
			deployment.Title.Should().Be("LNU Lightning Complex");
			deployment.Keywords.Should().Contain("O-1").And.Contain("E-12").And.Contain("99");
			deployment.IsActive.Should().BeTrue();
			string.Join(" ", deployment.Summary, deployment.Keywords, deployment.SearchText, deployment.MetadataJson).Should().NotContain("rgdp", "the wrapper's notes are ADP catalog 27 and never leave the row");

			var card = await _service.BuildRateCardAsync(new RateCard { RateCardId = "rc-1", DepartmentId = 5, Name = "Standard 2026", IsDefault = true, Active = true });
			card.Title.Should().Be("Standard 2026"); card.Summary.Should().Be("Default · Active"); card.Url.Should().Be("/User/Invoicing/EditRateCard?id=rc-1");

			var type = await _service.BuildCertificationTypeAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 31, DepartmentId = 5, Type = "Firefighter I", Code = "FF1", IssuingAuthority = "State Fire Training", IsActive = true });
			type.Title.Should().Be("Firefighter I"); type.Keywords.Should().Contain("FF1"); type.Url.Should().Be("/User/Certifications/EditType?id=31");

			(await _service.BuildBidAsync(new Bid { BidId = "b-2", DepartmentId = 5, IsDeleted = true })).Should().BeNull();
			(await _service.BuildDeploymentAsync(new Deployment { DeploymentId = "dep-2", DepartmentId = 5, Name = "  " })).Should().BeNull("a nameless deployment has nothing safe to index");
		}

		[Test]
		public void Indexed_entity_types_include_the_business_operations_families_and_never_the_excluded_ones()
		{
			SearchEntityTypes.Indexed.Should().Contain(new[] { SearchEntityTypes.Invoice, SearchEntityTypes.RateCard, SearchEntityTypes.Bid, SearchEntityTypes.ServiceContract, SearchEntityTypes.Deployment, SearchEntityTypes.CertificationType });
			SearchEntityTypes.Indexed.Should().NotContain(t => t.Contains("TimeReport") || t.Contains("Expense") || t.Contains("RateSchedule") || t.Contains("Compliance") || t.Contains("CalOes") || t.Contains("Workforce") || t.Contains("PayData") || t.Contains("Certification") && t != SearchEntityTypes.CertificationType);
		}
	}
}
