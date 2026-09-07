using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using static Resgrid.Tests.Rms.FakeOrderFeedProvider;

namespace Resgrid.Tests.Rms
{
	/// <summary>The Resgrid Mutual-Aid Order Feed v1: what a source must send, what is refused, and how its statuses line up with the local fill lifecycle.</summary>
	[TestFixture]
	public class ExternalOrderFeedContractTests
	{
		[Test]
		public void A_well_formed_feed_parses_with_camel_case_names_and_offsets()
		{
			var json = JsonConvert.SerializeObject(Feed("2026-09-06T12:00", Order("O-1001", "mobilized", Request("O-1", "mobilized"), Request("E-3", "requested", "equipment"))));
			json.Should().Contain("\"orderNumber\":\"O-1001\"").And.Contain("\"requestNumber\":\"O-1\"").And.Contain("\"contract\":\"resgrid.mutual-aid-order-feed.v1\"");

			var feed = ExternalOrderFeedContract.Parse(json, out var problems);

			problems.Should().BeEmpty();
			feed.Source.System.Should().Be("Test Ordering");
			feed.Orders.Should().HaveCount(1);
			feed.Orders[0].CapturedOn.Value.UtcDateTime.Should().Be(new System.DateTime(2026, 9, 6, 11, 0, 0, System.DateTimeKind.Utc));
			feed.Orders[0].Requests.Select(r => r.RequestNumber).Should().Equal("O-1", "E-3");
			feed.Orders[0].Artifact.Url.Should().StartWith("https://");
		}

		[Test]
		public void Empty_malformed_and_foreign_documents_are_refused_with_a_reason()
		{
			ExternalOrderFeedContract.Parse("", out var empty).Should().BeNull(); empty.Should().ContainSingle(p => p.Contains("empty"));
			ExternalOrderFeedContract.Parse("{not json", out var malformed).Should().BeNull(); malformed.Should().ContainSingle(p => p.Contains("not valid JSON"));
			ExternalOrderFeedContract.Parse("{\"contract\":\"other.v2\",\"orders\":[]}", out var foreign).Should().BeNull(); foreign.Should().ContainSingle(p => p.Contains("other.v2") && p.Contains(ExternalOrderFeedContract.Version));
		}

		[Test]
		public void Every_order_needs_a_number_a_name_a_known_status_and_numbered_requests_with_known_statuses()
		{
			var bad = Order(null, "shipped", Request(null, "lost"));
			bad.IncidentName = " ";
			var json = JsonConvert.SerializeObject(Feed("v1", bad));

			ExternalOrderFeedContract.Parse(json, out var problems).Should().BeNull();

			problems.Should().Contain(p => p.EndsWith("orderNumber is required."));
			problems.Should().Contain(p => p.EndsWith("incidentName is required."));
			problems.Should().Contain(p => p.Contains("status 'shipped'"));
			problems.Should().Contain(p => p.EndsWith("requestNumber is required."));
			problems.Should().Contain(p => p.Contains("status 'lost'"));
		}

		[Test]
		public void Artifacts_must_be_https_and_pages_are_bounded()
		{
			var insecure = Order("O-1", "open", Request("O-1")); insecure.Artifact.Url = "http://orders.example.gov/O-1";
			ExternalOrderFeedContract.Parse(JsonConvert.SerializeObject(Feed("v1", insecure)), out var http).Should().BeNull();
			http.Should().ContainSingle(p => p.Contains("artifact.url must be https"));

			var tooMany = Feed("v1", Enumerable.Range(0, ExternalOrderFeedContract.MaxOrdersPerPage + 1).Select(i => Order("O-" + i, "open")).ToArray());
			ExternalOrderFeedContract.Parse(JsonConvert.SerializeObject(tooMany), out var paged).Should().BeNull();
			paged.Should().ContainSingle(p => p.Contains("Page with the cursor"));

			var crowded = Order("O-1", "open", Enumerable.Range(0, ExternalOrderFeedContract.MaxRequestsPerOrder + 1).Select(i => Request("R-" + i)).ToArray());
			ExternalOrderFeedContract.Parse(JsonConvert.SerializeObject(Feed("v1", crowded)), out var requests).Should().BeNull();
			requests.Should().ContainSingle(p => p.Contains($"the contract allows {ExternalOrderFeedContract.MaxRequestsPerOrder}"));
		}

		[Test]
		public void Request_statuses_rank_in_lifecycle_order_and_local_fill_statuses_map_onto_them()
		{
			var order = new List<string> { "requested", "filled", "mobilized", "checked-in", "assigned", "released", "demobilized" };
			order.Select(ExternalOrderFeedContract.RequestStatuses.Rank).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
			ExternalOrderFeedContract.RequestStatuses.Rank("cancelled").Should().Be(0, "a cancellation has no place on the ladder");
			ExternalOrderFeedContract.RequestStatuses.Rank("nonsense").Should().Be(0);

			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Requested).Should().Be("requested");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Accepted).Should().Be("filled");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Mobilized).Should().Be("mobilized");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.CheckedIn).Should().Be("checked-in");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Assigned).Should().Be("assigned");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Released).Should().Be("released");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Demobilized).Should().Be("demobilized");
			ExternalOrderFeedContract.LocalStatusOf(RmsDeploymentFillStatus.Declined).Should().Be("cancelled");
		}

		[Test]
		public void The_real_providers_fix_scheme_profile_and_the_identifiers_their_source_guarantees()
		{
			var iroc = new Resgrid.Services.Records.Connectors.IrocOrderFeedProvider();
			iroc.Key.Should().Be(RmsExternalOrderConnectorProviders.Iroc); iroc.DefaultScheme.Should().Be("iroc"); iroc.DefaultProfileKey.Should().Be(RmsDeploymentProfiles.UsWildland);
			iroc.ValidateOrder(Order("O-1", "open", Request("O-1"))).Should().BeEmpty();
			var blank = Order("O-1", "open", Request("O-1", "requested", null)); blank.IncidentNumber = null; blank.OrderingOffice = null; blank.DispatchOffice = null;
			iroc.ValidateOrder(blank).Should().HaveCount(3);

			var ciffc = new Resgrid.Services.Records.Connectors.CiffcOrderFeedProvider();
			ciffc.DefaultScheme.Should().Be("ciffc"); ciffc.DefaultProfileKey.Should().Be(RmsDeploymentProfiles.CaWildland);
			var noAgreement = Order("X-1", "open"); noAgreement.AgreementReference = null; noAgreement.RequestingAgency = null; noAgreement.ReceivingAgency = null;
			ciffc.ValidateOrder(noAgreement).Should().HaveCount(2);

			new Resgrid.Services.Records.Connectors.GenericOrderFeedProvider().ValidateOrder(noAgreement).Should().BeEmpty("a generic source promises nothing beyond the contract");
			new Resgrid.Services.Records.Connectors.AgencyOrderFeedProvider().DefaultProfileKey.Should().Be(RmsDeploymentProfiles.Generic);
		}
	}
}
