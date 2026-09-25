using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Read-only adoption checklist. Editorial effort ranges are not completion evidence or service commitments.</summary>
	public static class SetupPlanBuilder
	{
		public static SetupPlan Build(IAdminAssistCatalog catalog, SetupWorkspace workspace,
			IReadOnlyList<CapabilityAccess> access, IReadOnlyList<CapabilitySetupAssessment> assessments, string revision, DateTime now)
		{
			var selected = workspace.Areas.Where(a => a.Value == SetupAreaChoice.UseNow).Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
			var interests = workspace.InterestedCapabilityIds.ToHashSet(StringComparer.Ordinal);
			var tasks = new List<SetupTask>();
			foreach (var capability in catalog.Capabilities.Where(c => interests.Contains(c.Id) || selected.Contains(c.AreaId)))
			{
				var availability = access.SingleOrDefault(a => a.CapabilityId == capability.Id);
				var assessment = assessments.SingleOrDefault(a => a.CapabilityId == capability.Id);
				var fresh = availability != null && availability.AsOfUtc <= now && now - availability.AsOfUtc <= TimeSpan.FromMinutes(1);
				var allowed = fresh && availability is { State: EvidenceState.Known, CanConfigure: true };
				var destination = allowed ? availability?.Destination : null;
				void Add(string step, string guidance, string state, string? link = null) => tasks.Add(new(capability.Id + "." + step,
					capability.Id, "Ui.SetupTask." + step, guidance, state, link, assessment?.RuleIds ?? Array.Empty<string>()));
				Add("Learn", capability.PurposeKey, workspace.LearnedCapabilityIds.Contains(capability.Id) ? "Learned" : "Pending");
				Add("Prerequisites", capability.AdoptionKey, allowed ? "AccessAvailable" : fresh && availability?.State == EvidenceState.Unavailable ? "Blocked" : "Unknown", destination);
				Add("Configure", assessment?.GuidanceKey ?? "Ui.ConfigurationNotAssessed", assessment?.State is CapabilitySetupState.ConfigurationPresent or CapabilitySetupState.ChecksPassed or CapabilitySetupState.NeedsAttention ? "ConfigurationPresent" : "Pending", destination);
				// A lesson, subscription or manually checked box never verifies a feature.
				Add("Verify", assessment?.GuidanceKey ?? "Ui.ConfigurationNotAssessed", allowed && assessment?.State == CapabilitySetupState.ChecksPassed && assessment.SnapshotRevision == revision &&
					assessment.AsOfUtc <= now && now - assessment.AsOfUtc <= TimeSpan.FromMinutes(1) ? "ChecksPassed" : "Unknown", destination);
			}
			var effort = catalog.Areas.Where(a => selected.Contains(a.Id) || catalog.Capabilities.Any(c => c.AreaId == a.Id && interests.Contains(c.Id)))
				.Select(a => new SetupEffort(a.Id, a.Id is "home" or "plans" ? 15 : 30, a.Id is "home" or "plans" ? 45 : 120, "Ui.EffortAssumptions")).ToArray();
			return new(tasks, effort, workspace.RevisitOnUtc <= now, workspace.RevisitOnUtc, revision);
		}
	}
}
