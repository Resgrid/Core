using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsDefinitionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-1C deployments (Preview): fixture-driven external orders for IROC, CIFFC and cross-border profiles, the fill lifecycle, closeout refusal and snapshot supersession.</summary>
	[TestFixture]
	public class RecordDeploymentsServiceTests
	{
		private RmsDefinitionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsDefinitionHarness();

		private static RecordDeploymentCreateInput Iroc() => new RecordDeploymentCreateInput
		{
			ProfileKey = RmsDeploymentProfiles.UsWildland, OrderNumber = "O-1234", IncidentName = "Bear Creek", IncidentNumber = "OR-UPF-000123", IncidentCountry = "us", IncidentSubdivision = "OR",
			OrderingOffice = "ORCOC", DispatchOffice = "Central Oregon", RequestingAgency = "USFS", SendingAgency = "Test County Fire", CostCode = "P4NABC", AgreementReference = "MA-2026-01",
			ArtifactData = Encoding.UTF8.GetBytes("{\"order\":\"O-1234\"}"), ArtifactFileName = "resource-order.json", ArtifactContentType = "application/json", ArtifactSafeUrl = "https://iroc.example.gov/orders/O-1234",
			Fills = new List<RecordDeploymentFillInput>
			{
				new RecordDeploymentFillInput { RequestNumber = "O-1", RequestCategory = "Overhead", ResourceKind = "person", Position = "DIVS", AssignedUserId = Author, HomeUnit = "Station 1", NeededOn = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc) },
				new RecordDeploymentFillInput { RequestNumber = "E-3", RequestCategory = "Equipment", ResourceKind = "unit", ResourceType = "Engine T3", AssignedUnitId = 5, AssignedUserId = "p2", Position = "ENGB" }
			}
		};

		[Test]
		public async Task Creating_from_an_external_order_provisions_the_deployment_definition_and_records_the_order_and_fills()
		{
			var deployment = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, Iroc());
			deployment.IsPreview.Should().BeTrue();
			deployment.Order.SourceScheme.Should().Be("iroc");
			deployment.Order.CurrencyCode.Should().Be("USD"); deployment.Order.MeasurementSystem.Should().Be("customary");
			deployment.Order.HomeProfileKey.Should().Be("us"); deployment.Order.HostProfileKey.Should().Be("us");
			deployment.Order.ArtifactChecksum.Should().NotBeNullOrEmpty();
			deployment.Order.ArtifactSafeUrl.Should().Be("https://iroc.example.gov/orders/O-1234");
			deployment.Order.Status.Should().Be((int)RmsExternalOrderStatus.Open);
			deployment.Fills.Select(f => f.RequestNumber).Should().Equal("E-3", "O-1");
			deployment.Fills.Should().OnlyContain(f => f.Status == (int)RmsDeploymentFillStatus.Requested && f.RecordId == deployment.Order.RecordId);
			deployment.Fills.Single(f => f.RequestNumber == "E-3").ResourceTypeScheme.Should().Be("iroc");
			deployment.Profile.ProfileKey.Should().Be("us");

			var definition = (await _h.Definitions.ListAsync(Dept)).Single(d => !d.Locked);
			definition.Key.Should().Be(RecordDeploymentsService.DefaultDefinitionKey);
			definition.TemplateKey.Should().Be(RecordDeploymentsService.DeploymentTemplateKey);
			definition.PublishedVersion.Should().Be(1);
			definition.JurisdictionProfileKey.Should().Be("us");

			var record = deployment.Record;
			record.Record.DefinitionKey.Should().Be(RecordDeploymentsService.DefaultDefinitionKey);
			record.Record.ExternalId.Should().Be("O-1234");
			record.Values.Scalar("profile").Display.Should().Be("US wildland");
			record.Values.Scalar("order_number").ReferenceId.Should().Be("O-1234");
			record.Values.Scalar("incident_subdivision").Value.Should().Be("US-OR");
			record.Values.Scalar("coordinator").Display.Should().Be("Ada Admin");
			var roster = record.Values.Section("roster").Rows;
			roster.Should().HaveCount(2);
			roster.Select(r => r.Cell("position").Value).Should().Equal("DIVS", "ENGB");
			roster[1].Cell("unit").Display.Should().Be("Engine 5");

			(await _h.Deployments.ListAsync(Dept, Author, false)).Should().ContainSingle(o => o.OrderNumber == "O-1234");
			(await _h.Deployments.GetForRecordAsync(Dept, Author, deployment.Order.RecordId)).Order.RmsExternalOrderId.Should().Be(deployment.Order.RmsExternalOrderId);
			var second = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput { ProfileKey = RmsDeploymentProfiles.CaWildland, OrderNumber = "CIFFC-77", IncidentName = "Lac Rouge", IncidentCountry = "CA", IncidentSubdivision = "QC", Fills = new List<RecordDeploymentFillInput> { new RecordDeploymentFillInput { RequestNumber = "R-1", ResourceKind = "crew", ResourceType = "Type 2 IA" } } });
			second.Order.SourceScheme.Should().Be("ciffc"); second.Order.CurrencyCode.Should().Be("CAD"); second.Order.MeasurementSystem.Should().Be("metric");
			(await _h.Definitions.ListAsync(Dept)).Where(d => !d.Locked).Should().HaveCount(1, "the deployment definition is provisioned once");
		}

		[Test]
		public async Task Cross_border_orders_carry_home_and_host_profiles_and_reject_bad_inputs()
		{
			var deployment = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput
			{
				ProfileKey = RmsDeploymentProfiles.CrossBorder, OrderNumber = "IMG-9", IncidentName = "Boundary Fire", IncidentCountry = "CA", IncidentSubdivision = "BC", CurrencyCode = "cad", TimeZoneId = "America/Vancouver",
				Fills = new List<RecordDeploymentFillInput> { new RecordDeploymentFillInput { RequestNumber = "C-1", ResourceKind = "crew", ResourceType = "Type 1 crew", ResourceTypeScheme = "nwcg" } }
			});
			deployment.Order.HomeProfileKey.Should().Be("us"); deployment.Order.HostProfileKey.Should().Be("ca");
			deployment.Order.SourceScheme.Should().Be("iroc-ciffc"); deployment.Order.CurrencyCode.Should().Be("CAD");
			deployment.HomeProfile.ProfileKey.Should().Be("us"); deployment.HostProfile.ProfileKey.Should().Be("ca");
			deployment.Record.Values.Scalar("profile").Display.Should().Be("US-CA cross-border");

			Func<Task> profile = () => _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput { ProfileKey = "mars", OrderNumber = "x", IncidentName = "x" });
			await profile.Should().ThrowAsync<ArgumentException>().WithMessage("*not a deployment profile*");
			Func<Task> number = () => _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput { ProfileKey = RmsDeploymentProfiles.Generic, IncidentName = "x" });
			await number.Should().ThrowAsync<ArgumentException>();
			Func<Task> fill = () => _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput { ProfileKey = RmsDeploymentProfiles.Generic, OrderNumber = "L-1", IncidentName = "x", Fills = new List<RecordDeploymentFillInput> { new RecordDeploymentFillInput { ResourceKind = "person" } } });
			await fill.Should().ThrowAsync<ArgumentException>().WithMessage("*request number*");
			_h.Authorization.Setup(a => a.HasPermissionAsync("viewer", Dept, PermissionTypes.CreateRecord)).ReturnsAsync(false);
			Func<Task> denied = () => _h.Deployments.CreateFromExternalOrderAsync(Dept, "viewer", Iroc());
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			var unsafeUrl = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, new RecordDeploymentCreateInput { ProfileKey = RmsDeploymentProfiles.LocalMutualAid, OrderNumber = "L-2", IncidentName = "Local", ArtifactSafeUrl = "http://share.example/orders?token=abc" });
			unsafeUrl.Order.ArtifactSafeUrl.Should().BeNull("only plain https links without a query are kept");
			unsafeUrl.Order.SourceScheme.Should().Be("local");
		}

		[Test]
		public async Task Fills_walk_the_lifecycle_and_closeout_waits_for_every_resource_to_return()
		{
			var deployment = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, Iroc());
			var orderId = deployment.Order.RmsExternalOrderId;
			var overhead = deployment.Fills.Single(f => f.RequestNumber == "O-1");
			var engine = deployment.Fills.Single(f => f.RequestNumber == "E-3");

			Func<Task> skip = () => _h.Deployments.TransitionFillAsync(Dept, Admin, overhead.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.CheckedIn });
			await skip.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot move from Requested to CheckedIn*");
			Func<Task> declineNoReason = () => _h.Deployments.TransitionFillAsync(Dept, Admin, engine.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Declined });
			await declineNoReason.Should().ThrowAsync<ArgumentException>();
			await _h.Deployments.TransitionFillAsync(Dept, Admin, engine.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Declined, Reason = "Engine out of service" });

			foreach (var status in new[] { RmsDeploymentFillStatus.Accepted, RmsDeploymentFillStatus.Mobilized, RmsDeploymentFillStatus.CheckedIn, RmsDeploymentFillStatus.Assigned })
				await _h.Deployments.TransitionFillAsync(Dept, Admin, overhead.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = status, Notes = status.ToString() });
			var current = await _h.Deployments.GetAsync(Dept, Admin, orderId);
			current.Order.Status.Should().Be((int)RmsExternalOrderStatus.Mobilized);
			current.Order.MobilizedOn.Should().NotBeNull();
			var assigned = current.Fills.Single(f => f.RequestNumber == "O-1");
			assigned.Status.Should().Be((int)RmsDeploymentFillStatus.Assigned);
			assigned.FilledOn.Should().NotBeNull(); assigned.MobilizedOn.Should().NotBeNull(); assigned.CheckedInOn.Should().NotBeNull(); assigned.AssignedOn.Should().NotBeNull();
			assigned.Notes.Should().Contain("Accepted").And.Contain("Assigned");
			current.AllReturned.Should().BeFalse();

			await _h.Deployments.TransitionFillAsync(Dept, Admin, overhead.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Released });
			current = await _h.Deployments.GetAsync(Dept, Admin, orderId);
			current.Order.Status.Should().Be((int)RmsExternalOrderStatus.Released, "every active fill is released");
			Func<Task> early = () => _h.Deployments.CloseoutAsync(Dept, Admin, orderId, current.Order.RowVersion, "done");
			await early.Should().ThrowAsync<InvalidOperationException>().WithMessage("*still out: O-1*", "an external release flag never returns a resource");

			await _h.Deployments.TransitionFillAsync(Dept, Admin, overhead.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Demobilized });
			await _h.Deployments.TransitionFillAsync(Dept, Admin, overhead.RmsExternalOrderFillId, new RecordDeploymentFillTransitionInput { Status = RmsDeploymentFillStatus.Returned });
			current = await _h.Deployments.GetAsync(Dept, Admin, orderId);
			current.AllReturned.Should().BeTrue("declined fills do not block closeout");
			Func<Task> stale = () => _h.Deployments.CloseoutAsync(Dept, Admin, orderId, current.Order.RowVersion - 1, "done");
			await stale.Should().ThrowAsync<RecordConcurrencyException>();
			var closed = await _h.Deployments.CloseoutAsync(Dept, Admin, orderId, current.Order.RowVersion, "All returned 09-10");
			closed.Status.Should().Be((int)RmsExternalOrderStatus.ClosedOut);
			closed.CloseoutNotes.Should().Be("All returned 09-10");
			Func<Task> afterClose = () => _h.Deployments.AddFillAsync(Dept, Admin, orderId, new RecordDeploymentFillInput { RequestNumber = "O-9" });
			await afterClose.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed out*");
			(await _h.Deployments.ListAsync(Dept, Admin, false)).Should().BeEmpty();
			(await _h.Deployments.ListAsync(Dept, Admin, true)).Should().HaveCount(1);
			_h.Store.Audits.Count(a => a.RecordId == deployment.Order.RecordId && a.Purpose.StartsWith("Fill ")).Should().Be(8, "every transition is audited");
		}

		[Test]
		public async Task Later_source_snapshots_supersede_without_erasing_the_earlier_artifact_reference()
		{
			var deployment = await _h.Deployments.CreateFromExternalOrderAsync(Dept, Admin, Iroc());
			var orderId = deployment.Order.RmsExternalOrderId;
			var firstChecksum = deployment.Order.ArtifactChecksum;
			Func<Task> empty = () => _h.Deployments.RecordSourceSnapshotAsync(Dept, Admin, orderId, null, new byte[0], "x.json", "application/json");
			await empty.Should().ThrowAsync<ArgumentException>();

			var updated = await _h.Deployments.RecordSourceSnapshotAsync(Dept, Admin, orderId, null, Encoding.UTF8.GetBytes("{\"order\":\"O-1234\",\"rev\":2}"), "resource-order-v2.json", "application/json");
			updated.SourceVersion.Should().Be("2", "an unnamed snapshot increments the numeric source version");
			updated.ArtifactChecksum.Should().NotBe(firstChecksum);
			updated.ArtifactFileName.Should().Be("resource-order-v2.json");
			var superseded = _h.Defs.References.Single();
			superseded.RecordId.Should().Be(deployment.Order.RecordId);
			superseded.SemanticRole.Should().Be("superseded-snapshot");
			superseded.Checksum.Should().Be(firstChecksum);
			superseded.SourceVersion.Should().Be("1");
			superseded.IdentifierScheme.Should().Be("iroc");

			var named = await _h.Deployments.RecordSourceSnapshotAsync(Dept, Admin, orderId, "2026-09-06T14:00", Encoding.UTF8.GetBytes("{\"rev\":3}"), "v3.json", "application/json");
			named.SourceVersion.Should().Be("2026-09-06T14:00");
			_h.Defs.References.Should().HaveCount(2);
			var added = await _h.Deployments.AddFillAsync(Dept, Admin, orderId, new RecordDeploymentFillInput { RequestNumber = "O-2", RequestCategory = "Overhead", Position = "TFLD", AssignedUserId = "p2" });
			added.HostAgency.Should().BeNull(); added.CostCode.Should().Be("P4NABC", "fills inherit the order's cost code");
			(await _h.Deployments.GetAsync(Dept, Admin, orderId)).Fills.Should().HaveCount(3);
			_h.Authorization.Setup(a => a.CanUserViewRecordAsync("stranger", It.IsAny<string>(), Dept)).ReturnsAsync(false);
			Func<Task> hidden = () => _h.Deployments.GetAsync(Dept, "stranger", orderId);
			await hidden.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public void Profile_option_keys_and_default_schemes_cover_every_deployment_profile()
		{
			foreach (var profile in RmsDeploymentProfiles.All)
			{
				RecordDeploymentsService.ProfileOptionKey(profile).Should().NotBeNullOrEmpty();
				RecordDeploymentsService.DefaultScheme(profile).Should().NotBeNullOrEmpty();
			}
			RecordDeploymentsService.DefaultScheme(RmsDeploymentProfiles.Compact).Should().Be("emac");
			RecordDeploymentsService.ProfileOptionKey("unknown").Should().Be("generic");
			RmsDeploymentProfiles.IsKnown("US-WILDLAND").Should().BeTrue();
		}
	}
}
