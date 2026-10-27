using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// The Incident Back Office worker passes (registry §4C: 74, 75, 52-55, 57, 58). Each walks the open activations of departments
	/// with the module switched on (flag and switch only — workers never call billing), acts as the system actor, and is safe to run
	/// every minute: evaluated triggers go through <see cref="IncidentAlertLatch"/> so each start and each recovery is emitted once,
	/// and the daily cost pass is idempotent per source row. One activation's failure is logged and the pass continues.
	/// </summary>
	public sealed class IncidentAutomationService : IIncidentAutomationService
	{
		private static readonly int[] OnIncident = { (int)IncidentSupportStatuses.Arrived, (int)IncidentSupportStatuses.Supported, (int)IncidentSupportStatuses.PendingDemob };
		private static readonly int[] Inbound = { (int)IncidentSupportStatuses.Mobilizing, (int)IncidentSupportStatuses.EnRoute };

		private readonly IIncidentLookupRepository _lookup;
		private readonly IIncidentBackOfficeAccessService _access;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentEventPublisher _publisher;
		private readonly IncidentAlertLatch _latch;
		private readonly IIncidentOperationalIntelligenceService _intelligence;
		private readonly IIncidentParticipantsService _participantsService;
		private readonly IIncidentOrganizationService _organization;
		private readonly IIncidentNotificationRoutingService _routing;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly IIncidentLedgerRepository<IncidentOperationalPeriod> _periods;
		private readonly IIncidentLedgerRepository<IncidentParticipant> _participants;
		private readonly IIncidentLedgerRepository<IncidentResource> _resources;
		private readonly IIncidentLedgerRepository<IncidentRequestFill> _fills;
		private readonly IIncidentLedgerRepository<IncidentDemobPlan> _demob;
		private readonly IIncidentLedgerRepository<IncidentDemobChecklistItem> _demobItems;
		private readonly IIncidentLedgerRepository<IncidentFacility> _facilities;
		private readonly IIncidentLedgerRepository<IncidentFacilityCapacity> _capacities;
		private readonly IIncidentLedgerRepository<IncidentLodgingBed> _beds;
		private readonly IIncidentLedgerRepository<IncidentLodgingNightInventory> _nights;
		private readonly IIncidentLedgerRepository<IncidentLodgingBlock> _blocks;
		private readonly IIncidentLedgerRepository<IncidentMealPlan> _mealPlans;
		private readonly IIncidentLedgerRepository<IncidentMealDemand> _mealDemands;
		private readonly IIncidentLedgerRepository<IncidentMealOrder> _mealOrders;
		private readonly IIncidentLedgerRepository<IncidentWaterPlan> _waterPlans;
		private readonly IIncidentLedgerRepository<IncidentWaterStock> _waterStock;
		private readonly IIncidentLedgerRepository<VendorComplianceDocument> _compliance;
		private readonly IIncidentLedgerRepository<IncidentVendorCapacityCommitment> _holds;
		private readonly IIncidentLedgerRepository<IncidentBudgetLine> _budgetLines;
		private readonly IIncidentLedgerRepository<IncidentCommitment> _commitments;
		private readonly IIncidentLedgerRepository<VendorAgreement> _agreements;
		private readonly IIncidentLedgerRepository<IncidentTimeRecord> _time;
		private readonly IIncidentLedgerRepository<IncidentEquipmentUseRecord> _equipmentUse;
		private readonly IIncidentLedgerRepository<IncidentExpense> _expenses;
		private readonly IIncidentLedgerRepository<IncidentRateSnapshot> _rates;
		private readonly IIncidentLedgerRepository<IncidentCostTransaction> _costs;
		private readonly IIncidentLedgerRepository<IncidentAccrualSnapshot> _accruals;
		private readonly IIncidentLedgerRepository<IncidentCostProjection> _projections;
		private readonly IIncidentLedgerRepository<IncidentCostShareAgreement> _shareAgreements;
		private readonly IIncidentLedgerRepository<IncidentCostShareRule> _shareRules;
		private readonly IIncidentLedgerRepository<IncidentApportionmentRun> _runs;
		private readonly IIncidentLedgerService<IncidentApportionmentRun> _runService;
		private readonly IIncidentLedgerRepository<IncidentCostAllocation> _allocations;
		private readonly IIncidentLedgerRepository<IncidentCommunicationsResourceAssignment> _custody;
		private readonly IIncidentLedgerRepository<IncidentIntakeItem> _intake;
		private readonly IIncidentLedgerRepository<IncidentRecoveryDeadline> _recoveryDeadlines;
		private readonly IIncidentLedgerRepository<IncidentDisasterDeclaration> _declarations;
		private readonly IIncidentLedgerRepository<IncidentClaim> _claims;
		private readonly IIncidentLedgerRepository<IncidentOpenAction> _actions;
		private readonly IIncidentLedgerRepository<IncidentDecision> _decisions;
		private readonly IIncidentLedgerRepository<CoordinationBoard> _boards;
		private readonly IIncidentLedgerRepository<CoordinationBoardLane> _lanes;
		private readonly IIncidentLedgerRepository<DepartmentIncidentTeam> _teams;
		private readonly IIncidentLedgerRepository<IncidentOperationalSnapshot> _snapshots;
		private readonly IIncidentLedgerRepository<IncidentMetricValue> _metrics;

		public IncidentAutomationService(IIncidentLookupRepository lookup, IIncidentBackOfficeAccessService access, IIncidentBackOfficeSettingsService settings,
			IIncidentEventPublisher publisher, IncidentAlertLatch latch, IIncidentOperationalIntelligenceService intelligence, IIncidentParticipantsService participantsService,
			IIncidentOrganizationService organization, IIncidentNotificationRoutingService routing,
			IIncidentLedgerRepository<IncidentActivation> activations, IIncidentLedgerRepository<IncidentOperationalPeriod> periods,
			IIncidentLedgerRepository<IncidentParticipant> participants, IIncidentLedgerRepository<IncidentResource> resources, IIncidentLedgerRepository<IncidentRequestFill> fills,
			IIncidentLedgerRepository<IncidentDemobPlan> demob, IIncidentLedgerRepository<IncidentDemobChecklistItem> demobItems,
			IIncidentLedgerRepository<IncidentFacility> facilities, IIncidentLedgerRepository<IncidentFacilityCapacity> capacities,
			IIncidentLedgerRepository<IncidentLodgingBed> beds, IIncidentLedgerRepository<IncidentLodgingNightInventory> nights, IIncidentLedgerRepository<IncidentLodgingBlock> blocks,
			IIncidentLedgerRepository<IncidentMealPlan> mealPlans, IIncidentLedgerRepository<IncidentMealDemand> mealDemands, IIncidentLedgerRepository<IncidentMealOrder> mealOrders,
			IIncidentLedgerRepository<IncidentWaterPlan> waterPlans, IIncidentLedgerRepository<IncidentWaterStock> waterStock,
			IIncidentLedgerRepository<VendorComplianceDocument> compliance, IIncidentLedgerRepository<IncidentVendorCapacityCommitment> holds,
			IIncidentLedgerRepository<IncidentBudgetLine> budgetLines, IIncidentLedgerRepository<IncidentCommitment> commitments, IIncidentLedgerRepository<VendorAgreement> agreements,
			IIncidentLedgerRepository<IncidentTimeRecord> time, IIncidentLedgerRepository<IncidentEquipmentUseRecord> equipmentUse, IIncidentLedgerRepository<IncidentExpense> expenses,
			IIncidentLedgerRepository<IncidentRateSnapshot> rates, IIncidentLedgerRepository<IncidentCostTransaction> costs, IIncidentLedgerRepository<IncidentAccrualSnapshot> accruals,
			IIncidentLedgerRepository<IncidentCostProjection> projections, IIncidentLedgerRepository<IncidentCostShareAgreement> shareAgreements,
			IIncidentLedgerRepository<IncidentCostShareRule> shareRules, IIncidentLedgerRepository<IncidentApportionmentRun> runs, IIncidentLedgerService<IncidentApportionmentRun> runService,
			IIncidentLedgerRepository<IncidentCostAllocation> allocations, IIncidentLedgerRepository<IncidentCommunicationsResourceAssignment> custody,
			IIncidentLedgerRepository<IncidentIntakeItem> intake, IIncidentLedgerRepository<IncidentRecoveryDeadline> recoveryDeadlines,
			IIncidentLedgerRepository<IncidentDisasterDeclaration> declarations, IIncidentLedgerRepository<IncidentClaim> claims,
			IIncidentLedgerRepository<IncidentOpenAction> actions, IIncidentLedgerRepository<IncidentDecision> decisions,
			IIncidentLedgerRepository<CoordinationBoard> boards, IIncidentLedgerRepository<CoordinationBoardLane> lanes, IIncidentLedgerRepository<DepartmentIncidentTeam> teams,
			IIncidentLedgerRepository<IncidentOperationalSnapshot> snapshots, IIncidentLedgerRepository<IncidentMetricValue> metrics)
		{
			_lookup = lookup;
			_access = access;
			_settings = settings;
			_publisher = publisher;
			_latch = latch;
			_intelligence = intelligence;
			_participantsService = participantsService;
			_organization = organization;
			_routing = routing;
			_activations = activations;
			_periods = periods;
			_participants = participants;
			_resources = resources;
			_fills = fills;
			_demob = demob;
			_demobItems = demobItems;
			_facilities = facilities;
			_capacities = capacities;
			_beds = beds;
			_nights = nights;
			_blocks = blocks;
			_mealPlans = mealPlans;
			_mealDemands = mealDemands;
			_mealOrders = mealOrders;
			_waterPlans = waterPlans;
			_waterStock = waterStock;
			_compliance = compliance;
			_holds = holds;
			_budgetLines = budgetLines;
			_commitments = commitments;
			_agreements = agreements;
			_time = time;
			_equipmentUse = equipmentUse;
			_expenses = expenses;
			_rates = rates;
			_costs = costs;
			_accruals = accruals;
			_projections = projections;
			_shareAgreements = shareAgreements;
			_shareRules = shareRules;
			_runs = runs;
			_runService = runService;
			_allocations = allocations;
			_custody = custody;
			_intake = intake;
			_recoveryDeadlines = recoveryDeadlines;
			_declarations = declarations;
			_claims = claims;
			_actions = actions;
			_decisions = decisions;
			_boards = boards;
			_lanes = lanes;
			_teams = teams;
			_snapshots = snapshots;
			_metrics = metrics;
		}

		#region Scope

		private static IncidentLedgerQuery In(string activationId) => new IncidentLedgerQuery { ActivationIds = new[] { activationId }, Take = 10000 };

		/// <summary>Runs <paramref name="pass"/> for every open activation of an enabled department; returns how many ran.</summary>
		private async Task<int> ForEachActivationAsync(Func<IncidentActor, IncidentActivation, Task> pass, bool businessOnly, bool coordinationOnly, CancellationToken cancellationToken)
		{
			var count = 0;
			foreach (var department in (await _lookup.GetOpenActivationsAsync(cancellationToken)).GroupBy(a => a.DepartmentId))
			{
				if (cancellationToken.IsCancellationRequested) break;
				try
				{
					if (!await _access.IsEnabledAsync(department.Key)) continue;
					if (businessOnly && !await _access.IsBusinessEnabledAsync(department.Key)) continue;
					if (coordinationOnly && !await _access.IsCoordinationEnabledAsync(department.Key)) continue;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Incident Back Office access check failed for department {department.Key}");
					continue;
				}
				var actor = IncidentActor.System(department.Key);
				foreach (var reference in department)
				{
					try
					{
						var activation = await _activations.GetForDepartmentAsync(department.Key, reference.IncidentActivationId, false, cancellationToken);
						if (activation == null) continue;
						await pass(actor, activation);
						count++;
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex, $"Incident Back Office pass failed for activation {reference.IncidentActivationId}");
					}
				}
			}
			return count;
		}

		/// <summary>Publishes on an incident latch edge (true = started, false = resolved).</summary>
		private async Task EdgeAsync(IncidentActivation activation, WorkflowTriggerEventType trigger, string subject, bool active, Func<Dictionary<string, object>> variables,
			string aggregateType, string aggregateId, CancellationToken cancellationToken)
		{
			var edge = await _latch.SetAsync(activation.DepartmentId, activation.IncidentActivationId, IncidentAlertLatch.Key(trigger, subject), active, cancellationToken);
			if (!edge.HasValue)
				return;
			var payload = variables();
			payload["resolved"] = !edge.Value;
			await _publisher.PublishAsync(activation.DepartmentId, activation, trigger, payload, aggregateType, aggregateId, cancellationToken);
		}

		/// <summary>Population on the incident at <paramref name="atUtc"/>: arrived and supported, plus inbound expected by then.</summary>
		private static int PopulationAt(IReadOnlyList<IncidentParticipant> participants, DateTime atUtc, Func<IncidentParticipant, bool> needs) =>
			participants.Count(p => !p.IsDraft && needs(p) && (OnIncident.Contains(p.SupportStatus) || (Inbound.Contains(p.SupportStatus) && p.ExpectedArrivalOn.HasValue && p.ExpectedArrivalOn <= atUtc))
				&& (!p.TentativeReleaseOn.HasValue || p.TentativeReleaseOn > atUtc));

		#endregion

		#region 74 / 75 snapshots

		public Task<int> ProjectSnapshotsAsync(CancellationToken cancellationToken = default) =>
			ForEachActivationAsync((actor, activation) => _intelligence.ProjectSnapshotAsync(actor, activation.IncidentActivationId, IncidentSnapshotKinds.Incremental, cancellationToken), false, false, cancellationToken);

		public Task<int> ReconcileSnapshotsAsync(CancellationToken cancellationToken = default) =>
			ForEachActivationAsync((actor, activation) => _intelligence.ProjectSnapshotAsync(actor, activation.IncidentActivationId, IncidentSnapshotKinds.Rebuild, cancellationToken), false, false, cancellationToken);

		#endregion

		#region 52 demand forecast

		public Task<int> ForecastDemandAsync(CancellationToken cancellationToken = default) => ForEachActivationAsync(async (actor, activation) =>
		{
			var departmentId = activation.DepartmentId;
			var forecast = await _settings.GetAsync<IncidentForecastPolicy>(departmentId);
			var enrollment = await _settings.GetAsync<IncidentEnrollmentPolicy>(departmentId);
			var today = DateTime.UtcNow.Date;
			var plans = (await _mealPlans.ListAsync(departmentId, In(activation.IncidentActivationId).Between("ServiceDate", today, today.AddDays(3)), cancellationToken))
				.Where(p => p.Status is (int)IncidentMealPlanStatuses.Planned or (int)IncidentMealPlanStatuses.Active).ToList();
			if (plans.Count == 0)
				return;
			var participants = (await _participants.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken)).ToList();
			var resources = (await _resources.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken)).ToList();
			var unregistered = enrollment.UnregisteredForecastBand && enrollment.Mode != (int)IncidentEnrollmentPolicies.Off
				? (await _participantsService.GetEnrollmentQueueAsync(actor, activation.IncidentActivationId, cancellationToken)).Count : 0;
			foreach (var plan in plans)
			{
				var at = plan.WindowStart;
				var population = PopulationAt(participants, at, p => p.NeedsMeals && !p.IsSelfSufficient);
				var crews = resources.Where(r => r.Kind is (int)IncidentResourceKinds.Crew or (int)IncidentResourceKinds.Team && r.LeaderParticipantId == null
					&& (OnIncident.Contains(r.SupportStatus) || (Inbound.Contains(r.SupportStatus) && r.ArrivedOn == null))).Sum(r => r.PersonnelCount);
				var departures = participants.Count(p => p.NeedsMeals && OnIncident.Contains(p.SupportStatus) && p.TentativeReleaseOn.HasValue && p.TentativeReleaseOn <= at);
				var predictedTimeouts = forecast.PredictedTimeoutsReduceForecast
					? participants.Count(p => p.NeedsMeals && OnIncident.Contains(p.SupportStatus) && p.TimeoutOn.HasValue && p.TimeoutOn.Value < at.Date) : 0;
				var baseCount = population + crews + unregistered - predictedTimeouts;
				var count = (int)Math.Ceiling(Math.Max(0, baseCount) * (1 + forecast.MealContingencyPercent / 100m));
				var latest = (await _mealDemands.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentMealPlanId", plan.IncidentMealPlanId).Order("Version", true).Page(0, 1), cancellationToken)).FirstOrDefault();
				if (latest != null && latest.ForecastCount == count && latest.ManualOverride == null)
					continue;
				await _mealDemands.InsertNewAsync(new IncidentMealDemand
				{
					DepartmentId = departmentId,
					IncidentActivationId = activation.IncidentActivationId,
					IncidentMealPlanId = plan.IncidentMealPlanId,
					Version = (latest?.Version ?? 0) + 1,
					MetricVersion = 1,
					Population = population,
					Inbound = crews,
					Departures = departures,
					UnregisteredBand = unregistered,
					PredictedTimeoutBand = predictedTimeouts,
					ContingencyPercent = forecast.MealContingencyPercent,
					ForecastCount = count,
					ManualOverride = latest?.ManualOverride,
					OverrideReason = latest?.OverrideReason,
					InputsJson = JsonConvert.SerializeObject(new { population, crews, departures, unregistered, predictedTimeouts, forecast.MealContingencyPercent })
				}, null, cancellationToken);
			}
		}, false, false, cancellationToken);

		#endregion

		#region 53 vendor validation

		public async Task<int> ValidateVendorsAsync(CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var departmentId in await _lookup.GetDepartmentsWithRowsAsync("VendorComplianceDocuments", cancellationToken))
			{
				try
				{
					if (!await _access.IsEnabledAsync(departmentId)) continue;
					var policy = await _settings.GetAsync<IncidentVendorPolicy>(departmentId);
					var today = DateTime.UtcNow.Date;
					var documents = (await _compliance.ListAsync(departmentId, new IncidentLedgerQuery { Take = 10000 }.WhereNotNull("ExpiresOn"), cancellationToken)).ToList();
					var labels = (await _lookup.GetLabelsAsync("InventoryVendors", departmentId, documents.Select(d => d.VendorId).Distinct().ToList(), cancellationToken))
						.ToDictionary(r => r.Id, r => r.Label, StringComparer.OrdinalIgnoreCase);
					foreach (var document in documents)
					{
						var expires = document.ExpiresOn.Value.Date;
						if (expires < today && document.Status != (int)IncidentCatalogStatuses.Expired)
						{
							document.Status = (int)IncidentCatalogStatuses.Expired;
							await _compliance.UpdateVersionedAsync(document, document.RowVersion, null, cancellationToken);
						}
						var expiring = expires <= today.AddDays(policy.ComplianceLeadDays) && document.Status != (int)IncidentCatalogStatuses.Suspended;
						var edge = await _latch.SetDepartmentAsync(departmentId, IncidentAlertLatch.Key(WorkflowTriggerEventType.IncidentVendorComplianceExpiring, document.VendorComplianceDocumentId), expiring);
						if (!edge.HasValue) continue;
						await _publisher.PublishAsync(departmentId, null, WorkflowTriggerEventType.IncidentVendorComplianceExpiring, new Dictionary<string, object>
						{
							["vendor.name"] = labels.TryGetValue(document.VendorId, out var name) ? name : null,
							["item.kind"] = Enum.GetName(typeof(IncidentComplianceDocumentKinds), document.Kind),
							["expires.on"] = expires,
							["days_remaining"] = (int)(expires - today).TotalDays,
							["resolved"] = !edge.Value
						}, document.TableName, document.VendorComplianceDocumentId, cancellationToken);
					}
					count++;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Incident Back Office vendor validation failed for department {departmentId}");
				}
			}
			return count;
		}

		#endregion

		#region 54 hold expiry

		public Task<int> ExpireHoldsAsync(CancellationToken cancellationToken = default) => ForEachActivationAsync(async (actor, activation) =>
		{
			var departmentId = activation.DepartmentId;
			var policy = await _settings.GetAsync<IncidentVendorPolicy>(departmentId);
			var now = DateTime.UtcNow;
			var holds = (await _holds.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Kind", (int)IncidentCommitmentKinds.Hold).Where("Status", (int)IncidentHoldStatuses.Active).WhereNotNull("ExpiresOn"), cancellationToken)).ToList();
			foreach (var hold in holds)
			{
				if (hold.ExpiresOn <= now)
				{
					hold.Status = (int)IncidentHoldStatuses.Expired;
					await _holds.UpdateVersionedAsync(hold, hold.RowVersion, null, cancellationToken);
				}
				var expiring = hold.Status == (int)IncidentHoldStatuses.Active && hold.ExpiresOn <= now.AddHours(policy.HoldLeadHours);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentHoldExpiring, hold.IncidentVendorCapacityCommitmentId, expiring, () => new Dictionary<string, object>
				{
					["hold.kind"] = "VendorCapacity",
					["capacity.held"] = hold.Quantity,
					["capacity.unit"] = hold.Unit,
					["expires.on"] = hold.ExpiresOn
				}, hold.TableName, hold.IncidentVendorCapacityCommitmentId, cancellationToken);
			}

			var blocks = (await _blocks.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentLodgingBlockStatuses.Held).WhereNotNull("CutoffOn"), cancellationToken)).ToList();
			foreach (var block in blocks)
			{
				var expiring = block.CutoffOn <= now.AddHours(policy.HoldLeadHours);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentHoldExpiring, block.IncidentLodgingBlockId, expiring, () => new Dictionary<string, object>
				{
					["hold.kind"] = "LodgingBlock",
					["capacity.held"] = block.RoomsBlocked - block.RoomsConfirmed,
					["capacity.unit"] = "rooms",
					["expires.on"] = block.CutoffOn
				}, block.TableName, block.IncidentLodgingBlockId, cancellationToken);
			}
		}, false, false, cancellationToken);

		#endregion

		#region 55 alert evaluation

		public async Task<int> EvaluateAlertsAsync(CancellationToken cancellationToken = default)
		{
			var count = await ForEachActivationAsync(async (actor, activation) =>
			{
				await ArrivalsAsync(activation, cancellationToken);
				await DemobAsync(activation, cancellationToken);
				await FacilitiesAsync(activation, cancellationToken);
				await LodgingAsync(activation, cancellationToken);
				await MealsAndWaterAsync(activation, cancellationToken);
				await TimeoutsAsync(activation, cancellationToken);
				await CustodyAsync(activation, cancellationToken);
				await EnrollmentBacklogAsync(actor, activation, cancellationToken);
				if (await _access.IsBusinessEnabledAsync(activation.DepartmentId))
				{
					await BudgetsAsync(activation, cancellationToken);
					await TimeMissingAsync(activation, cancellationToken);
					await ShiftTicketsAsync(activation, cancellationToken);
					await IntakeBacklogAsync(activation, cancellationToken);
					await DeadlinesAsync(actor, activation, cancellationToken);
				}
				if (await _access.IsCoordinationEnabledAsync(activation.DepartmentId))
					await CoordinationItemsAsync(activation, cancellationToken);
			}, false, false, cancellationToken);
			count += await CoordinationLanesAsync(cancellationToken);
			count += await TeamReadinessAsync(cancellationToken);
			return count;
		}

		/// <summary>122: a fill's ETA is inside the lead window, or it is late; resolves on arrival.</summary>
		private async Task ArrivalsAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentForecastPolicy>(activation.DepartmentId);
			var now = DateTime.UtcNow;
			var fills = (await _fills.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).WhereIn("Status",
				new[] { (int)IncidentFillStatuses.Accepted, (int)IncidentFillStatuses.EnRoute, (int)IncidentFillStatuses.Arrived }), cancellationToken)).ToList();
			foreach (var fill in fills.Where(f => f.EtaOn.HasValue))
			{
				var due = fill.Status != (int)IncidentFillStatuses.Arrived && fill.EtaOn <= now.AddMinutes(policy.ArrivalLeadMinutes);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentResourceArrivalDue, fill.IncidentRequestFillId, due, () => new Dictionary<string, object>
				{
					["resource.name"] = fill.Provider,
					["resource.kind"] = Enum.GetName(typeof(IncidentResourceKinds), fill.ResourceKind),
					["eta.utc"] = fill.EtaOn,
					["eta.confidence"] = Enum.GetName(typeof(IncidentEtaConfidences), fill.EtaConfidence),
					["eta.is_late"] = fill.EtaOn < now
				}, fill.TableName, fill.IncidentRequestFillId, cancellationToken);
			}
		}

		/// <summary>124: an approved release within a day still has an open blocking checklist item.</summary>
		private async Task DemobAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var plans = (await _demob.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentDemobStatuses.Approved), cancellationToken)).ToList();
			if (plans.Count == 0)
				return;
			var items = (await _demobItems.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).Where("IsBlocking", true).Where("Status", (int)IncidentGateItemStatuses.Open), cancellationToken)).ToList();
			foreach (var plan in plans)
			{
				var release = plan.ApprovedReleaseOn ?? plan.TentativeReleaseOn;
				var blocker = items.FirstOrDefault(i => i.IncidentDemobPlanId == plan.IncidentDemobPlanId);
				var blocked = blocker != null && release.HasValue && release <= now.AddHours(24);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentDemobBlocked, plan.IncidentDemobPlanId, blocked, () => new Dictionary<string, object>
				{
					["blocker.category"] = blocker == null ? null : Enum.GetName(typeof(IncidentDemobCategories), blocker.Category),
					["blocker.item"] = blocker?.Name,
					["blocker.owner"] = blocker?.OwnerPosition,
					["demob.planned_release"] = release
				}, plan.TableName, plan.IncidentDemobPlanId, cancellationToken);
			}
		}

		/// <summary>126: a facility capacity at or over its threshold (row threshold, else setting 88).</summary>
		private async Task FacilitiesAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentForecastPolicy>(activation.DepartmentId);
			var capacities = (await _capacities.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId), cancellationToken)).ToList();
			if (capacities.Count == 0)
				return;
			var facilities = (await _facilities.GetByIdsAsync(activation.DepartmentId, capacities.Select(c => c.IncidentFacilityId), cancellationToken)).ToDictionary(f => f.IncidentFacilityId, StringComparer.OrdinalIgnoreCase);
			foreach (var capacity in capacities)
			{
				var usable = capacity.UsableCapacity - capacity.OutOfService;
				var percent = usable <= 0 ? (capacity.CurrentUse + capacity.Reserved > 0 ? 100m : 0m) : Math.Round((capacity.CurrentUse + capacity.Reserved) / usable * 100m, 1);
				var over = percent >= (capacity.ThresholdPercent ?? policy.FacilityThresholdPercent);
				facilities.TryGetValue(capacity.IncidentFacilityId, out var facility);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentFacilityCapacityThreshold, capacity.IncidentFacilityCapacityId, over, () => new Dictionary<string, object>
				{
					["facility.name"] = facility?.Name,
					["facility.type"] = facility == null ? null : Enum.GetName(typeof(IncidentFacilityTypes), facility.FacilityType),
					["service.kind"] = Enum.GetName(typeof(IncidentFacilityServiceKinds), capacity.ServiceKind),
					["capacity.max"] = capacity.MaxCapacity,
					["capacity.used"] = capacity.CurrentUse,
					["capacity.reserved"] = capacity.Reserved,
					["capacity.utilization_percent"] = percent
				}, capacity.TableName, capacity.IncidentFacilityCapacityId, cancellationToken);
			}
		}

		/// <summary>127: for tonight and the next two nights, people needing lodging exceed the usable beds less the reserve (setting 88).</summary>
		private async Task LodgingAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentForecastPolicy>(activation.DepartmentId);
			var beds = (await _beds.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId), cancellationToken)).Count(b => !b.IsOutOfService);
			var participants = (await _participants.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).Where("NeedsLodging", true), cancellationToken)).ToList();
			if (beds == 0 && participants.Count == 0)
				return;
			var available = (int)Math.Floor(beds * (1 - policy.BedReservePercent / 100m));
			for (var offset = 0; offset < 3; offset++)
			{
				var night = DateTime.UtcNow.Date.AddDays(offset);
				var required = PopulationAt(participants, night.AddHours(20), p => true);
				var shortfall = required - available;
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentLodgingShortfall, "n" + offset.ToString(CultureInfo.InvariantCulture), shortfall > 0, () => new Dictionary<string, object>
				{
					["night.date"] = night,
					["beds.required"] = required,
					["beds.available"] = available,
					["beds.shortfall"] = Math.Max(0, shortfall)
				}, "IncidentActivations", activation.IncidentActivationId, cancellationToken);
			}
		}

		/// <summary>129: a meal cutoff inside the lead window with orders below the forecast; 130: water below its reorder or days-of-supply threshold.</summary>
		private async Task MealsAndWaterAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var policy = await _settings.GetAsync<IncidentForecastPolicy>(departmentId);
			var now = DateTime.UtcNow;
			var plans = (await _mealPlans.ListAsync(departmentId, In(activation.IncidentActivationId).WhereNotNull("CutoffOn").Between("ServiceDate", now.Date.AddDays(-1), now.Date.AddDays(3)), cancellationToken))
				.Where(p => p.Status is (int)IncidentMealPlanStatuses.Planned or (int)IncidentMealPlanStatuses.Active).ToList();
			foreach (var plan in plans)
			{
				var demand = (await _mealDemands.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentMealPlanId", plan.IncidentMealPlanId).Order("Version", true).Page(0, 1), cancellationToken)).FirstOrDefault();
				var forecast = demand?.ManualOverride ?? demand?.ForecastCount ?? 0;
				var ordered = (await _mealOrders.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentMealPlanId", plan.IncidentMealPlanId), cancellationToken))
					.Where(o => o.Status != (int)IncidentOrderStatuses.Cancelled).Sum(o => o.OrderedCount);
				var approaching = plan.CutoffOn > now && plan.CutoffOn <= now.AddMinutes(policy.MealCutoffLeadMinutes) && ordered < forecast;
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentMealCutoffApproaching, plan.IncidentMealPlanId, approaching, () => new Dictionary<string, object>
				{
					["meal.window"] = Enum.GetName(typeof(IncidentMealTypes), plan.MealType),
					["site.name"] = plan.SiteName,
					["cutoff.utc"] = plan.CutoffOn,
					["forecast.count"] = forecast,
					["ordered.count"] = ordered,
					["gap"] = forecast - ordered
				}, plan.TableName, plan.IncidentMealPlanId, cancellationToken);
			}

			var waterPlans = (await _waterPlans.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken))
				.Where(w => w.Status is (int)IncidentMealPlanStatuses.Planned or (int)IncidentMealPlanStatuses.Active).ToList();
			if (waterPlans.Count == 0)
				return;
			var participants = (await _participants.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken)).ToList();
			var population = PopulationAt(participants, now, p => !p.IsSelfSufficient);
			foreach (var water in waterPlans)
			{
				var stock = (await _waterStock.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentWaterPlanId", water.IncidentWaterPlanId).Order("MeasuredOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
				if (stock == null)
					continue;
				var onHand = stock.CanonicalQuantity ?? stock.Quantity;
				var daily = population * policy.DrinkingWaterLitresPerPersonDay * (1 + policy.WaterContingencyPercent / 100m);
				var days = daily <= 0 ? (decimal?)null : Math.Round(onHand / daily, 1);
				var reorder = water.ReorderThreshold.HasValue && onHand <= water.ReorderThreshold.Value;
				var supply = days.HasValue && days < (water.DaysOfSupplyThreshold ?? policy.WaterDaysOfSupplyThreshold);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentWaterBelowThreshold, water.IncidentWaterPlanId, reorder || supply, () => new Dictionary<string, object>
				{
					["site.name"] = water.SiteName,
					["on_hand.quantity"] = onHand,
					["on_hand.unit"] = stock.CanonicalUnit ?? stock.UnitOfMeasure,
					["days_of_supply"] = days,
					["threshold.kind"] = reorder ? "Reorder" : "DaysOfSupply"
				}, water.TableName, water.IncidentWaterPlanId, cancellationToken);
			}
		}

		/// <summary>192: a resource or person on the incident reaches its timeout inside the lead window (setting 88).</summary>
		private async Task TimeoutsAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentForecastPolicy>(activation.DepartmentId);
			var horizon = DateTime.UtcNow.Date.AddDays(policy.TimeoutLeadDays);
			foreach (var resource in (await _resources.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).WhereNotNull("TimeoutOn"), cancellationToken)))
			{
				var approaching = OnIncident.Contains(resource.SupportStatus) && resource.TimeoutOn <= horizon;
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentResourceTimeoutApproaching, resource.IncidentResourceId, approaching, () => new Dictionary<string, object>
				{
					["resource.name"] = resource.Name,
					["resource.kind"] = Enum.GetName(typeof(IncidentResourceKinds), resource.Kind),
					["assignment.start_on"] = resource.AssignmentStartOn,
					["timeout.on"] = resource.TimeoutOn
				}, resource.TableName, resource.IncidentResourceId, cancellationToken);
			}
		}

		/// <summary>201: issued custody (radios, kits) still out after its expected return.</summary>
		private async Task CustodyAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			foreach (var item in await _custody.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentCustodyStatuses.Issued).WhereNotNull("ExpectedReturnOn"), cancellationToken))
			{
				var overdue = item.ExpectedReturnOn < now;
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentAccountabilityException, item.IncidentCommunicationsResourceAssignmentId, overdue, () => new Dictionary<string, object>
				{
					["item.kind"] = Enum.GetName(typeof(IncidentCommsItemKinds), item.ItemKind),
					["item.label"] = item.ItemLabel,
					["holder.name"] = item.HolderName,
					["exception.kind"] = "Overdue"
				}, item.TableName, item.IncidentCommunicationsResourceAssignmentId, cancellationToken);
			}
		}

		/// <summary>199: the enrollment queue is longer, or older, than setting 86 allows.</summary>
		private async Task EnrollmentBacklogAsync(IncidentActor actor, IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentEnrollmentPolicy>(activation.DepartmentId);
			if (policy.Mode == (int)IncidentEnrollmentPolicies.Off)
				return;
			var queue = await _participantsService.GetEnrollmentQueueAsync(actor, activation.IncidentActivationId, cancellationToken);
			var oldest = queue.Where(c => c.SeenSince.HasValue).Select(c => (int)(DateTime.UtcNow - c.SeenSince.Value).TotalMinutes).DefaultIfEmpty(0).Max();
			var backlog = queue.Count > policy.BacklogCount || (queue.Count > 0 && oldest > policy.BacklogMinutes);
			await EdgeAsync(activation, WorkflowTriggerEventType.IncidentEnrollmentBacklog, "queue", backlog, () => new Dictionary<string, object>
			{
				["unenrolled.count"] = queue.Count,
				["oldest_minutes"] = oldest,
				["policy"] = Enum.GetName(typeof(IncidentEnrollmentPolicies), policy.Mode)
			}, "IncidentActivations", activation.IncidentActivationId, cancellationToken);
		}

		/// <summary>136: a budget line's committed spend crosses its threshold; 137: an agreement's spend nears its ceiling (setting 96).</summary>
		private async Task BudgetsAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var policy = await _settings.GetAsync<IncidentFinancePolicy>(departmentId);
			var commitments = (await _commitments.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken)).Where(c => c.Status != (int)IncidentCommitmentStatuses.Void).ToList();
			foreach (var line in await _budgetLines.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken))
			{
				if (line.RevisedAmount <= 0) continue;
				var consumed = commitments.Where(c => c.IncidentBudgetLineId == line.IncidentBudgetLineId).Sum(c => c.Amount);
				var percent = Math.Round(consumed / line.RevisedAmount * 100m, 1);
				var over = percent >= (line.ThresholdPercent ?? policy.BudgetThresholdPercent) || (line.FloorAmount.HasValue && line.RevisedAmount - consumed <= line.FloorAmount.Value);
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentBudgetThreshold, line.IncidentBudgetLineId, over, () => new Dictionary<string, object>
				{
					["budget.line"] = line.Name,
					["budget.authorized_amount"] = line.RevisedAmount,
					["budget.consumed_percent"] = percent,
					["budget.remaining_amount"] = line.RevisedAmount - consumed,
					["measure"] = "Committed"
				}, line.TableName, line.IncidentBudgetLineId, cancellationToken);
			}

			var agreementIds = commitments.Where(c => c.VendorAgreementId != null).Select(c => c.VendorAgreementId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (agreementIds.Count == 0)
				return;
			foreach (var agreement in (await _agreements.GetByIdsAsync(departmentId, agreementIds, cancellationToken)).Where(a => a.CeilingAmount > 0))
			{
				var consumed = commitments.Where(c => c.VendorAgreementId == agreement.VendorAgreementId).Sum(c => c.Amount);
				var percent = Math.Round(consumed / agreement.CeilingAmount.Value * 100m, 1);
				var vendor = (await _lookup.GetLabelsAsync("InventoryVendors", departmentId, new[] { agreement.VendorId }, cancellationToken)).FirstOrDefault()?.Label;
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentAgreementCeilingRisk, agreement.VendorAgreementId, percent >= policy.AgreementCeilingThresholdPercent, () => new Dictionary<string, object>
				{
					["vendor.name"] = vendor,
					["agreement.number"] = agreement.AgreementNumber,
					["ceiling.amount"] = agreement.CeilingAmount,
					["consumed_percent"] = percent
				}, agreement.TableName, agreement.VendorAgreementId, cancellationToken);
			}
		}

		/// <summary>142: people on the incident with no time posted for the last completed operational period.</summary>
		private async Task TimeMissingAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var now = DateTime.UtcNow;
			var period = (await _periods.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken)).Where(p => p.EndOn <= now).OrderByDescending(p => p.EndOn).FirstOrDefault();
			if (period == null)
				return;
			var people = (await _participants.ListAsync(departmentId, In(activation.IncidentActivationId), cancellationToken))
				.Where(p => !p.IsDraft && p.PaymentClass != (int)IncidentPaymentClasses.NonReimbursable && p.CheckedInOn.HasValue && p.CheckedInOn < period.EndOn
					&& (!p.ReleasedOn.HasValue || p.ReleasedOn > period.StartOn)).ToList();
			var posted = new HashSet<string>((await _time.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentOperationalPeriodId", period.IncidentOperationalPeriodId), cancellationToken))
				.Where(t => t.IncidentParticipantId != null).Select(t => t.IncidentParticipantId), StringComparer.OrdinalIgnoreCase);
			var missing = people.Where(p => !posted.Contains(p.IncidentParticipantId)).ToList();
			await EdgeAsync(activation, WorkflowTriggerEventType.IncidentTimeMissing, period.IncidentOperationalPeriodId, missing.Count > 0, () => new Dictionary<string, object>
			{
				["period.number"] = period.Number,
				["missing.count"] = missing.Count,
				["subjects.sample"] = string.Join(", ", missing.Take(5).Select(p => p.DisplayName))
			}, period.TableName, period.IncidentOperationalPeriodId, cancellationToken);
		}

		/// <summary>144: contracted equipment on the incident without a shift ticket for yesterday.</summary>
		private async Task ShiftTicketsAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var yesterday = DateTime.UtcNow.Date.AddDays(-1);
			var equipment = (await _resources.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Kind", (int)IncidentResourceKinds.Equipment).Where("PaymentClass", (int)IncidentPaymentClasses.Contractor), cancellationToken))
				.Where(r => OnIncident.Contains(r.SupportStatus) && r.ArrivedOn.HasValue && r.ArrivedOn.Value.Date <= yesterday).ToList();
			if (equipment.Count == 0)
				return;
			var tickets = new HashSet<string>((await _equipmentUse.ListAsync(departmentId, In(activation.IncidentActivationId).Where("ShiftDate", yesterday), cancellationToken))
				.Select(u => u.IncidentResourceId), StringComparer.OrdinalIgnoreCase);
			foreach (var resource in equipment)
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentShiftTicketMissing, resource.IncidentResourceId, !tickets.Contains(resource.IncidentResourceId), () => new Dictionary<string, object>
				{
					["resource.name"] = resource.Name,
					["shift.date"] = yesterday
				}, resource.TableName, resource.IncidentResourceId, cancellationToken);
		}

		/// <summary>206: unfiled intake longer or older than setting 96 allows.</summary>
		private async Task IntakeBacklogAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var policy = await _settings.GetAsync<IncidentFinancePolicy>(activation.DepartmentId);
			var items = (await _intake.ListAsync(activation.DepartmentId, In(activation.IncidentActivationId).WhereIn("Status", new[] { (int)IncidentIntakeStatuses.Unfiled, (int)IncidentIntakeStatuses.Quarantined }), cancellationToken)).ToList();
			var unfiled = items.Where(i => i.Status == (int)IncidentIntakeStatuses.Unfiled).ToList();
			var oldest = unfiled.Select(i => (int)(DateTime.UtcNow - i.ReceivedOn).TotalHours).DefaultIfEmpty(0).Max();
			var backlog = unfiled.Count > policy.IntakeBacklogCount || (unfiled.Count > 0 && oldest > policy.IntakeBacklogHours);
			await EdgeAsync(activation, WorkflowTriggerEventType.IncidentIntakeBacklog, "intake", backlog, () => new Dictionary<string, object>
			{
				["unfiled.count"] = unfiled.Count,
				["oldest_hours"] = oldest,
				["quarantined.count"] = items.Count - unfiled.Count
			}, "IncidentActivations", activation.IncidentActivationId, cancellationToken);
		}

		/// <summary>207: a recovery obligation due inside the lead window; claim deadlines notify the compensation positions (notice 43).</summary>
		private async Task DeadlinesAsync(IncidentActor actor, IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var policy = await _settings.GetAsync<IncidentFinancePolicy>(departmentId);
			var today = DateTime.UtcNow.Date;
			var deadlines = (await _recoveryDeadlines.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentGateItemStatuses.Open), cancellationToken)).ToList();
			if (deadlines.Count > 0)
			{
				var declarations = (await _declarations.GetByIdsAsync(departmentId, deadlines.Select(d => d.IncidentDisasterDeclarationId), cancellationToken)).ToDictionary(d => d.IncidentDisasterDeclarationId, StringComparer.OrdinalIgnoreCase);
				foreach (var deadline in deadlines)
				{
					declarations.TryGetValue(deadline.IncidentDisasterDeclarationId, out var declaration);
					await EdgeAsync(activation, WorkflowTriggerEventType.IncidentRecoveryDeadlineApproaching, deadline.IncidentRecoveryDeadlineId, deadline.DueOn.Date <= today.AddDays(policy.RecoveryDeadlineLeadDays), () => new Dictionary<string, object>
					{
						["declaration.number"] = declaration?.DeclarationNumber,
						["obligation.kind"] = Enum.GetName(typeof(IncidentRecoveryObligationKinds), deadline.Kind),
						["due.on"] = deadline.DueOn,
						["days_remaining"] = (int)(deadline.DueOn.Date - today).TotalDays
					}, deadline.TableName, deadline.IncidentRecoveryDeadlineId, cancellationToken);
				}
			}

			var claims = (await _claims.ListAsync(departmentId, In(activation.IncidentActivationId).WhereNotNull("DeadlineOn").WhereNull("DeadlineNotifiedOn"), cancellationToken))
				.Where(c => c.Status is (int)IncidentClaimStatuses.Open or (int)IncidentClaimStatuses.UnderReview && c.DeadlineOn.Value.Date <= today.AddDays(policy.ClaimDeadlineLeadDays)).ToList();
			foreach (var claim in claims)
			{
				await _routing.NotifyPositionsAsync(actor, activation, EventTypes.IncidentClaimNotice,
					culture => IncidentNoticeText.Format(culture, "Notice.ClaimDeadline", claim.ClaimNumber, claim.DeadlineOn.Value.ToString("d", culture)), null, cancellationToken);
				claim.DeadlineNotifiedOn = DateTime.UtcNow;
				await _claims.UpdateVersionedAsync(claim, claim.RowVersion, null, cancellationToken);
			}
		}

		/// <summary>196: an open action or decision due inside the lead window (setting 120), or overdue; NotifiedOn is its latch.</summary>
		private async Task CoordinationItemsAsync(IncidentActivation activation, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			var defaults = await _settings.GetAsync<CoordinationBoardDefaults>(departmentId);
			var now = DateTime.UtcNow;
			var horizon = now.AddMinutes(defaults.ItemDueLeadMinutes);
			foreach (var action in await _actions.ListAsync(departmentId, In(activation.IncidentActivationId).WhereIn("Status", new[] { (int)IncidentOpenActionStatuses.Open, (int)IncidentOpenActionStatuses.InProgress }).WhereNotNull("DueOn").WhereNull("NotifiedOn"), cancellationToken))
			{
				if (action.DueOn > horizon) continue;
				await _publisher.PublishAsync(departmentId, activation, WorkflowTriggerEventType.IncidentCoordinationItemDue, new Dictionary<string, object>
				{
					["item.kind"] = "OpenAction",
					["item.title"] = action.Title,
					["item.owner"] = action.OwnerName,
					["deadline.utc"] = action.DueOn,
					["is_overdue"] = action.DueOn < now
				}, action.TableName, action.IncidentOpenActionId, cancellationToken);
				action.NotifiedOn = now;
				await _actions.UpdateVersionedAsync(action, action.RowVersion, null, cancellationToken);
			}
			foreach (var decision in await _decisions.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentDecisionStatuses.Open).WhereNotNull("DeadlineOn").WhereNull("NotifiedOn"), cancellationToken))
			{
				if (decision.DeadlineOn > horizon) continue;
				await _publisher.PublishAsync(departmentId, activation, WorkflowTriggerEventType.IncidentCoordinationItemDue, new Dictionary<string, object>
				{
					["item.kind"] = "Decision",
					["item.title"] = decision.Question?.Length > 200 ? decision.Question.Substring(0, 200) : decision.Question,
					["item.owner"] = decision.OwnerName,
					["deadline.utc"] = decision.DeadlineOn,
					["is_overdue"] = decision.DeadlineOn < now
				}, decision.TableName, decision.IncidentDecisionId, cancellationToken);
				decision.NotifiedOn = now;
				await _decisions.UpdateVersionedAsync(decision, decision.RowVersion, null, cancellationToken);
			}
		}

		/// <summary>194: a board lane whose source has not changed within its staleness window; StaleSince is its latch.</summary>
		private async Task<int> CoordinationLanesAsync(CancellationToken cancellationToken)
		{
			var count = 0;
			foreach (var departmentId in await _lookup.GetDepartmentsWithRowsAsync("CoordinationBoardLanes", cancellationToken))
			{
				try
				{
					if (!await _access.IsCoordinationEnabledAsync(departmentId)) continue;
					var now = DateTime.UtcNow;
					var boards = (await _boards.ListAsync(departmentId, new IncidentLedgerQuery { Take = 1000 }.Where("IsArchived", false), cancellationToken)).ToDictionary(b => b.CoordinationBoardId, StringComparer.OrdinalIgnoreCase);
					foreach (var lane in await _lanes.ListAsync(departmentId, new IncidentLedgerQuery { Take = 5000 }.Where("AlarmOnStale", true), cancellationToken))
					{
						if (!boards.TryGetValue(lane.CoordinationBoardId, out var board) || board.IsFrozen) continue;
						var stale = lane.LastSourceOn.HasValue && lane.LastSourceOn < now.AddMinutes(-Math.Max(1, lane.StalenessMinutes));
						if (stale == lane.StaleSince.HasValue) continue;
						lane.StaleSince = stale ? now : null;
						await _lanes.UpdateVersionedAsync(lane, lane.RowVersion, null, cancellationToken);
						var activation = board.IncidentActivationId == null ? null : await _activations.GetForDepartmentAsync(departmentId, board.IncidentActivationId, false, cancellationToken);
						await _publisher.PublishAsync(departmentId, activation, WorkflowTriggerEventType.CoordinationLaneStale, new Dictionary<string, object>
						{
							["board.name"] = board.Name,
							["lane.type"] = lane.LaneType,
							["lane.name"] = lane.Name,
							["stale_since"] = lane.LastSourceOn,
							["threshold_minutes"] = lane.StalenessMinutes,
							["resolved"] = !stale
						}, lane.TableName, lane.CoordinationBoardLaneId, cancellationToken);
					}
					count++;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Incident Back Office lane evaluation failed for department {departmentId}");
				}
			}
			return count;
		}

		/// <summary>198: a standing team's computed readiness for the next fourteen days changed (department scope).</summary>
		private async Task<int> TeamReadinessAsync(CancellationToken cancellationToken)
		{
			var count = 0;
			foreach (var departmentId in await _lookup.GetDepartmentsWithRowsAsync("DepartmentIncidentTeams", cancellationToken))
			{
				try
				{
					if (!await _access.IsEnabledAsync(departmentId)) continue;
					var actor = IncidentActor.System(departmentId);
					var start = DateTime.UtcNow;
					foreach (var team in (await _teams.ListAsync(departmentId, new IncidentLedgerQuery { Take = 500 }.Where("IsActive", true), cancellationToken)))
					{
						var readiness = await _organization.GetTeamReadinessAsync(actor, team.DepartmentIncidentTeamId, start, start.AddDays(14), cancellationToken);
						if (readiness == null) continue;
						var key = IncidentAlertLatch.Key(WorkflowTriggerEventType.IncidentTeamReadinessChanged, team.DepartmentIncidentTeamId);
						// A team's first evaluation as Ready is its normal state, not a change; anything else (and every later move) is announced.
						var firstLook = await _latch.SetDepartmentAsync(departmentId, key + ":seen", true) == true;
						foreach (var state in Enum.GetValues(typeof(IncidentTeamReadiness)).Cast<IncidentTeamReadiness>())
						{
							var edge = await _latch.SetDepartmentAsync(departmentId, key + ":" + (int)state, readiness.Readiness == state);
							if (edge != true || (firstLook && state == IncidentTeamReadiness.Ready)) continue;
							await _publisher.PublishAsync(departmentId, null, WorkflowTriggerEventType.IncidentTeamReadinessChanged, new Dictionary<string, object>
							{
								["team.name"] = team.Name,
								["team.type"] = Enum.GetName(typeof(IncidentTeamTypes), team.TeamType),
								["window.start"] = readiness.WindowStart,
								["window.end"] = readiness.WindowEnd,
								["readiness"] = state.ToString(),
								["gaps.count"] = readiness.Gaps
							}, team.TableName, team.DepartmentIncidentTeamId, cancellationToken);
						}
					}
					count++;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Incident Back Office team readiness failed for department {departmentId}");
				}
			}
			return count;
		}

		#endregion

		#region 57 daily cost

		public Task<int> RunDailyCostAsync(CancellationToken cancellationToken = default) => ForEachActivationAsync(async (actor, activation) =>
		{
			var departmentId = activation.DepartmentId;
			var day = DateTime.UtcNow.Date.AddDays(-1);
			var rates = new Dictionary<string, IncidentRateSnapshot>(StringComparer.OrdinalIgnoreCase);
			async Task<IncidentRateSnapshot> RateAsync(string id)
			{
				if (string.IsNullOrWhiteSpace(id)) return null;
				if (!rates.TryGetValue(id, out var rate))
					rates[id] = rate = await _rates.GetForDepartmentAsync(departmentId, id, false, cancellationToken);
				return rate;
			}
			var existing = new HashSet<string>((await _costs.ListAsync(departmentId, In(activation.IncidentActivationId).Where("CostDate", day).Where("IsSystemGenerated", true), cancellationToken))
				.Select(c => c.SourceType + ":" + c.SourceId), StringComparer.OrdinalIgnoreCase);
			var created = new List<IncidentCostTransaction>();
			async Task PostAsync(IncidentCostTransaction transaction)
			{
				if (!existing.Add(transaction.SourceType + ":" + transaction.SourceId)) return;
				transaction.DepartmentId = departmentId;
				transaction.IncidentActivationId = activation.IncidentActivationId;
				transaction.CostDate = day;
				transaction.IsSystemGenerated = true;
				transaction.Currency ??= activation.Currency;
				created.Add(await _costs.InsertNewAsync(transaction, null, cancellationToken));
			}

			// Personnel time: certified or approved spans that ended on the day, priced from their rate snapshot.
			foreach (var record in (await _time.ListAsync(departmentId, In(activation.IncidentActivationId).Between("EndOn", day, day.AddDays(1)), cancellationToken))
				.Where(t => t.Status is (int)IncidentTimeStatuses.Certified or (int)IncidentTimeStatuses.Approved))
			{
				var hours = (decimal)(record.EndOn - record.StartOn).TotalHours - record.UnpaidBreakMinutes / 60m;
				var rate = await RateAsync(record.RateSnapshotId);
				await PostAsync(new IncidentCostTransaction
				{
					Measure = (int)IncidentCostMeasures.Accrued,
					Category = (int)IncidentCostCategories.Personnel,
					SourceType = "TimeRecord",
					SourceId = record.IncidentTimeRecordId,
					IncidentParticipantId = record.IncidentParticipantId,
					IncidentResourceId = record.IncidentResourceId,
					IncidentOperationalPeriodId = record.IncidentOperationalPeriodId,
					IncidentRecoveryProjectId = record.IncidentRecoveryProjectId,
					Section = (int)IncidentSections.Other,
					Quantity = Math.Round(hours, 2),
					Unit = "h",
					Amount = rate?.Amount == null ? null : Math.Round(hours * rate.Amount.Value, 2),
					Currency = rate?.Currency ?? record.Currency
				});
			}

			// Equipment: certified or approved shift tickets for the day.
			foreach (var use in (await _equipmentUse.ListAsync(departmentId, In(activation.IncidentActivationId).Where("ShiftDate", day), cancellationToken))
				.Where(u => u.Status is (int)IncidentTimeStatuses.Certified or (int)IncidentTimeStatuses.Approved))
			{
				var quantity = use.Quantity ?? use.Hours ?? 0;
				var rate = await RateAsync(use.RateSnapshotId);
				await PostAsync(new IncidentCostTransaction
				{
					Measure = (int)IncidentCostMeasures.Accrued,
					Category = (int)IncidentCostCategories.Equipment,
					SourceType = "EquipmentUse",
					SourceId = use.IncidentEquipmentUseRecordId,
					IncidentResourceId = use.IncidentResourceId,
					IncidentOperationalPeriodId = use.IncidentOperationalPeriodId,
					IncidentRecoveryProjectId = use.IncidentRecoveryProjectId,
					Section = (int)IncidentSections.Operations,
					Quantity = quantity,
					Unit = use.UseUnit ?? "h",
					Amount = rate?.Amount == null ? null : Math.Round(quantity * rate.Amount.Value, 2),
					Currency = rate?.Currency ?? use.Currency
				});
			}

			// Expenses approved for the day are actual cost.
			foreach (var expense in (await _expenses.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncurredOn", day).Where("Status", (int)IncidentExpenseStatuses.Approved), cancellationToken)))
				await PostAsync(new IncidentCostTransaction
				{
					Measure = (int)IncidentCostMeasures.Actual,
					Category = (int)IncidentCostCategories.Expense,
					SourceType = "Expense",
					SourceId = expense.IncidentExpenseId,
					VendorId = expense.VendorId,
					IncidentOperationalPeriodId = expense.IncidentOperationalPeriodId,
					IncidentRecoveryProjectId = expense.IncidentRecoveryProjectId,
					Section = (int)IncidentSections.FinanceAdministration,
					Amount = expense.Amount,
					Currency = expense.Currency
				});

			// Accrual snapshot and projection from every non-adjusted transaction to date.
			var all = (await _costs.ListAsync(departmentId, In(activation.IncidentActivationId).Between("CostDate", null, day.AddDays(1)), cancellationToken)).Where(c => c.Amount.HasValue).ToList();
			if (all.Count == 0)
				return;
			var runId = Guid.NewGuid().ToString();
			foreach (var group in all.Where(c => c.CostDate == day).GroupBy(c => (c.Measure, c.Category)))
				await _accruals.InsertNewAsync(new IncidentAccrualSnapshot
				{
					DepartmentId = departmentId,
					IncidentActivationId = activation.IncidentActivationId,
					AsOfDate = day,
					Measure = group.Key.Measure,
					Category = group.Key.Category,
					Amount = group.Sum(c => c.Amount.Value),
					Currency = activation.Currency,
					SubjectCount = group.Select(c => c.IncidentParticipantId ?? c.IncidentResourceId ?? c.SourceId).Distinct().Count(),
					RunId = runId
				}, null, cancellationToken);
			var dailyBurn = all.Where(c => c.CostDate == day).Sum(c => c.Amount.Value);
			var cumulative = all.Sum(c => c.Amount.Value);
			await _projections.InsertNewAsync(new IncidentCostProjection
			{
				DepartmentId = departmentId,
				IncidentActivationId = activation.IncidentActivationId,
				ScenarioName = "Daily",
				DailyBurn = dailyBurn,
				CumulativeToDate = cumulative,
				ForecastAtCompletion = cumulative,
				Currency = activation.Currency,
				Assumptions = "Accrued and actual to " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				RunId = runId
			}, null, cancellationToken);

			var policy = await _settings.GetAsync<IncidentFinancePolicy>(departmentId);
			if (policy.DailyBurnThreshold > 0)
				await EdgeAsync(activation, WorkflowTriggerEventType.IncidentCostThreshold, "burn", dailyBurn > policy.DailyBurnThreshold, () => new Dictionary<string, object>
				{
					["measure"] = "DailyBurn",
					["amount"] = dailyBurn,
					["threshold.amount"] = policy.DailyBurnThreshold
				}, "IncidentActivations", activation.IncidentActivationId, cancellationToken);

			await ApportionAsync(actor, activation, day, all, cancellationToken);
		}, true, false, cancellationToken);

		/// <summary>
		/// Cost-share apportionment (§9.27): each active agreement splits the cost to date by its ordered rules — a percentage split
		/// ({"parties":[{"key","name","percent"}]}), a direct charge to one party, or an exclusion — first matching rule wins per
		/// transaction. A run is recorded only when its inputs changed since the agreement's last run.
		/// </summary>
		private async Task ApportionAsync(IncidentActor actor, IncidentActivation activation, DateTime through, IReadOnlyList<IncidentCostTransaction> costs, CancellationToken cancellationToken)
		{
			var departmentId = activation.DepartmentId;
			foreach (var agreement in await _shareAgreements.ListAsync(departmentId, In(activation.IncidentActivationId).Where("Status", (int)IncidentCostShareStatuses.Active), cancellationToken))
			{
				var scoped = costs.Where(c => (!agreement.EffectiveFrom.HasValue || c.CostDate >= agreement.EffectiveFrom) && (!agreement.EffectiveTo.HasValue || c.CostDate <= agreement.EffectiveTo)).ToList();
				var inputChecksum = Checksum(string.Join("|", scoped.OrderBy(c => c.IncidentCostTransactionId).Select(c => c.IncidentCostTransactionId + "=" + c.Amount.Value.ToString(CultureInfo.InvariantCulture))));
				var last = (await _runs.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentCostShareAgreementId", agreement.IncidentCostShareAgreementId).Order("AddedOn", true).Page(0, 1), cancellationToken)).FirstOrDefault();
				if (last != null && last.InputChecksum == inputChecksum && last.AgreementVersion == agreement.Version)
					continue;
				var rules = (await _shareRules.ListAsync(departmentId, In(activation.IncidentActivationId).Where("IncidentCostShareAgreementId", agreement.IncidentCostShareAgreementId).Order("RuleOrder"), cancellationToken)).ToList();
				var allocations = new Dictionary<(string Party, int Category, string RuleId, int Method), (string Name, decimal Amount)>();
				foreach (var cost in scoped)
				{
					var rule = rules.FirstOrDefault(r => Matches(r, cost));
					if (rule == null || rule.Method == (int)IncidentCostShareMethods.Excluded)
						continue;
					foreach (var (key, name, share) in Shares(rule))
					{
						var k = (key, cost.Category, rule.IncidentCostShareRuleId, rule.Method);
						allocations[k] = (name, (allocations.TryGetValue(k, out var current) ? current.Amount : 0) + Math.Round(cost.Amount.Value * share, 2));
					}
				}
				var total = allocations.Values.Sum(a => a.Amount);
				var outputChecksum = Checksum(string.Join("|", allocations.OrderBy(a => a.Key.Party).ThenBy(a => a.Key.Category).Select(a => a.Key.Party + ":" + a.Key.Category + "=" + a.Value.Amount.ToString(CultureInfo.InvariantCulture))));
				var run = await _runService.SaveAsync(actor, new IncidentApportionmentRun
				{
					IncidentActivationId = activation.IncidentActivationId,
					IncidentCostShareAgreementId = agreement.IncidentCostShareAgreementId,
					AgreementVersion = agreement.Version,
					RunKind = last == null ? (int)IncidentApportionmentRunKinds.Scheduled : (int)IncidentApportionmentRunKinds.Reapportionment,
					ThroughDate = through,
					InputChecksum = inputChecksum,
					OutputChecksum = outputChecksum,
					TotalApportioned = total,
					Currency = activation.Currency
				}, null, cancellationToken);
				foreach (var (key, value) in allocations)
					await _allocations.InsertNewAsync(new IncidentCostAllocation
					{
						DepartmentId = departmentId,
						IncidentActivationId = activation.IncidentActivationId,
						IncidentApportionmentRunId = run.IncidentApportionmentRunId,
						IncidentCostShareRuleId = key.RuleId,
						PartyKey = key.Party,
						PartyName = value.Name,
						Category = key.Category,
						Amount = value.Amount,
						Currency = activation.Currency,
						Method = key.Method
					}, null, cancellationToken);
			}
		}

		private static bool Matches(IncidentCostShareRule rule, IncidentCostTransaction cost) => rule.ScopeKind switch
		{
			(int)IncidentCostShareScopes.All => true,
			(int)IncidentCostShareScopes.CostCategory => string.Equals(rule.ScopeValue, Enum.GetName(typeof(IncidentCostCategories), cost.Category), StringComparison.OrdinalIgnoreCase) || rule.ScopeValue == cost.Category.ToString(CultureInfo.InvariantCulture),
			(int)IncidentCostShareScopes.Section => string.Equals(rule.ScopeValue, Enum.GetName(typeof(IncidentSections), cost.Section), StringComparison.OrdinalIgnoreCase),
			(int)IncidentCostShareScopes.FundingCode => string.Equals(rule.ScopeValue, cost.IncidentFundingCodeId, StringComparison.OrdinalIgnoreCase),
			(int)IncidentCostShareScopes.Facility => string.Equals(rule.ScopeValue, cost.IncidentFacilityId, StringComparison.OrdinalIgnoreCase),
			(int)IncidentCostShareScopes.Period => string.Equals(rule.ScopeValue, cost.IncidentOperationalPeriodId, StringComparison.OrdinalIgnoreCase),
			_ => false
		};

		private sealed class ShareParty
		{
			public string Key { get; set; }
			public string Name { get; set; }
			public decimal Percent { get; set; }
		}

		private sealed class ShareParameters
		{
			public List<ShareParty> Parties { get; set; } = new List<ShareParty>();
		}

		/// <summary>The rule's parties and fractions; a direct charge is one party at 100 %.</summary>
		private static IEnumerable<(string Key, string Name, decimal Share)> Shares(IncidentCostShareRule rule)
		{
			ShareParameters parameters;
			try { parameters = string.IsNullOrWhiteSpace(rule.ParametersJson) ? new ShareParameters() : JsonConvert.DeserializeObject<ShareParameters>(rule.ParametersJson) ?? new ShareParameters(); }
			catch (JsonException) { yield break; }
			var parties = parameters.Parties.Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToList();
			if (rule.Method == (int)IncidentCostShareMethods.DirectCharge)
			{
				var party = parties.FirstOrDefault();
				if (party != null) yield return (party.Key, party.Name ?? party.Key, 1m);
				yield break;
			}
			var total = parties.Sum(p => p.Percent);
			if (total <= 0) yield break;
			foreach (var party in parties)
				yield return (party.Key, party.Name ?? party.Key, party.Percent / total);
		}

		private static string Checksum(string value)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).Substring(0, 64);
		}

		#endregion

		#region 58 retention

		public async Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var department in (await _lookup.GetClosedActivationsAsync(DateTime.UtcNow.AddDays(-1), cancellationToken)).GroupBy(a => a.DepartmentId))
			{
				try
				{
					var policy = await _settings.GetAsync<IncidentRetentionPolicy>(department.Key);
					var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, policy.SnapshotDaysAfterClose));
					foreach (var activation in department.Where(a => a.ClosedOn.HasValue && a.ClosedOn < cutoff))
					{
						await _metrics.PurgeWhereAsync(department.Key, "IncidentActivationId", activation.IncidentActivationId, cancellationToken: cancellationToken);
						await _snapshots.PurgeWhereAsync(department.Key, "IncidentActivationId", activation.IncidentActivationId, cancellationToken: cancellationToken);
						count++;
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Incident Back Office retention failed for department {department.Key}");
				}
			}
			return count;
		}

		#endregion
	}
}
