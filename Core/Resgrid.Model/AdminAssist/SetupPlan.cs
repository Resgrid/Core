using System;
using System.Collections.Generic;

namespace Resgrid.Model.AdminAssist
{
	public sealed record SetupTask(string Id, string CapabilityId, string LabelKey, string GuidanceKey,
		string State, string Destination, IReadOnlyList<string> RuleIds);
	public sealed record SetupEffort(string AreaId, int MinimumMinutes, int MaximumMinutes, string AssumptionKey);
	public sealed record SetupPlan(IReadOnlyList<SetupTask> Tasks, IReadOnlyList<SetupEffort> Effort,
		bool RevisitDue, DateTime? RevisitOnUtc, string SnapshotRevision);
}
