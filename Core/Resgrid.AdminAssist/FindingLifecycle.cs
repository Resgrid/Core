using System;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	public static class FindingLifecycle
	{
		/// <summary>Unknown evidence cannot resolve a finding; acknowledgement and exceptions never change rule results.</summary>
		public static WorkflowTriggerEventType? Observe(AdminAssistFindingRow row, ConfigurationFinding finding, DateTime now)
		{
			var previous = (RuleResult)row.Result;
			var first = row.Revision == 0;
			WorkflowTriggerEventType? transition = null;
			if (finding.Result == RuleResult.Fail && (row.Episode == 0 || previous == RuleResult.Pass || row.ReviewStatus == (int)FindingReviewStatus.Resolved))
			{
				transition = row.Episode == 0 ? WorkflowTriggerEventType.AdminAssistFindingOpened : WorkflowTriggerEventType.AdminAssistFindingReopened;
				row.Episode++;
				row.ReviewStatus = (int)(row.OwnerId == null ? FindingReviewStatus.Unassigned : FindingReviewStatus.Assigned);
				row.ExceptionUntil = null;
			}
			else if (!first && finding.Result == RuleResult.Pass && row.ReviewStatus != (int)FindingReviewStatus.Resolved && row.Episode > 0)
			{
				transition = WorkflowTriggerEventType.AdminAssistFindingResolved;
				row.ReviewStatus = (int)FindingReviewStatus.Resolved;
				row.ExceptionUntil = null;
			}
			// Keep an expired exception pending while evidence is unknown: it is no longer an active
			// exception, but its next verified failure must still produce exactly one reopened episode.
			if (row.ReviewStatus == (int)FindingReviewStatus.AcceptedException && row.ExceptionUntil <= now && finding.Result == RuleResult.Fail)
			{
				row.ReviewStatus = (int)(row.OwnerId == null ? FindingReviewStatus.Unassigned : FindingReviewStatus.Assigned);
				row.ExceptionUntil = null;
				if (finding.Result == RuleResult.Fail) { transition = WorkflowTriggerEventType.AdminAssistFindingReopened; row.Episode++; }
			}
			row.Result = (int)finding.Result;
			row.Severity = (int)finding.Severity;
			row.SnapshotRevision = finding.SnapshotRevision;
			if (first) row.FirstObservedOn = now;
			row.LastObservedOn = now;
			row.Revision++;
			return transition;
		}
	}
}
