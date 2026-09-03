using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Lifecycle state-machine tests (RMS plan section 7): every transition in section 5.7 exists, none
	/// exists outside the table, each preset permits exactly its declared subset, and each transition
	/// maps to the right Workflow trigger.
	/// </summary>
	[TestFixture]
	public class RmsLifecycleTests
	{
		[Test]
		public void Quick_entry_goes_draft_to_finalized_and_never_through_review()
		{
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.Draft, RmsRecordState.Finalized).Should().BeTrue();
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.Draft, RmsRecordState.ReadyForReview).Should().BeFalse();
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.ReadyForReview, RmsRecordState.Finalized).Should().BeFalse();
			RmsLifecycle.NextStates(RmsLifecyclePreset.QuickEntry, RmsRecordState.Draft).Should().BeEquivalentTo(new[] { RmsRecordState.Finalized, RmsRecordState.Cancelled });
		}

		[Test]
		public void Review_required_needs_a_reviewer_to_finalize_and_supports_unlimited_returns()
		{
			var p = RmsLifecyclePreset.ReviewRequired;
			RmsLifecycle.CanTransition(p, RmsRecordState.Draft, RmsRecordState.Finalized).Should().BeFalse("the author cannot finalize directly");
			RmsLifecycle.CanTransition(p, RmsRecordState.Draft, RmsRecordState.ReadyForReview).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.ReadyForReview, RmsRecordState.Finalized).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.ReadyForReview, RmsRecordState.Returned).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.Returned, RmsRecordState.Draft).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.ReadyForReview, RmsRecordState.Approved).Should().BeFalse("Approved belongs to the Approval preset only");
		}

		[Test]
		public void Approval_preset_requires_the_approve_step_before_finalize()
		{
			var p = RmsLifecyclePreset.ApprovalAcknowledgement;
			RmsLifecycle.CanTransition(p, RmsRecordState.ReadyForReview, RmsRecordState.Finalized).Should().BeFalse();
			RmsLifecycle.CanTransition(p, RmsRecordState.ReadyForReview, RmsRecordState.Approved).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.Approved, RmsRecordState.Finalized).Should().BeTrue();
			RmsLifecycle.CanTransition(p, RmsRecordState.Approved, RmsRecordState.Returned).Should().BeTrue();
		}

		[Test]
		public void Voided_and_cancelled_are_terminal_with_no_outgoing_transitions()
		{
			foreach (var preset in Enum.GetValues(typeof(RmsLifecyclePreset)).Cast<RmsLifecyclePreset>())
			{
				RmsLifecycle.NextStates(preset, RmsRecordState.Voided).Should().BeEmpty();
				RmsLifecycle.NextStates(preset, RmsRecordState.Cancelled).Should().BeEmpty();
			}

			RmsLifecycle.IsTerminal(RmsRecordState.Voided).Should().BeTrue();
			RmsLifecycle.IsTerminal(RmsRecordState.Cancelled).Should().BeTrue();
			RmsLifecycle.IsTerminal(RmsRecordState.Finalized).Should().BeFalse();
		}

		[Test]
		public void Void_is_only_reachable_from_the_finalized_family_and_cancel_only_from_unfinalized()
		{
			foreach (var preset in Enum.GetValues(typeof(RmsLifecyclePreset)).Cast<RmsLifecyclePreset>())
			{
				RmsLifecycle.CanTransition(preset, RmsRecordState.Draft, RmsRecordState.Voided).Should().BeFalse();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Finalized, RmsRecordState.Cancelled).Should().BeFalse();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Finalized, RmsRecordState.Voided).Should().BeTrue();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Amended, RmsRecordState.Voided).Should().BeTrue();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Draft, RmsRecordState.Cancelled).Should().BeTrue();
			}
		}

		[Test]
		public void Amend_creates_a_new_revision_from_finalized_or_amended()
		{
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.Finalized, RmsRecordState.Amended).Should().BeTrue();
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.Amended, RmsRecordState.Amended).Should().BeTrue();
			RmsLifecycle.CanTransition(RmsLifecyclePreset.QuickEntry, RmsRecordState.Draft, RmsRecordState.Amended).Should().BeFalse();
		}

		[Test]
		public void No_transition_exists_outside_the_table()
		{
			var states = Enum.GetValues(typeof(RmsRecordState)).Cast<RmsRecordState>().ToList();
			var presets = Enum.GetValues(typeof(RmsLifecyclePreset)).Cast<RmsLifecyclePreset>().ToList();

			var allowed = 0;
			foreach (var preset in presets)
			foreach (var from in states)
			foreach (var to in states)
				if (RmsLifecycle.CanTransition(preset, from, to))
					allowed++;

			var expected = RmsLifecycle.Table.Sum(t => t.Presets.Length);
			allowed.Should().Be(expected, "every allowed transition must be one row of the table");

			// A few nonsense transitions that must never be permitted.
			foreach (var preset in presets)
			{
				RmsLifecycle.CanTransition(preset, RmsRecordState.Finalized, RmsRecordState.Draft).Should().BeFalse();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Cancelled, RmsRecordState.Draft).Should().BeFalse();
				RmsLifecycle.CanTransition(preset, RmsRecordState.Voided, RmsRecordState.Finalized).Should().BeFalse();
			}
		}

		[Test]
		public void Transitions_map_to_the_registry_triggers()
		{
			RmsLifecycle.TriggerFor(RmsRecordState.Draft, RmsRecordState.ReadyForReview).Should().Be(WorkflowTriggerEventType.RecordSubmittedForReview);
			RmsLifecycle.TriggerFor(RmsRecordState.ReadyForReview, RmsRecordState.Returned).Should().Be(WorkflowTriggerEventType.RecordReturnedForCorrection);
			RmsLifecycle.TriggerFor(RmsRecordState.Draft, RmsRecordState.Finalized).Should().Be(WorkflowTriggerEventType.RecordFinalized);
			RmsLifecycle.TriggerFor(RmsRecordState.Finalized, RmsRecordState.Amended).Should().Be(WorkflowTriggerEventType.RecordAmended);
			RmsLifecycle.TriggerFor(RmsRecordState.Finalized, RmsRecordState.Voided).Should().Be(WorkflowTriggerEventType.RecordVoided);
			RmsLifecycle.TriggerFor(RmsRecordState.Draft, RmsRecordState.Cancelled).Should().Be(WorkflowTriggerEventType.RecordCancelled);
			RmsLifecycle.TriggerFor(RmsRecordState.ReadyForReview, RmsRecordState.Approved).Should().BeNull("RecordApproved (103) is RMS-1B");
			RmsLifecycle.TriggerFor(RmsRecordState.Returned, RmsRecordState.Draft).Should().BeNull("re-opening a returned draft is not an event");
		}

		[Test]
		public void Locked_definitions_default_to_quick_entry_for_logs_parity()
		{
			RmsDefinitionKeys.LockedDefaultPreset.Should().Be(RmsLifecyclePreset.QuickEntry);
			RmsDefinitionKeys.LockedTypes.Should().HaveCount(7);
			RmsDefinitionKeys.CardinalityFor(RmsDefinitionKeys.UnitActivity).Should().Be(RmsRecordCardinality.OnePerSubjectPerCall);
			RmsDefinitionKeys.CardinalityFor(RmsDefinitionKeys.NerisIncidentReport).Should().Be(RmsRecordCardinality.SingleAuthoritative);
			RmsDefinitionKeys.CardinalityFor(RmsDefinitionKeys.Run).Should().Be(RmsRecordCardinality.MultiplePerCall);
			RmsDefinitionKeys.IsSystemKey("system.run").Should().BeTrue();
			RmsDefinitionKeys.IsSystemKey("security-patrol").Should().BeFalse();
		}

		[Test]
		public void Retention_policy_resolves_hold_then_override_then_department_default_then_class_default()
		{
			var policy = new RecordsRetentionPolicy();
			policy.ResolveYears(RmsDefinitionKeys.Training).Should().Be(7);
			policy.ResolveYears(RmsDefinitionKeys.Coroner).Should().Be(RecordsRetentionPolicy.Permanent, "Coroner is restricted-class: permanent by default");

			policy.DepartmentDefaultYears = 10;
			policy.ResolveYears(RmsDefinitionKeys.Training).Should().Be(10);
			policy.ResolveYears(RmsDefinitionKeys.Coroner).Should().Be(RecordsRetentionPolicy.Permanent, "the department default never silently applies to a restricted class");

			policy.Overrides.Add(new RecordsRetentionOverride { DefinitionKey = RmsDefinitionKeys.Coroner, RetentionYears = 25, AppliesFrom = DateTime.UtcNow });
			policy.ResolveYears(RmsDefinitionKeys.Coroner).Should().Be(25, "an explicit override is the only way to shorten a restricted class");
		}
	}
}
