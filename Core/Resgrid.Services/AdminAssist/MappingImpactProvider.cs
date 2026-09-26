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
	/// <summary>Counts the same v4 marker choices at one captured time. Coordinates and identities never leave this request.</summary>
	public sealed class MappingImpactProvider(IUsersService users, IUnitsService units, IUnitsRepository unitRows,
		IUnitStatesRepository unitStates, IActionLogsRepository actions, IAdminAssistPermissionEvaluator authorization,
		IRecordsAuthorizationService membership) : IOperationalImpactProvider, IComposedOperationalImpactProvider
	{
		private static readonly string[] Ids = { "setting.MappingPersonnelLocationTTL", "setting.MappingUnitLocationTTL",
			"setting.MappingPersonnelAllowStatusWithNoLocationToOverwrite", "setting.MappingUnitAllowStatusWithNoLocationToOverwrite" };
		public bool Supports(string settingId) => Ids.Contains(settingId, StringComparer.Ordinal);
		public bool AppliesTo(IReadOnlyList<string> settingIds) => settingIds.Any(Supports);
		public async Task<OperationalImpact> EvaluateComposedAsync(AdminAssistActor actor, ConfigurationSnapshot before, ConfigurationSnapshot after, IReadOnlyList<string> settingIds, CancellationToken ct)
		{
			var metrics = new List<ConfigurationImpactMetric>(); var limits = new List<string>();
			foreach (var personnel in new[] { true, false }) {
				var prefix = personnel ? "MappingPersonnel" : "MappingUnit";
				if (!settingIds.Any(id => id.StartsWith("setting." + prefix, StringComparison.Ordinal))) continue;
				// Evaluate both endpoints with the complete setting vector. Values are projected in memory,
				// so auto-status and TTL/overwrite interactions participate in the final result.
				var request = new ConfigurationImpactRequest("setting." + prefix + "LocationTTL", before.Revision, Number: before.Find(prefix + "LocationTTL").Number);
				var first = await EvaluateAsync(actor, before, request, ct);
				var last = await EvaluateAsync(actor, after, request with { Number = after.Find(prefix + "LocationTTL").Number }, ct);
				var key = personnel ? "Impact.PersonnelMarkers" : "Impact.UnitMarkers";
				var old = first.Metrics.SingleOrDefault(m => m.LabelKey == key); var next = last.Metrics.SingleOrDefault(m => m.LabelKey == key);
				var known = old?.State == EvidenceState.Known && next?.State == EvidenceState.Known;
				metrics.Add(new(key, known ? EvidenceState.Known : EvidenceState.Unknown, known ? old.Before : null, known ? next.After : null));
				limits.AddRange(first.LimitKeys); limits.AddRange(last.LimitKeys);
			}
			return new(metrics, limits.Distinct().ToArray(), "composed-map-v1");
		}
		private sealed record Marker(DateTime? PingOn, DateTime? StatusOn, Func<bool> HasStatusLocation);
		public async Task<OperationalImpact> EvaluateAsync(AdminAssistActor actor, ConfigurationSnapshot snapshot, ConfigurationImpactRequest request, CancellationToken ct)
		{
			if (!Supports(request.SettingId)) throw new ArgumentException("Unsupported mapping proposal.");
			var label = request.SettingId.Contains("Personnel", StringComparison.Ordinal) ? "Impact.PersonnelMarkers" : "Impact.UnitMarkers";
			OperationalImpact Unknown(EvidenceState state) => new(new[] { new ConfigurationImpactMetric(label, state, null, null, "SourceUnavailable") }, new[] { "Impact.MapEvidenceUnavailable" }, "map-markers-v1");
			try
			{
				var personnel = request.SettingId.Contains("Personnel", StringComparison.Ordinal);
				var prefix = personnel ? "MappingPersonnel" : "MappingUnit";
				var ttl = snapshot.Find(prefix + "LocationTTL");
				var overwrite = snapshot.Find(prefix + "AllowStatusWithNoLocationToOverwrite");
				var maximumAge = TimeSpan.FromSeconds(Math.Clamp(Config.AdminAssistConfig.EvidenceFreshnessSeconds, 1, 300));
				if (!ttl.IsFresh(snapshot.AsOfUtc, maximumAge) || !ttl.Number.HasValue || ttl.Number < 0 || ttl.Number > 525600 ||
					!overwrite.IsFresh(snapshot.AsOfUtc, maximumAge) || !overwrite.Boolean.HasValue) return Unknown(EvidenceState.Unknown);
				var markers = personnel ? await ReadPersonnelAsync(actor, snapshot, ct) : await ReadUnitsAsync(actor, snapshot.AsOfUtc, ct);
				var currentTtl = (int)ttl.Number.Value;
				var proposedTtl = request.SettingId.EndsWith("LocationTTL", StringComparison.Ordinal) ? checked((int)request.Number.Value) : currentTtl;
				var proposedOverwrite = request.Boolean ?? overwrite.Boolean.Value;
				int before = 0, after = 0, added = 0, removed = 0, priorFallback = 0, fallback = 0;
				foreach (var marker in markers)
				{
					ct.ThrowIfCancellationRequested();
					var was = MappingMarkerSelection.Select(marker.PingOn, marker.StatusOn, marker.HasStatusLocation, currentTtl, overwrite.Boolean.Value, snapshot.AsOfUtc);
					var next = MappingMarkerSelection.Select(marker.PingOn, marker.StatusOn, marker.HasStatusLocation, proposedTtl, proposedOverwrite, snapshot.AsOfUtc);
					if (was != MappingMarkerSource.None) before++;
					if (next != MappingMarkerSource.None) after++;
					if (was == MappingMarkerSource.None && next != MappingMarkerSource.None) added++;
					if (was != MappingMarkerSource.None && next == MappingMarkerSource.None) removed++;
					if (was == MappingMarkerSource.Status) priorFallback++;
					if (next == MappingMarkerSource.Status) fallback++;
				}
				return new(new[] {
					new ConfigurationImpactMetric(label, EvidenceState.Known, before, after),
					new ConfigurationImpactMetric("Impact.MarkersAdded", EvidenceState.Known, 0, added),
					new ConfigurationImpactMetric("Impact.MarkersRemoved", EvidenceState.Known, 0, removed),
					new ConfigurationImpactMetric("Impact.StatusFallbackMarkers", EvidenceState.Known, priorFallback, fallback),
					new ConfigurationImpactMetric("Impact.MapSample", EvidenceState.Known, markers.Count, markers.Count)
				}, new[] { "Impact.MapScope", "Impact.MapFallback", "Impact.MapSampling" }, "map-markers-v1");
			}
			catch (OperationCanceledException) { throw; }
			catch (UnauthorizedAccessException) { return Unknown(EvidenceState.Redacted); }
			catch (Exception) { return Unknown(EvidenceState.Unknown); }
		}
		private async Task<List<Marker>> ReadUnitsAsync(AdminAssistActor actor, DateTime now, CancellationToken ct)
		{
			var owned = (await unitRows.GetAllUnitsByDepartmentIdAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			var pings = await units.ReadLatestLocationsForAdministrationAsync(actor.DepartmentId).WaitAsync(ct) ?? throw new InvalidOperationException();
			var statuses = (await unitStates.GetLatestUnitStatesForDepartmentAsync(actor.DepartmentId).WaitAsync(ct))?.ToList() ?? throw new InvalidOperationException();
			Bound(owned.Count, pings.Count, statuses.Count);
			if (owned.Any(u => u.DepartmentId != actor.DepartmentId) || pings.Any(p => p.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
			var pingByUnit = pings.ToDictionary(p => p.UnitId); var stateByUnit = statuses.ToDictionary(s => s.UnitId);
			var result = new List<Marker>();
			var allowed = await authorization.EvaluateCurrentTargetsAsync(actor, nameof(PermissionTypes.CanSeeUnitLocations), owned.Select(u => u.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), ct);
			foreach (var unit in owned)
			{
				ct.ThrowIfCancellationRequested();
				if (allowed == null || !allowed.TryGetValue(unit.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var canView) || !canView) throw new UnauthorizedAccessException();
				pingByUnit.TryGetValue(unit.UnitId, out var ping); stateByUnit.TryGetValue(unit.UnitId, out var status);
				// The map's owning service supplies a current Available state when a unit has no status.
				result.Add(new Marker(ping?.Timestamp, status?.Timestamp ?? now, () => status?.HasLocation() == true));
			}
			return result;
		}
		private async Task<List<Marker>> ReadPersonnelAsync(AdminAssistActor actor, ConfigurationSnapshot snapshot, CancellationToken ct)
		{
			var disableAuto = snapshot.Find("DisabledAutoAvailable");
			if (disableAuto.State != EvidenceState.Known || !disableAuto.Boolean.HasValue) throw new InvalidOperationException();
			var people = await users.GetUserGroupAndRolesByDepartmentIdAsync(actor.DepartmentId, false, false, false).WaitAsync(ct) ?? throw new InvalidOperationException();
			var pings = await users.ReadLatestLocationsForAdministrationAsync(actor.DepartmentId).WaitAsync(ct) ?? throw new InvalidOperationException();
			// Read the same status projection directly, without the map service's ETA/provider enrichment.
			var statuses = (await actions.ReadLatestForAdministrationAsync(actor.DepartmentId, disableAuto.Boolean.Value, snapshot.AsOfUtc, Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000), ct))?.ToList() ?? throw new InvalidOperationException();
			Bound(people.Count, pings.Count, statuses.Count);
			if (pings.Any(p => p.DepartmentId != actor.DepartmentId) || statuses.Any(s => s.DepartmentId != actor.DepartmentId)) throw new InvalidOperationException();
			var pingByPerson = pings.ToDictionary(p => p.UserId, StringComparer.Ordinal);
			var stateByPerson = statuses.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ActionLogId).First(), StringComparer.Ordinal);
			if (people.Select(p => p.UserId).Distinct(StringComparer.Ordinal).Count() != people.Count) throw new InvalidOperationException();
			var result = new List<Marker>();
			var allowed = await authorization.EvaluateCurrentTargetsAsync(actor, nameof(PermissionTypes.CanSeePersonnelLocations), people.Select(p => p.UserId).ToArray(), ct);
			foreach (var person in people)
			{
				ct.ThrowIfCancellationRequested();
				if (!await membership.IsAssignableMemberAsync(person.UserId, actor.DepartmentId) ||
					(allowed == null || !allowed.TryGetValue(person.UserId, out var canView) || !canView)) throw new UnauthorizedAccessException();
				pingByPerson.TryGetValue(person.UserId, out var ping); stateByPerson.TryGetValue(person.UserId, out var status);
				result.Add(new Marker(ping?.Timestamp, status?.Timestamp, () => status?.HasLocation() == true));
			}
			return result;
		}
		private static void Bound(params int[] counts)
		{
			if (counts.Any(c => c > Math.Clamp(Config.AdminAssistConfig.MaxEvidenceRows, 1, 10000))) throw new InvalidOperationException("Mapping preview row bound exceeded.");
		}
	}
}
