using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-5 occupancy master, crosswalk, ownership switch and the Contacts projection (RMS plan section 4.3).</summary>
	[TestFixture]
	public class RecordsOccupancyServiceTests
	{
		private RmsPreventionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsPreventionHarness();

		[Test]
		public async Task Saving_an_occupancy_numbers_it_seals_it_and_records_field_provenance()
		{
			var saved = await _h.OccupancyService.SaveAsync(Dept, Admin, new RmsOccupancy { Name = "Riverside Mill", AddressText = "12 River Road", City = "Portland", GateCode = "4471", HazmatOnSite = true, OccupancyType = 3 });
			saved.OccupancyNumber.Should().Be($"OCC-{DateTime.UtcNow.Year}-0001");
			saved.NormalizedAddress.Should().Be("12 RIVER RD PORTLAND");
			saved.GateCode.Should().Be("4471", "the caller gets its plaintext back after the seal");
			_h.Protection.Writes.Should().Contain("occupancy");
			_h.Provenance.Rows.Should().Contain(p => p.FieldKey == "GateCode" && p.SourceKind == 0 && p.ReviewedOn != null);
			_h.Audits.Rows.Should().ContainSingle(a => a.Purpose == "Occupancy created" && a.CorrelationId == saved.RmsOccupancyId && a.RecordId == null);

			var aggregate = await _h.OccupancyService.GetAsync(Dept, Member, saved.RmsOccupancyId);
			aggregate.Occupancy.Name.Should().Be("Riverside Mill");
			aggregate.Provenance.Should().NotBeEmpty();
		}

		[Test]
		public async Task Changing_prevention_data_needs_the_prevention_administrator_permission_and_the_module_flag()
		{
			Func<Task> asMember = () => _h.OccupancyService.SaveAsync(Dept, Member, new RmsOccupancy { Name = "x" });
			await asMember.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> asOutsider = () => _h.OccupancyService.ListAsync(Dept, Outsider, new RmsOccupancyQuery());
			await asOutsider.Should().ThrowAsync<UnauthorizedAccessException>();

			_h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionOccupancy);
			Func<Task> disabled = () => _h.OccupancyService.ListAsync(Dept, Admin, new RmsOccupancyQuery());
			(await disabled.Should().ThrowAsync<RecordsModuleDisabledException>()).Which.Module.Should().Be(RecordsPreventionModule.Occupancy);
			(await _h.OccupancyService.IsModuleEnabledAsync(Dept)).Should().BeFalse();

			_h.DisabledFlags.Clear();
			_h.RecordsUsable = false;
			(await _h.OccupancyService.IsModuleEnabledAsync(Dept)).Should().BeFalse("a module flag never bypasses the Records cutover");
		}

		[Test]
		public async Task Hazards_and_role_separated_contact_links_hang_off_the_master()
		{
			var o = _h.SeedOccupancy();
			_h.Contacts.Setup(c => c.GetByIdAsync("c-owner")).ReturnsAsync(new Contact { ContactId = "c-owner", DepartmentId = Dept, CompanyName = "Owner LLC", ContactType = 1 });
			_h.Contacts.Setup(c => c.GetByIdAsync("c-site")).ReturnsAsync(new Contact { ContactId = "c-site", DepartmentId = Dept, FirstName = "Site", LastName = "Manager" });

			var hazard = await _h.OccupancyService.SaveHazardAsync(Dept, Admin, new RmsOccupancyHazard { RmsOccupancyId = o.RmsOccupancyId, Title = "Propane tank", Severity = 4, ShouldAlert = true, Description = "500 gal, NE corner" });
			hazard.Description.Should().Be("500 gal, NE corner");
			var owner = await _h.OccupancyService.LinkContactAsync(Dept, Admin, o.RmsOccupancyId, "c-owner", RmsOccupancyContactRole.Owner, false);
			var site = await _h.OccupancyService.LinkContactAsync(Dept, Admin, o.RmsOccupancyId, "c-site", RmsOccupancyContactRole.Site, true);
			await _h.OccupancyService.LinkContactAsync(Dept, Admin, o.RmsOccupancyId, "c-owner", RmsOccupancyContactRole.EmergencyContact, true);

			var aggregate = await _h.OccupancyService.GetAsync(Dept, Admin, o.RmsOccupancyId);
			aggregate.Hazards.Should().ContainSingle(h => h.Title == "Propane tank");
			aggregate.ContactLinks.Should().HaveCount(3);
			aggregate.ContactLinks.Count(l => l.IsPrimary).Should().Be(1, "only one primary link per occupancy");
			aggregate.ContactLinks.Single(l => l.IsPrimary).Role.Should().Be((int)RmsOccupancyContactRole.EmergencyContact);

			Func<Task> foreign = () => _h.OccupancyService.LinkContactAsync(Dept, Admin, o.RmsOccupancyId, "c-missing", RmsOccupancyContactRole.Site, false);
			await foreign.Should().ThrowAsync<ArgumentException>();
			await _h.OccupancyService.UnlinkContactAsync(Dept, Admin, owner.RmsOccupancyContactLinkId);
			(await _h.OccupancyService.GetAsync(Dept, Admin, o.RmsOccupancyId)).ContactLinks.Should().HaveCount(2);
		}

		private void SeedContactsWorld()
		{
			_h.Contacts.Setup(c => c.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Contact>
			{
				new Contact { ContactId = "c1", DepartmentId = Dept, CompanyName = "Riverside Mill", ContactType = 1, PhysicalAddressId = 10, LocationGpsCoordinates = "45.5,-122.6" },
				new Contact { ContactId = "c2", DepartmentId = Dept, CompanyName = "Mill Annex", ContactType = 1, LocationGpsCoordinates = "45.50010,-122.60010" },
				new Contact { ContactId = "c3", DepartmentId = Dept, FirstName = "Pat", LastName = "Person", ContactType = 0, LocationGpsCoordinates = "45.9,-122.9" },
				new Contact { ContactId = "c4", DepartmentId = Dept, CompanyName = "No Site Corp", ContactType = 1 }
			});
			_h.Contacts.Setup(c => c.GetByIdAsync("c1")).ReturnsAsync(new Contact { ContactId = "c1", DepartmentId = Dept, CompanyName = "Riverside Mill", ContactType = 1, PhysicalAddressId = 10 });
			_h.Addresses.Setup(a => a.GetByIdAsync(10)).ReturnsAsync(new Address { AddressId = 10, Address1 = "12 River Road", City = "Portland", State = "OR", PostalCode = "97201" });
			var preplan = new ContactPreplan { ContactPreplanId = "pp1", DepartmentId = Dept, ContactId = "c1", ConstructionType = 2, RoofType = 1, OccupancyType = 5, GateCode = "4471", KnoxBoxLocation = "Front door, left", TacticalSummary = "Sprinklered", HazmatOnSite = true, NearestHydrantLocation = "SE corner", NextReviewDue = DateTime.UtcNow.AddMonths(6) };
			_h.ContactPreplans.Setup(p => p.GetPreplansByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ContactPreplan> { preplan });
			_h.ContactPreplans.Setup(p => p.GetPreplanByContactIdAsync("c1", Dept)).ReturnsAsync(preplan);
			_h.ContactHazards.Setup(h => h.GetHazardsByContactIdAsync("c1", Dept)).ReturnsAsync(new List<ContactPreplanHazard> { new ContactPreplanHazard { ContactPreplanHazardId = "ch1", ContactPreplanId = "pp1", ContactId = "c1", Title = "Propane", Severity = 3, ShouldAlert = true, Description = "500 gal" } });
			_h.Pois.Setup(p => p.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<Poi> { new Poi { PoiId = 77, Name = "Water tower", Address = "1 Hill Ct", Latitude = 45.7, Longitude = -122.7 } });
		}

		[Test]
		public async Task Inventory_groups_sources_by_address_and_proximity_and_suggests_matching_occupancies()
		{
			SeedContactsWorld();
			var existing = _h.SeedOccupancy("Riverside Mill (RMS)", "12 River Rd, Portland OR 97201");

			var result = await _h.OccupancyService.InventoryCandidatesAsync(Dept, Admin);
			result.SourcesScanned.Should().Be(3, "pre-plan c1, company c2 with coordinates, POI 77; the person and the site-less company are skipped");
			result.CandidatesCreated.Should().Be(3);
			var rows = _h.Crosswalks.Rows;
			var pp = rows.Single(r => r.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan);
			pp.SuggestedOccupancyId.Should().Be(existing.RmsOccupancyId);
			pp.MatchConfidence.Should().Be(90);
			pp.ContactId.Should().Be("c1");
			var annex = rows.Single(r => r.SourceId == "c2");
			annex.GroupKey.Should().Be(pp.GroupKey, "the annex sits 13 m from the mill, so it shares the group");
			rows.Single(r => r.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.Poi).GroupKey.Should().NotBe(pp.GroupKey);
			rows.Should().NotContain(r => r.SourceId == "c3" || r.SourceId == "c4");

			var status = await _h.OccupancyService.GetReconciliationStatusAsync(Dept);
			status.State.Should().Be(RmsOccupancyOwnershipState.Reconciling);
			status.Candidates.Should().Be(3);
			status.UnreconciledPreplans.Should().Be(1);
			status.CanSwitchToRecords.Should().BeFalse();

			(await _h.OccupancyService.InventoryCandidatesAsync(Dept, Admin)).CandidatesUpdated.Should().Be(3, "a second inventory updates rather than duplicates");
			_h.Crosswalks.Rows.Should().HaveCount(3);
		}

		[Test]
		public async Task Linking_a_preplan_candidate_creates_an_occupancy_with_provenance_hazards_and_a_site_link_then_the_switch_becomes_possible()
		{
			SeedContactsWorld();
			await _h.OccupancyService.InventoryCandidatesAsync(Dept, Admin);
			var pp = _h.Crosswalks.Rows.Single(r => r.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan);

			var occupancy = await _h.OccupancyService.LinkCandidateAsync(Dept, Admin, pp.RmsOccupancyCrosswalkId, null);
			occupancy.GateCode.Should().Be("4471");
			occupancy.KnoxBoxLocation.Should().Be("Front door, left");
			occupancy.WaterSupplyNotes.Should().Contain("Nearest hydrant: SE corner");
			occupancy.OccupancyType.Should().Be(5);
			occupancy.HazmatOnSite.Should().BeTrue();
			_h.Hazards.Rows.Should().ContainSingle(h => h.RmsOccupancyId == occupancy.RmsOccupancyId && h.SourceContactPreplanHazardId == "ch1" && h.Description == "500 gal");
			_h.Provenance.Rows.Where(p => p.RmsOccupancyId == occupancy.RmsOccupancyId).Should().Contain(p => p.FieldKey == "GateCode" && p.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan && p.SourceId == "pp1");
			_h.Links.Rows.Should().ContainSingle(l => l.RmsOccupancyId == occupancy.RmsOccupancyId && l.ContactId == "c1" && l.Role == (int)RmsOccupancyContactRole.Site && l.IsPrimary);
			pp.State.Should().Be((int)RmsOccupancyCrosswalkState.Linked);
			_h.Protection.Writes.Should().Contain("occupancy").And.Contain("occupancy hazard");

			Func<Task> again = () => _h.OccupancyService.LinkCandidateAsync(Dept, Admin, pp.RmsOccupancyCrosswalkId, null);
			await again.Should().ThrowAsync<InvalidOperationException>();

			Func<Task> early = () => _h.OccupancyService.SwitchWriteOwnershipAsync(Dept, Admin, "go live");
			(await early.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*undecided candidate*");

			foreach (var candidate in _h.Crosswalks.Rows.Where(r => r.State == (int)RmsOccupancyCrosswalkState.Candidate).ToList())
				await _h.OccupancyService.RejectCandidateAsync(Dept, Admin, candidate.RmsOccupancyCrosswalkId, "not a structure we track");
			var status = await _h.OccupancyService.GetReconciliationStatusAsync(Dept);
			status.CanSwitchToRecords.Should().BeTrue();

			var ownership = await _h.OccupancyService.SwitchWriteOwnershipAsync(Dept, Admin, "go live");
			ownership.State.Should().Be((int)RmsOccupancyOwnershipState.RecordsOwned);
			(await _h.OccupancyService.IsRecordsOwnedAsync(Dept)).Should().BeTrue();
			Func<Task> twice = () => _h.OccupancyService.SwitchWriteOwnershipAsync(Dept, Admin, "again");
			await twice.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task A_concealed_preplan_is_never_copied_as_the_sentinel()
		{
			SeedContactsWorld();
			_h.ProtectedReads.Setup(r => r.ResolveContactPreplansForReadAsync(Dept, It.IsAny<IReadOnlyList<ContactPreplan>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult { IsProtected = true, RedactedFields = new List<string> { "contactpreplans.gatecode" }, ProtectedReason = "step_up_required" });
			await _h.OccupancyService.InventoryCandidatesAsync(Dept, Admin);
			var pp = _h.Crosswalks.Rows.Single(r => r.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan);
			Func<Task> link = () => _h.OccupancyService.LinkCandidateAsync(Dept, Admin, pp.RmsOccupancyCrosswalkId, null);
			(await link.Should().ThrowAsync<RecordProtectedContentException>()).Which.Reason.Should().Be("step_up_required");
			_h.Occupancies.Rows.Should().BeEmpty();
			pp.State.Should().Be((int)RmsOccupancyCrosswalkState.Candidate);
		}

		[Test]
		public async Task The_dispatch_projection_carries_the_contacts_shape_and_follows_merges()
		{
			SeedContactsWorld();
			await _h.OccupancyService.InventoryCandidatesAsync(Dept, Admin);
			var pp = _h.Crosswalks.Rows.Single(r => r.SourceKind == (int)RmsOccupancyCrosswalkSourceKind.ContactPreplan);
			var occupancy = await _h.OccupancyService.LinkCandidateAsync(Dept, Admin, pp.RmsOccupancyCrosswalkId, null);
			_h.Violations.Rows.Add(new RmsViolation { RmsViolationId = "v1", DepartmentId = Dept, RmsOccupancyId = occupancy.RmsOccupancyId, RmsInspectionId = "i1", State = (int)RmsViolationState.Open, Severity = 3, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow });

			var projection = await _h.OccupancyService.GetDispatchProjectionForContactAsync(Dept, "c1");
			projection.ContractVersion.Should().Be(OccupancyDispatchProjectionV1.CurrentContractVersion);
			projection.GateCode.Should().Be("4471");
			projection.Hazards.Should().ContainSingle(h => h.Title == "Propane" && h.ShouldAlert);
			projection.Contacts.Should().ContainSingle(c => c.ContactId == "c1" && c.Role == (int)RmsOccupancyContactRole.Site);
			projection.OpenViolationCount.Should().Be(1);
			projection.Sources.Should().Contain("ContactPreplan");

			(await _h.OccupancyService.GetPreplanProjectionsAsync(Dept, new[] { "c1" })).Should().BeEmpty("Contacts still owns writes, so Contacts reads its own rows");
			await _h.OccupancyService.RejectCandidateAsync(Dept, Admin, _h.Crosswalks.Rows.Single(r => r.SourceId == "c2").RmsOccupancyCrosswalkId, "dup");
			await _h.OccupancyService.RejectCandidateAsync(Dept, Admin, _h.Crosswalks.Rows.Single(r => r.SourceId == "77").RmsOccupancyCrosswalkId, "poi");
			await _h.OccupancyService.SwitchWriteOwnershipAsync(Dept, Admin, "go live");

			var view = (await _h.OccupancyService.GetPreplanProjectionsAsync(Dept, new[] { "c1", "c9" }))["c1"];
			view.ContactPreplanId.Should().Be("occ:" + occupancy.RmsOccupancyId);
			view.ContactId.Should().Be("c1");
			view.GateCode.Should().Be("4471");
			view.NearestHydrantLocation.Should().BeNull("no RMS hydrant is linked; the pre-plan text moved into the water notes");
			view.Hazards.Should().ContainSingle(h => h.Title == "Propane" && h.ContactPreplanId == view.ContactPreplanId);

			var survivor = _h.SeedOccupancy("Riverside Campus", "12 River Rd");
			await _h.OccupancyService.MergeAsync(Dept, Admin, occupancy.RmsOccupancyId, survivor.RmsOccupancyId);
			occupancy.Status.Should().Be((int)RmsOccupancyStatus.Merged);
			(await _h.OccupancyService.GetOccupancyIdForContactAsync(Dept, "c1")).Should().Be(survivor.RmsOccupancyId);
			(await _h.OccupancyService.GetDispatchProjectionForContactAsync(Dept, "c1")).Hazards.Should().ContainSingle(h => h.Title == "Propane", "hazards moved to the survivor");
		}

		[Test]
		public async Task Ownership_lookup_failures_leave_contacts_in_charge()
		{
			var broken = new Mock<IRmsOccupancyOwnershipsRepository>();
			broken.Setup(o => o.GetForDepartmentAsync(It.IsAny<int>())).ThrowsAsync(new InvalidOperationException("relation does not exist"));
			var service = new RecordsOccupancyService(_h.Gate, _h.Occupancies, _h.Links, _h.Hazards, _h.Crosswalks, _h.Provenance, broken.Object, _h.Violations, _h.Hydrants, _h.ContactPreplans.Object, _h.ContactHazards.Object, _h.Contacts.Object, _h.Addresses.Object, _h.Pois.Object, _h.ProtectedReads.Object, _h.Grant.Object, _h.Protection, _h.UnitOfWork.Object);
			(await service.IsRecordsOwnedAsync(Dept)).Should().BeFalse();
			(await service.GetPreplanProjectionsAsync(Dept, new[] { "c1" })).Should().BeEmpty();
		}

		[Test]
		public async Task Deleting_is_refused_while_violations_are_open_and_review_marks_provenance()
		{
			var o = _h.SeedOccupancy();
			_h.Violations.Rows.Add(new RmsViolation { RmsViolationId = "v1", DepartmentId = Dept, RmsOccupancyId = o.RmsOccupancyId, RmsInspectionId = "i1", State = (int)RmsViolationState.Open, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow });
			Func<Task> delete = () => _h.OccupancyService.DeleteAsync(Dept, Admin, o.RmsOccupancyId);
			await delete.Should().ThrowAsync<InvalidOperationException>();
			_h.Violations.Rows[0].State = (int)RmsViolationState.Verified;

			var reviewed = await _h.OccupancyService.MarkReviewedAsync(Dept, Admin, o.RmsOccupancyId, 18);
			reviewed.NextReviewDue.Should().BeCloseTo(DateTime.UtcNow.AddMonths(18), TimeSpan.FromMinutes(1));
			reviewed.IsReviewOverdue(DateTime.UtcNow).Should().BeFalse();

			await _h.OccupancyService.DeleteAsync(Dept, Admin, o.RmsOccupancyId);
			(await _h.OccupancyService.GetAsync(Dept, Admin, o.RmsOccupancyId)).Should().BeNull();
		}
	}
}
