using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Narrow query adapter. No operational writer, messaging, checkout, MCP executor or raw-content source is exposed.</summary>
	public sealed class AdminAssistAskQueries(IAdminAssistCatalog catalog, IAdminAssistReferenceSearch search,
		IAdminAssistService assist, IAdminAssistAccessService access, IConfigurationImpactService impacts, TimeProvider clock, IAdminAssistPlanQueries plans) : IAdminAssistAskQueries
	{
		private static readonly ResourceManager Labels = new(typeof(Resgrid.Localization.Areas.User.AdminAssist.AdminAssist));
		private static readonly IReadOnlyDictionary<string, decimal> Empty = new Dictionary<string, decimal>();
		public async Task<IReadOnlyList<AskEvidence>> ReadAsync(AdminAssistActor actor, AskToolInput tool, CancellationToken ct)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			var now = clock.GetUtcNow().UtcDateTime;
			var result = new List<AskEvidence>();
			string Text(string key) => Labels.GetString(key, CultureInfo.GetCultureInfo(actor.Locale ?? "en")) ?? key;
			AskEvidence Card(string id, string kind, string title, IEnumerable<string> texts, string state = "Reference", string destination = null, string revision = null, IReadOnlyDictionary<string, decimal> numbers = null, string publicText = null, string citation = null) =>
				new(id, kind, title, texts.ToArray(), numbers ?? Empty, state, destination, citation ?? id, catalog.Version, revision, now, publicText);
			async Task AddCapability(ProductCapability capability)
			{
				var current = await access.GetCapabilityAsync(actor, capability.Id, ct);
				result.Add(Card("capability:" + capability.Id, "Capability", capability.LabelKey,
					new[] { capability.PurposeKey, capability.ValueKey, capability.AdoptionKey }.Concat(current?.ReasonCodes.Select(c => "Ui." + c) ?? Array.Empty<string>()),
					current?.State.ToString() ?? "Unknown", current?.CanConfigure == true ? current.Destination : null));
			}
			async Task AddSetting(SettingCatalogEntry setting, AdminAssistOverview overview)
			{
				var number = new Dictionary<string, decimal>();
				var state = "Unknown";
				// Only catalog-classified non-secret scalar settings may cross the inference boundary.
				if (!setting.Secret && setting.Classification == "Internal" && setting.Id.StartsWith("setting.", StringComparison.Ordinal) && overview.Report.Snapshot.Consistent)
				{
					var fact = overview.Report.Snapshot.Find(setting.Id.Substring("setting.".Length));
					if (fact.IsFresh(now, TimeSpan.FromMinutes(1))) {
						if (fact.Number.HasValue) { number["Ui.CurrentValue"] = fact.Number.Value; state = "Known"; }
						else if (fact.Boolean.HasValue) { state = fact.Boolean.Value ? "True" : "False"; }
					}
				}
				var candidates = catalog.Capabilities.Where(c => c.Location.Controller == setting.Location.Controller && c.Location.Action == setting.Location.Action).ToArray();
				string destination = null;
				foreach (var candidate in candidates) if ((await access.GetCapabilityAsync(actor, candidate.Id, ct))?.CanConfigure == true) { destination = setting.Location.Url; break; }
				result.Add(Card("setting:" + setting.Id, "Setting", setting.LabelKey, new[] { setting.HelpKey, setting.Impact.OperationKey, setting.Impact.TimingKey, setting.Impact.VerificationKey }, state, destination, overview.Report.Snapshot.Revision, number));
			}
			switch (tool.Name)
			{
				case "draft_plan":
				{
					var template = Resgrid.AdminAssist.ChangePlanPolicy.Templates(catalog).SingleOrDefault(t => t.Id == tool.Id) ?? throw new ArgumentException("Select a reviewed template.");
					var draft = await plans.DraftAsync(actor, new PlanDraftRequest(Text(template.LabelKey), template.Id), ct);
					foreach (var step in draft.Steps.Take(8)) result.Add(Card("draft:" + template.Id + ":" + step.Change.Id, "PlanDraft", step.LabelKey, new[] { step.RationaleKey, step.InstructionsKey, "Plan.Boundary" }, "Proposed", step.Destination, draft.SnapshotRevision));
					break;
				}
				case "verify_step":
				{
					var check = await plans.VerifyStepAsync(actor, tool.Id, tool.Value, ct);
					result.Add(Card("verify:" + tool.Id + ":" + tool.Value, "PlanVerification", "Plan.Verification", new[] { "Plan." + check.Saved, "Plan." + check.Rule, "Plan." + check.Propagated, "Plan." + check.BehaviorTested, "Plan.VerificationBoundary" }, check.Saved, revision: check.EvidenceDigest));
					break;
				}
				case "search_reference": case "search_docs":
					if (string.IsNullOrWhiteSpace(tool.Query) || tool.Query.Length > 256) throw new ArgumentException("Invalid query.");
					foreach (var hit in search.Search(tool.Query, actor.Locale, 3)) {
						var article = catalog.Articles.Single(a => a.Id == hit.Id);
						result.Add(Card("doc:" + hit.Id, "Documentation", hit.TitleKey, Array.Empty<string>(), publicText: article.Body.Length > 900 ? article.Body.Substring(0, 900) : article.Body, citation: article.Id + "#" + article.Anchor));
					}
					break;
				case "search_capabilities":
					if (string.IsNullOrWhiteSpace(tool.Query) || tool.Query.Length > 256) throw new ArgumentException("Invalid query.");
					foreach (var capability in catalog.Capabilities.Where(c => (Text(c.LabelKey) + " " + Text(c.PurposeKey)).Contains(tool.Query, StringComparison.CurrentCultureIgnoreCase) || c.Id.Equals(tool.Query, StringComparison.Ordinal)).Take(5)) await AddCapability(capability);
					break;
				case "explain_capability_access":
					await AddCapability(catalog.Capabilities.SingleOrDefault(c => c.Id == tool.Id) ?? throw new ArgumentException("Unknown capability.")); break;
				case "compare_addon_capabilities":
					if (tool.Ids == null || tool.Ids.Count is < 1 or > 5) throw new ArgumentException("Invalid add-ons.");
					foreach (var id in tool.Ids) await AddCapability(catalog.Capabilities.SingleOrDefault(c => c.Id == id && c.Id.StartsWith("addon-", StringComparison.Ordinal)) ?? throw new ArgumentException("Unknown add-on."));
					break;
				case "get_setting": case "get_section": case "get_permissions":
				{
					IEnumerable<SettingCatalogEntry> settings;
					if (tool.Name == "get_setting") settings = new[] { catalog.Settings.SingleOrDefault(s => s.Id == tool.Id) ?? throw new ArgumentException("Unknown setting.") };
					else if (tool.Name == "get_section") { if (!catalog.Areas.Any(a => a.Id == tool.Id)) throw new ArgumentException("Unknown area."); settings = catalog.Settings.Where(s => s.AreaId == tool.Id).Take(5); }
					else { if (tool.Id != "all" && !catalog.Settings.Any(s => s.Id == tool.Id && s.Binding.StartsWith("PermissionTypes."))) throw new ArgumentException("Unknown permission."); settings = catalog.Settings.Where(s => s.Binding.StartsWith("PermissionTypes.") && (tool.Id == "all" || s.Id == tool.Id)).Take(5); }
					var overview = await assist.GetOverviewAsync(actor, false, ct);
					foreach (var setting in settings) await AddSetting(setting, overview);
					break;
				}
				case "get_findings": case "get_setup_report": case "get_setup_next_steps": case "get_operating_profile":
				{
					var overview = await assist.GetOverviewAsync(actor, false, ct);
					var report = overview.Report;
					if (tool.Name == "get_setup_report") result.Add(Card("setup:report", "Setup", "Ui.report", new[] { "Ui.ReportBoundary" }, report.Snapshot.Consistent ? "Known" : "Unknown", revision: report.Snapshot.Revision,
						numbers: new Dictionary<string, decimal> { ["Ui.Required"] = report.Required, ["Ui.Verified"] = report.Verified, ["Ui.Failures"] = report.Failed, ["Ui.UnknownChecks"] = report.Unknown }));
					else if (tool.Name == "get_setup_next_steps") foreach (var task in (overview.SetupPlan?.Tasks ?? Array.Empty<SetupTask>()).Where(t => t.State is not ("Learned" or "ChecksPassed")).Take(5))
						result.Add(Card("task:" + task.Id, "SetupTask", catalog.Capabilities.Single(c => c.Id == task.CapabilityId).LabelKey, new[] { task.LabelKey, task.GuidanceKey }, task.State, task.Destination, report.Snapshot.Revision));
					else if (tool.Name == "get_operating_profile") {
						var fact = report.Snapshot.Find("operatingPackIds");
						if (fact.IsFresh(now, TimeSpan.FromMinutes(1))) foreach (var pack in catalog.Packs.Where(p => (fact.Code ?? "").Split(',').Contains(p.Id))) result.Add(Card("pack:" + pack.Id, "OperatingPack", pack.LabelKey, new[] { pack.PurposeKey }.Concat(pack.PrerequisiteKeys), "Reference", revision: report.Snapshot.Revision));
					}
					else {
						if (tool.Id is not ("baseline" or "security" or "dispatch")) throw new ArgumentException("Unknown profile.");
						foreach (var finding in report.Findings.Where(f => tool.Id == "baseline" || f.AreaId == (tool.Id == "dispatch" ? "calls" : "security")).OrderByDescending(f => f.Severity).Take(5))
							result.Add(Card("finding:" + finding.RuleId, "Finding", finding.TitleKey, new[] { finding.ExplanationKey, finding.NextActionKey }, finding.Result.ToString(), revision: report.Snapshot.Revision));
					}
					break;
				}
				case "get_recent_changes":
					if (tool.WindowDays is < 1 or > 30) throw new ArgumentException("Invalid window.");
					foreach (var change in (await assist.GetHistoryAsync(actor, 0, 30, ct)).Where(h => h.Source == "Configuration" && h.OccurredOnUtc >= now.AddDays(-tool.WindowDays)).Take(5))
						result.Add(Card("change:" + change.Id, "History", "Ui.history", new[] { "Ui.AskHistoryScope" }, "Recorded", revision: change.Revision.ToString(CultureInfo.InvariantCulture)));
					break;
				case "evaluate_impact":
				{
					var setting = catalog.Settings.SingleOrDefault(s => s.Id == tool.Id && !s.Secret && s.Classification == "Internal") ?? throw new ArgumentException("Unsupported setting.");
					var overview = await assist.GetOverviewAsync(actor, false, ct);
					var preview = await impacts.PreviewAsync(actor, new ConfigurationImpactRequest(setting.Id, overview.Report.Snapshot.Revision, bool.TryParse(tool.Value, out var boolean) ? boolean : null, decimal.TryParse(tool.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null), ct);
					result.Add(Card("impact:" + setting.Id, "Impact", setting.LabelKey, preview.LimitKeys, "Preview", preview.Destination, overview.Report.Snapshot.Revision));
					foreach (var metric in preview.Metrics.Take(5)) {
						var numbers = new Dictionary<string, decimal>();
						if (metric.Before.HasValue) numbers["Ui.Before"] = metric.Before.Value;
						if (metric.After.HasValue) numbers["Ui.After"] = metric.After.Value;
						result.Add(Card("impact:" + setting.Id + ":" + metric.LabelKey, "ImpactMetric", metric.LabelKey, preview.LimitKeys, metric.State.ToString(), preview.Destination, preview.SnapshotRevision, numbers));
					}
					break;
				}
				default: throw new ArgumentException("Unsupported read tool.");
			}
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			return result;
		}
	}
}
