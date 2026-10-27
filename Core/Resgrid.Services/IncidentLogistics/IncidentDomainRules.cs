using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	// LI-B / LI-C table rules (large-incident-logistics-finance-plan.md §9.4-§9.27, §11.5, §15.1). Each rule runs inside the ledger
	// write's transaction, so an invariant it enforces and a derived row it writes commit or roll back with the change. Rules read
	// and write sibling tables through their repositories (never through another ledger service), which keeps the rule graph free of
	// construction cycles. Every threshold comes from a department policy, never from a constant.

	/// <summary>Base with no-op hooks, so a rule overrides only the side it needs.</summary>
	public abstract class IncidentRule<T> : IIncidentLedgerRule<T> where T : class, IIncidentLedgerEntity
	{
		public virtual Task BeforeSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken) => Task.CompletedTask;
		public virtual Task AfterSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken) => Task.CompletedTask;

		protected static bool Moved(IncidentLedgerChange<T> change) => change.IsTransition && change.OldStatus != change.NewStatus;
		protected static bool MovedTo(IncidentLedgerChange<T> change, int status) => Moved(change) && change.NewStatus == status;
	}

	/// <summary>
	/// Human-readable numbers for requests, tickets, requisitions, commitments, payment packages and claims: "{short code}-{prefix}{n}",
	/// sequential per activation under a lock on the activation row. A number the user typed (a locally issued request number) is kept.
	/// </summary>
	public sealed class IncidentNumberingRule<T> : IncidentRule<T> where T : class, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		private readonly IIncidentLedgerRepository<T> _rows;
		private readonly IIncidentLedgerRepository<IncidentActivation> _activations;
		private readonly Func<T, string> _get;
		private readonly Action<T, string> _set;
		private readonly string _prefix;

		public IncidentNumberingRule(IIncidentLedgerRepository<T> rows, IIncidentLedgerRepository<IncidentActivation> activations, Func<T, string> get, Action<T, string> set, string prefix)
		{
			_rows = rows;
			_activations = activations;
			_get = get;
			_set = set;
			_prefix = prefix;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken)
		{
			if (!change.IsNew)
			{
				_set(change.Entity, _get(change.Existing));
				return;
			}
			if (!string.IsNullOrWhiteSpace(_get(change.Entity)) || string.IsNullOrWhiteSpace(change.Entity.IncidentActivationId))
				return;
			var activation = await _activations.LockForUpdateAsync(change.Actor.DepartmentId, change.Entity.IncidentActivationId, cancellationToken) ?? change.Activation;
			var count = await _rows.CountAsync(change.Actor.DepartmentId, new IncidentLedgerQuery { ActivationIds = new[] { change.Entity.IncidentActivationId }, IncludeDeleted = true }, cancellationToken);
			_set(change.Entity, string.Format(CultureInfo.InvariantCulture, "{0}-{1}{2:D4}", activation?.ShortCode ?? "INC", _prefix, count + 1));
		}
	}

	/// <summary>Request stamps (§9.4): who submitted, approved and ordered, and the reason on a rejection or cancellation.</summary>
	public sealed class IncidentRequestRule : IncidentRule<IncidentResourceRequest>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentResourceRequest> change, CancellationToken cancellationToken)
		{
			var request = change.Entity;
			var now = DateTime.UtcNow;
			if (change.IsNew)
				request.RequestedByUserId ??= change.Actor.UserId;
			if (!Moved(change))
				return Task.CompletedTask;
			switch ((IncidentRequestStatuses)change.NewStatus)
			{
				case IncidentRequestStatuses.Submitted: request.SubmittedOn = now; break;
				case IncidentRequestStatuses.Approved: request.ApprovedOn = now; request.ApprovedByUserId = change.Actor.UserId; break;
				case IncidentRequestStatuses.Ordered: request.OrderedOn = now; request.OrderedByUserId = change.Actor.UserId; break;
				case IncidentRequestStatuses.Closed: request.ClosedOn = now; break;
				case IncidentRequestStatuses.Rejected: request.RejectedReason = Trim(change.Reason, 500); break;
				case IncidentRequestStatuses.Cancelled: request.CancelledReason = Trim(change.Reason, 500); break;
			}
			return Task.CompletedTask;
		}

		internal static string Trim(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}

	/// <summary>
	/// Fills (§9.4): accepting, moving and arriving a fill re-derives the request's status from the accepted quantity (partially
	/// filled → filled → en route → received) and emits trigger 121 with the fill's counts; an arrival notifies the receiving
	/// positions (notice 38).
	/// </summary>
	public sealed class IncidentRequestFillRule : IncidentRule<IncidentRequestFill>
	{
		private static readonly int[] Live = { (int)IncidentFillStatuses.Accepted, (int)IncidentFillStatuses.EnRoute, (int)IncidentFillStatuses.Arrived };
		private static readonly int[] Fillable =
		{
			(int)IncidentRequestStatuses.Ordered, (int)IncidentRequestStatuses.PartiallyFilled, (int)IncidentRequestStatuses.Filled,
			(int)IncidentRequestStatuses.EnRoute, (int)IncidentRequestStatuses.Received
		};

		private readonly IIncidentLedgerRepository<IncidentRequestFill> _fills;
		private readonly IIncidentLedgerRepository<IncidentResourceRequest> _requests;
		private readonly IIncidentEventPublisher _publisher;
		private readonly IIncidentNotificationRoutingService _routing;

		public IncidentRequestFillRule(IIncidentLedgerRepository<IncidentRequestFill> fills, IIncidentLedgerRepository<IncidentResourceRequest> requests,
			IIncidentEventPublisher publisher, IIncidentNotificationRoutingService routing)
		{
			_fills = fills;
			_requests = requests;
			_publisher = publisher;
			_routing = routing;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentRequestFill> change, CancellationToken cancellationToken)
		{
			var fill = change.Entity;
			var request = await _requests.GetForDepartmentAsync(change.Actor.DepartmentId, fill.IncidentResourceRequestId, false, cancellationToken);
			if (request == null || request.IncidentActivationId != fill.IncidentActivationId)
				throw new IncidentRuleException("RequestNotFound");
			if (change.IsNew && !Fillable.Contains(request.Status))
				throw new IncidentRuleException("RequestNotOrdered");
			if (MovedTo(change, (int)IncidentFillStatuses.Accepted)) fill.AcceptedOn = DateTime.UtcNow;
			if (MovedTo(change, (int)IncidentFillStatuses.Arrived)) fill.ArrivedOn = DateTime.UtcNow;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentRequestFill> change, CancellationToken cancellationToken)
		{
			var fill = change.Entity;
			var departmentId = change.Actor.DepartmentId;
			var request = await _requests.LockForUpdateAsync(departmentId, fill.IncidentResourceRequestId, cancellationToken);
			if (request == null || !Fillable.Contains(request.Status))
				return;
			var fills = (await _fills.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { request.IncidentActivationId } }
				.Where("IncidentResourceRequestId", request.IncidentResourceRequestId).WhereIn("Status", Live), cancellationToken)).ToList();
			var accepted = fills.Sum(f => f.Quantity);
			var target = accepted <= 0 ? (int)IncidentRequestStatuses.Ordered
				: accepted < request.Quantity ? (int)IncidentRequestStatuses.PartiallyFilled
				: fills.All(f => f.Status == (int)IncidentFillStatuses.Arrived) ? (int)IncidentRequestStatuses.Received
				: fills.Any(f => f.Status is (int)IncidentFillStatuses.EnRoute or (int)IncidentFillStatuses.Arrived) ? (int)IncidentRequestStatuses.EnRoute
				: (int)IncidentRequestStatuses.Filled;
			if (target != request.Status)
			{
				var old = request.Status;
				request.Status = target;
				await _requests.UpdateVersionedAsync(request, request.RowVersion, change.Actor.UserId, cancellationToken);
				await _publisher.PublishAsync(departmentId, change.Activation, WorkflowTriggerEventType.IncidentRequestStatusChanged, new Dictionary<string, object>
				{
					["request.number"] = request.RequestNumber,
					["request.kind"] = Enum.GetName(typeof(IncidentResourceKinds), request.Kind),
					["request.type"] = request.ResourceType,
					["request.priority"] = Enum.GetName(typeof(IncidentRequestPriorities), request.Priority),
					["request.need_by"] = request.NeedBy,
					["request.source_channel"] = Enum.GetName(typeof(IncidentSourceChannels), request.SourceChannel),
					["old_status"] = Enum.GetName(typeof(IncidentRequestStatuses), old),
					["new_status"] = Enum.GetName(typeof(IncidentRequestStatuses), target),
					["request.quantity"] = request.Quantity,
					["request.filled_quantity"] = accepted,
					["fill.apparatus_count"] = fill.ApparatusCount,
					["fill.personnel_count"] = fill.PersonnelCount,
					["fill.eta"] = fill.EtaOn
				}, request.TableName, request.IncidentResourceRequestId, cancellationToken);
			}

			if (MovedTo(change, (int)IncidentFillStatuses.Arrived) && change.Activation != null)
				await _routing.NotifyPositionsAsync(change.Actor, change.Activation, EventTypes.IncidentArrivalUpdate,
					culture => IncidentNoticeText.Format(culture, "Notice.FillArrived", request.RequestNumber, fill.Provider ?? string.Empty), null, cancellationToken);
		}
	}

	/// <summary>
	/// Bed-nights (§9.7, §11.5): an active lodging assignment holds one row per bed per night; the filtered unique index on (bed,
	/// night) is the no-overlap invariant and this rule writes the rows in the assignment's transaction. A checkout frees the nights
	/// from today on; a cancellation, no-show or delete frees them all.
	/// </summary>
	public sealed class IncidentLodgingNightRule : IncidentRule<IncidentLodgingAssignment>
	{
		private const int MaxNights = 180;
		private static readonly int[] Holding = { (int)IncidentLodgingAssignmentStatuses.Held, (int)IncidentLodgingAssignmentStatuses.Confirmed, (int)IncidentLodgingAssignmentStatuses.Occupied };

		private readonly IIncidentLedgerRepository<IncidentLodgingNightInventory> _nights;
		private readonly IIncidentLedgerRepository<IncidentLodgingBed> _beds;

		public IncidentLodgingNightRule(IIncidentLedgerRepository<IncidentLodgingNightInventory> nights, IIncidentLedgerRepository<IncidentLodgingBed> beds)
		{
			_nights = nights;
			_beds = beds;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentLodgingAssignment> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				return;
			var assignment = change.Entity;
			assignment.FirstNight = assignment.FirstNight.Date;
			assignment.LastNight = assignment.LastNight.Date;
			if (assignment.LastNight < assignment.FirstNight)
				throw new IncidentRuleException("NightsOutOfOrder");
			if ((assignment.LastNight - assignment.FirstNight).TotalDays >= MaxNights)
				throw new IncidentRuleException("TooManyNights", MaxNights);
			var bed = await _beds.LockForUpdateAsync(change.Actor.DepartmentId, assignment.IncidentLodgingBedId, cancellationToken);
			if (bed == null || bed.IsDeleted || bed.IncidentActivationId != assignment.IncidentActivationId)
				throw new IncidentRuleException("BedNotFound");
			if (bed.IsOutOfService && Holding.Contains(assignment.Status))
				throw new IncidentRuleException("BedOutOfService");
			if (assignment.IsAccessible && !bed.IsAccessible)
				throw new IncidentRuleException("BedNotAccessible");
			assignment.IncidentLodgingPropertyId = bed.IncidentLodgingPropertyId;
			if (!Holding.Contains(assignment.Status))
				return;
			var taken = (await _nights.ListAsync(change.Actor.DepartmentId, new IncidentLedgerQuery().Where("IncidentLodgingBedId", bed.IncidentLodgingBedId)
				.Between("Night", assignment.FirstNight, assignment.LastNight.AddDays(1)), cancellationToken))
				.Where(n => !string.Equals(n.IncidentLodgingAssignmentId, assignment.IncidentLodgingAssignmentId, StringComparison.OrdinalIgnoreCase)).ToList();
			if (taken.Count > 0)
				throw new IncidentRuleException("BedOccupied", taken.Min(n => n.Night).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentLodgingAssignment> change, CancellationToken cancellationToken)
		{
			var assignment = change.Entity;
			var departmentId = change.Actor.DepartmentId;
			if (change.IsDelete || assignment.Status is (int)IncidentLodgingAssignmentStatuses.Cancelled or (int)IncidentLodgingAssignmentStatuses.NoShow)
			{
				await _nights.PurgeWhereAsync(departmentId, "IncidentLodgingAssignmentId", assignment.IncidentLodgingAssignmentId, cancellationToken: cancellationToken);
				return;
			}
			if (assignment.Status == (int)IncidentLodgingAssignmentStatuses.Departed)
			{
				await _nights.PurgeWhereAsync(departmentId, "IncidentLodgingAssignmentId", assignment.IncidentLodgingAssignmentId, "Night", DateTime.UtcNow.Date, cancellationToken);
				return;
			}
			if (!change.IsNew && !Moved(change) && change.Existing.FirstNight == assignment.FirstNight && change.Existing.LastNight == assignment.LastNight
				&& change.Existing.IncidentLodgingBedId == assignment.IncidentLodgingBedId)
				return;
			await _nights.PurgeWhereAsync(departmentId, "IncidentLodgingAssignmentId", assignment.IncidentLodgingAssignmentId, cancellationToken: cancellationToken);
			for (var night = assignment.FirstNight; night <= assignment.LastNight; night = night.AddDays(1))
				await _nights.InsertNewAsync(new IncidentLodgingNightInventory
				{
					DepartmentId = departmentId,
					IncidentActivationId = assignment.IncidentActivationId,
					IncidentLodgingBedId = assignment.IncidentLodgingBedId,
					IncidentLodgingAssignmentId = assignment.IncidentLodgingAssignmentId,
					Night = night
				}, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>
	/// Vendor capacity (§9.12): holds and commitments on one vendor capability may not exceed its capacity across every incident of the
	/// department in an overlapping bucket. The capability row is locked so two incidents cannot both take the last unit. An
	/// authorized override (reason plus an administrator or approver) is recorded and emits trigger 133; otherwise the write is refused.
	/// </summary>
	public sealed class IncidentVendorCapacityRule : IncidentRule<IncidentVendorCapacityCommitment>
	{
		private readonly IIncidentLedgerRepository<IncidentVendorCapacityCommitment> _commitments;
		private readonly IIncidentLedgerRepository<VendorCapability> _capabilities;
		private readonly IIncidentLedgerRepository<IncidentVendorEngagement> _engagements;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentEventPublisher _publisher;
		private readonly IIncidentLookupService _lookups;

		public IncidentVendorCapacityRule(IIncidentLedgerRepository<IncidentVendorCapacityCommitment> commitments, IIncidentLedgerRepository<VendorCapability> capabilities,
			IIncidentLedgerRepository<IncidentVendorEngagement> engagements, IIncidentBackOfficeSettingsService settings, IIncidentEventPublisher publisher, IIncidentLookupService lookups)
		{
			_commitments = commitments;
			_capabilities = capabilities;
			_engagements = engagements;
			_settings = settings;
			_publisher = publisher;
			_lookups = lookups;
		}

		private async Task<(VendorCapability Capability, decimal Used)> UsageAsync(IncidentLedgerChange<IncidentVendorCapacityCommitment> change, CancellationToken cancellationToken)
		{
			var commitment = change.Entity;
			var departmentId = change.Actor.DepartmentId;
			var capability = await _capabilities.LockForUpdateAsync(departmentId, commitment.VendorCapabilityId, cancellationToken);
			if (capability == null || capability.IsDeleted)
				throw new IncidentRuleException("CapabilityNotFound");
			var others = await _commitments.ListAsync(departmentId, new IncidentLedgerQuery { Take = 5000 }
				.Where("VendorCapabilityId", capability.VendorCapabilityId).WhereIn("Status", new[] { (int)IncidentHoldStatuses.Active }), cancellationToken);
			var used = others.Where(o => !string.Equals(o.IncidentVendorCapacityCommitmentId, commitment.IncidentVendorCapacityCommitmentId, StringComparison.OrdinalIgnoreCase)
				&& o.BucketStart < commitment.BucketEnd && commitment.BucketStart < o.BucketEnd).Sum(o => o.Quantity);
			return (capability, used);
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentVendorCapacityCommitment> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				return;
			var commitment = change.Entity;
			if (commitment.BucketEnd <= commitment.BucketStart)
				throw new IncidentRuleException("BucketOutOfOrder");
			if (commitment.Quantity <= 0)
				throw new IncidentRuleException("QuantityRequired");
			var engagement = await _engagements.GetForDepartmentAsync(change.Actor.DepartmentId, commitment.IncidentVendorEngagementId, false, cancellationToken);
			if (engagement == null || engagement.IncidentActivationId != commitment.IncidentActivationId)
				throw new IncidentRuleException("EngagementNotFound");
			if (change.IsNew && commitment.Kind == (int)IncidentCommitmentKinds.Hold && !commitment.ExpiresOn.HasValue)
			{
				var policy = await _settings.GetAsync<IncidentVendorPolicy>(change.Actor.DepartmentId);
				commitment.ExpiresOn = DateTime.UtcNow.AddHours(policy.HoldExpiryHours);
			}
			if (commitment.Status != (int)IncidentHoldStatuses.Active)
				return;
			var (capability, used) = await UsageAsync(change, cancellationToken);
			if (!string.Equals(capability.VendorId, engagement.VendorId, StringComparison.OrdinalIgnoreCase))
				throw new IncidentRuleException("CapabilityNotVendors");
			commitment.Unit ??= capability.Unit;
			if (used + commitment.Quantity <= capability.MaxCapacity)
				return;
			if (!commitment.IsOverride || string.IsNullOrWhiteSpace(commitment.OverrideReason) || !change.Actor.IsDepartmentAdmin)
				throw new IncidentRuleException("VendorCapacityExceeded", Math.Max(0, capability.MaxCapacity - used).ToString("0.##", CultureInfo.InvariantCulture), capability.Unit);
			commitment.ApprovedByUserId = change.Actor.UserId;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentVendorCapacityCommitment> change, CancellationToken cancellationToken)
		{
			var commitment = change.Entity;
			if (change.IsDelete || commitment.Status != (int)IncidentHoldStatuses.Active || !commitment.IsOverride)
				return;
			var (capability, used) = await UsageAsync(change, cancellationToken);
			if (used + commitment.Quantity <= capability.MaxCapacity)
				return;
			var vendor = (await _lookups.GetLabelsAsync(change.Actor, "InventoryVendors", new[] { capability.VendorId }, cancellationToken)).TryGetValue(capability.VendorId, out var name) ? name : null;
			var others = (await _commitments.ListAsync(change.Actor.DepartmentId, new IncidentLedgerQuery { Take = 5000 }
				.Where("VendorCapabilityId", capability.VendorCapabilityId).WhereIn("Status", new[] { (int)IncidentHoldStatuses.Active }), cancellationToken))
				.Where(o => o.BucketStart < commitment.BucketEnd && commitment.BucketStart < o.BucketEnd && o.IncidentActivationId != commitment.IncidentActivationId)
				.Select(o => o.IncidentActivationId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
			await _publisher.PublishAsync(change.Actor.DepartmentId, change.Activation, WorkflowTriggerEventType.IncidentVendorCapacityConflict, new Dictionary<string, object>
			{
				["vendor.name"] = vendor,
				["capability.kind"] = capability.Name,
				["capability.unit"] = capability.Unit,
				["bucket.start"] = commitment.BucketStart,
				["bucket.end"] = commitment.BucketEnd,
				["requested"] = commitment.Quantity,
				["available"] = Math.Max(0, capability.MaxCapacity - used),
				["conflicting_incidents_count"] = others,
				["was_override"] = true
			}, commitment.TableName, commitment.IncidentVendorCapacityCommitmentId, cancellationToken);
		}
	}

	/// <summary>
	/// Separation of duties (§9.13, §15.1): the requester does not approve, the approver does not commit, and the committer does not
	/// accept the receipt — unless the department recorded single-person operation (setting 96, with who acknowledged it and when).
	/// Approval above a recorded spending delegation is refused once the incident has delegations.
	/// </summary>
	public sealed class IncidentProcurementRule : IncidentRule<PurchaseRequisition>
	{
		private readonly IIncidentLedgerRepository<PurchaseRequisition> _requisitions;
		private readonly IIncidentLedgerRepository<IncidentSpendingDelegation> _delegations;
		private readonly IIncidentBackOfficeSettingsService _settings;

		public IncidentProcurementRule(IIncidentLedgerRepository<PurchaseRequisition> requisitions, IIncidentLedgerRepository<IncidentSpendingDelegation> delegations,
			IIncidentBackOfficeSettingsService settings)
		{
			_requisitions = requisitions;
			_delegations = delegations;
			_settings = settings;
		}

		internal static async Task<bool> SinglePersonAsync(IIncidentBackOfficeSettingsService settings, int departmentId)
		{
			var policy = await settings.GetAsync<IncidentFinancePolicy>(departmentId);
			return policy.AllowSinglePersonOperation && !string.IsNullOrWhiteSpace(policy.SinglePersonAcknowledgedByUserId);
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<PurchaseRequisition> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				return;
			var requisition = change.Entity;
			var departmentId = change.Actor.DepartmentId;
			if (change.IsNew)
			{
				requisition.RequestedByUserId ??= change.Actor.UserId;
				var policy = await _settings.GetAsync<IncidentFinancePolicy>(departmentId);
				if (requisition.Method == (int)IncidentPurchaseMethods.MicroPurchase && !string.IsNullOrWhiteSpace(requisition.VendorId))
				{
					var since = DateTime.UtcNow.AddDays(-(double)policy.SplitPurchaseWindowDays);
					var recent = (await _requisitions.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { requisition.IncidentActivationId } }
						.Where("VendorId", requisition.VendorId).Where("Method", (int)IncidentPurchaseMethods.MicroPurchase).Between("AddedOn", since, null), cancellationToken))
						.Where(r => r.Status != (int)IncidentRequisitionStatuses.Cancelled && r.Status != (int)IncidentRequisitionStatuses.Rejected).Sum(r => r.EstimatedAmount);
					requisition.IsSplitPurchaseWarning = recent + requisition.EstimatedAmount > policy.MicroPurchaseThreshold;
				}
			}
			if (!Moved(change))
				return;
			switch ((IncidentRequisitionStatuses)change.NewStatus)
			{
				case IncidentRequisitionStatuses.Submitted:
					requisition.SubmittedOn = DateTime.UtcNow;
					break;
				case IncidentRequisitionStatuses.Approved:
					if (string.Equals(requisition.RequestedByUserId, change.Actor.UserId, StringComparison.OrdinalIgnoreCase) && !await SinglePersonAsync(_settings, departmentId))
						throw new IncidentRuleException("SeparationOfDuties");
					var delegations = (await _delegations.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { requisition.IncidentActivationId } }
						.Where("Action", (int)IncidentSpendingActions.Approve), cancellationToken))
						.Where(d => d.EffectiveOn <= DateTime.UtcNow && (!d.ExpiresOn.HasValue || d.ExpiresOn > DateTime.UtcNow)).ToList();
					if (delegations.Count > 0 && !change.Actor.IsDepartmentAdmin
						&& !delegations.Any(d => string.Equals(d.UserId, change.Actor.UserId, StringComparison.OrdinalIgnoreCase) && d.ThresholdAmount >= requisition.EstimatedAmount))
						throw new IncidentRuleException("ApprovalAboveDelegation");
					requisition.ApprovedOn = DateTime.UtcNow;
					requisition.ApprovedByUserId = change.Actor.UserId;
					break;
			}
		}
	}

	/// <summary>Commitments (§9.13): an approved requisition, the approver not committing, and the requisition moved to Committed.</summary>
	public sealed class IncidentCommitmentRule : IncidentRule<IncidentCommitment>
	{
		private readonly IIncidentLedgerRepository<PurchaseRequisition> _requisitions;
		private readonly IIncidentBackOfficeSettingsService _settings;

		public IncidentCommitmentRule(IIncidentLedgerRepository<PurchaseRequisition> requisitions, IIncidentBackOfficeSettingsService settings)
		{
			_requisitions = requisitions;
			_settings = settings;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentCommitment> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete || !change.IsNew)
				return;
			var commitment = change.Entity;
			commitment.CommittedByUserId ??= change.Actor.UserId;
			if (commitment.CommittedOn == default)
				commitment.CommittedOn = DateTime.UtcNow;
			if (string.IsNullOrWhiteSpace(commitment.PurchaseRequisitionId))
				return;
			var requisition = await _requisitions.LockForUpdateAsync(change.Actor.DepartmentId, commitment.PurchaseRequisitionId, cancellationToken);
			if (requisition == null || requisition.IncidentActivationId != commitment.IncidentActivationId)
				throw new IncidentRuleException("RequisitionNotFound");
			if (requisition.Status is not ((int)IncidentRequisitionStatuses.Approved or (int)IncidentRequisitionStatuses.Sourcing))
				throw new IncidentRuleException("RequisitionNotApproved");
			if (string.Equals(requisition.ApprovedByUserId, change.Actor.UserId, StringComparison.OrdinalIgnoreCase) && !await IncidentProcurementRule.SinglePersonAsync(_settings, change.Actor.DepartmentId))
				throw new IncidentRuleException("SeparationOfDuties");
			commitment.VendorId ??= requisition.VendorId;
			commitment.IncidentBudgetLineId ??= requisition.IncidentBudgetLineId;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentCommitment> change, CancellationToken cancellationToken)
		{
			if (!change.IsNew || string.IsNullOrWhiteSpace(change.Entity.PurchaseRequisitionId))
				return;
			var requisition = await _requisitions.GetForDepartmentAsync(change.Actor.DepartmentId, change.Entity.PurchaseRequisitionId, false, cancellationToken);
			if (requisition == null || requisition.Status == (int)IncidentRequisitionStatuses.Committed)
				return;
			requisition.Status = (int)IncidentRequisitionStatuses.Committed;
			await _requisitions.UpdateVersionedAsync(requisition, requisition.RowVersion, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>Receipts against a commitment (§9.13): the committer does not accept, and the commitment's received state follows the accepted amount.</summary>
	public sealed class IncidentPurchaseReceiptRule : IncidentRule<PurchaseReceipt>
	{
		private readonly IIncidentLedgerRepository<IncidentCommitment> _commitments;
		private readonly IIncidentLedgerRepository<PurchaseReceipt> _receipts;
		private readonly IIncidentBackOfficeSettingsService _settings;

		public IncidentPurchaseReceiptRule(IIncidentLedgerRepository<IncidentCommitment> commitments, IIncidentLedgerRepository<PurchaseReceipt> receipts, IIncidentBackOfficeSettingsService settings)
		{
			_commitments = commitments;
			_receipts = receipts;
			_settings = settings;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<PurchaseReceipt> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				return;
			var receipt = change.Entity;
			var commitment = await _commitments.GetForDepartmentAsync(change.Actor.DepartmentId, receipt.IncidentCommitmentId, false, cancellationToken);
			if (commitment == null || commitment.IncidentActivationId != receipt.IncidentActivationId)
				throw new IncidentRuleException("CommitmentNotFound");
			if (commitment.Status == (int)IncidentCommitmentStatuses.Void)
				throw new IncidentRuleException("CommitmentVoid");
			if (change.IsNew)
			{
				receipt.AcceptedByUserId ??= change.Actor.UserId;
				if (receipt.AcceptedOn == default)
					receipt.AcceptedOn = DateTime.UtcNow;
				if (string.Equals(commitment.CommittedByUserId, receipt.AcceptedByUserId, StringComparison.OrdinalIgnoreCase) && !await IncidentProcurementRule.SinglePersonAsync(_settings, change.Actor.DepartmentId))
					throw new IncidentRuleException("SeparationOfDuties");
			}
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<PurchaseReceipt> change, CancellationToken cancellationToken)
		{
			var departmentId = change.Actor.DepartmentId;
			var commitment = await _commitments.LockForUpdateAsync(departmentId, change.Entity.IncidentCommitmentId, cancellationToken);
			if (commitment == null || commitment.Status is (int)IncidentCommitmentStatuses.Closed or (int)IncidentCommitmentStatuses.Void)
				return;
			var accepted = (await _receipts.ListAsync(departmentId, new IncidentLedgerQuery { ActivationIds = new[] { commitment.IncidentActivationId } }
				.Where("IncidentCommitmentId", commitment.IncidentCommitmentId), cancellationToken))
				.Where(r => r.Status is (int)IncidentReceiptStatuses.Accepted or (int)IncidentReceiptStatuses.Partial or (int)IncidentReceiptStatuses.Received).Sum(r => r.Amount);
			var status = accepted <= 0 ? (int)IncidentCommitmentStatuses.Open : accepted < commitment.Amount ? (int)IncidentCommitmentStatuses.PartiallyReceived : (int)IncidentCommitmentStatuses.Received;
			if (status == commitment.Status)
				return;
			commitment.Status = status;
			await _commitments.UpdateVersionedAsync(commitment, commitment.RowVersion, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>
	/// Time immutability (§9.15): certified and approved time is never edited — a correction is an adjusting record linked by an
	/// IncidentTimeAdjustment. Certification and approval are stamped and separated (the certifier does not approve, unless
	/// single-person operation). A span longer than the work/rest rule's shift limit emits trigger 147; it is recorded, not refused.
	/// </summary>
	public sealed class IncidentTimeRecordRule : IncidentRule<IncidentTimeRecord>
	{
		private const double MaxSpanHours = 48;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly IIncidentEventPublisher _publisher;
		private readonly IIncidentLookupService _lookups;

		public IncidentTimeRecordRule(IIncidentBackOfficeSettingsService settings, IIncidentEventPublisher publisher, IIncidentLookupService lookups)
		{
			_settings = settings;
			_publisher = publisher;
			_lookups = lookups;
		}

		internal static bool Locked(int status) => status is (int)IncidentTimeStatuses.Certified or (int)IncidentTimeStatuses.Approved or (int)IncidentTimeStatuses.Adjusted or (int)IncidentTimeStatuses.Void;

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentTimeRecord> change, CancellationToken cancellationToken)
		{
			var record = change.Entity;
			if (change.IsDelete)
			{
				if (Locked(record.Status))
					throw new IncidentRuleException("TimeRecordLocked");
				return;
			}
			if (!change.IsNew && !change.IsTransition && Locked(change.Existing.Status))
				throw new IncidentRuleException("TimeRecordLocked");
			if (record.EndOn <= record.StartOn)
				throw new IncidentRuleException("TimeSpanOutOfOrder");
			if ((record.EndOn - record.StartOn).TotalHours > MaxSpanHours)
				throw new IncidentRuleException("TimeSpanTooLong", MaxSpanHours);
			if (MovedTo(change, (int)IncidentTimeStatuses.Certified))
			{
				record.CertifiedByUserId = change.Actor.UserId;
				record.CertifiedOn = DateTime.UtcNow;
			}
			if (MovedTo(change, (int)IncidentTimeStatuses.Approved))
			{
				if (string.Equals(record.CertifiedByUserId, change.Actor.UserId, StringComparison.OrdinalIgnoreCase) && !await IncidentProcurementRule.SinglePersonAsync(_settings, change.Actor.DepartmentId))
					throw new IncidentRuleException("SeparationOfDuties");
				record.ApprovedByUserId = change.Actor.UserId;
				record.ApprovedOn = DateTime.UtcNow;
			}
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentTimeRecord> change, CancellationToken cancellationToken)
		{
			var record = change.Entity;
			if (change.IsDelete || change.IsTransition || record.EntryType != 0)
				return;
			var policy = await _settings.GetAsync<IncidentAssignmentRulePolicy>(change.Actor.DepartmentId);
			var worked = (decimal)(record.EndOn - record.StartOn).TotalHours - record.UnpaidBreakMinutes / 60m;
			if (worked <= policy.MaxWorkHoursPerShift)
				return;
			var subject = record.IncidentParticipantId != null
				? (await _lookups.GetLabelsAsync(change.Actor, "IncidentParticipants", new[] { record.IncidentParticipantId }, cancellationToken)).GetValueOrDefault(record.IncidentParticipantId)
				: record.IncidentResourceId != null ? (await _lookups.GetLabelsAsync(change.Actor, "IncidentResources", new[] { record.IncidentResourceId }, cancellationToken)).GetValueOrDefault(record.IncidentResourceId) : null;
			await _publisher.PublishAsync(change.Actor.DepartmentId, change.Activation, WorkflowTriggerEventType.IncidentWorkRestViolation, new Dictionary<string, object>
			{
				["subject.name"] = subject,
				["rule.name"] = policy.RulePack,
				["hours.worked"] = Math.Round(worked, 2),
				["hours.limit"] = policy.MaxWorkHoursPerShift
			}, record.TableName, record.IncidentTimeRecordId, cancellationToken);
		}
	}

	/// <summary>Equipment use follows the same lock: certified or approved shift tickets are corrected by an adjusting record.</summary>
	public sealed class IncidentEquipmentUseRule : IncidentRule<IncidentEquipmentUseRecord>
	{
		private readonly IIncidentBackOfficeSettingsService _settings;

		public IncidentEquipmentUseRule(IIncidentBackOfficeSettingsService settings)
		{
			_settings = settings;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentEquipmentUseRecord> change, CancellationToken cancellationToken)
		{
			var record = change.Entity;
			if (change.IsDelete)
			{
				if (IncidentTimeRecordRule.Locked(record.Status))
					throw new IncidentRuleException("TimeRecordLocked");
				return;
			}
			if (!change.IsNew && !change.IsTransition && IncidentTimeRecordRule.Locked(change.Existing.Status))
				throw new IncidentRuleException("TimeRecordLocked");
			if (record.StartOn.HasValue && record.EndOn.HasValue)
			{
				if (record.EndOn <= record.StartOn)
					throw new IncidentRuleException("TimeSpanOutOfOrder");
				record.Hours ??= Math.Round((decimal)(record.EndOn.Value - record.StartOn.Value).TotalHours, 2);
			}
			record.ShiftDate = record.ShiftDate.Date;
			if (MovedTo(change, (int)IncidentTimeStatuses.Certified))
			{
				record.CertifiedByUserId = change.Actor.UserId;
				record.CertifiedOn = DateTime.UtcNow;
			}
			if (MovedTo(change, (int)IncidentTimeStatuses.Approved))
			{
				if (string.Equals(record.CertifiedByUserId, change.Actor.UserId, StringComparison.OrdinalIgnoreCase) && !await IncidentProcurementRule.SinglePersonAsync(_settings, change.Actor.DepartmentId))
					throw new IncidentRuleException("SeparationOfDuties");
				record.ApprovedByUserId = change.Actor.UserId;
				record.ApprovedOn = DateTime.UtcNow;
			}
		}
	}

	/// <summary>An adjustment marks the adjusted record Adjusted; both records must be the same incident's.</summary>
	public sealed class IncidentTimeAdjustmentRule : IncidentRule<IncidentTimeAdjustment>
	{
		private readonly IIncidentLedgerRepository<IncidentTimeRecord> _records;

		public IncidentTimeAdjustmentRule(IIncidentLedgerRepository<IncidentTimeRecord> records)
		{
			_records = records;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentTimeAdjustment> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				throw new IncidentRuleException("RecordCannotBeDeleted");
			var adjustment = change.Entity;
			if (string.Equals(adjustment.IncidentTimeRecordId, adjustment.AdjustingTimeRecordId, StringComparison.OrdinalIgnoreCase))
				throw new IncidentRuleException("AdjustmentSelf");
			var records = (await _records.GetByIdsAsync(change.Actor.DepartmentId, new[] { adjustment.IncidentTimeRecordId, adjustment.AdjustingTimeRecordId }, cancellationToken)).ToList();
			if (records.Count != 2 || records.Any(r => r.IncidentActivationId != adjustment.IncidentActivationId))
				throw new IncidentRuleException("RecordNotFound");
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentTimeAdjustment> change, CancellationToken cancellationToken)
		{
			if (!change.IsNew)
				return;
			var original = await _records.LockForUpdateAsync(change.Actor.DepartmentId, change.Entity.IncidentTimeRecordId, cancellationToken);
			if (original == null || original.Status == (int)IncidentTimeStatuses.Adjusted)
				return;
			original.Status = (int)IncidentTimeStatuses.Adjusted;
			await _records.UpdateVersionedAsync(original, original.RowVersion, change.Actor.UserId, cancellationToken);
			var adjusting = await _records.GetForDepartmentAsync(change.Actor.DepartmentId, change.Entity.AdjustingTimeRecordId, false, cancellationToken);
			if (adjusting != null && adjusting.AdjustsTimeRecordId == null)
			{
				adjusting.AdjustsTimeRecordId = original.IncidentTimeRecordId;
				await _records.UpdateVersionedAsync(adjusting, adjusting.RowVersion, change.Actor.UserId, cancellationToken);
			}
		}
	}

	/// <summary>
	/// Support-status history (§9.5): every move of a resource's support status is kept, and the arrival, release and departure
	/// times are stamped on the resource.
	/// </summary>
	public sealed class IncidentResourceStatusRule : IncidentRule<IncidentResource>
	{
		private readonly IIncidentLedgerRepository<IncidentResourceStatusHistory> _history;

		public IncidentResourceStatusRule(IIncidentLedgerRepository<IncidentResourceStatusHistory> history)
		{
			_history = history;
		}

		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentResource> change, CancellationToken cancellationToken)
		{
			if (!Moved(change))
				return Task.CompletedTask;
			var resource = change.Entity;
			var now = DateTime.UtcNow;
			switch ((IncidentSupportStatuses)change.NewStatus)
			{
				case IncidentSupportStatuses.Arrived: resource.ArrivedOn ??= now; break;
				case IncidentSupportStatuses.Supported: resource.CheckedInOn ??= now; break;
				case IncidentSupportStatuses.Released: resource.ReleasedOn ??= now; break;
				case IncidentSupportStatuses.Returned: resource.DepartedOn ??= now; break;
			}
			return Task.CompletedTask;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentResource> change, CancellationToken cancellationToken)
		{
			if (!Moved(change))
				return;
			await _history.InsertNewAsync(new IncidentResourceStatusHistory
			{
				DepartmentId = change.Actor.DepartmentId,
				IncidentActivationId = change.Entity.IncidentActivationId,
				SubjectType = (int)IncidentSubjectTypes.Resource,
				SubjectId = change.Entity.IncidentResourceId,
				OldStatus = change.OldStatus ?? 0,
				NewStatus = change.NewStatus ?? 0,
				Reason = IncidentRequestRule.Trim(change.Reason, 500)
			}, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>The same history for participants (a person's support status, never their operational status).</summary>
	public sealed class IncidentParticipantStatusRule : IncidentRule<IncidentParticipant>
	{
		private readonly IIncidentLedgerRepository<IncidentResourceStatusHistory> _history;

		public IncidentParticipantStatusRule(IIncidentLedgerRepository<IncidentResourceStatusHistory> history)
		{
			_history = history;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentParticipant> change, CancellationToken cancellationToken)
		{
			if (!Moved(change))
				return;
			await _history.InsertNewAsync(new IncidentResourceStatusHistory
			{
				DepartmentId = change.Actor.DepartmentId,
				IncidentActivationId = change.Entity.IncidentActivationId,
				SubjectType = (int)IncidentSubjectTypes.Participant,
				SubjectId = change.Entity.IncidentParticipantId,
				OldStatus = change.OldStatus ?? 0,
				NewStatus = change.NewStatus ?? 0,
				Reason = IncidentRequestRule.Trim(change.Reason, 500)
			}, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>
	/// Demobilization (§9.6): a new plan is seeded with the standard checklist (blocking items for property, vehicle, time, receipts
	/// and lodging); release is refused while a blocking item is open; approval and departure are stamped.
	/// </summary>
	public sealed class IncidentDemobPlanRule : IncidentRule<IncidentDemobPlan>
	{
		private static readonly (IncidentDemobCategories Category, string Name, string Owner, bool Blocking)[] Standard =
		{
			(IncidentDemobCategories.RoleHandoff, "Role handed off", null, false),
			(IncidentDemobCategories.Property, "Issued property returned", "SPUL", true),
			(IncidentDemobCategories.Vehicle, "Vehicle inspected", "GSUL", true),
			(IncidentDemobCategories.Time, "Time and equipment posted", "TIME", true),
			(IncidentDemobCategories.ReceiptClaim, "Receipts and claims filed", "FSC", true),
			(IncidentDemobCategories.Lodging, "Lodging checked out", "FACL", true),
			(IncidentDemobCategories.Travel, "Travel arranged", "DMOB", false),
			(IncidentDemobCategories.Evaluation, "Performance evaluation", null, false)
		};

		private readonly IIncidentLedgerRepository<IncidentDemobChecklistItem> _items;

		public IncidentDemobPlanRule(IIncidentLedgerRepository<IncidentDemobChecklistItem> items)
		{
			_items = items;
		}

		public override async Task BeforeSaveAsync(IncidentLedgerChange<IncidentDemobPlan> change, CancellationToken cancellationToken)
		{
			var plan = change.Entity;
			if (MovedTo(change, (int)IncidentDemobStatuses.Approved))
			{
				plan.ApprovedByUserId = change.Actor.UserId;
				plan.ApprovedReleaseOn ??= plan.TentativeReleaseOn ?? DateTime.UtcNow;
			}
			if (MovedTo(change, (int)IncidentDemobStatuses.Released))
			{
				var open = (await _items.ListAsync(change.Actor.DepartmentId, new IncidentLedgerQuery { ActivationIds = new[] { plan.IncidentActivationId } }
					.Where("IncidentDemobPlanId", plan.IncidentDemobPlanId).Where("IsBlocking", true).Where("Status", (int)IncidentGateItemStatuses.Open), cancellationToken)).ToList();
				if (open.Count > 0)
					throw new IncidentRuleException("DemobBlocked", string.Join(", ", open.Select(i => i.Name)));
			}
			if (MovedTo(change, (int)IncidentDemobStatuses.Departed))
				plan.DepartedOn ??= DateTime.UtcNow;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentDemobPlan> change, CancellationToken cancellationToken)
		{
			if (!change.IsNew)
				return;
			foreach (var item in Standard)
				await _items.InsertNewAsync(new IncidentDemobChecklistItem
				{
					DepartmentId = change.Actor.DepartmentId,
					IncidentActivationId = change.Entity.IncidentActivationId,
					IncidentDemobPlanId = change.Entity.IncidentDemobPlanId,
					Category = (int)item.Category,
					Name = item.Name,
					OwnerPosition = item.Owner,
					IsBlocking = item.Blocking,
					Status = (int)IncidentGateItemStatuses.Open
				}, change.Actor.UserId, cancellationToken);
		}
	}

	/// <summary>Gate items (demobilization and closeout): who completed or waived them and why.</summary>
	public sealed class IncidentDemobItemRule : IncidentRule<IncidentDemobChecklistItem>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentDemobChecklistItem> change, CancellationToken cancellationToken)
		{
			if (MovedTo(change, (int)IncidentGateItemStatuses.Complete)) { change.Entity.CompletedOn = DateTime.UtcNow; change.Entity.CompletedByUserId = change.Actor.UserId; }
			if (MovedTo(change, (int)IncidentGateItemStatuses.Waived)) { change.Entity.WaivedByUserId = change.Actor.UserId; change.Entity.WaivedReason = IncidentRequestRule.Trim(change.Reason, 500); }
			return Task.CompletedTask;
		}
	}

	public sealed class IncidentCloseoutItemRule : IncidentRule<IncidentCloseoutChecklistItem>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentCloseoutChecklistItem> change, CancellationToken cancellationToken)
		{
			if (MovedTo(change, (int)IncidentGateItemStatuses.Complete)) { change.Entity.CompletedOn = DateTime.UtcNow; change.Entity.CompletedByUserId = change.Actor.UserId; }
			if (MovedTo(change, (int)IncidentGateItemStatuses.Waived)) { change.Entity.WaivedByUserId = change.Actor.UserId; change.Entity.WaivedReason = IncidentRequestRule.Trim(change.Reason, 500); }
			return Task.CompletedTask;
		}
	}

	/// <summary>Claims open and close with a time; decisions record when they were decided.</summary>
	public sealed class IncidentClaimRule : IncidentRule<IncidentClaim>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentClaim> change, CancellationToken cancellationToken)
		{
			if (change.IsNew && change.Entity.OpenedOn == default) change.Entity.OpenedOn = DateTime.UtcNow;
			if (Moved(change) && change.NewStatus is (int)IncidentClaimStatuses.Closed or (int)IncidentClaimStatuses.Settled) change.Entity.ClosedOn ??= DateTime.UtcNow;
			return Task.CompletedTask;
		}
	}

	public sealed class IncidentDecisionRule : IncidentRule<IncidentDecision>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentDecision> change, CancellationToken cancellationToken)
		{
			if (MovedTo(change, (int)IncidentDecisionStatuses.Decided)) change.Entity.DecidedOn ??= DateTime.UtcNow;
			return Task.CompletedTask;
		}
	}

	/// <summary>
	/// Budget authority (§9.14): a funding authorization moves the revised amount of its line (a transfer takes it from the source
	/// line), and the budget's revised total follows its lines. Authorizations are append-only.
	/// </summary>
	public sealed class IncidentFundingAuthorizationRule : IncidentRule<IncidentFundingAuthorization>
	{
		private readonly IIncidentLedgerRepository<IncidentBudgetLine> _lines;
		private readonly IIncidentLedgerRepository<IncidentBudget> _budgets;

		public IncidentFundingAuthorizationRule(IIncidentLedgerRepository<IncidentBudgetLine> lines, IIncidentLedgerRepository<IncidentBudget> budgets)
		{
			_lines = lines;
			_budgets = budgets;
		}

		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentFundingAuthorization> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete || !change.IsNew)
				throw new IncidentRuleException("AuthorizationAppendOnly");
			var authorization = change.Entity;
			if (authorization.Kind == (int)IncidentFundingAuthorizationKinds.Transfer && (string.IsNullOrWhiteSpace(authorization.FromBudgetLineId) || authorization.Amount <= 0))
				throw new IncidentRuleException("TransferNeedsSource");
			if (authorization.AuthorizedOn == default)
				authorization.AuthorizedOn = DateTime.UtcNow;
			return Task.CompletedTask;
		}

		public override async Task AfterSaveAsync(IncidentLedgerChange<IncidentFundingAuthorization> change, CancellationToken cancellationToken)
		{
			var authorization = change.Entity;
			var departmentId = change.Actor.DepartmentId;
			var budgets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			async Task MoveAsync(string lineId, decimal delta)
			{
				var line = await _lines.LockForUpdateAsync(departmentId, lineId, cancellationToken);
				if (line == null || line.IncidentActivationId != authorization.IncidentActivationId)
					throw new IncidentRuleException("BudgetLineNotFound");
				line.RevisedAmount += delta;
				if (line.RevisedAmount < 0)
					throw new IncidentRuleException("BudgetLineNegative", line.Name);
				await _lines.UpdateVersionedAsync(line, line.RowVersion, change.Actor.UserId, cancellationToken);
				budgets.Add(line.IncidentBudgetId);
			}
			// The initial authorization is the line's original amount; revisions and transfers move the revised amount.
			if (authorization.Kind != (int)IncidentFundingAuthorizationKinds.Initial && !string.IsNullOrWhiteSpace(authorization.IncidentBudgetLineId))
				await MoveAsync(authorization.IncidentBudgetLineId, authorization.Amount);
			if (authorization.Kind == (int)IncidentFundingAuthorizationKinds.Transfer)
				await MoveAsync(authorization.FromBudgetLineId, -authorization.Amount);
			foreach (var budgetId in budgets)
			{
				var budget = await _budgets.LockForUpdateAsync(departmentId, budgetId, cancellationToken);
				if (budget == null) continue;
				budget.RevisedAmount = await _lines.SumAsync(departmentId, "RevisedAmount", new IncidentLedgerQuery { ActivationIds = new[] { budget.IncidentActivationId } }.Where("IncidentBudgetId", budgetId), cancellationToken);
				await _budgets.UpdateVersionedAsync(budget, budget.RowVersion, change.Actor.UserId, cancellationToken);
			}
		}
	}

	/// <summary>
	/// A budget line's revised amount is authority, not an editable field: it starts at the original amount and moves only through
	/// funding authorizations (or by the change of the original amount itself).
	/// </summary>
	public sealed class IncidentBudgetLineRule : IncidentRule<IncidentBudgetLine>
	{
		public override Task BeforeSaveAsync(IncidentLedgerChange<IncidentBudgetLine> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete)
				return Task.CompletedTask;
			var line = change.Entity;
			line.RevisedAmount = change.IsNew ? line.OriginalAmount : change.Existing.RevisedAmount + (line.OriginalAmount - change.Existing.OriginalAmount);
			return Task.CompletedTask;
		}
	}

	public static class IncidentDomainRules
	{
		private static void Number<T>(ContainerBuilder builder, Func<T, string> get, Action<T, string> set, string prefix) where T : class, IIncidentLedgerEntity, IIncidentScopedEntity
		{
			builder.Register(c => new IncidentNumberingRule<T>(c.Resolve<IIncidentLedgerRepository<T>>(), c.Resolve<IIncidentLedgerRepository<IncidentActivation>>(), get, set, prefix))
				.As<IIncidentLedgerRule<T>>().InstancePerLifetimeScope();
		}

		public static void Register(ContainerBuilder builder)
		{
			Number<IncidentResourceRequest>(builder, e => e.RequestNumber, (e, v) => e.RequestNumber = v, "R");
			Number<IncidentServiceRequest>(builder, e => e.TicketNumber, (e, v) => e.TicketNumber = v, "T");
			Number<PurchaseRequisition>(builder, e => e.RequisitionNumber, (e, v) => e.RequisitionNumber = v, "REQ");
			Number<IncidentCommitment>(builder, e => e.CommitmentNumber, (e, v) => e.CommitmentNumber = v, "C");
			Number<PaymentPackage>(builder, e => e.PackageNumber, (e, v) => e.PackageNumber = v, "PP");
			Number<IncidentClaim>(builder, e => e.ClaimNumber, (e, v) => e.ClaimNumber = v, "CL");

			builder.RegisterType<IncidentRequestRule>().As<IIncidentLedgerRule<IncidentResourceRequest>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentRequestFillRule>().As<IIncidentLedgerRule<IncidentRequestFill>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentLodgingNightRule>().As<IIncidentLedgerRule<IncidentLodgingAssignment>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentVendorCapacityRule>().As<IIncidentLedgerRule<IncidentVendorCapacityCommitment>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentProcurementRule>().As<IIncidentLedgerRule<PurchaseRequisition>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentCommitmentRule>().As<IIncidentLedgerRule<IncidentCommitment>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentPurchaseReceiptRule>().As<IIncidentLedgerRule<PurchaseReceipt>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentTimeRecordRule>().As<IIncidentLedgerRule<IncidentTimeRecord>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentEquipmentUseRule>().As<IIncidentLedgerRule<IncidentEquipmentUseRecord>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentTimeAdjustmentRule>().As<IIncidentLedgerRule<IncidentTimeAdjustment>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentResourceStatusRule>().As<IIncidentLedgerRule<IncidentResource>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentParticipantStatusRule>().As<IIncidentLedgerRule<IncidentParticipant>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentDemobPlanRule>().As<IIncidentLedgerRule<IncidentDemobPlan>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentDemobItemRule>().As<IIncidentLedgerRule<IncidentDemobChecklistItem>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentCloseoutItemRule>().As<IIncidentLedgerRule<IncidentCloseoutChecklistItem>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentClaimRule>().As<IIncidentLedgerRule<IncidentClaim>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentDecisionRule>().As<IIncidentLedgerRule<IncidentDecision>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentFundingAuthorizationRule>().As<IIncidentLedgerRule<IncidentFundingAuthorization>>().InstancePerLifetimeScope();
			builder.RegisterType<IncidentBudgetLineRule>().As<IIncidentLedgerRule<IncidentBudgetLine>>().InstancePerLifetimeScope();
		}
	}
}
