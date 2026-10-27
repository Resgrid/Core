using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// Plan §7: per-domain authority. A department keeps profiles; an activation takes a snapshot so its rules cannot change
	/// silently mid-incident. With no profile the no-connector default applies: Resgrid authoritative in every domain.
	/// </summary>
	public class IncidentAuthorityService : IIncidentAuthorityService
	{
		private readonly IIncidentLedgerService<DepartmentIncidentAuthorityProfile> _profiles;
		private readonly IIncidentLedgerService<IncidentDomainAuthority> _domains;
		private readonly IIncidentLedgerService<IncidentAuthorityProfileSnapshot> _snapshots;

		public IncidentAuthorityService(IIncidentLedgerService<DepartmentIncidentAuthorityProfile> profiles, IIncidentLedgerService<IncidentDomainAuthority> domains,
			IIncidentLedgerService<IncidentAuthorityProfileSnapshot> snapshots)
		{
			_profiles = profiles;
			_domains = domains;
			_snapshots = snapshots;
		}

		public async Task<DepartmentIncidentAuthorityProfile> GetDefaultProfileAsync(IncidentActor actor, CancellationToken cancellationToken = default)
		{
			var profiles = await _profiles.ListAsync(actor, new IncidentLedgerQuery().Order("IsDefault", true, "Name"), cancellationToken);
			return profiles.FirstOrDefault(p => p.IsDefault) ?? profiles.FirstOrDefault();
		}

		public async Task<IReadOnlyList<IncidentDomainAuthority>> GetDomainsAsync(IncidentActor actor, IncidentAuthorityOwnerKinds ownerKind, string ownerId, CancellationToken cancellationToken = default)
		{
			var stored = string.IsNullOrWhiteSpace(ownerId) ? new List<IncidentDomainAuthority>()
				: (await _domains.ListAsync(actor, new IncidentLedgerQuery().Where("OwnerKind", (int)ownerKind).Where("OwnerId", ownerId).Order("Domain"), cancellationToken)).ToList();
			// Every domain is answered: a missing row is the no-connector default (Resgrid authoritative).
			foreach (var domain in Enum.GetValues(typeof(IncidentAuthorityDomains)).Cast<IncidentAuthorityDomains>())
				if (stored.All(d => d.Domain != (int)domain))
					stored.Add(new IncidentDomainAuthority { DepartmentId = actor.DepartmentId, OwnerKind = (int)ownerKind, OwnerId = ownerId, Domain = (int)domain, Mode = (int)IncidentAuthorityModes.Resgrid, ConnectorState = (int)IncidentConnectorStates.NotConfigured });
			return stored.OrderBy(d => d.Domain).ToList();
		}

		public async Task SaveDomainsAsync(IncidentActor actor, string profileId, IReadOnlyList<IncidentDomainAuthority> domains, CancellationToken cancellationToken = default)
		{
			if (await _profiles.GetAsync(actor, profileId, false, cancellationToken) == null)
				throw new IncidentRuleException("RecordNotFound");
			var existing = (await _domains.ListAsync(actor, new IncidentLedgerQuery().Where("OwnerKind", (int)IncidentAuthorityOwnerKinds.Profile).Where("OwnerId", profileId), cancellationToken))
				.ToDictionary(d => d.Domain);
			foreach (var domain in domains ?? Array.Empty<IncidentDomainAuthority>())
			{
				if (!Enum.IsDefined(typeof(IncidentAuthorityDomains), domain.Domain) || !Enum.IsDefined(typeof(IncidentAuthorityModes), domain.Mode))
					throw new IncidentRuleException("InvalidValue");
				var row = existing.TryGetValue(domain.Domain, out var stored) ? stored : new IncidentDomainAuthority { OwnerKind = (int)IncidentAuthorityOwnerKinds.Profile, OwnerId = profileId, Domain = domain.Domain };
				row.Mode = domain.Mode;
				row.SourceSystem = Trim(domain.SourceSystem, 100);
				row.ConnectorState = Enum.IsDefined(typeof(IncidentConnectorStates), domain.ConnectorState) ? domain.ConnectorState : (int)IncidentConnectorStates.NotConfigured;
				row.Precedence = domain.Precedence;
				row.Notes = domain.Notes;
				await _domains.SaveAsync(actor, row, null, cancellationToken);
			}
		}

		public async Task<IncidentAuthorityProfileSnapshot> SnapshotAsync(IncidentActor actor, string activationId, string profileId, CancellationToken cancellationToken = default)
		{
			var profile = string.IsNullOrWhiteSpace(profileId) ? await GetDefaultProfileAsync(actor, cancellationToken) : await _profiles.GetAsync(actor, profileId, false, cancellationToken);
			var snapshot = await _snapshots.SaveAsync(actor, new IncidentAuthorityProfileSnapshot
			{
				IncidentActivationId = activationId,
				SourceProfileId = profile?.DepartmentIncidentAuthorityProfileId,
				Name = profile?.Name ?? "Resgrid authoritative (no connectors)",
				TakenOn = DateTime.UtcNow,
				TakenByUserId = actor.UserId
			}, null, cancellationToken);

			foreach (var domain in await GetDomainsAsync(actor, IncidentAuthorityOwnerKinds.Profile, profile?.DepartmentIncidentAuthorityProfileId, cancellationToken))
				await _domains.SaveAsync(actor, new IncidentDomainAuthority
				{
					OwnerKind = (int)IncidentAuthorityOwnerKinds.Snapshot,
					OwnerId = snapshot.IncidentAuthorityProfileSnapshotId,
					Domain = domain.Domain,
					Mode = domain.Mode,
					SourceSystem = domain.SourceSystem,
					ConnectorState = domain.ConnectorState,
					Precedence = domain.Precedence,
					Notes = domain.Notes
				}, null, cancellationToken);
			return snapshot;
		}

		internal static string Trim(string value, int length) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= length ? value.Trim() : value.Trim().Substring(0, length);
	}

	/// <summary>
	/// Plan §9.1: numbered operational periods (a planning window, not a staffing roster), copy-forward without completed
	/// transactions, freeze and approval, the IAP assembly manifest (approval and immutability are RMS's) and the weather block
	/// snapshotted at approval (§9.21).
	/// </summary>
	public class IncidentPlanningService : IIncidentPlanningService
	{
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<IncidentOperationalPeriod> _periods;
		private readonly IIncidentLedgerService<IncidentObjective> _objectives;
		private readonly IIncidentLedgerService<IncidentOrganizationNode> _nodes;
		private readonly IIncidentLedgerService<IncidentPositionAssignment> _assignments;
		private readonly IIncidentLedgerService<IncidentPlanVersion> _plans;
		private readonly IIncidentLedgerService<IncidentPlanWeatherBlock> _weather;
		private readonly IIncidentEventPublisher _events;
		private readonly IWeatherAlertService _weatherAlerts;

		public IncidentPlanningService(IIncidentLedgerService<IncidentActivation> activations, IIncidentLedgerService<IncidentOperationalPeriod> periods,
			IIncidentLedgerService<IncidentObjective> objectives, IIncidentLedgerService<IncidentOrganizationNode> nodes,
			IIncidentLedgerService<IncidentPositionAssignment> assignments, IIncidentLedgerService<IncidentPlanVersion> plans,
			IIncidentLedgerService<IncidentPlanWeatherBlock> weather, IIncidentEventPublisher events, IWeatherAlertService weatherAlerts)
		{
			_activations = activations;
			_periods = periods;
			_objectives = objectives;
			_nodes = nodes;
			_assignments = assignments;
			_plans = plans;
			_weather = weather;
			_events = events;
			_weatherAlerts = weatherAlerts;
		}

		public async Task<IReadOnlyList<IncidentOperationalPeriod>> GetPeriodsAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
			=> await _periods.ListAsync(actor, IncidentLedgerQuery.ForActivation(activationId).Order("Number").Page(0, 1000), cancellationToken);

		public async Task<IncidentOperationalPeriod> GetCurrentPeriodAsync(IncidentActor actor, string activationId, CancellationToken cancellationToken = default)
		{
			var periods = await GetPeriodsAsync(actor, activationId, cancellationToken);
			var now = DateTime.UtcNow;
			return periods.LastOrDefault(p => p.Status == (int)IncidentPeriodStatuses.Current)
				?? periods.LastOrDefault(p => p.StartOn <= now && p.EndOn > now);
		}

		public async Task<IncidentOperationalPeriod> CreatePeriodAsync(IncidentActor actor, string activationId, DateTime startUtc, DateTime endUtc, DateTime? briefingUtc, CancellationToken cancellationToken = default)
		{
			if (endUtc <= startUtc)
				throw new IncidentRuleException("PeriodWindowInvalid");
			var activation = await _activations.GetAsync(actor, activationId, false, cancellationToken) ?? throw new IncidentRuleException("ActivationNotFound");
			var periods = await GetPeriodsAsync(actor, activationId, cancellationToken);
			if (periods.Any(p => p.StartOn < endUtc && startUtc < p.EndOn))
				throw new IncidentRuleException("PeriodOverlaps");
			return await _periods.SaveAsync(actor, new IncidentOperationalPeriod
			{
				IncidentActivationId = activationId,
				Number = periods.Count == 0 ? 1 : periods.Max(p => p.Number) + 1,
				StartOn = startUtc,
				EndOn = endUtc,
				BriefingOn = briefingUtc,
				TimeZoneId = activation.TimeZoneId,
				Status = (int)IncidentPeriodStatuses.Planned,
				PlannerUserId = actor.UserId
			}, null, cancellationToken);
		}

		public async Task<IncidentOperationalPeriod> StartPeriodAsync(IncidentActor actor, string periodId, CancellationToken cancellationToken = default)
		{
			var period = await _periods.GetAsync(actor, periodId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var periods = await GetPeriodsAsync(actor, period.IncidentActivationId, cancellationToken);
			foreach (var current in periods.Where(p => p.Status == (int)IncidentPeriodStatuses.Current && p.IncidentOperationalPeriodId != periodId))
				await _periods.TransitionAsync(actor, current.IncidentOperationalPeriodId, (int)IncidentPeriodStatuses.Closed, null, null, cancellationToken);
			var started = await _periods.TransitionAsync(actor, periodId, (int)IncidentPeriodStatuses.Current, null, null, cancellationToken);

			var activation = await _activations.GetAsync(actor, started.IncidentActivationId, false, cancellationToken);
			await _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentPeriodStarted, new Dictionary<string, object>
			{
				["period.number"] = started.Number,
				["period.start"] = started.StartOn,
				["period.end"] = started.EndOn,
				["period.timezone"] = started.TimeZoneId,
				["period.briefing_time"] = started.BriefingOn,
				["period.is_first"] = started.Number == 1
			}, started.TableName, started.IncidentOperationalPeriodId, cancellationToken);
			return started;
		}

		public async Task<IncidentOperationalPeriod> CopyForwardAsync(IncidentActor actor, string fromPeriodId, CancellationToken cancellationToken = default)
		{
			var from = await _periods.GetAsync(actor, fromPeriodId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var length = from.EndOn - from.StartOn;
			var next = await CreatePeriodAsync(actor, from.IncidentActivationId, from.EndOn, from.EndOn + length, from.BriefingOn?.Add(length), cancellationToken);
			next.CopiedFromPeriodId = from.IncidentOperationalPeriodId;
			next = await _periods.SaveAsync(actor, next, next.RowVersion, cancellationToken);

			// Open objectives carry forward; met and dropped ones stay with their period.
			foreach (var objective in await _objectives.ListAsync(actor, IncidentLedgerQuery.ForActivation(from.IncidentActivationId).Where("IncidentOperationalPeriodId", fromPeriodId), cancellationToken))
			{
				if (objective.Status is not ((int)IncidentObjectiveStatuses.Open or (int)IncidentObjectiveStatuses.Carried))
					continue;
				await _objectives.SaveAsync(actor, new IncidentObjective
				{
					IncidentActivationId = objective.IncidentActivationId,
					IncidentOperationalPeriodId = next.IncidentOperationalPeriodId,
					Number = objective.Number,
					Text = objective.Text,
					Status = (int)IncidentObjectiveStatuses.Open,
					CarriedFromObjectiveId = objective.IncidentObjectiveId
				}, null, cancellationToken);
			}

			// The organization chart copies node for node (parents remapped).
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var nodes = await _nodes.ListAsync(actor, IncidentLedgerQuery.ForActivation(from.IncidentActivationId).Where("IncidentOperationalPeriodId", fromPeriodId).Order("SortOrder").Page(0, 2000), cancellationToken);
			foreach (var node in nodes.OrderBy(n => n.ParentNodeId == null ? 0 : 1))
			{
				var copy = await _nodes.SaveAsync(actor, new IncidentOrganizationNode
				{
					IncidentActivationId = node.IncidentActivationId,
					IncidentOperationalPeriodId = next.IncidentOperationalPeriodId,
					Kind = node.Kind,
					Name = node.Name,
					Section = node.Section,
					SortOrder = node.SortOrder,
					ParentNodeId = node.ParentNodeId != null && map.TryGetValue(node.ParentNodeId, out var parent) ? parent : null
				}, null, cancellationToken);
				map[node.IncidentOrganizationNodeId] = copy.IncidentOrganizationNodeId;
			}

			// Period-scoped assignments still running at the boundary continue into the next period; effective dating preserves history.
			var assignments = await _assignments.ListAsync(actor, IncidentLedgerQuery.ForActivation(from.IncidentActivationId).Where("IncidentOperationalPeriodId", fromPeriodId).Page(0, 5000), cancellationToken);
			foreach (var assignment in assignments.Where(a => a.EndOn == null || a.EndOn > from.EndOn))
			{
				var copy = assignment.CloneJson();
				copy.IncidentPositionAssignmentId = null;
				copy.IncidentOperationalPeriodId = next.IncidentOperationalPeriodId;
				copy.IncidentOrganizationNodeId = copy.IncidentOrganizationNodeId != null && map.TryGetValue(copy.IncidentOrganizationNodeId, out var node) ? node : null;
				copy.StartOn = next.StartOn;
				copy.EndOn = assignment.EndOn;
				copy.RowVersion = 1;
				copy.AddedOn = default;
				await _assignments.SaveAsync(actor, copy, null, cancellationToken);
			}

			return next;
		}

		public async Task<IncidentOperationalPeriod> FreezeAsync(IncidentActor actor, string periodId, bool approve, CancellationToken cancellationToken = default)
		{
			var period = await _periods.GetAsync(actor, periodId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			period.IsFrozen = true;
			period.FrozenOn ??= DateTime.UtcNow;
			period.FrozenByUserId ??= actor.UserId;
			if (approve)
			{
				period.ApprovedOn = DateTime.UtcNow;
				period.ApprovedByUserId = actor.UserId;
			}
			return await _periods.SaveAsync(actor, period, period.RowVersion, cancellationToken);
		}

		public async Task<IncidentPlanVersion> AssemblePlanAsync(IncidentActor actor, string periodId, IncidentPlanManifest manifest, CancellationToken cancellationToken = default)
		{
			var period = await _periods.GetAsync(actor, periodId, false, cancellationToken) ?? throw new IncidentRuleException("RecordNotFound");
			var versions = await _plans.ListAsync(actor, IncidentLedgerQuery.ForActivation(period.IncidentActivationId).Where("IncidentOperationalPeriodId", periodId), cancellationToken);
			manifest ??= new IncidentPlanManifest();
			return await _plans.SaveAsync(actor, new IncidentPlanVersion
			{
				IncidentActivationId = period.IncidentActivationId,
				IncidentOperationalPeriodId = periodId,
				VersionNumber = versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1,
				Status = (int)IncidentPlanVersionStatuses.Assembled,
				ManifestJson = JsonConvert.SerializeObject(new { manifest.Items, manifest.Maps }),
				DistributionJson = JsonConvert.SerializeObject(manifest.Distribution),
				PrintCopies = Math.Max(0, manifest.PrintCopies)
			}, null, cancellationToken);
		}

		public async Task<IncidentPlanVersion> ApprovePlanAsync(IncidentActor actor, string planVersionId, CancellationToken cancellationToken = default)
		{
			var plan = await _plans.TransitionAsync(actor, planVersionId, (int)IncidentPlanVersionStatuses.Approved, null, null, cancellationToken);
			plan.ApprovedOn = DateTime.UtcNow;
			plan.ApprovedByUserId = actor.UserId;
			plan = await _plans.SaveAsync(actor, plan, plan.RowVersion, cancellationToken);

			// Earlier approved or distributed versions of the same period are superseded by this one.
			foreach (var older in await _plans.ListAsync(actor, IncidentLedgerQuery.ForActivation(plan.IncidentActivationId).Where("IncidentOperationalPeriodId", plan.IncidentOperationalPeriodId), cancellationToken))
				if (older.IncidentPlanVersionId != plan.IncidentPlanVersionId && older.Status is (int)IncidentPlanVersionStatuses.Approved or (int)IncidentPlanVersionStatuses.Distributed)
				{
					await _plans.TransitionAsync(actor, older.IncidentPlanVersionId, (int)IncidentPlanVersionStatuses.Superseded, null, null, cancellationToken);
					var superseded = await _plans.GetAsync(actor, older.IncidentPlanVersionId, false, cancellationToken);
					superseded.SupersededByPlanVersionId = plan.IncidentPlanVersionId;
					await _plans.SaveAsync(actor, superseded, superseded.RowVersion, cancellationToken);
				}

			// §9.21: the weather block is snapshotted at approval with the alerts in effect then.
			var block = (await _weather.ListAsync(actor, IncidentLedgerQuery.ForActivation(plan.IncidentActivationId).Where("IncidentPlanVersionId", plan.IncidentPlanVersionId), cancellationToken)).FirstOrDefault()
				?? new IncidentPlanWeatherBlock { IncidentActivationId = plan.IncidentActivationId, IncidentPlanVersionId = plan.IncidentPlanVersionId, IncidentOperationalPeriodId = plan.IncidentOperationalPeriodId };
			try
			{
				var alerts = await _weatherAlerts.GetActiveAlertsByDepartmentIdAsync(actor.DepartmentId);
				block.ActiveAlertIdsJson = JsonConvert.SerializeObject(alerts?.Select(a => a.WeatherAlertId.ToString()).ToList() ?? new List<string>());
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
			}
			block.SnapshottedOn = DateTime.UtcNow;
			await _weather.SaveAsync(actor, block, (block as IIncidentVersioned)?.RowVersion, cancellationToken);

			var manifest = string.IsNullOrWhiteSpace(plan.ManifestJson) ? null : JsonConvert.DeserializeObject<IncidentPlanManifest>(plan.ManifestJson);
			var period = await _periods.GetAsync(actor, plan.IncidentOperationalPeriodId, false, cancellationToken);
			var activation = await _activations.GetAsync(actor, plan.IncidentActivationId, false, cancellationToken);
			await _events.PublishAsync(actor.DepartmentId, activation, WorkflowTriggerEventType.IncidentPlanApproved, new Dictionary<string, object>
			{
				["period.number"] = period?.Number,
				["plan.version"] = plan.VersionNumber,
				["plan.record_id"] = plan.RecordId,
				["plan.form_count"] = manifest?.Items?.Count ?? 0,
				["plan.distribution_count"] = string.IsNullOrWhiteSpace(plan.DistributionJson) ? 0 : JsonConvert.DeserializeObject<List<string>>(plan.DistributionJson)?.Count ?? 0
			}, plan.TableName, plan.IncidentPlanVersionId, cancellationToken);
			return plan;
		}
	}
}
