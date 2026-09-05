using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Neris;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Local NERIS validation (RMS plan section 7: requiredness, conditional value sets, time-sequence tests):
	/// the mandatory base/dispatch facts, value-set membership, identifier shapes and per-unit time order surface
	/// before a submission is queued; destination replies fold into the same issue shape.
	/// </summary>
	[TestFixture]
	public class NerisValidationTests
	{
		private static NerisValidationService Service()
		{
			return new NerisValidationService(Mock.Of<INerisApiClient>(), Mock.Of<INerisProfileService>());
		}

		[Test]
		public void A_complete_report_has_no_errors()
		{
			var issues = Service().ValidateLocal(NerisMappingTests.Snapshot(), NerisMappingTests.Profile());

			issues.Where(i => i.Severity == (int)RmsValidationSeverity.Error).Should().BeEmpty(string.Join("; ", issues.Select(i => i.RuleKey)));
		}

		[Test]
		public void A_latitude_without_a_longitude_is_reported_rather_than_throwing()
		{
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Location.Latitude = 41.88m;
			snapshot.Location.Longitude = null;

			var issues = Service().ValidateLocal(snapshot, NerisMappingTests.Profile());

			issues.Select(i => i.RuleKey).Should().Contain("neris.location.point");
			issues.Select(i => i.RuleKey).Should().NotContain("neris.location.point.range",
				"the range check has nothing to compare and must not dereference the missing coordinate");
		}

		[Test]
		public void Both_coordinates_present_and_out_of_range_is_still_reported()
		{
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Location.Latitude = 91m;
			snapshot.Location.Longitude = 10m;

			Service().ValidateLocal(snapshot, NerisMappingTests.Profile()).Select(i => i.RuleKey).Should().Contain("neris.location.point.range");
		}

		[Test]
		public void Missing_mandatory_facts_are_reported_with_stable_rule_keys()
		{
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Report.IncidentNumber = null;
			snapshot.Report.CallCreatedOn = null;
			snapshot.Report.CallArrivalOn = null;
			snapshot.Types.Clear();
			snapshot.Location = null;

			var keys = Service().ValidateLocal(snapshot, NerisMappingTests.Profile()).Select(i => i.RuleKey).ToList();

			keys.Should().Contain(new[] { "neris.base.incident_number", "neris.dispatch.call_create", "neris.dispatch.call_arrival", "neris.incident_types.required", "neris.base.location" });
		}

		[Test]
		public void The_profile_entity_id_shape_and_a_single_primary_type_are_enforced()
		{
			var profile = NerisMappingTests.Profile();
			profile.NerisEntityId = "24027000";
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Types.ForEach(t => t.IsPrimary = true);
			snapshot.Types.Add(new RmsIncidentType { TypeCode = "NOT||A||TYPE", Ordinal = 2 });

			var issues = Service().ValidateLocal(snapshot, profile);

			issues.Select(i => i.RuleKey).Should().Contain(new[] { "neris.profile.entity.shape", "neris.incident_types.primary", "neris.incident_types.code" });
			issues.Single(i => i.RuleKey == "neris.incident_types.code").Message.Should().Contain("NOT||A||TYPE");
		}

		[Test]
		public void Unit_identifier_shape_response_mode_and_time_order_are_checked_per_unit()
		{
			var snapshot = NerisMappingTests.Snapshot();
			var unit = snapshot.Units[0];
			unit.UnitNerisId = "E1-BAD";
			unit.ResponseMode = "FAST";
			unit.OnSceneOn = unit.DispatchedOn.Value.AddMinutes(-10);

			var issues = Service().ValidateLocal(snapshot, NerisMappingTests.Profile());

			issues.Select(i => i.RuleKey).Should().Contain(new[] { "neris.unit.id.shape", "neris.unit.response_mode", "neris.unit.sequence" });
			issues.Where(i => i.FieldPath != null && i.FieldPath.StartsWith("dispatch.unit_responses[0]")).Should().HaveCount(3);
		}

		[Test]
		public void Aid_entries_need_a_counterpart_id_and_valid_codes()
		{
			var snapshot = NerisMappingTests.Snapshot();
			snapshot.Aids[0].CounterpartNerisId = "ABC";
			snapshot.Aids[0].AidType = "HELP";
			snapshot.Aids[1].NonFdType = "PIZZA";

			var keys = Service().ValidateLocal(snapshot, NerisMappingTests.Profile()).Select(i => i.RuleKey).ToList();

			keys.Should().Contain(new[] { "neris.aid.counterpart", "neris.aid.type", "neris.aid.nonfd" });
		}

		[Test]
		public void Destination_replies_fold_into_issues_of_source_destination()
		{
			var rejected = new NerisSubmissionOutcome
			{
				Kind = NerisOutcomeKind.Rejected,
				Errors = new List<NerisSubmissionError> { new NerisSubmissionError { Code = "missing", FieldPath = "dispatch.call_answered", Message = "Field required" } }
			};
			var transient = new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Transient, Message = "NERIS returned 503." };

			var rejectedIssues = NerisValidationService.ToIssues(rejected, 4, "rep-1");
			rejectedIssues.Should().ContainSingle(i => i.RuleKey == "neris.destination.missing" && i.FieldPath == "dispatch.call_answered" && i.Source == (int)RmsValidationSource.Destination && i.Severity == (int)RmsValidationSeverity.Error);

			var transientIssues = NerisValidationService.ToIssues(transient, 4, "rep-1");
			transientIssues.Should().ContainSingle(i => i.RuleKey == "neris.destination.unavailable" && i.Severity == (int)RmsValidationSeverity.Warning);

			NerisValidationService.ToIssues(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Accepted }, 4, "rep-1").Should().BeEmpty();
		}

		[Test]
		public void Labels_and_parents_derive_from_the_code_without_changing_it()
		{
			NerisProfileService.Label("FIRE||OUTSIDE_FIRE||TRASH_RUBBISH_FIRE").Should().Be("Trash Rubbish Fire");
			NerisProfileService.Parent("FIRE||OUTSIDE_FIRE||TRASH_RUBBISH_FIRE").Should().Be("FIRE||OUTSIDE_FIRE");
			NerisProfileService.Parent("MCI").Should().BeNull();
			NerisProfileService.Label("EMS").Should().Be("EMS");
		}
	}
}
