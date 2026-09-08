using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-5 hydrants: NFPA 291 arithmetic, service state, CSV import, map layer and nearest lookup.</summary>
	[TestFixture]
	public class RecordsHydrantsServiceTests
	{
		private RmsPreventionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsPreventionHarness();

		[TestCase(0.9, 2.5, 25, 840, RmsHydrantFlowClass.B)]
		[TestCase(0.9, 2.5, 64, 1340, RmsHydrantFlowClass.A)]
		[TestCase(0.8, 4.0, 40, 2410, RmsHydrantFlowClass.AA)]
		[TestCase(0.7, 2.5, 9, 390, RmsHydrantFlowClass.C)]
		public void Flow_follows_nfpa_291(double c, double d, int pitot, int expectedGpm, RmsHydrantFlowClass expectedClass)
		{
			var gpm = HydrantFlowCalculator.FlowGpm((decimal)c, (decimal)d, pitot);
			gpm.Should().Be(expectedGpm);
			HydrantFlowCalculator.Classify(gpm).Should().Be(expectedClass);
			HydrantFlowCalculator.Classify(null).Should().Be(RmsHydrantFlowClass.Unknown);
			HydrantFlowCalculator.FlowGpm(0, 2.5m, 25).Should().Be(0);
		}

		[Test]
		public async Task A_flow_test_updates_the_hydrant_and_maintenance_can_return_it_to_service()
		{
			var hydrant = await _h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-101", Latitude = 45.5m, Longitude = -122.6m, Type = (int)RmsHydrantType.DryBarrel, MainSizeInches = 8 });
			hydrant.InService.Should().BeTrue();
			var test = await _h.HydrantsService.RecordFlowTestAsync(Dept, Admin, new RmsHydrantFlowTest { RmsHydrantId = hydrant.RmsHydrantId, StaticPressurePsi = 70, ResidualPressurePsi = 55, PitotPressurePsi = 64, OutletDiameterInches = 2.5m, Coefficient = 0.9m });
			test.FlowGpm.Should().Be(1340);
			hydrant.FlowGpm.Should().Be(1340);
			hydrant.FlowClass.Should().Be((int)RmsHydrantFlowClass.A);
			hydrant.LastTestedOn.Should().NotBeNull();

			Func<Task> noReason = () => _h.HydrantsService.SetServiceStateAsync(Dept, Admin, hydrant.RmsHydrantId, false, "");
			await noReason.Should().ThrowAsync<ArgumentException>();
			await _h.HydrantsService.SetServiceStateAsync(Dept, Admin, hydrant.RmsHydrantId, false, "Sheared by vehicle");
			(await _h.HydrantsService.GetMapLayerAsync(Dept, Member, null, null, null, null)).Should().ContainSingle(p => p.HydrantNumber == "H-101" && !p.InService && p.FlowClass == (int)RmsHydrantFlowClass.A);
			await _h.HydrantsService.RecordMaintenanceAsync(Dept, Admin, new RmsHydrantMaintenance { RmsHydrantId = hydrant.RmsHydrantId, Kind = (int)RmsHydrantMaintenanceKind.Repair, ReturnedToService = true, Notes = "Replaced barrel" });
			hydrant.InService.Should().BeTrue();
			hydrant.OutOfServiceReason.Should().BeNull();
			(await _h.HydrantsService.GetAsync(Dept, Member, hydrant.RmsHydrantId)).Maintenance.Should().HaveCount(1);
			(await _h.HydrantsService.CountTestDueAsync(Dept, DateTime.UtcNow.AddMonths(13))).Should().Be(1);
		}

		[Test]
		public async Task Csv_import_creates_updates_and_rejects_rows_and_the_nearest_lookup_orders_by_distance()
		{
			await _h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-1", Latitude = 45.5m, Longitude = -122.6m });
			var result = await _h.HydrantsService.ImportCsvAsync(Dept, Admin,
				"number,latitude,longitude,type,address,main_size,flow_gpm,owner\nH-1,45.5001,-122.6001,wet barrel,1 River Rd,6,1100,City\nH-2,45.6,-122.7,dry,,8,,\nH-3,bad,-122.7,dry,,,,\n,45.6,-122.7,dry,,,,\nH-4,45.51,-122.61,cistern,Tank Hill,,,Private Water Co\n");
			result.RowsRead.Should().Be(5);
			result.Created.Should().Be(2);
			result.Updated.Should().Be(1);
			result.Rejected.Select(r => r.Error).Should().BeEquivalentTo(new[] { "Invalid coordinates", "Missing hydrant number" });
			var h1 = _h.Hydrants.Rows.Single(h => h.HydrantNumber == "H-1");
			h1.Type.Should().Be((int)RmsHydrantType.WetBarrel);
			h1.FlowGpm.Should().Be(1100);
			h1.FlowClass.Should().Be((int)RmsHydrantFlowClass.A);
			h1.Source.Should().Be("manual", "an update keeps the row's original source");
			_h.Hydrants.Rows.Single(h => h.HydrantNumber == "H-2").Source.Should().Be("csv-import");
			_h.Hydrants.Rows.Single(h => h.HydrantNumber == "H-4").Type.Should().Be((int)RmsHydrantType.Cistern);

			var nearest = await _h.HydrantsService.GetNearestAsync(Dept, 45.5m, -122.6m, 2, 5000);
			nearest.Select(h => h.HydrantNumber).Should().Equal("H-1", "H-4");
			(await _h.HydrantsService.GetMapLayerAsync(Dept, Member, 45.55m, 45.65m, -122.75m, -122.65m)).Should().ContainSingle(p => p.HydrantNumber == "H-2");
		}
	}

	/// <summary>RMS-5 permits: application, plan review cycles, issue/expiry, fee reference and the expiry sweep (trigger 163).</summary>
	[TestFixture]
	public class RecordsPermitsServiceTests
	{
		private RmsPreventionHarness _h;
		private RmsPermitType _hotWork;
		private RmsPermitType _tent;

		[SetUp]
		public async Task SetUp()
		{
			_h = new RmsPreventionHarness();
			_hotWork = await _h.PermitsService.SaveTypeAsync(Dept, Admin, new RmsPermitType { Name = "Hot work", Code = "HW", DefaultValidityDays = 30, RequiresPlanReview = false, FeeAmount = 75m, ConditionsTemplate = "Fire watch for 60 minutes after work ends.", IsActive = true });
			_tent = await _h.PermitsService.SaveTypeAsync(Dept, Admin, new RmsPermitType { Name = "Tent over 400 sq ft", Code = "TENT", DefaultValidityDays = 10, RequiresPlanReview = true, IsActive = true });
		}

		[Test]
		public async Task A_permit_moves_through_review_issue_and_expiry_with_dates_from_its_type()
		{
			var occupancy = _h.SeedOccupancy();
			var permit = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = _tent.RmsPermitTypeId, RmsOccupancyId = occupancy.RmsOccupancyId, ApplicantName = "Sam Applicant", ApplicantPhone = "555-0100", Description = "Festival tent" });
			permit.PermitNumber.Should().Be($"PRM-{DateTime.UtcNow.Year}-0001");
			permit.State.Should().Be((int)RmsPermitState.UnderReview, "the type requires plan review");
			permit.ApplicantName.Should().Be("Sam Applicant");
			_h.Protection.Writes.Should().Contain("permit");

			var cycle1 = await _h.PermitsService.RecordPlanReviewAsync(Dept, Admin, permit.RmsPermitId, RmsPlanReviewOutcome.CorrectionsRequired, "Show exit spacing.");
			cycle1.CycleNumber.Should().Be(1);
			permit.State.Should().Be((int)RmsPermitState.UnderReview);
			var cycle2 = await _h.PermitsService.RecordPlanReviewAsync(Dept, Admin, permit.RmsPermitId, RmsPlanReviewOutcome.Approved, "Exits acceptable.");
			cycle2.CycleNumber.Should().Be(2);
			permit.State.Should().Be((int)RmsPermitState.Approved);

			Func<Task> skip = () => _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Expired, null, null, null);
			await skip.Should().ThrowAsync<InvalidOperationException>();
			await _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Issued, null, null, null);
			permit.IssuedOn.Should().NotBeNull();
			permit.ExpiresOn.Should().BeCloseTo(permit.EffectiveOn.Value.AddDays(10), TimeSpan.FromSeconds(1));
			await _h.PermitsService.RecordFeePaidAsync(Dept, Admin, permit.RmsPermitId, 120m, "INV-2026-0009");
			permit.InvoiceReference.Should().Be("INV-2026-0009");

			var aggregate = await _h.PermitsService.GetAsync(Dept, Member, permit.RmsPermitId);
			aggregate.PlanReviews.Should().HaveCount(2);
			aggregate.Type.Code.Should().Be("TENT");
			aggregate.Occupancy.RmsOccupancyId.Should().Be(occupancy.RmsOccupancyId);
		}

		[Test]
		public async Task Denial_and_revocation_need_a_reason_and_hot_work_skips_review()
		{
			var permit = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = _hotWork.RmsPermitTypeId, ApplicantName = "Welder" });
			permit.State.Should().Be((int)RmsPermitState.Applied);
			permit.Conditions.Should().Contain("Fire watch");
			permit.FeeAmount.Should().Be(75m);
			Func<Task> deny = () => _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Denied, "", null, null);
			await deny.Should().ThrowAsync<ArgumentException>();
			await _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Approved, null, null, null);
			await _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Issued, null, DateTime.UtcNow, DateTime.UtcNow.AddDays(5));
			await _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Revoked, "Unsafe practice observed", null, null);
			permit.DecisionReason.Should().Be("Unsafe practice observed");
			Func<Task> member = () => _h.PermitsService.ApplyAsync(Dept, Member, new RmsPermit { RmsPermitTypeId = _hotWork.RmsPermitTypeId });
			await member.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task The_expiry_sweep_notifies_once_inside_the_window_and_expires_past_due_permits()
		{
			var now = DateTime.UtcNow;
			var soon = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = _hotWork.RmsPermitTypeId, ApplicantName = "A" });
			await _h.PermitsService.TransitionAsync(Dept, Admin, soon.RmsPermitId, RmsPermitState.Approved, null, null, null);
			await _h.PermitsService.TransitionAsync(Dept, Admin, soon.RmsPermitId, RmsPermitState.Issued, null, now.AddDays(-20), now.AddDays(10));
			var past = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = _hotWork.RmsPermitTypeId, ApplicantName = "B" });
			await _h.PermitsService.TransitionAsync(Dept, Admin, past.RmsPermitId, RmsPermitState.Approved, null, null, null);
			await _h.PermitsService.TransitionAsync(Dept, Admin, past.RmsPermitId, RmsPermitState.Issued, null, now.AddDays(-40), now.AddDays(-1));
			var far = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = _hotWork.RmsPermitTypeId, ApplicantName = "C" });
			await _h.PermitsService.TransitionAsync(Dept, Admin, far.RmsPermitId, RmsPermitState.Approved, null, null, null);
			await _h.PermitsService.TransitionAsync(Dept, Admin, far.RmsPermitId, RmsPermitState.Issued, null, now, now.AddDays(200));

			var (expired, notified) = await _h.PermitsService.SweepExpiryAsync(Dept, now, 30);
			expired.Should().Be(1);
			notified.Should().Be(1);
			past.State.Should().Be((int)RmsPermitState.Expired);
			soon.ExpiringEmittedOn.Should().NotBeNull();
			far.ExpiringEmittedOn.Should().BeNull();
			var evt = _h.Outbox.Enqueued.Should().ContainSingle().Which.Envelope;
			evt.Trigger.Should().Be(WorkflowTriggerEventType.RecordPermitExpiring);
			var payload = JObject.FromObject(evt.Payload);
			payload["permit"]["type_code"].Value<string>().Should().Be("HW");
			payload["permit"]["days_until_expiry"].Value<int>().Should().Be(10);
			payload.ToString().Should().NotContain("Applicant");

			(await _h.PermitsService.SweepExpiryAsync(Dept, now.AddDays(1), 30)).Should().Be((0, 0), "no repeat notice, nothing else due");
			(await _h.PermitsService.CountAsync(Dept, Member, new RmsPermitQuery { States = new List<int> { (int)RmsPermitState.Issued } })).Should().Be(2);
		}
	}
}
