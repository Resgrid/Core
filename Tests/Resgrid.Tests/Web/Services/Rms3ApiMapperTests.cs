using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The RMS-3 v4 mapping rules that a client depends on and that a restricted grant decides: conditional
	/// sections carry the rule and its value sets, restricted content is withheld <em>and named</em> rather than
	/// silently dropped, and the bodies that would be expensive or sensitive to ship are only carried on the
	/// single-item reads.
	/// </summary>
	[TestFixture]
	public class Rms3ApiMapperTests
	{
		private const int Dept = 42;

		#region Incident report sections

		[Test]
		public void Sections_carry_the_rule_its_value_sets_and_whether_the_report_already_has_one()
		{
			var aggregate = Report();
			aggregate.Modules.Add(new RmsIncidentModule
			{
				RmsIncidentModuleId = "m1", ModuleKind = (int)RmsIncidentModuleKind.Fire, PrimaryCode = "HYDRANT", SecondaryCode = "NO", Ordinal = 0
			});

			var sections = new List<NerisSectionRequirement>
			{
				new NerisSectionRequirement { Kind = RmsIncidentModuleKind.Fire, Required = true, Reason = "A fire incident carries fire detail.", PrimaryCodeSet = "water_supply", SecondaryCodeSet = "fire_invest_need" },
				new NerisSectionRequirement { Kind = RmsIncidentModuleKind.SmokeAlarm, Required = false, Reason = "Alarm performance is worth recording.", PrimaryCodeSet = "alarm_smoke", SecondaryCodeSet = "alarm_operation" }
			};

			var data = IncidentReportsApiMapper.ToReport(aggregate, false, true, sections);

			data.Sections.Should().HaveCount(2);

			var fire = data.Sections.Single(s => s.Kind == (int)RmsIncidentModuleKind.Fire);
			fire.Required.Should().BeTrue();
			fire.Present.Should().BeTrue("the report already carries a fire section");
			fire.PayloadPath.Should().Be("fire_detail");
			fire.SchemaName.Should().Be("FirePayload");
			fire.PrimaryCodeSet.Should().Be("water_supply");
			fire.SecondaryCodeSet.Should().Be("fire_invest_need");

			var alarm = data.Sections.Single(s => s.Kind == (int)RmsIncidentModuleKind.SmokeAlarm);
			alarm.Required.Should().BeFalse();
			alarm.Present.Should().BeFalse();
			alarm.Reason.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public void A_report_with_no_section_rules_yet_still_maps()
		{
			var data = IncidentReportsApiMapper.ToReport(Report(), false);

			data.Sections.Should().BeEmpty();
			data.Modules.Should().BeEmpty();
			data.WithheldFields.Should().BeEmpty();
		}

		[Test]
		public void A_rejected_report_can_be_re_queued_and_the_flag_says_so()
		{
			var aggregate = Report();
			aggregate.Report.State = (int)RmsRecordState.Rejected;
			aggregate.Report.CurrentRevisionId = "rev-1";
			aggregate.Submissions.Add(new RmsSubmission
			{
				RmsSubmissionId = "s1", DepartmentId = Dept, RecordId = "r1", RevisionId = "rev-1",
				State = (int)RmsSubmissionState.Rejected, QueuedOn = DateTime.UtcNow
			});

			// The destination stores a rejection as RmsSubmissionState.Rejected and QueueSubmissionCoreAsync re-queues
			// it; reporting only Failed hid the retry from every client that reads the flag.
			IncidentReportsApiMapper.ToReport(aggregate, true).CanQueueSubmission.Should().BeTrue();
		}

		#endregion

		#region Restricted withholding

		[Test]
		public void A_casualty_stays_visible_without_the_restricted_grant_but_its_identifying_half_is_withheld()
		{
			var aggregate = Report();
			aggregate.Casualties.Add(Casualty());

			var data = IncidentReportsApiMapper.ToReport(aggregate, false, false);

			var casualty = data.Casualties.Should().ContainSingle().Subject;
			casualty.CasualtyCause.Should().Be("FALL", "that somebody was hurt is part of the incident");
			casualty.WasFatal.Should().BeFalse();

			casualty.PersonnelUserId.Should().BeNull();
			casualty.Rank.Should().BeNull();
			casualty.BirthMonthYear.Should().BeNull();
			casualty.Gender.Should().BeNull();
			casualty.Race.Should().BeNull();
			casualty.InjuryDetailJson.Should().BeNull();

			data.WithheldFields.Should().Contain(new[]
			{
				"Casualties.PersonnelUserId", "Casualties.Rank", "Casualties.BirthMonthYear",
				"Casualties.Gender", "Casualties.Race", "Casualties.InjuryDetailJson"
			});
		}

		[Test]
		public void The_restricted_grant_carries_the_whole_casualty()
		{
			var aggregate = Report();
			aggregate.Casualties.Add(Casualty());

			var data = IncidentReportsApiMapper.ToReport(aggregate, false, true);

			var casualty = data.Casualties.Should().ContainSingle().Subject;
			casualty.PersonnelUserId.Should().Be("member-1");
			casualty.Rank.Should().Be("LIEUTENANT");
			casualty.BirthMonthYear.Should().Be("1979-04");
			data.WithheldFields.Should().BeEmpty();
		}

		[Test]
		public void A_vehicle_keeps_its_make_and_damage_but_loses_vin_and_plate_without_the_grant()
		{
			var aggregate = Analysis();
			aggregate.Vehicles.Add(Vehicle());

			var data = IncidentAnalysisApiMapper.ToAnalysis(aggregate, false);

			var vehicle = data.Vehicles.Should().ContainSingle().Subject;
			vehicle.Make.Should().Be("FORD");
			vehicle.DamageType.Should().Be("DAMAGED_NOT_DRIVABLE");
			vehicle.Vin.Should().BeNull();
			vehicle.LicensePlate.Should().BeNull();
			vehicle.LicenseState.Should().BeNull();
			data.WithheldFields.Should().Contain(new[] { "Vehicles.Vin", "Vehicles.LicensePlate", "Vehicles.LicenseState" });
		}

		[Test]
		public void A_disclosure_request_withholds_who_asked_without_the_restricted_grant()
		{
			var request = new RmsDisclosureRequest
			{
				RmsDisclosureRequestId = "d1", DepartmentId = Dept, RequestNumber = "PRR-2026-0001", State = (int)RmsDisclosureState.Received,
				RequesterName = "A. Reporter", RequesterOrganization = "The Gazette", RequesterContact = "a@example.test",
				ReceivedOn = DateTime.UtcNow, RowVersion = 3
			};

			var withheld = DisclosuresApiMapper.ToRequest(request, false);
			withheld.RequestNumber.Should().Be("PRR-2026-0001", "the reference is not itself sensitive");
			withheld.RequesterName.Should().BeNull();
			withheld.RequesterOrganization.Should().BeNull();
			withheld.RequesterContact.Should().BeNull();
			withheld.WithheldFields.Should().BeEquivalentTo(new[] { "RequesterName", "RequesterOrganization", "RequesterContact" });

			var granted = DisclosuresApiMapper.ToRequest(request, true);
			granted.RequesterName.Should().Be("A. Reporter");
			granted.WithheldFields.Should().BeEmpty();
		}

		#endregion

		#region Bodies are only carried where they belong

		[Test]
		public void An_evidence_list_never_ships_manifests_and_a_restricted_one_says_it_was_withheld()
		{
			var artifact = new RmsEvidenceArtifact
			{
				RmsEvidenceArtifactId = "e1", DepartmentId = Dept, RecordId = "r1", Kind = (int)RmsEvidenceKind.TrackingFix,
				Title = "Unit tracking", ManifestJson = "{\"fixes\":[]}", Checksum = "abc", SourceItemCount = 4,
				Classification = (int)RmsEvidenceClassification.Restricted, CapturedOn = DateTime.UtcNow
			};

			RecordEvidenceApiMapper.ToArtifact(artifact, true).ManifestJson
				.Should().BeNull("a list response carries the header only, however many artifacts it holds");

			var withheld = RecordEvidenceApiMapper.ToArtifact(artifact, false, true);
			withheld.Title.Should().Be("Unit tracking");
			withheld.ManifestJson.Should().BeNull();
			withheld.ManifestWithheld.Should().BeTrue("the caller should know there is content they are not seeing");

			var granted = RecordEvidenceApiMapper.ToArtifact(artifact, true, true);
			granted.ManifestJson.Should().Be("{\"fixes\":[]}");
			granted.ManifestWithheld.Should().BeFalse();
		}

		[Test]
		public void An_unrestricted_manifest_is_readable_without_the_restricted_grant()
		{
			var artifact = new RmsEvidenceArtifact
			{
				RmsEvidenceArtifactId = "e2", DepartmentId = Dept, RecordId = "r1", Kind = (int)RmsEvidenceKind.RunCardActivation,
				ManifestJson = "{\"card\":\"A\"}", Classification = (int)RmsEvidenceClassification.Unrestricted, CapturedOn = DateTime.UtcNow
			};

			RecordEvidenceApiMapper.ToArtifact(artifact, false, true).ManifestJson.Should().Be("{\"card\":\"A\"}");
		}

		[Test]
		public void A_production_listing_omits_the_released_content_but_keeps_the_produced_set()
		{
			var production = new RmsDisclosureProduction
			{
				RmsDisclosureProductionId = "p1", DisclosureRequestId = "d1", ProductionNumber = 1,
				RedactionProfile = RmsRedactionProfiles.Standard, ProducedSetJson = "[{\"recordId\":\"r1\"}]",
				WithheldFieldsJson = "[]", ArtifactJson = "{\"records\":[]}", Checksum = "def", RecordCount = 1, PreparedOn = DateTime.UtcNow
			};

			var listed = DisclosuresApiMapper.ToProduction(production);
			listed.ArtifactJson.Should().BeNull();
			listed.ProducedSetJson.Should().NotBeNull("the produced set is what proves a later amendment changed nothing");

			DisclosuresApiMapper.ToProduction(production, true).ArtifactJson.Should().Be("{\"records\":[]}");
		}

		#endregion

		#region Scope input

		[Test]
		public void A_scope_query_never_takes_the_viewer_fields_from_the_client()
		{
			var scope = DisclosuresApiMapper.ToScopeQuery(new DisclosureScopeQueryInput
			{
				States = new List<int> { (int)RmsRecordState.Finalized },
				DefinitionKey = "  ",
				Year = 2026,
				Take = 5000
			});

			scope.States.Should().BeEquivalentTo(new[] { (int)RmsRecordState.Finalized });
			scope.DefinitionKey.Should().BeNull("a blank filter is no filter");
			scope.Year.Should().Be(2026);
			scope.Take.Should().Be(500, "the page size is clamped so a scope cannot ask for the whole department at once");
			scope.ViewerUserId.Should().BeNull();
			scope.VisibleGroupIds.Should().BeNull();
		}

		[Test]
		public void A_missing_scope_is_null_rather_than_an_empty_query_that_would_match_everything()
		{
			DisclosuresApiMapper.ToScopeQuery(null).Should().BeNull();
		}

		#endregion

		#region Fixtures

		private static IncidentReportAggregate Report()
		{
			return new IncidentReportAggregate
			{
				Report = new RmsIncidentReport
				{
					RmsIncidentReportId = "r1", DepartmentId = Dept, CallId = 7, DefinitionKey = "neris.incident", DefinitionVersion = 1,
					LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, State = (int)RmsRecordState.Draft, DraftReference = "D-ABCDE",
					RowVersion = 3, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow
				}
			};
		}

		private static IncidentAnalysisAggregate Analysis()
		{
			return new IncidentAnalysisAggregate
			{
				Analysis = new RmsIncidentAnalysis
				{
					RmsIncidentAnalysisId = "a1", DepartmentId = Dept, IncidentReportId = "r1", State = (int)RmsIncidentAnalysisState.Draft,
					RowVersion = 2, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow
				},
				Report = new RmsIncidentReport { RmsIncidentReportId = "r1", DepartmentId = Dept }
			};
		}

		private static RmsCasualtyRescue Casualty()
		{
			return new RmsCasualtyRescue
			{
				RmsCasualtyRescueId = "c1", DepartmentId = Dept, RecordId = "r1", Kind = (int)RmsCasualtyRescueKind.Casualty,
				PersonType = RmsCasualtyPersonTypes.Firefighter, PersonnelUserId = "member-1", Rank = "LIEUTENANT", BirthMonthYear = "1979-04",
				Gender = "MALE", Race = "WHITE", WasInjured = true, WasFatal = false, CasualtyCause = "FALL",
				InjuryDetailJson = "{\"body_part\":\"ANKLE\"}", Ordinal = 0
			};
		}

		private static RmsIncidentVehicle Vehicle()
		{
			return new RmsIncidentVehicle
			{
				RmsIncidentVehicleId = "v1", DepartmentId = Dept, RecordId = "a1", VehicleKind = "AUTOMOBILE", Make = "FORD",
				DamageType = "DAMAGED_NOT_DRIVABLE", Vin = "1FTFW1ET5DFC12345", LicensePlate = "ABC 123", LicenseState = "IL", Ordinal = 0
			};
		}

		#endregion
	}
}
