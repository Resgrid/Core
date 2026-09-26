using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Resgrid.Model.AdminAssist;

namespace Resgrid.AdminAssist
{
	/// <summary>Versioned proposals, dependency ordering and pure intermediate-state evaluation. Never writes configuration.</summary>
	public static class ChangePlanPolicy
	{
		public const string Version = "configuration-change-set-v1";
		public const int MaximumSteps = 8;
		public static string Digest(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
		public static bool IsId(string id) => id?.Length is >= 1 and <= 128 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
		public static string Value(ConfigurationEvidence fact, DateTime now) => !fact.IsFresh(now, TimeSpan.FromMinutes(1)) ? "Unknown" :
			fact.Boolean.HasValue ? (fact.Boolean.Value ? "true" : "false") : fact.Number?.ToString(CultureInfo.InvariantCulture) ?? "Unknown";
		public static string Proposed(ConfigurationChange change) => change.Boolean.HasValue ? (change.Boolean.Value ? "true" : "false") : change.Number?.ToString(CultureInfo.InvariantCulture) ?? "Configured";
		public static IReadOnlyList<PlanTemplate> Templates(IAdminAssistCatalog catalog)
		{
			ConfigurationChange Step(string id, string setting, bool? b = null, decimal? n = null, params string[] depends) => new(id, "setting." + setting, b, n, depends);
			PlanTemplate Template(string id, params ConfigurationChange[] changes) => new(id, "Plan.Template." + id, "Plan.TemplateHelp." + id, new(Version, "goal", id, changes));
			var result = new List<PlanTemplate> {
				Template("map-freshness", Step("personnel", "MappingPersonnelLocationTTL", n: 15), Step("units", "MappingUnitLocationTTL", n: 15)),
				Template("admin-security", Step("admin-mfa", "Require2FAForAdmins", n: 1)),
				Template("shift-routing", Step("shift-routing", "DispatchShiftInsteadOfGroup", true), Step("auto-status", "AutoSetStatusForShiftDispatchPersonnel", false, null, "shift-routing"))
			};
			foreach (var pack in catalog.Packs)
			{
				var changes = catalog.Capabilities.Where(c => c.ReleaseStatus == "available" && c.Setup != null && pack.AreaIds.Contains(c.AreaId))
					.OrderBy(c => c.Id, StringComparer.Ordinal).Take(MaximumSteps).Select(c => new ConfigurationChange(c.Id, c.Id, null, null, Array.Empty<string>())).ToArray();
				if (changes.Length > 0) result.Add(new("pack-" + pack.Id, pack.LabelKey, pack.PurposeKey, new(Version, "archetype", pack.Id, changes)));
			}
			return result;
		}
		public static PlanDraftRequest Normalize(PlanDraftRequest request, IAdminAssistCatalog catalog)
		{
			if (request == null || request.Goal?.Trim().Length is not (>= 1 and <= 1000) || request.Goal.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) throw new ArgumentException("A bounded goal is required.");
			if (request.TemplateId != null && request.ChangeSet != null) throw new ArgumentException("Select a template or a manual change set.");
			var set = request.TemplateId == null ? request.ChangeSet : Templates(catalog).SingleOrDefault(t => t.Id == request.TemplateId)?.ChangeSet;
			if (set == null || set.Version != Version || set.Source is not ("goal" or "manual" or "finding" or "conversation" or "archetype") ||
				set.SourceId?.Length > 128 || set.Changes?.Count is not (>= 1 and <= MaximumSteps)) throw new ArgumentException("Invalid change set.");
			if (set.Changes.Any(c => c == null || !IsId(c.Id) || !IsId(c.CatalogId) || c.Prerequisites == null || c.Prerequisites.Count > MaximumSteps) ||
				set.Changes.Select(c => c.Id).Distinct().Count() != set.Changes.Count || set.Changes.Select(c => c.CatalogId).Distinct().Count() != set.Changes.Count) throw new ArgumentException("Duplicate or invalid steps.");
			foreach (var change in set.Changes)
			{
				if (change.Prerequisites.Distinct().Count() != change.Prerequisites.Count || change.Prerequisites.Any(id => id == change.Id || !set.Changes.Any(c => c.Id == id))) throw new ArgumentException("Invalid prerequisite.");
				if (change.CatalogId.StartsWith("setting.", StringComparison.Ordinal))
				{
					if (!ConfigurationImpactEvaluator.Supports(change.CatalogId) || !catalog.Settings.Any(s => s.Id == change.CatalogId && !s.Secret && s.Classification == "Internal")) throw new ArgumentException("Unsupported setting.");
					var key = change.CatalogId.Substring(8);
					if (ConfigurationImpactEvaluator.BooleanSettings.Contains(key)) { if (!change.Boolean.HasValue || change.Number.HasValue) throw new ArgumentException("Boolean required."); }
					else if (change.Boolean.HasValue || !change.Number.HasValue || change.Number < 0 || change.Number > (key == "Require2FAForAdmins" ? 2 : 525600) || decimal.Truncate(change.Number.Value) != change.Number) throw new ArgumentException("Invalid number.");
				}
				else if (change.Boolean.HasValue || change.Number.HasValue || !catalog.Capabilities.Any(c => c.Id == change.CatalogId && c.ReleaseStatus == "available")) throw new ArgumentException("Invalid guided task.");
			}
			var ordered = new List<ConfigurationChange>();
			while (ordered.Count < set.Changes.Count)
			{
				var next = set.Changes.FirstOrDefault(c => !ordered.Contains(c) && c.Prerequisites.All(id => ordered.Any(o => o.Id == id)));
				if (next == null) throw new ArgumentException("Cyclic prerequisites."); ordered.Add(next);
			}
			if (request.DispatchScenario != null && (request.DispatchScenario.CallId <= 0 || request.DispatchScenario.SimulationTimeUtc.Kind != DateTimeKind.Utc || !set.Changes.Any(c => c.CatalogId is "setting.DispatchShiftInsteadOfGroup" or "setting.AutoSetStatusForShiftDispatchPersonnel"))) throw new ArgumentException("Invalid routing scenario.");
			return new(request.Goal.Trim(), ChangeSet: set with { Changes = ordered }, DispatchScenario: request.DispatchScenario);
		}
		public static ConfigurationSnapshot Overlay(ConfigurationSnapshot snapshot, ConfigurationChange change, DateTime now)
		{
			if (!change.CatalogId.StartsWith("setting.", StringComparison.Ordinal)) return snapshot;
			var values = snapshot.Evidence.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
			var id = change.CatalogId.Substring(8);
			values[id] = snapshot.Find(id) with { State = EvidenceState.Known, Boolean = change.Boolean, Number = change.Number, Code = null, AsOfUtc = now, Source = "InMemoryProposal" };
			if (id == "MappingUseMapboxOverride" && change.Boolean == false)
				foreach (var key in new[] { "mapTokenPresent", "mapStylePresent" }) values[key] = snapshot.Find(key) with { State = EvidenceState.Known, Boolean = false, AsOfUtc = now, Source = "InMemoryProposal" };
			return snapshot with { Evidence = values };
		}
		public static IReadOnlyList<ConfigurationImpactRule> RuleDelta(IAdminAssistCatalog catalog, ConfigurationSnapshot before, ConfigurationSnapshot after, DateTime now) =>
			catalog.Rules.Select(r => new ConfigurationImpactRule(r.Id, r.TitleKey, new ConfigurationRule(r).Evaluate(before, now, TimeSpan.FromMinutes(1)).Result,
				new ConfigurationRule(r).Evaluate(after, now, TimeSpan.FromMinutes(1)).Result)).Where(r => r.Before != r.After).ToArray();
		public static string HighestRisk(IEnumerable<string> risks) => risks.OrderByDescending(r => r switch { "Critical" => 4, "High" => 3, "Medium" => 2, _ => 1 }).FirstOrDefault() ?? "Unknown";
		public static string State(string stored, string current, string proposed, string initial, PlanVerification verification, bool prerequisitesDone)
		{
			if (stored is "Skipped" or "Superseded") return stored;
			if (current != "Unknown" && current != proposed && current != initial) return "Drifted";
			if (verification.Saved == "Confirmed" && verification.Rule == "Pass" && verification.Propagated == "HumanConfirmed" && verification.BehaviorTested == "HumanConfirmed" && prerequisitesDone) return "Done";
			if (stored == "Done") return "Drifted";
			return stored == "InProgress" || verification.Saved == "Confirmed" ? "InProgress" : "Proposed";
		}
	}
}
