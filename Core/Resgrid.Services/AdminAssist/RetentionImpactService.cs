using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Prospective policy preview, preserving the same historical policy boundaries as the owning editor.</summary>
	public sealed class RetentionImpactService(IAdminAssistAccessService access, IAdminAssistRepository repository,
		IAdminAssistCatalog catalog, IDepartmentSettingsRepository settings, IRetentionImpactStore store, TimeProvider clock,
		IRecordsAuthorizationService authorization, IRecordsCutoverService cutover) : IRetentionImpactService
	{
		public async Task<ConfigurationImpactReport> PreviewAsync(AdminAssistActor actor, RetentionImpactRequest request, CancellationToken ct = default)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
			if (request == null || request.ProposedDefaultYears is < 0 or > 1000) throw new ArgumentException("Invalid retention proposal.");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.SnapshotTimeoutSeconds, 1, 60)));
			ct = timeout.Token;
			var revision = (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture);
			if (revision != request.ExpectedRevision) throw new AdminAssistConcurrencyException();
			var now = clock.GetUtcNow().UtcDateTime;
			ConfigurationImpactMetric[] metrics;
			try
			{
				if (!await authorization.HasPermissionAsync(actor.UserId, actor.DepartmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
				if ((await cutover.GetModuleStateAsync(actor.DepartmentId, true).WaitAsync(ct))?.FlagEnabled != true) throw new UnauthorizedAccessException();
				var raw = await ReadPolicyAsync(actor.DepartmentId, ct);
				var policy = string.IsNullOrEmpty(raw) ? new RecordsRetentionPolicy() : ObjectSerialization.Deserialize<RecordsRetentionPolicy>(raw) ?? throw new InvalidOperationException();
				var proposed = new RecordsRetentionPolicy { DepartmentDefaultYears = request.ProposedDefaultYears,
					Overrides = policy.Overrides?.Select(o => new RecordsRetentionOverride { DefinitionKey = o.DefinitionKey, RetentionYears = o.RetentionYears, AppliesFrom = o.AppliesFrom }).ToList() };
				proposed.PreserveHistory(policy, now);
				var bound = Math.Min(250, Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000));
				var rows = (await store.ReadRetentionHeadersAsync(actor.DepartmentId, bound, ct))?.ToArray() ?? throw new InvalidOperationException();
				if (rows.Length > 2 * (bound + 1) || rows.Any(r => r == null || string.IsNullOrEmpty(r.RecordId) || r.FinalizedOn > now || r.ModifiedOn > now ||
					r.Kind != (int)RmsRecordKind.Operational && r.Kind != (int)RmsRecordKind.IncidentReport)) throw new InvalidOperationException();
				foreach (var row in rows)
					if (!await authorization.CanUserViewRecordAsync(actor.UserId, row.RecordId, actor.DepartmentId)) throw new UnauthorizedAccessException();
				if (raw != await ReadPolicyAsync(actor.DepartmentId, ct) || JsonConvert.SerializeObject(rows) != JsonConvert.SerializeObject(await store.ReadRetentionHeadersAsync(actor.DepartmentId, bound, ct))) throw new AdminAssistConcurrencyException();
				var complete = rows.GroupBy(r => r.Kind).All(g => g.Count() <= bound);
				var sample = rows.GroupBy(r => r.Kind).SelectMany(g => g.Take(bound)).ToArray();
				bool Window(RetentionImpactHeader row, RecordsRetentionPolicy value) => row.AmendsRevisionId == null && row.ModifiedOn <= now.AddHours(-25) &&
					new[] { RmsRecordState.Finalized, RmsRecordState.Amended, RmsRecordState.Accepted, RmsRecordState.Voided, RmsRecordState.Cancelled }.Contains((RmsRecordState)row.State) &&
					RecordsRetentionWindow.HasExpired(row.FinalizedOn, row.FinalizedOn.HasValue ? value.ResolveYears(row.DefinitionKey, row.FinalizedOn.Value) : 0, now);
				var before = sample.Where(r => Window(r, policy)).ToArray(); var after = sample.Where(r => Window(r, proposed)).ToArray();
				metrics = new[] {
					new ConfigurationImpactMetric("Impact.RetentionSample", EvidenceState.Known, sample.Length, sample.Length),
					new ConfigurationImpactMetric("Impact.RetentionFullPopulation", EvidenceState.Known, complete ? 1 : 0, complete ? 1 : 0),
					new ConfigurationImpactMetric("Impact.RetentionDefault", EvidenceState.Known, policy.DepartmentDefaultYears ?? RecordsRetentionPolicy.StandardClassDefaultYears, request.ProposedDefaultYears ?? RecordsRetentionPolicy.StandardClassDefaultYears),
					new ConfigurationImpactMetric("Impact.RetentionExpired", EvidenceState.Known, before.Length, after.Length),
					new ConfigurationImpactMetric("Impact.RetentionHeld", EvidenceState.Known, before.Count(r => r.HoldOrPermanentContent != 0), after.Count(r => r.HoldOrPermanentContent != 0)),
					new ConfigurationImpactMetric("Impact.RetentionHoldUnknown", EvidenceState.Known, before.Count(r => r.HoldOrPermanentContent == 0 && r.HistoricalHoldUncertainty != 0), after.Count(r => r.HoldOrPermanentContent == 0 && r.HistoricalHoldUncertainty != 0)),
					new ConfigurationImpactMetric("Impact.RetentionCandidates", EvidenceState.Known, before.Count(r => r.HoldOrPermanentContent == 0 && r.HistoricalHoldUncertainty == 0), after.Count(r => r.HoldOrPermanentContent == 0 && r.HistoricalHoldUncertainty == 0)),
					new ConfigurationImpactMetric("Impact.RetentionActualEligibility", EvidenceState.Unknown, null, null, "OwningLifecycleDependenciesNotEvaluated") };
			}
			catch (AdminAssistConcurrencyException) { throw; }
			catch (UnauthorizedAccessException) { throw; }
			catch (OperationCanceledException) { throw; }
			catch (Exception) { metrics = new[] { new ConfigurationImpactMetric("Impact.RetentionCandidates", EvidenceState.Unknown, null, null, "SourceUnavailable") }; }
			if ((await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(CultureInfo.InvariantCulture) != revision) throw new AdminAssistConcurrencyException();
			if ((await cutover.GetModuleStateAsync(actor.DepartmentId, true).WaitAsync(ct))?.FlagEnabled != true || !await access.CanAccessAsync(actor, false, ct) || !await authorization.HasPermissionAsync(actor.UserId, actor.DepartmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			var entry = catalog.Settings.Single(s => s.Id == "setting.RecordsRetentionPolicy");
			return new(entry.Id, revision, now, "retention-window-v1", entry.Impact, metrics, Array.Empty<ConfigurationImpactRule>(),
				new[] { "Impact.NoMutation", "Impact.RetentionHistory", "Impact.RetentionLimits", "Impact.RetentionProtection", "Impact.Window" }, entry.Location.Url);
		}
		private async Task<string> ReadPolicyAsync(int departmentId, CancellationToken ct)
		{
			var rows = (await settings.GetAllByDepartmentIdAsync(departmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			if (rows.Count > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000) || rows.Any(r => r.DepartmentId != departmentId)) throw new InvalidOperationException();
			return rows.SingleOrDefault(r => r.SettingType == (int)DepartmentSettingTypes.RecordsRetentionPolicy)?.Setting;
		}
	}
}
