using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Side-effect-free scalar overlays and health-rule deltas. Unmeasured operational effects remain explicit.</summary>
	public sealed class ConfigurationImpactEvaluator(IAdminAssistCatalog catalog)
	{
		// Deliberate allowlist, not arbitrary reflection or client-supplied evidence.
		public static readonly IReadOnlyList<string> BooleanSettings = Array.AsReadOnly(new[] {
			"DispatchShiftInsteadOfGroup", "AutoSetStatusForShiftDispatchPersonnel", "DisabledAutoAvailable", "EnableTextToCall", "EnableTextCommand",
			"MappingUseMapboxOverride", "CheckInTimersAutoEnableForNewCalls", "WeatherAlertsEnabled", "RequirePasswordResetViaEmail",
			"MappingPersonnelAllowStatusWithNoLocationToOverwrite", "MappingUnitAllowStatusWithNoLocationToOverwrite"
		});
		public static readonly IReadOnlyList<string> NumberSettings = Array.AsReadOnly(new[] {
			"Require2FAForAdmins", "MappingPersonnelLocationTTL", "MappingUnitLocationTTL"
		});
		public static bool Supports(string settingId) => settingId != null && settingId.StartsWith("setting.", StringComparison.Ordinal) &&
			BooleanSettings.Concat(NumberSettings).Contains(settingId.Substring(8), StringComparer.Ordinal);

		public ConfigurationImpactReport Evaluate(ConfigurationSnapshot snapshot, ConfigurationImpactRequest request, DateTime now, TimeSpan maximumAge)
		{
			if (request == null || !Supports(request.SettingId)) throw new ArgumentException("This setting has no scalar preview.");
			if (!snapshot.Consistent || snapshot.Revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var setting = catalog.Settings.Single(s => s.Id == request.SettingId);
			var id = request.SettingId.Substring(8);
			var original = snapshot.Find(id);
			if (BooleanSettings.Contains(id))
			{
				if (!request.Boolean.HasValue || request.Number.HasValue) throw new ArgumentException("A Boolean proposal is required.");
			}
			else if (request.Boolean.HasValue || !request.Number.HasValue || request.Number < 0 || request.Number > (id == "Require2FAForAdmins" ? 2 : 525600) || decimal.Truncate(request.Number.Value) != request.Number)
				throw new ArgumentException("The proposed value is outside the supported range.");

			var values = snapshot.Evidence.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
			// A proposal is known; its current value and dependencies can still be unknown.
			values[id] = original with { State = EvidenceState.Known, AsOfUtc = now, Source = "InMemoryProposal", Boolean = request.Boolean, Number = request.Number, ReasonCode = null };
			var metrics = new List<ConfigurationImpactMetric>();
			var limits = new List<string> { "Impact.NoMutation", "Impact.Unquantified", "Impact.Window" };
			if (id is "EnableTextToCall" or "EnableTextCommand") limits.Add("Impact.TextProviderLimits");
			if (id == "MappingUseMapboxOverride" && request.Boolean == false)
			{
				// The existing editor deletes these values when the override is disabled. Model that composed effect.
				foreach (var presence in new[] { "mapTokenPresent", "mapStylePresent" })
					values[presence] = snapshot.Find(presence) with { State = EvidenceState.Known, Boolean = false, AsOfUtc = now, Source = "InMemoryProposal" };
				limits.Add("Impact.MapCredentialsRemoved");
			}
			metrics.Add(new ConfigurationImpactMetric("Impact.StoredValue", original.IsFresh(now, maximumAge) ? EvidenceState.Known : original.State == EvidenceState.Redacted ? EvidenceState.Redacted : EvidenceState.Unknown,
				original.IsFresh(now, maximumAge) ? original.Number ?? (original.Boolean.HasValue ? original.Boolean.Value ? 1m : 0m : null) : null,
				request.Number ?? (request.Boolean == true ? 1 : 0), original.IsFresh(now, maximumAge) ? null : "EvidenceNotObserved"));
			if (id == "Require2FAForAdmins")
			{
				var unenrolled = snapshot.Find("adminsWithoutMfa");
				var groupUnenrolled = snapshot.Find("groupOnlyAdminsWithoutMfa");
				decimal? Enrollment(decimal? scope)
				{
					if (!scope.HasValue || scope < 0 || scope > 2) return null;
					if (scope == 0) return 0;
					if (!unenrolled.IsFresh(now, maximumAge) || !unenrolled.Number.HasValue) return null;
					if (scope == 1) return unenrolled.Number;
					return groupUnenrolled.IsFresh(now, maximumAge) && groupUnenrolled.Number.HasValue ? unenrolled.Number + groupUnenrolled.Number : null;
				}
				var before = original.IsFresh(now, maximumAge) ? Enrollment(original.Number) : null;
				var after = Enrollment(request.Number);
				metrics.Add(new ConfigurationImpactMetric("Impact.AdminsRequiringEnrollment", before.HasValue && after.HasValue ? EvidenceState.Known : EvidenceState.Unknown, before, after));
				limits.Add("Impact.MfaRecoveryUnknown");
			}
			var proposed = snapshot with { Evidence = new ReadOnlyDictionary<string, ConfigurationEvidence>(values) };
			var changes = new List<ConfigurationImpactRule>();
			foreach (var definition in catalog.Rules)
			{
				var rule = new ConfigurationRule(definition);
				var before = rule.Evaluate(snapshot, now, maximumAge).Result;
				var after = rule.Evaluate(proposed, now, maximumAge).Result;
				if (before != after) changes.Add(new(definition.Id, definition.TitleKey, before, after));
			}
			return new(setting.Id, snapshot.Revision, snapshot.AsOfUtc, "scalar-impact-v1", setting.Impact,
				metrics.AsReadOnly(), changes.AsReadOnly(), limits.AsReadOnly(), setting.Location.Url);
		}
	}
}
