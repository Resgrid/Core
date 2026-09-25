using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Uses the owning status query's actual one-hour reset filter. Preview never creates a status row.</summary>
	public sealed class StatusAutomationImpactProvider(IActionLogsRepository actions, IAuthorizationService visibility,
		IRecordsAuthorizationService membership) : IOperationalImpactProvider, IComposedOperationalImpactProvider
	{
		public bool AppliesTo(IReadOnlyList<string> settingIds) => settingIds.Any(Supports);
		public Task<OperationalImpact> EvaluateComposedAsync(AdminAssistActor actor, ConfigurationSnapshot before, ConfigurationSnapshot after, IReadOnlyList<string> settingIds, CancellationToken ct) =>
			EvaluateAsync(actor, before, new("setting.DisabledAutoAvailable", before.Revision, Boolean: after.Find("DisabledAutoAvailable").Boolean), ct);
		public bool Supports(string settingId) => settingId == "setting.DisabledAutoAvailable";
		public async Task<OperationalImpact> EvaluateAsync(AdminAssistActor actor, ConfigurationSnapshot snapshot, ConfigurationImpactRequest request, CancellationToken ct)
		{
			if (!Supports(request.SettingId) || !request.Boolean.HasValue) throw new ArgumentException("A status-automation proposal is required.");
			OperationalImpact Unknown(EvidenceState state) => new(new[] { new ConfigurationImpactMetric("Impact.StatusChanges", state, null, null, "SourceUnavailable") }, new[] { "Impact.StatusScope", "Impact.StatusEvidenceUnavailable" }, "auto-available-v1");
			var current = snapshot.Find("DisabledAutoAvailable");
			if (!current.IsFresh(snapshot.AsOfUtc, TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300))) || !current.Boolean.HasValue) return Unknown(EvidenceState.Unknown);
			try
			{
				var limit = Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000);
				async Task<Dictionary<string, ActionLog>> Read(bool disabled)
				{
					var rows = await actions.ReadLatestForAdministrationAsync(actor.DepartmentId, disabled, snapshot.AsOfUtc, limit, ct) ?? throw new InvalidOperationException();
					if (rows.Count > limit || rows.Any(r => r.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
					return rows.ToDictionary(r => r.UserId, StringComparer.Ordinal);
				}
				var before = await Read(current.Boolean.Value);
				var after = current.Boolean.Value == request.Boolean.Value ? before : await Read(request.Boolean.Value);
				var ids = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).ToArray();
				int changed = 0, standingByBefore = 0, standingByAfter = 0;
				foreach (var id in ids)
				{
					ct.ThrowIfCancellationRequested();
					if (!await membership.IsAssignableMemberAsync(id, actor.DepartmentId) || !await visibility.CanUserViewPersonAsync(actor.UserId, id, actor.DepartmentId)) throw new UnauthorizedAccessException();
					var was = before.TryGetValue(id, out var old) ? old.ActionTypeId : (int)ActionTypes.StandingBy;
					var next = after.TryGetValue(id, out var proposed) ? proposed.ActionTypeId : (int)ActionTypes.StandingBy;
					if (was != next) changed++;
					if (was == (int)ActionTypes.StandingBy) standingByBefore++;
					if (next == (int)ActionTypes.StandingBy) standingByAfter++;
				}
				return new(new[] {
					new ConfigurationImpactMetric("Impact.StatusChanges", EvidenceState.Known, 0, changed),
					new ConfigurationImpactMetric("Impact.StatusStandingBy", EvidenceState.Known, standingByBefore, standingByAfter),
					new ConfigurationImpactMetric("Impact.StatusSample", EvidenceState.Known, ids.Length, ids.Length)
				}, new[] { "Impact.StatusScope", "Impact.StatusTiming" }, "auto-available-v1");
			}
			catch (OperationCanceledException) { throw; }
			catch (UnauthorizedAccessException) { return Unknown(EvidenceState.Redacted); }
			catch (Exception) { return Unknown(EvidenceState.Unknown); }
		}
	}
}
