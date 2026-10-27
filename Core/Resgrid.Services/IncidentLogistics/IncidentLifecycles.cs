using Autofac;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// The fixed lifecycles of the Incident Back Office (plan §9.4, §9.5, §9.12.3, §9.13, §9.14 and §10 R1), as transition tables.
	/// Departments label sub-states through CustomState; they never add, remove or reorder these values. A status moves only
	/// through IIncidentLedgerService.TransitionAsync, so every move is checked here and audited.
	/// </summary>
	public static class IncidentLifecycles
	{
		private static void Add<T, TStatus>(ContainerBuilder builder, IncidentLifecycle<T, TStatus> lifecycle)
			where T : class, IIncidentLedgerEntity where TStatus : struct, System.Enum
			=> builder.RegisterInstance(lifecycle).As<IIncidentLifecycle<T>>().SingleInstance();

		public static void Register(ContainerBuilder builder)
		{
			// §9.0 activation: Preparing → Active → Demobilizing → Closed → Sealed, with an audited, reasoned reopen.
			Add(builder, new IncidentLifecycle<IncidentActivation, IncidentActivationStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentActivationStatuses.Preparing, IncidentActivationStatuses.Active, IncidentActivationStatuses.Closed)
				.Allow(IncidentActivationStatuses.Active, IncidentActivationStatuses.Demobilizing, IncidentActivationStatuses.Closed)
				.Allow(IncidentActivationStatuses.Demobilizing, IncidentActivationStatuses.Active, IncidentActivationStatuses.Closed)
				.Allow(IncidentActivationStatuses.Closed, IncidentActivationStatuses.Sealed, IncidentActivationStatuses.Active)
				.Allow(IncidentActivationStatuses.Sealed, IncidentActivationStatuses.Active));

			Add(builder, new IncidentLifecycle<IncidentOperationalPeriod, IncidentPeriodStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentPeriodStatuses.Planned, IncidentPeriodStatuses.Current, IncidentPeriodStatuses.Closed)
				.Allow(IncidentPeriodStatuses.Current, IncidentPeriodStatuses.Closed)
				.Terminal(IncidentPeriodStatuses.Closed));

			Add(builder, new IncidentLifecycle<IncidentPlanVersion, IncidentPlanVersionStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentPlanVersionStatuses.Draft, IncidentPlanVersionStatuses.Assembled, IncidentPlanVersionStatuses.Superseded)
				.Allow(IncidentPlanVersionStatuses.Assembled, IncidentPlanVersionStatuses.Approved, IncidentPlanVersionStatuses.Draft, IncidentPlanVersionStatuses.Superseded)
				.Allow(IncidentPlanVersionStatuses.Approved, IncidentPlanVersionStatuses.Distributed, IncidentPlanVersionStatuses.Superseded)
				.Allow(IncidentPlanVersionStatuses.Distributed, IncidentPlanVersionStatuses.Superseded)
				.Terminal(IncidentPlanVersionStatuses.Superseded));

			Add(builder, new IncidentLifecycle<IncidentObjective, IncidentObjectiveStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentObjectiveStatuses.Open, IncidentObjectiveStatuses.Met, IncidentObjectiveStatuses.Carried, IncidentObjectiveStatuses.Dropped)
				.Allow(IncidentObjectiveStatuses.Carried, IncidentObjectiveStatuses.Open, IncidentObjectiveStatuses.Met, IncidentObjectiveStatuses.Dropped));

			Add(builder, new IncidentLifecycle<IncidentReconciliationItem, IncidentReconciliationStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentReconciliationStatuses.Open, IncidentReconciliationStatuses.Resolved, IncidentReconciliationStatuses.Dismissed)
				.Terminal(IncidentReconciliationStatuses.Resolved, IncidentReconciliationStatuses.Dismissed)
				.ReasonFor(IncidentReconciliationStatuses.Dismissed));

			// §9.4 the request lifecycle. Rejected and Cancelled are terminal branches with reason and authority.
			Add(builder, new IncidentLifecycle<IncidentResourceRequest, IncidentRequestStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentRequestStatuses.Draft, IncidentRequestStatuses.Submitted)
				.Allow(IncidentRequestStatuses.Submitted, IncidentRequestStatuses.Approved, IncidentRequestStatuses.Rejected, IncidentRequestStatuses.Draft)
				.Allow(IncidentRequestStatuses.Approved, IncidentRequestStatuses.Ordered)
				.Allow(IncidentRequestStatuses.Ordered, IncidentRequestStatuses.PartiallyFilled, IncidentRequestStatuses.Filled)
				.Allow(IncidentRequestStatuses.PartiallyFilled, IncidentRequestStatuses.Filled, IncidentRequestStatuses.EnRoute, IncidentRequestStatuses.Received)
				.Allow(IncidentRequestStatuses.Filled, IncidentRequestStatuses.EnRoute, IncidentRequestStatuses.Received)
				.Allow(IncidentRequestStatuses.EnRoute, IncidentRequestStatuses.Received)
				.Allow(IncidentRequestStatuses.Received, IncidentRequestStatuses.Assigned, IncidentRequestStatuses.Released, IncidentRequestStatuses.Closed)
				.Allow(IncidentRequestStatuses.Assigned, IncidentRequestStatuses.Released)
				.Allow(IncidentRequestStatuses.Released, IncidentRequestStatuses.Demobilized, IncidentRequestStatuses.Closed)
				.Allow(IncidentRequestStatuses.Demobilized, IncidentRequestStatuses.Closed)
				.Terminal(IncidentRequestStatuses.Closed, IncidentRequestStatuses.Rejected, IncidentRequestStatuses.Cancelled)
				.AllowFromAnyOpen(IncidentRequestStatuses.Cancelled)
				.ReasonFor(IncidentRequestStatuses.Rejected, IncidentRequestStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentRequestFill, IncidentFillStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentFillStatuses.Offered, IncidentFillStatuses.Accepted, IncidentFillStatuses.Cancelled)
				.Allow(IncidentFillStatuses.Accepted, IncidentFillStatuses.EnRoute, IncidentFillStatuses.Arrived, IncidentFillStatuses.Cancelled)
				.Allow(IncidentFillStatuses.EnRoute, IncidentFillStatuses.Arrived, IncidentFillStatuses.Cancelled)
				.Terminal(IncidentFillStatuses.Arrived, IncidentFillStatuses.Cancelled)
				.ReasonFor(IncidentFillStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentReceipt, IncidentReceiptStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentReceiptStatuses.Received, IncidentReceiptStatuses.Accepted, IncidentReceiptStatuses.Partial, IncidentReceiptStatuses.Rejected)
				.Allow(IncidentReceiptStatuses.Partial, IncidentReceiptStatuses.Accepted, IncidentReceiptStatuses.Rejected)
				.ReasonFor(IncidentReceiptStatuses.Rejected));

			// §9.5 support status of resources and participants.
			var support = new IncidentLifecycle<IncidentResource, IncidentSupportStatuses>(e => e.SupportStatus, (e, s) => e.SupportStatus = s)
				.Allow(IncidentSupportStatuses.Ordered, IncidentSupportStatuses.Mobilizing, IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.Mobilizing, IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.Arrived, IncidentSupportStatuses.Supported, IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.Supported, IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Supported, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.Released, IncidentSupportStatuses.Returned, IncidentSupportStatuses.Closed)
				.Allow(IncidentSupportStatuses.Returned, IncidentSupportStatuses.Closed)
				.Terminal(IncidentSupportStatuses.Closed);
			Add(builder, support);
			Add(builder, new IncidentLifecycle<IncidentParticipant, IncidentSupportStatuses>(e => e.SupportStatus, (e, s) => e.SupportStatus = s)
				.Allow(IncidentSupportStatuses.Ordered, IncidentSupportStatuses.Mobilizing, IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.Mobilizing, IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.EnRoute, IncidentSupportStatuses.Arrived)
				.Allow(IncidentSupportStatuses.Arrived, IncidentSupportStatuses.Supported, IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.Supported, IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.PendingDemob, IncidentSupportStatuses.Supported, IncidentSupportStatuses.Released)
				.Allow(IncidentSupportStatuses.Released, IncidentSupportStatuses.Returned, IncidentSupportStatuses.Closed)
				.Allow(IncidentSupportStatuses.Returned, IncidentSupportStatuses.Closed)
				.Terminal(IncidentSupportStatuses.Closed));

			Add(builder, new IncidentLifecycle<IncidentDemobPlan, IncidentDemobStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentDemobStatuses.Tentative, IncidentDemobStatuses.Approved, IncidentDemobStatuses.Cancelled)
				.Allow(IncidentDemobStatuses.Approved, IncidentDemobStatuses.Released, IncidentDemobStatuses.Tentative, IncidentDemobStatuses.Cancelled)
				.Allow(IncidentDemobStatuses.Released, IncidentDemobStatuses.Departed)
				.Allow(IncidentDemobStatuses.Departed, IncidentDemobStatuses.Closed)
				.Terminal(IncidentDemobStatuses.Closed, IncidentDemobStatuses.Cancelled)
				.ReasonFor(IncidentDemobStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentDemobChecklistItem, IncidentGateItemStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentGateItemStatuses.Open, IncidentGateItemStatuses.Complete, IncidentGateItemStatuses.Waived)
				.Allow(IncidentGateItemStatuses.Complete, IncidentGateItemStatuses.Open)
				.ReasonFor(IncidentGateItemStatuses.Waived));

			Add(builder, new IncidentLifecycle<IncidentCloseoutChecklistItem, IncidentGateItemStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentGateItemStatuses.Open, IncidentGateItemStatuses.Complete, IncidentGateItemStatuses.Waived)
				.Allow(IncidentGateItemStatuses.Complete, IncidentGateItemStatuses.Open)
				.ReasonFor(IncidentGateItemStatuses.Waived));

			Add(builder, new IncidentLifecycle<IncidentFacility, IncidentFacilityStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentFacilityStatuses.Planned, IncidentFacilityStatuses.Active, IncidentFacilityStatuses.Closed)
				.Allow(IncidentFacilityStatuses.Active, IncidentFacilityStatuses.Closed)
				.Allow(IncidentFacilityStatuses.Closed, IncidentFacilityStatuses.Active));

			Add(builder, new IncidentLifecycle<IncidentLodgingBlock, IncidentLodgingBlockStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentLodgingBlockStatuses.Requested, IncidentLodgingBlockStatuses.Held, IncidentLodgingBlockStatuses.Confirmed, IncidentLodgingBlockStatuses.Released)
				.Allow(IncidentLodgingBlockStatuses.Held, IncidentLodgingBlockStatuses.Confirmed, IncidentLodgingBlockStatuses.Released)
				.Allow(IncidentLodgingBlockStatuses.Confirmed, IncidentLodgingBlockStatuses.Released)
				.Terminal(IncidentLodgingBlockStatuses.Released));

			// §9.7: NoShow and Cancelled free the bed-nights; Departed releases the nights after the departure.
			Add(builder, new IncidentLifecycle<IncidentLodgingAssignment, IncidentLodgingAssignmentStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentLodgingAssignmentStatuses.Held, IncidentLodgingAssignmentStatuses.Confirmed, IncidentLodgingAssignmentStatuses.Occupied, IncidentLodgingAssignmentStatuses.Cancelled, IncidentLodgingAssignmentStatuses.NoShow)
				.Allow(IncidentLodgingAssignmentStatuses.Confirmed, IncidentLodgingAssignmentStatuses.Occupied, IncidentLodgingAssignmentStatuses.NoShow, IncidentLodgingAssignmentStatuses.Cancelled)
				.Allow(IncidentLodgingAssignmentStatuses.Occupied, IncidentLodgingAssignmentStatuses.Departed)
				.Terminal(IncidentLodgingAssignmentStatuses.NoShow, IncidentLodgingAssignmentStatuses.Departed, IncidentLodgingAssignmentStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentMealPlan, IncidentMealPlanStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentMealPlanStatuses.Planned, IncidentMealPlanStatuses.Active, IncidentMealPlanStatuses.Cancelled)
				.Allow(IncidentMealPlanStatuses.Active, IncidentMealPlanStatuses.Closed, IncidentMealPlanStatuses.Cancelled)
				.Terminal(IncidentMealPlanStatuses.Closed, IncidentMealPlanStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentMealOrder, IncidentOrderStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentOrderStatuses.Draft, IncidentOrderStatuses.Placed, IncidentOrderStatuses.Cancelled)
				.Allow(IncidentOrderStatuses.Placed, IncidentOrderStatuses.Confirmed, IncidentOrderStatuses.Delivered, IncidentOrderStatuses.Cancelled)
				.Allow(IncidentOrderStatuses.Confirmed, IncidentOrderStatuses.Delivered, IncidentOrderStatuses.Cancelled)
				.Terminal(IncidentOrderStatuses.Delivered, IncidentOrderStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentServiceRequest, IncidentServiceRequestStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentServiceRequestStatuses.Open, IncidentServiceRequestStatuses.Assigned, IncidentServiceRequestStatuses.InProgress, IncidentServiceRequestStatuses.OnHold, IncidentServiceRequestStatuses.Completed)
				.Allow(IncidentServiceRequestStatuses.Assigned, IncidentServiceRequestStatuses.InProgress, IncidentServiceRequestStatuses.OnHold, IncidentServiceRequestStatuses.Completed)
				.Allow(IncidentServiceRequestStatuses.InProgress, IncidentServiceRequestStatuses.OnHold, IncidentServiceRequestStatuses.Completed)
				.Allow(IncidentServiceRequestStatuses.OnHold, IncidentServiceRequestStatuses.InProgress)
				.Terminal(IncidentServiceRequestStatuses.Completed, IncidentServiceRequestStatuses.Cancelled)
				.AllowFromAnyOpen(IncidentServiceRequestStatuses.Cancelled)
				.ReasonFor(IncidentServiceRequestStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentTransportRequest, IncidentTransportStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentTransportStatuses.Requested, IncidentTransportStatuses.Scheduled, IncidentTransportStatuses.Cancelled)
				.Allow(IncidentTransportStatuses.Scheduled, IncidentTransportStatuses.EnRoute, IncidentTransportStatuses.Cancelled)
				.Allow(IncidentTransportStatuses.EnRoute, IncidentTransportStatuses.Arrived)
				.Allow(IncidentTransportStatuses.Arrived, IncidentTransportStatuses.Completed)
				.Terminal(IncidentTransportStatuses.Completed, IncidentTransportStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentTransportMovement, IncidentTransportStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentTransportStatuses.Requested, IncidentTransportStatuses.Scheduled, IncidentTransportStatuses.Cancelled)
				.Allow(IncidentTransportStatuses.Scheduled, IncidentTransportStatuses.EnRoute, IncidentTransportStatuses.Cancelled)
				.Allow(IncidentTransportStatuses.EnRoute, IncidentTransportStatuses.Arrived, IncidentTransportStatuses.Cancelled)
				.Allow(IncidentTransportStatuses.Arrived, IncidentTransportStatuses.Completed)
				.Terminal(IncidentTransportStatuses.Completed, IncidentTransportStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentCommunicationsResourceAssignment, IncidentCustodyStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentCustodyStatuses.Issued, IncidentCustodyStatuses.Returned, IncidentCustodyStatuses.Lost, IncidentCustodyStatuses.Damaged, IncidentCustodyStatuses.InRepair)
				.Allow(IncidentCustodyStatuses.InRepair, IncidentCustodyStatuses.Issued, IncidentCustodyStatuses.Returned)
				.Allow(IncidentCustodyStatuses.Damaged, IncidentCustodyStatuses.InRepair, IncidentCustodyStatuses.Returned)
				.Allow(IncidentCustodyStatuses.Lost, IncidentCustodyStatuses.Returned));

			// §9.12.3 vendor engagement: explicit states, organization eligibility kept separate.
			Add(builder, new IncidentLifecycle<IncidentVendorEngagement, IncidentEngagementStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentEngagementStatuses.Available, IncidentEngagementStatuses.Contacted, IncidentEngagementStatuses.QuoteRequested, IncidentEngagementStatuses.Held, IncidentEngagementStatuses.Engaged)
				.Allow(IncidentEngagementStatuses.Contacted, IncidentEngagementStatuses.QuoteRequested, IncidentEngagementStatuses.Held, IncidentEngagementStatuses.Engaged, IncidentEngagementStatuses.Closed)
				.Allow(IncidentEngagementStatuses.QuoteRequested, IncidentEngagementStatuses.Held, IncidentEngagementStatuses.Engaged, IncidentEngagementStatuses.Closed)
				.Allow(IncidentEngagementStatuses.Held, IncidentEngagementStatuses.Engaged, IncidentEngagementStatuses.Released)
				.Allow(IncidentEngagementStatuses.Engaged, IncidentEngagementStatuses.Active, IncidentEngagementStatuses.Suspended, IncidentEngagementStatuses.Released)
				.Allow(IncidentEngagementStatuses.Active, IncidentEngagementStatuses.AtCapacity, IncidentEngagementStatuses.Suspended, IncidentEngagementStatuses.Released)
				.Allow(IncidentEngagementStatuses.AtCapacity, IncidentEngagementStatuses.Active, IncidentEngagementStatuses.Suspended, IncidentEngagementStatuses.Released)
				.Allow(IncidentEngagementStatuses.Suspended, IncidentEngagementStatuses.Active, IncidentEngagementStatuses.Released)
				.Allow(IncidentEngagementStatuses.Released, IncidentEngagementStatuses.Closed)
				.Terminal(IncidentEngagementStatuses.Closed)
				.ReasonFor(IncidentEngagementStatuses.Suspended));

			Add(builder, new IncidentLifecycle<IncidentVendorCapacityCommitment, IncidentHoldStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentHoldStatuses.Active, IncidentHoldStatuses.Released, IncidentHoldStatuses.Expired, IncidentHoldStatuses.Converted, IncidentHoldStatuses.Consumed)
				.Terminal(IncidentHoldStatuses.Released, IncidentHoldStatuses.Expired, IncidentHoldStatuses.Converted, IncidentHoldStatuses.Consumed));

			Add(builder, new IncidentLifecycle<IncidentVendorQuote, IncidentQuoteStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentQuoteStatuses.Requested, IncidentQuoteStatuses.Received, IncidentQuoteStatuses.Declined)
				.Allow(IncidentQuoteStatuses.Received, IncidentQuoteStatuses.Accepted, IncidentQuoteStatuses.Declined)
				.Terminal(IncidentQuoteStatuses.Accepted, IncidentQuoteStatuses.Declined));

			// §9.12.3 procurement: requisition → approval → sourcing → commitment → receipt → invoice match → payment package.
			Add(builder, new IncidentLifecycle<PurchaseRequisition, IncidentRequisitionStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentRequisitionStatuses.Draft, IncidentRequisitionStatuses.Submitted)
				.Allow(IncidentRequisitionStatuses.Submitted, IncidentRequisitionStatuses.Approved, IncidentRequisitionStatuses.Rejected, IncidentRequisitionStatuses.Draft)
				.Allow(IncidentRequisitionStatuses.Approved, IncidentRequisitionStatuses.Sourcing, IncidentRequisitionStatuses.Committed)
				.Allow(IncidentRequisitionStatuses.Sourcing, IncidentRequisitionStatuses.Committed)
				.Allow(IncidentRequisitionStatuses.Committed, IncidentRequisitionStatuses.Closed)
				.Terminal(IncidentRequisitionStatuses.Rejected, IncidentRequisitionStatuses.Cancelled, IncidentRequisitionStatuses.Closed)
				.AllowFromAnyOpen(IncidentRequisitionStatuses.Cancelled)
				.ReasonFor(IncidentRequisitionStatuses.Rejected, IncidentRequisitionStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentCommitment, IncidentCommitmentStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentCommitmentStatuses.Open, IncidentCommitmentStatuses.PartiallyReceived, IncidentCommitmentStatuses.Received, IncidentCommitmentStatuses.Void)
				.Allow(IncidentCommitmentStatuses.PartiallyReceived, IncidentCommitmentStatuses.Received, IncidentCommitmentStatuses.Closed)
				.Allow(IncidentCommitmentStatuses.Received, IncidentCommitmentStatuses.Closed)
				.Terminal(IncidentCommitmentStatuses.Closed, IncidentCommitmentStatuses.Void)
				.ReasonFor(IncidentCommitmentStatuses.Void));

			Add(builder, new IncidentLifecycle<PurchaseReceipt, IncidentReceiptStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentReceiptStatuses.Received, IncidentReceiptStatuses.Accepted, IncidentReceiptStatuses.Partial, IncidentReceiptStatuses.Rejected)
				.Allow(IncidentReceiptStatuses.Partial, IncidentReceiptStatuses.Accepted, IncidentReceiptStatuses.Rejected)
				.ReasonFor(IncidentReceiptStatuses.Rejected));

			Add(builder, new IncidentLifecycle<VendorInvoiceReference, IncidentInvoiceStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentInvoiceStatuses.Received, IncidentInvoiceStatuses.Matched, IncidentInvoiceStatuses.Exception, IncidentInvoiceStatuses.Rejected)
				.Allow(IncidentInvoiceStatuses.Matched, IncidentInvoiceStatuses.Accepted, IncidentInvoiceStatuses.Exception)
				.Allow(IncidentInvoiceStatuses.Exception, IncidentInvoiceStatuses.Matched, IncidentInvoiceStatuses.Accepted, IncidentInvoiceStatuses.Rejected)
				.Allow(IncidentInvoiceStatuses.Accepted, IncidentInvoiceStatuses.Packaged)
				.Allow(IncidentInvoiceStatuses.Packaged, IncidentInvoiceStatuses.Paid, IncidentInvoiceStatuses.Accepted)
				.Terminal(IncidentInvoiceStatuses.Paid, IncidentInvoiceStatuses.Rejected)
				.ReasonFor(IncidentInvoiceStatuses.Rejected));

			Add(builder, new IncidentLifecycle<PaymentPackage, IncidentPaymentPackageStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentPaymentPackageStatuses.Assembled, IncidentPaymentPackageStatuses.Submitted)
				.Allow(IncidentPaymentPackageStatuses.Submitted, IncidentPaymentPackageStatuses.Accepted, IncidentPaymentPackageStatuses.Rejected)
				.Allow(IncidentPaymentPackageStatuses.Accepted, IncidentPaymentPackageStatuses.Paid)
				.Allow(IncidentPaymentPackageStatuses.Rejected, IncidentPaymentPackageStatuses.Assembled)
				.Terminal(IncidentPaymentPackageStatuses.Paid)
				.ReasonFor(IncidentPaymentPackageStatuses.Rejected));

			Add(builder, new IncidentLifecycle<IncidentBudget, IncidentBudgetStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentBudgetStatuses.Draft, IncidentBudgetStatuses.Authorized)
				.Allow(IncidentBudgetStatuses.Authorized, IncidentBudgetStatuses.Closed)
				.Terminal(IncidentBudgetStatuses.Closed));

			// §9.13 time and equipment use: Certified and Approved rows may only become Void; corrections are adjusting rows.
			Add(builder, new IncidentLifecycle<IncidentTimeRecord, IncidentTimeStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentTimeStatuses.Draft, IncidentTimeStatuses.Submitted, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Submitted, IncidentTimeStatuses.Certified, IncidentTimeStatuses.Draft, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Certified, IncidentTimeStatuses.Approved, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Approved, IncidentTimeStatuses.Void)
				.Terminal(IncidentTimeStatuses.Void)
				.ReasonFor(IncidentTimeStatuses.Void));

			Add(builder, new IncidentLifecycle<IncidentEquipmentUseRecord, IncidentTimeStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentTimeStatuses.Draft, IncidentTimeStatuses.Submitted, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Submitted, IncidentTimeStatuses.Certified, IncidentTimeStatuses.Draft, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Certified, IncidentTimeStatuses.Approved, IncidentTimeStatuses.Void)
				.Allow(IncidentTimeStatuses.Approved, IncidentTimeStatuses.Void)
				.Terminal(IncidentTimeStatuses.Void)
				.ReasonFor(IncidentTimeStatuses.Void));

			Add(builder, new IncidentLifecycle<IncidentExpense, IncidentExpenseStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentExpenseStatuses.Draft, IncidentExpenseStatuses.Submitted, IncidentExpenseStatuses.Void)
				.Allow(IncidentExpenseStatuses.Submitted, IncidentExpenseStatuses.Approved, IncidentExpenseStatuses.Rejected, IncidentExpenseStatuses.Draft)
				.Allow(IncidentExpenseStatuses.Approved, IncidentExpenseStatuses.Void)
				.Terminal(IncidentExpenseStatuses.Rejected, IncidentExpenseStatuses.Void)
				.ReasonFor(IncidentExpenseStatuses.Rejected, IncidentExpenseStatuses.Void));

			Add(builder, new IncidentLifecycle<IncidentCostShareAgreement, IncidentCostShareStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentCostShareStatuses.Draft, IncidentCostShareStatuses.Active)
				.Allow(IncidentCostShareStatuses.Active, IncidentCostShareStatuses.Superseded)
				.Terminal(IncidentCostShareStatuses.Superseded));

			// §9.14 claims, §9.26 recovery, §9.27 intake.
			Add(builder, new IncidentLifecycle<IncidentClaim, IncidentClaimStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentClaimStatuses.Open, IncidentClaimStatuses.UnderReview, IncidentClaimStatuses.Closed)
				.Allow(IncidentClaimStatuses.UnderReview, IncidentClaimStatuses.Approved, IncidentClaimStatuses.Denied, IncidentClaimStatuses.Open)
				.Allow(IncidentClaimStatuses.Approved, IncidentClaimStatuses.Settled)
				.Allow(IncidentClaimStatuses.Settled, IncidentClaimStatuses.Closed)
				.Allow(IncidentClaimStatuses.Denied, IncidentClaimStatuses.Closed)
				.Terminal(IncidentClaimStatuses.Closed)
				.ReasonFor(IncidentClaimStatuses.Denied));

			Add(builder, new IncidentLifecycle<IncidentDisasterDeclaration, IncidentDeclarationStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentDeclarationStatuses.Draft, IncidentDeclarationStatuses.Open)
				.Allow(IncidentDeclarationStatuses.Open, IncidentDeclarationStatuses.Closed)
				.Allow(IncidentDeclarationStatuses.Closed, IncidentDeclarationStatuses.Open));

			Add(builder, new IncidentLifecycle<IncidentRecoveryProject, IncidentDeclarationStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentDeclarationStatuses.Draft, IncidentDeclarationStatuses.Open)
				.Allow(IncidentDeclarationStatuses.Open, IncidentDeclarationStatuses.Closed)
				.Allow(IncidentDeclarationStatuses.Closed, IncidentDeclarationStatuses.Open));

			Add(builder, new IncidentLifecycle<IncidentRecoveryDeadline, IncidentGateItemStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentGateItemStatuses.Open, IncidentGateItemStatuses.Complete, IncidentGateItemStatuses.Waived)
				.ReasonFor(IncidentGateItemStatuses.Waived));

			Add(builder, new IncidentLifecycle<IncidentIntakeItem, IncidentIntakeStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentIntakeStatuses.Unfiled, IncidentIntakeStatuses.Filed, IncidentIntakeStatuses.Rejected, IncidentIntakeStatuses.Quarantined)
				.Allow(IncidentIntakeStatuses.Quarantined, IncidentIntakeStatuses.Unfiled, IncidentIntakeStatuses.Rejected)
				.Terminal(IncidentIntakeStatuses.Filed, IncidentIntakeStatuses.Rejected)
				.ReasonFor(IncidentIntakeStatuses.Rejected));

			// §9.25 coordination.
			Add(builder, new IncidentLifecycle<IncidentOpenAction, IncidentOpenActionStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentOpenActionStatuses.Open, IncidentOpenActionStatuses.InProgress, IncidentOpenActionStatuses.Done, IncidentOpenActionStatuses.Cancelled)
				.Allow(IncidentOpenActionStatuses.InProgress, IncidentOpenActionStatuses.Done, IncidentOpenActionStatuses.Cancelled, IncidentOpenActionStatuses.Open)
				.Terminal(IncidentOpenActionStatuses.Done, IncidentOpenActionStatuses.Cancelled));

			Add(builder, new IncidentLifecycle<IncidentDecision, IncidentDecisionStatuses>(e => e.Status, (e, s) => e.Status = s)
				.Allow(IncidentDecisionStatuses.Open, IncidentDecisionStatuses.Decided, IncidentDecisionStatuses.Withdrawn)
				.Terminal(IncidentDecisionStatuses.Decided, IncidentDecisionStatuses.Withdrawn)
				.ReasonFor(IncidentDecisionStatuses.Decided, IncidentDecisionStatuses.Withdrawn));
		}
	}
}
