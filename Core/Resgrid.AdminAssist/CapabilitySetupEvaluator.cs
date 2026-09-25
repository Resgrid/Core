using System;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Learning, purchase, recorded configuration and supported verification remain separate dimensions.</summary>
	public static class CapabilitySetupEvaluator
	{
		public static CapabilitySetupAssessment Evaluate(ProductCapability capability, CapabilityAccess access, ConfigurationReport report, DateTime now, TimeSpan freshness)
		{
			var state = CapabilitySetupState.NotAssessed;
			var definition = capability.Setup;
			var accessFresh = access != null && access.AsOfUtc <= now && now - access.AsOfUtc <= freshness;
			if (definition != null && accessFresh && access.State == EvidenceState.Known && report.Snapshot.Consistent)
			{
				var fact = report.Snapshot.Find(definition.EvidenceId);
				if (fact.IsFresh(now, freshness) && fact.Number.HasValue && fact.Number >= 0)
				{
					state = fact.Number < definition.Minimum ? CapabilitySetupState.NotConfigured : CapabilitySetupState.ConfigurationPresent;
					if (state == CapabilitySetupState.ConfigurationPresent && definition.RuleIds.Count > 0)
					{
						var checks = definition.RuleIds.Select(id => report.Findings.SingleOrDefault(f => f.RuleId == id)).ToArray();
						bool current(ConfigurationFinding check) => check != null && check.SnapshotRevision == report.Snapshot.Revision &&
							check.EvaluatedOnUtc <= now && now - check.EvaluatedOnUtc <= freshness;
						if (checks.Any(check => current(check) && check.Result == RuleResult.Fail)) state = CapabilitySetupState.NeedsAttention;
						// N/A, missing or stale checks do not verify a capability. No data-usage or delivery claim.
						else if (checks.All(check => current(check) && check.Result == RuleResult.Pass)) state = CapabilitySetupState.ChecksPassed;
					}
				}
			}
			var addon = capability.Requirements.Any(r => r.Kind == "addon");
			var opportunity = capability.ReleaseStatus is not ("available" or "preview") ? "Ui.OpportunityPlanned" :
				!accessFresh || addon && (access.CommercialState == null || access.CommercialState == EvidenceState.Unknown || access.CommercialState == EvidenceState.Redacted) ? "Ui.OpportunityUnknown" :
				addon && access.CommercialState == EvidenceState.Unavailable ? "Ui.OpportunityNotOwned" :
				state == CapabilitySetupState.NotConfigured ? addon ? "Ui.OpportunitySubscribedNotConfigured" : "Ui.OpportunityIncludedNotConfigured" :
				addon ? "Ui.OpportunitySubscribed" : "Ui.OpportunityNoAddon";
			return new(capability.Id, state, opportunity, definition?.GuidanceKey ?? "Ui.ConfigurationNotAssessed",
				definition?.RuleIds ?? Array.Empty<string>(), report.Snapshot.Revision, report.Snapshot.AsOfUtc);
		}
	}
}
