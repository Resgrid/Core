using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The single Records state machine (RMS plan section 5.7, "Lifecycle states"). A preset is a subset
	/// of permitted transitions, never a different machine, so a definition that changes preset does not
	/// change how existing Records are interpreted. No transition exists outside this table.
	/// </summary>
	public static class RmsLifecycle
	{
		public readonly struct Transition
		{
			public Transition(RmsRecordState from, RmsRecordState to, RmsLifecyclePreset[] presets)
			{
				From = from;
				To = to;
				Presets = presets;
			}

			public RmsRecordState From { get; }
			public RmsRecordState To { get; }
			public RmsLifecyclePreset[] Presets { get; }
		}

		private static readonly RmsLifecyclePreset[] All = { RmsLifecyclePreset.QuickEntry, RmsLifecyclePreset.ReviewRequired, RmsLifecyclePreset.ApprovalAcknowledgement };
		private static readonly RmsLifecyclePreset[] ReviewPresets = { RmsLifecyclePreset.ReviewRequired, RmsLifecyclePreset.ApprovalAcknowledgement };
		private static readonly RmsLifecyclePreset[] ApprovalOnly = { RmsLifecyclePreset.ApprovalAcknowledgement };
		private static readonly RmsLifecyclePreset[] QuickAndReview = { RmsLifecyclePreset.QuickEntry, RmsLifecyclePreset.ReviewRequired };

		public static readonly IReadOnlyList<Transition> Table = new List<Transition>
		{
			// Quick Entry: Draft -> Finalized directly.
			new Transition(RmsRecordState.Draft, RmsRecordState.Finalized, new[] { RmsLifecyclePreset.QuickEntry }),
			// Review Required / Approval: author submits.
			new Transition(RmsRecordState.Draft, RmsRecordState.ReadyForReview, ReviewPresets),
			// Reviewer finalizes (Review Required) or approver approves (Approval).
			new Transition(RmsRecordState.ReadyForReview, RmsRecordState.Finalized, new[] { RmsLifecyclePreset.ReviewRequired }),
			new Transition(RmsRecordState.ReadyForReview, RmsRecordState.Approved, ApprovalOnly),
			new Transition(RmsRecordState.Approved, RmsRecordState.Finalized, ApprovalOnly),
			// Returns, from review or approval, back through Returned to Draft.
			new Transition(RmsRecordState.ReadyForReview, RmsRecordState.Returned, ReviewPresets),
			new Transition(RmsRecordState.Approved, RmsRecordState.Returned, ApprovalOnly),
			new Transition(RmsRecordState.Returned, RmsRecordState.Draft, ReviewPresets),
			// Amend: Finalized/Amended -> Amended (new revision; prior retained).
			new Transition(RmsRecordState.Finalized, RmsRecordState.Amended, All),
			new Transition(RmsRecordState.Amended, RmsRecordState.Amended, All),
			// Void: terminal from any finalized state.
			new Transition(RmsRecordState.Finalized, RmsRecordState.Voided, All),
			new Transition(RmsRecordState.Amended, RmsRecordState.Voided, All),
			new Transition(RmsRecordState.Accepted, RmsRecordState.Voided, All),
			new Transition(RmsRecordState.Rejected, RmsRecordState.Voided, All),
			// Cancel: terminal from any non-finalized state.
			new Transition(RmsRecordState.Draft, RmsRecordState.Cancelled, All),
			new Transition(RmsRecordState.ReadyForReview, RmsRecordState.Cancelled, ReviewPresets),
			new Transition(RmsRecordState.Returned, RmsRecordState.Cancelled, ReviewPresets),
			new Transition(RmsRecordState.Approved, RmsRecordState.Cancelled, ApprovalOnly),
			// Reporting destination states (RMS-2); only definitions that declare a destination use them.
			new Transition(RmsRecordState.Finalized, RmsRecordState.Submitted, All),
			new Transition(RmsRecordState.Amended, RmsRecordState.Submitted, All),
			new Transition(RmsRecordState.Submitted, RmsRecordState.Accepted, All),
			new Transition(RmsRecordState.Submitted, RmsRecordState.Rejected, All),
			new Transition(RmsRecordState.Rejected, RmsRecordState.Corrected, All),
			new Transition(RmsRecordState.Corrected, RmsRecordState.Submitted, All),
			new Transition(RmsRecordState.Accepted, RmsRecordState.Amended, All),
			new Transition(RmsRecordState.Rejected, RmsRecordState.Amended, All)
		};

		public static bool CanTransition(RmsLifecyclePreset preset, RmsRecordState from, RmsRecordState to)
		{
			return Table.Any(t => t.From == from && t.To == to && t.Presets.Contains(preset));
		}

		public static IEnumerable<RmsRecordState> NextStates(RmsLifecyclePreset preset, RmsRecordState from)
		{
			return Table.Where(t => t.From == from && t.Presets.Contains(preset)).Select(t => t.To).Distinct();
		}

		public static bool IsTerminal(RmsRecordState state)
		{
			return state == RmsRecordState.Voided || state == RmsRecordState.Cancelled;
		}

		/// <summary>States in which the Record has at least one immutable revision.</summary>
		public static bool IsFinalizedFamily(RmsRecordState state)
		{
			return state == RmsRecordState.Finalized || state == RmsRecordState.Amended || state == RmsRecordState.Voided ||
				   state == RmsRecordState.Submitted || state == RmsRecordState.Accepted || state == RmsRecordState.Rejected || state == RmsRecordState.Corrected;
		}

		/// <summary>The only state that autosaves (Draft), plus Returned which re-opens as Draft on save.</summary>
		public static bool IsEditable(RmsRecordState state)
		{
			return state == RmsRecordState.Draft || state == RmsRecordState.Returned;
		}

		/// <summary>The Workflow trigger a transition emits after commit, or null when it emits none.</summary>
		public static WorkflowTriggerEventType? TriggerFor(RmsRecordState from, RmsRecordState to)
		{
			switch (to)
			{
				case RmsRecordState.ReadyForReview: return WorkflowTriggerEventType.RecordSubmittedForReview;
				case RmsRecordState.Returned: return WorkflowTriggerEventType.RecordReturnedForCorrection;
				case RmsRecordState.Finalized: return WorkflowTriggerEventType.RecordFinalized;
				case RmsRecordState.Amended: return WorkflowTriggerEventType.RecordAmended;
				case RmsRecordState.Voided: return WorkflowTriggerEventType.RecordVoided;
				case RmsRecordState.Cancelled: return WorkflowTriggerEventType.RecordCancelled;
				case RmsRecordState.Submitted: return WorkflowTriggerEventType.RecordSubmissionQueued;
				case RmsRecordState.Accepted: return WorkflowTriggerEventType.RecordSubmissionAccepted;
				case RmsRecordState.Rejected: return WorkflowTriggerEventType.RecordSubmissionRejected;
				default: return null;
			}
		}
	}
}
