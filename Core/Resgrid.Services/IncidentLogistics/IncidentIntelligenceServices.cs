using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Edge latch for evaluated triggers (plan §13.7 "fires once, resolves once"): an incident-scoped condition is a row in
	/// IncidentDataFreshness keyed "a{trigger}:{subject}" whose IsStale is the condition; a department-scoped one (no activation to
	/// hang a row from) is a cache key. <see cref="SetAsync"/> answers true when the condition starts, false when it ends, null when
	/// nothing changed — so a worker that runs every minute emits each edge exactly once.
	/// </summary>
	public sealed class IncidentAlertLatch
	{
		private readonly IIncidentLedgerRepository<IncidentDataFreshness> _freshness;
		private readonly ICacheProvider _cache;

		public IncidentAlertLatch(IIncidentLedgerRepository<IncidentDataFreshness> freshness, ICacheProvider cache)
		{
			_freshness = freshness;
			_cache = cache;
		}

		public static string Key(WorkflowTriggerEventType trigger, string subject)
		{
			var key = "a" + ((int)trigger).ToString(CultureInfo.InvariantCulture) + ":" + subject;
			return key.Length <= 64 ? key : key.Substring(0, 64);
		}

		public async Task<bool?> SetAsync(int departmentId, string activationId, string key, bool active, CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;
			var row = (await _freshness.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 1 }.Where("SourceKey", key), cancellationToken)).FirstOrDefault();
			if (row == null)
			{
				if (!active)
					return null;
				await _freshness.InsertNewAsync(new IncidentDataFreshness
				{
					DepartmentId = departmentId,
					IncidentActivationId = activationId,
					SourceKey = key,
					IsStale = true,
					LastObservedOn = now,
					LastChangeOn = now,
					UpdatedOn = now
				}, null, cancellationToken);
				return true;
			}
			if (row.IsStale == active)
				return null;
			row.IsStale = active;
			row.LastChangeOn = now;
			row.LastObservedOn = now;
			row.UpdatedOn = now;
			await _freshness.UpdateVersionedAsync(row, 0, null, cancellationToken);
			return active;
		}

		public async Task<bool?> SetDepartmentAsync(int departmentId, string key, bool active)
		{
			var cacheKey = "IBO_LATCH_" + departmentId.ToString(CultureInfo.InvariantCulture) + "_" + key;
			var stored = await _cache.GetStringAsync(cacheKey) == "1";
			if (stored == active)
				return null;
			if (active)
				await _cache.SetStringAsync(cacheKey, "1", TimeSpan.FromDays(30));
			else
				await _cache.RemoveAsync(cacheKey);
			return active;
		}
	}

	/// <summary>
	/// The desks' numbers (plan §9.17) and the snapshot projection (§13.4). Every number is counted from the module's own tables at
	/// the moment of the call, so the desk and the snapshot agree; the snapshot adds the metric rows and the freshness the context bar
	/// shows. Personnel picture columns keep support status and operational status apart (§9.5).
	/// </summary>
	public sealed class IncidentOperationalIntelligenceService : IIncidentOperationalIntelligenceService
	{
		private static readonly int[] OnIncident = { (int)IncidentSupportStatuses.Arrived, (int)IncidentSupportStatuses.Supported, (int)IncidentSupportStatuses.PendingDemob };
		private static readonly int[] Inbound = { (int)IncidentSupportStatuses.Ordered, (int)IncidentSupportStatuses.Mobilizing, (int)IncidentSupportStatuses.EnRoute };
		private static readonly int[] OpenRequests =
		{
			(int)IncidentRequestStatuses.Submitted, (int)IncidentRequestStatuses.Approved, (int)IncidentRequestStatuses.Ordered,
			(int)IncidentRequestStatuses.PartiallyFilled, (int)IncidentRequestStatuses.Filled, (int)IncidentRequestStatuses.EnRoute
		};
		private static readonly (string Window, double Hours)[] Windows = { ("2h", 2), ("6h", 6), ("12h", 12), ("24h", 24) };
		/// <summary>How far back every per-minute snapshot is kept before the hourly thinning applies.</summary>
		private static readonly TimeSpan FullResolution = TimeSpan.FromHours(24);
		/// <summary>Snapshots read per thinning pass (the repository's list ceiling); a backlog drains over later hours.</summary>
		private const int ThinPage = 5000;

		private readonly IIncidentLedgerRepository<IncidentParticipant> _participants;
		private readonly IIncidentLedgerRepository<IncidentResource> _resources;
		private readonly IIncidentLedgerRepository<IncidentResourceRequest> _requests;
		private readonly IIncidentLedgerRepository<IncidentRequestFill> _fills;
		private readonly IIncidentLedgerRepository<IncidentLodgingNightInventory> _nights;
		private readonly IIncidentLedgerRepository<IncidentLodgingBed> _beds;
		private readonly IIncidentLedgerRepository<IncidentServiceRequest> _services;
		private readonly IIncidentLedgerRepository<IncidentDemobPlan> _demob;
		private readonly IIncidentLedgerRepository<IncidentOperationalSnapshot> _snapshots;
		private readonly IIncidentLedgerRepository<IncidentMetricValue> _metrics;
		private readonly IIncidentLedgerRepository<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerRepository<IncidentPositionDefinition> _positions;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentEventPublisher _publisher;
		private readonly IncidentAlertLatch _latch;

		public IncidentOperationalIntelligenceService(IIncidentLedgerRepository<IncidentParticipant> participants, IIncidentLedgerRepository<IncidentResource> resources,
			IIncidentLedgerRepository<IncidentResourceRequest> requests, IIncidentLedgerRepository<IncidentRequestFill> fills,
			IIncidentLedgerRepository<IncidentLodgingNightInventory> nights, IIncidentLedgerRepository<IncidentLodgingBed> beds,
			IIncidentLedgerRepository<IncidentServiceRequest> services, IIncidentLedgerRepository<IncidentDemobPlan> demob,
			IIncidentLedgerRepository<IncidentOperationalSnapshot> snapshots, IIncidentLedgerRepository<IncidentMetricValue> metrics,
			IIncidentLedgerRepository<IncidentPositionAssignment> assignments, IIncidentLedgerRepository<IncidentPositionDefinition> positions,
			IIncidentLedgerRepository<IncidentActivation> activations, IIncidentBackOfficeSettingsService settings, IIncidentEventPublisher publisher, IncidentAlertLatch latch)
		{
			_participants = participants;
			_resources = resources;
			_requests = requests;
			_fills = fills;
			_nights = nights;
			_beds = beds;
			_services = services;
			_demob = demob;
			_snapshots = snapshots;
			_metrics = metrics;
			_assignments = assignments;
			_positions = positions;
			_activations = activations;
			_settings = settings;
			_publisher = publisher;
			_latch = latch;
		}

		private static IncidentLedgerQuery In(string activationId) => new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 10000 };

		public async Task<IncidentDeskSummary> GetDeskSummaryAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var departmentId = actor.DepartmentId;
			var activation = await _activations.GetForDepartmentAsync(departmentId, activationId, false, cancellationToken);
			if (activation == null)
				return null;
			var now = DateTime.UtcNow;
			var summary = new IncidentDeskSummary { AsOf = now };

			var participants = (await _participants.ListAsync(departmentId, In(activationId), cancellationToken)).Where(p => !p.IsDraft).ToList();
			summary.Values["participants.total"] = participants.Count;
			summary.Values["participants.on_incident"] = participants.Count(p => OnIncident.Contains(p.SupportStatus));
			summary.Values["participants.inbound"] = participants.Count(p => Inbound.Contains(p.SupportStatus));

			var resources = (await _resources.ListAsync(departmentId, In(activationId), cancellationToken)).ToList();
			summary.Values["resources.on_incident"] = resources.Count(r => OnIncident.Contains(r.SupportStatus));
			summary.Values["resources.inbound"] = resources.Count(r => Inbound.Contains(r.SupportStatus));
			summary.Values["resources.personnel"] = resources.Where(r => OnIncident.Contains(r.SupportStatus)).Sum(r => r.PersonnelCount);

			var requests = (await _requests.ListAsync(departmentId, In(activationId).WhereIn("Status", OpenRequests), cancellationToken)).ToList();
			summary.Values["requests.open"] = requests.Count;
			summary.Values["requests.critical_unfilled"] = requests.Count(r => r.Priority == (int)IncidentRequestPriorities.Critical && r.Status < (int)IncidentRequestStatuses.Filled);
			summary.Values["requests.overdue"] = requests.Count(r => r.NeedBy.HasValue && r.NeedBy < now && r.Status < (int)IncidentRequestStatuses.Received);

			var fills = (await _fills.ListAsync(departmentId, In(activationId).WhereIn("Status", new[] { (int)IncidentFillStatuses.Accepted, (int)IncidentFillStatuses.EnRoute }), cancellationToken)).ToList();
			summary.Values["fills.en_route"] = fills.Count(f => f.Status == (int)IncidentFillStatuses.EnRoute);
			var from = now;
			foreach (var (window, hours) in Windows)
			{
				var until = now.AddHours(hours);
				var due = fills.Where(f => f.EtaOn.HasValue && f.EtaOn >= from && f.EtaOn < until).ToList();
				summary.Inbound.Add(new IncidentArrivalBucket { Window = window, Until = until, Apparatus = due.Sum(f => f.ApparatusCount), Personnel = due.Sum(f => f.PersonnelCount), Count = due.Count });
				from = until;
			}

			var demob = (await _demob.ListAsync(departmentId, In(activationId).WhereIn("Status", new[] { (int)IncidentDemobStatuses.Tentative, (int)IncidentDemobStatuses.Approved, (int)IncidentDemobStatuses.Released }), cancellationToken)).ToList();
			summary.Values["demob.pending"] = demob.Count;
			from = now;
			foreach (var (window, hours) in Windows)
			{
				var until = now.AddHours(hours);
				var leaving = demob.Where(d => (d.ApprovedReleaseOn ?? d.TentativeReleaseOn) is DateTime release && release >= from && release < until).ToList();
				var predicted = resources.Count(r => OnIncident.Contains(r.SupportStatus) && r.TimeoutOn.HasValue && r.TimeoutOn.Value >= from.Date && r.TimeoutOn.Value < until.Date.AddDays(1) && demob.All(d => d.SubjectId != r.IncidentResourceId));
				summary.Outbound.Add(new IncidentArrivalBucket { Window = window, Until = until, Count = leaving.Count, Predicted = predicted });
				from = until;
			}

			var beds = (await _beds.ListAsync(departmentId, In(activationId), cancellationToken)).Count(b => !b.IsOutOfService);
			var tonight = await _nights.CountAsync(departmentId, In(activationId).Where("Night", now.Date), cancellationToken);
			summary.Values["lodging.beds_held_tonight"] = tonight;
			summary.Values["lodging.beds_available_tonight"] = Math.Max(0, beds - tonight);
			summary.Values["lodging.needed"] = participants.Count(p => p.NeedsLodging && (OnIncident.Contains(p.SupportStatus) || Inbound.Contains(p.SupportStatus)));

			summary.Values["service.open"] = await _services.CountAsync(departmentId, In(activationId).WhereIn("Status",
				new[] { (int)IncidentServiceRequestStatuses.Open, (int)IncidentServiceRequestStatuses.Assigned, (int)IncidentServiceRequestStatuses.InProgress, (int)IncidentServiceRequestStatuses.OnHold }), cancellationToken);
			return summary;
		}

		public async Task<IReadOnlyList<IncidentPersonnelPictureRow>> GetPersonnelPictureAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var departmentId = actor.DepartmentId;
			var participants = (await _participants.ListAsync(departmentId, In(activationId).Order("DisplayName"), cancellationToken)).Where(p => !p.IsDraft).ToList();
			var now = DateTime.UtcNow;
			var open = (await _assignments.ListAsync(departmentId, In(activationId), cancellationToken)).Where(a => a.StartOn <= now && (!a.EndOn.HasValue || a.EndOn > now)).ToList();
			var positions = (await _positions.GetByIdsAsync(departmentId, open.Select(a => a.IncidentPositionDefinitionId), cancellationToken)).ToDictionary(p => p.IncidentPositionDefinitionId, p => p.Code, StringComparer.OrdinalIgnoreCase);
			string PositionOf(IncidentParticipant p) => open.Where(a => (a.UserId != null && string.Equals(a.UserId, p.UserId, StringComparison.OrdinalIgnoreCase)) || string.Equals(a.IncidentParticipantId, p.IncidentParticipantId, StringComparison.OrdinalIgnoreCase))
				.Select(a => positions.TryGetValue(a.IncidentPositionDefinitionId, out var code) ? code : null).FirstOrDefault(c => c != null);
			return participants.Select(p => new IncidentPersonnelPictureRow
			{
				ParticipantId = p.IncidentParticipantId,
				UserId = p.UserId,
				Name = p.DisplayName,
				Agency = p.Agency,
				SupportStatus = (IncidentSupportStatuses)p.SupportStatus,
				IsUnmatched = string.IsNullOrWhiteSpace(p.UserId),
				Position = PositionOf(p)
			}).ToList();
		}

		public async Task<IncidentOperationalSnapshot> ProjectSnapshotAsync(IncidentActor actor, string activationId, IncidentSnapshotKinds kind, CancellationToken cancellationToken = default)
		{
			var departmentId = actor.DepartmentId;
			var activation = await _activations.GetForDepartmentAsync(departmentId, activationId, false, cancellationToken);
			if (activation == null || activation.Status >= (int)IncidentActivationStatuses.Closed)
				return null;
			var defaults = await _settings.GetAsync<IncidentBackOfficeDefaults>(departmentId);
			var previous = (await _snapshots.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 1 }.Order("TakenOn", true), cancellationToken)).FirstOrDefault();
			var summary = await GetDeskSummaryAsync(actor, activationId, cancellationToken);
			var now = DateTime.UtcNow;
			var lag = previous == null ? 0 : (int)Math.Min(int.MaxValue, (now - previous.TakenOn).TotalSeconds);

			var drift = false;
			if (kind == IncidentSnapshotKinds.Rebuild && previous?.SummaryJson != null)
			{
				var before = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(previous.SummaryJson) ?? new Dictionary<string, decimal>();
				drift = before.Any(b => summary.Values.TryGetValue(b.Key, out var value) && value != b.Value && previous.TakenOn > now.AddMinutes(-2));
			}

			var snapshot = await _snapshots.InsertNewAsync(new IncidentOperationalSnapshot
			{
				DepartmentId = departmentId,
				IncidentActivationId = activationId,
				Cursor = now.Ticks,
				TakenOn = now,
				Kind = (int)kind,
				OldestSourceOn = now,
				LagSeconds = lag,
				DriftDetected = drift,
				SummaryJson = JsonConvert.SerializeObject(summary.Values)
			}, actor.UserId, cancellationToken);
			foreach (var (key, value) in summary.Values)
				await _metrics.InsertNewAsync(new IncidentMetricValue
				{
					DepartmentId = departmentId,
					IncidentActivationId = activationId,
					IncidentOperationalSnapshotId = snapshot.IncidentOperationalSnapshotId,
					MetricKey = key,
					MetricVersion = 1,
					Value = value,
					SourceCount = 1,
					AsOf = now
				}, actor.UserId, cancellationToken);

			// Trigger 120: the projection fell behind the department's staleness window (or drifted on rebuild); resolves on catch-up.
			var stale = lag > Math.Max(1, defaults.SnapshotStalenessMinutes) * 60 || drift;
			var edge = await _latch.SetAsync(departmentId, activationId, IncidentAlertLatch.Key(WorkflowTriggerEventType.IncidentSnapshotStale, "projection"), stale, cancellationToken);
			if (edge.HasValue)
				await _publisher.PublishAsync(departmentId, activation, WorkflowTriggerEventType.IncidentSnapshotStale, new Dictionary<string, object>
				{
					["health.kind"] = drift ? "Drift" : "Lag",
					["health.metric"] = "projection",
					["health.lag_seconds"] = lag,
					["health.oldest_source_utc"] = previous?.TakenOn,
					["resolved"] = !edge.Value
				}, snapshot.TableName, snapshot.IncidentOperationalSnapshotId, cancellationToken);

			// Worker 74 adds a snapshot every minute; once an hour (the first incremental pass of a new UTC hour) the history behind
			// the last day is thinned so a long incident does not keep ~1,440 snapshots and their metric rows per day until retention.
			if (kind == IncidentSnapshotKinds.Incremental && previous != null && HourOf(previous.TakenOn) != HourOf(now))
				await ThinSnapshotsAsync(departmentId, activationId, now, cancellationToken);
			return snapshot;
		}

		private static long HourOf(DateTime value) => value.Ticks / TimeSpan.TicksPerHour;

		/// <summary>
		/// The last <see cref="FullResolution"/> keeps every snapshot; each older UTC hour keeps its newest, plus any snapshot that
		/// detected drift. The newest snapshot is always the newest of its own hour, so everything that reads only the latest one — the
		/// context bar's staleness, the next pass's lag and drift check, the projection cursor — keeps its row. Metric values go with
		/// their snapshot, metrics first so a failure part-way never leaves values without their snapshot.
		/// </summary>
		private async Task ThinSnapshotsAsync(int departmentId, string activationId, DateTime now, CancellationToken cancellationToken)
		{
			var older = (await _snapshots.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = ThinPage }
				.Between("TakenOn", null, now - FullResolution).Order("Cursor", true), cancellationToken)).ToList();
			var hours = older.GroupBy(s => HourOf(s.TakenOn)).ToList();
			// A full page can stop part-way through its oldest hour; that hour waits for a later pass so its true newest is never removed.
			if (older.Count == ThinPage)
				hours.RemoveAt(hours.Count - 1);
			var remove = hours.SelectMany(h => h.OrderByDescending(s => s.Cursor).Skip(1)).Where(s => !s.DriftDetected).Select(s => s.IncidentOperationalSnapshotId).ToList();
			if (remove.Count == 0)
				return;
			await _metrics.PurgeInAsync(departmentId, "IncidentOperationalSnapshotId", remove, cancellationToken);
			await _snapshots.PurgeInAsync(departmentId, "IncidentOperationalSnapshotId", remove, cancellationToken);
		}
	}

	/// <summary>§12.3: the derived need numbers IC reads for its needs board; null when the module is off for the department.</summary>
	public sealed class IncidentRequestLinkReader : IIncidentRequestLinkReader
	{
		private static readonly int[] Live = { (int)IncidentFillStatuses.Accepted, (int)IncidentFillStatuses.EnRoute, (int)IncidentFillStatuses.Arrived };

		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IIncidentLedgerRepository<IncidentResourceRequest> _requests;
		private readonly IIncidentLedgerRepository<IncidentRequestFill> _fills;

		public IncidentRequestLinkReader(IIncidentBackOfficeAccessService access, IIncidentLedgerRepository<IncidentResourceRequest> requests, IIncidentLedgerRepository<IncidentRequestFill> fills)
		{
			_access = access;
			_requests = requests;
			_fills = fills;
		}

		public async Task<IReadOnlyDictionary<string, IncidentNeedDerivation>> GetNeedDerivationsAsync(int departmentId, IReadOnlyList<string> incidentNeedIds, CancellationToken cancellationToken = default)
		{
			if (incidentNeedIds == null || incidentNeedIds.Count == 0 || !await _access.IsEnabledAsync(departmentId))
				return null;
			var requests = (await _requests.ListAsync(departmentId, new IncidentLedgerQuery { Take = 5000 }.WhereIn("IncidentNeedId", incidentNeedIds), cancellationToken))
				.Where(r => r.Status is not ((int)IncidentRequestStatuses.Cancelled or (int)IncidentRequestStatuses.Rejected)).ToList();
			if (requests.Count == 0)
				return new Dictionary<string, IncidentNeedDerivation>(StringComparer.OrdinalIgnoreCase);
			var fills = (await _fills.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.WhereIn("IncidentResourceRequestId", requests.Select(r => r.IncidentResourceRequestId).ToList()).WhereIn("Status", Live), cancellationToken)).ToList();
			return requests.GroupBy(r => r.IncidentNeedId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => new IncidentNeedDerivation
			{
				LinkedRequestCount = g.Count(),
				DerivedQuantityFulfilled = fills.Where(f => g.Any(r => r.IncidentResourceRequestId == f.IncidentResourceRequestId)).Sum(f => f.Quantity),
				DerivedStatus = Enum.GetName(typeof(IncidentRequestStatuses), g.Min(r => r.Status))
			}, StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>§10A.3 projection contracts: references and safe display fields only; protected participant fields are named as excluded.</summary>
	public sealed class IncidentProjectionService : IIncidentProjectionService
	{
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly IIncidentLedgerRepository<IncidentParticipant> _participants;
		private readonly IIncidentLedgerRepository<IncidentResource> _resources;
		private readonly IIncidentLedgerRepository<IncidentResourceComponent> _components;
		private readonly IIncidentLedgerRepository<IncidentOperationalSnapshot> _snapshots;

		public IncidentProjectionService(IIncidentBackOfficeAccessService access, IIncidentLedgerRepository<IncidentActivation> activations,
			IIncidentLedgerRepository<IncidentParticipant> participants, IIncidentLedgerRepository<IncidentResource> resources,
			IIncidentLedgerRepository<IncidentResourceComponent> components, IIncidentLedgerRepository<IncidentOperationalSnapshot> snapshots)
		{
			_access = access;
			_activations = activations;
			_participants = participants;
			_resources = resources;
			_components = components;
			_snapshots = snapshots;
		}

		private static readonly string[] ParticipantExclusions = { "PersonalPhone", "PersonalEmail", "HomeAddress", "DateOfBirth", "ExternalIdentifier", "Notes" };

		private async Task<(bool Available, long Cursor, DateTime? Oldest)> HeadAsync(int departmentId, string activationId, CancellationToken cancellationToken)
		{
			if (!await _access.IsEnabledAsync(departmentId) || await _activations.GetForDepartmentAsync(departmentId, activationId, false, cancellationToken) == null)
				return (false, 0, null);
			var latest = (await _snapshots.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 1 }.Order("TakenOn", true), cancellationToken)).FirstOrDefault();
			return (true, latest?.Cursor ?? DateTime.UtcNow.Ticks, latest?.TakenOn);
		}

		public async Task<IncidentProjection<IncidentPersonnelCheckInRowV1>> GetPersonnelCheckInV1Async(int departmentId, string activationId, CancellationToken cancellationToken = default)
		{
			var head = await HeadAsync(departmentId, activationId, cancellationToken);
			var projection = new IncidentProjection<IncidentPersonnelCheckInRowV1> { Contract = "incident.personnel-checkin", Available = head.Available, Cursor = head.Cursor, OldestSourceOn = head.Oldest, ExcludedFields = ParticipantExclusions };
			if (!head.Available)
				return projection;
			projection.Rows = (await _participants.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 10000 }.Order("DisplayName"), cancellationToken))
				.Where(p => !p.IsDraft).Select(p => new IncidentPersonnelCheckInRowV1
				{
					ParticipantId = p.IncidentParticipantId,
					Name = p.DisplayName,
					Agency = p.Agency,
					HomeUnit = p.HomeUnit,
					Position = p.PositionTitle,
					OrderNumber = p.OrderNumber,
					RequestNumber = p.RequestNumber,
					CheckedInOn = p.CheckedInOn,
					ReleasedOn = p.ReleasedOn,
					SupportStatus = Enum.GetName(typeof(IncidentSupportStatuses), p.SupportStatus)
				}).ToList();
			return projection;
		}

		public async Task<IncidentProjection<IncidentResourceSummaryRowV1>> GetResourceSummaryV1Async(int departmentId, string activationId, CancellationToken cancellationToken = default)
		{
			var head = await HeadAsync(departmentId, activationId, cancellationToken);
			var projection = new IncidentProjection<IncidentResourceSummaryRowV1> { Contract = "incident.resource-summary", Available = head.Available, Cursor = head.Cursor, OldestSourceOn = head.Oldest };
			if (!head.Available)
				return projection;
			var query = new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 10000 };
			var components = (await _components.ListAsync(departmentId, query, cancellationToken)).GroupBy(c => c.IncidentResourceId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
			projection.Rows = (await _resources.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 10000 }.Order("Name"), cancellationToken))
				.Select(r => new IncidentResourceSummaryRowV1
				{
					ResourceId = r.IncidentResourceId,
					Name = r.Name,
					Kind = Enum.GetName(typeof(IncidentResourceKinds), r.Kind),
					ResourceType = r.ResourceType,
					Provider = r.ProviderAgency,
					OrderNumber = r.OrderNumber,
					RequestNumber = r.RequestNumber,
					PersonnelCount = r.PersonnelCount,
					ArrivedOn = r.ArrivedOn,
					ReleasedOn = r.ReleasedOn,
					SupportStatus = Enum.GetName(typeof(IncidentSupportStatuses), r.SupportStatus),
					ComponentCount = components.TryGetValue(r.IncidentResourceId, out var count) ? count : 0
				}).ToList();
			return projection;
		}
	}
}
