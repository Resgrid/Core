using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.IncidentLogistics
{
	/// <summary>
	/// The Incident Back Office workflow trigger catalog (large-incident-logistics-finance-plan.md §13.7; registry §4C: 116-148 and
	/// 192-207). Every event is emitted through the DomainEventOutbox with producer <see cref="Producer"/>; its payload is a flat map
	/// of dotted template variables ("incident.id", "period.number", ...) that the context builder nests. Payload safety is a hard
	/// rule: no ADP-catalogued field (<see cref="IncidentBackOfficeProtectedFields"/>) and no Restricted PII ever appears here —
	/// no room numbers, accommodation, dietary or medical detail, personal rates or per-person cost, claim detail, vendor tax or
	/// remittance data, intake sender or subject. Amount variables are budget-level totals (Finance-desk data).
	/// </summary>
	public static class IncidentBackOfficeWorkflowPayload
	{
		public const string Producer = "IncidentBackOffice";

		/// <summary>Variables on every incident-scoped trigger (plan §13.7 rev 4). Department-scoped triggers (134, 198) carry none.</summary>
		public static readonly (string Variable, string Type, string Description)[] Common =
		{
			("incident.id", "string", "Activation id"),
			("incident.name", "string", "Activation name"),
			("incident.short_code", "string", "Activation short code"),
			("incident.kind", "string", "Incident, Complex, PlannedEvent or EocActivation"),
			("incident.is_exercise", "bool", "True for an exercise activation"),
			("incident.complex_name", "string", "Parent complex name; empty outside a complex"),
			("incident.call_id", "int", "Linked dispatch call id"),
			("incident.period.number", "int", "Current operational period number")
		};

		private static (string, string, string) V(string variable, string type, string description) => (variable, type, description);

		public static readonly IReadOnlyDictionary<WorkflowTriggerEventType, (string Variable, string Type, string Description)[]> Variables =
			new Dictionary<WorkflowTriggerEventType, (string, string, string)[]>
			{
				[WorkflowTriggerEventType.IncidentPeriodStarted] = new[] { V("period.number", "int", "Period number"), V("period.start", "datetime", "Period start (UTC)"), V("period.end", "datetime", "Period end (UTC)"), V("period.timezone", "string", "Period time zone"), V("period.briefing_time", "datetime", "Briefing time (UTC)"), V("period.is_first", "bool", "True for the first period") },
				[WorkflowTriggerEventType.IncidentPlanApproved] = new[] { V("period.number", "int", "Period number"), V("plan.version", "int", "Plan version"), V("plan.record_id", "string", "RMS record id of the assembled IAP"), V("plan.form_count", "int", "Records in the package"), V("plan.distribution_count", "int", "Distribution recipients") },
				[WorkflowTriggerEventType.IncidentPositionAssigned] = new[] { V("position.code", "string", "Position code"), V("position.name", "string", "Position name"), V("position.section", "string", "Section"), V("assignment.subject_type", "string", "User, Participant or Placeholder"), V("assignment.subject_name", "string", "Holder display name"), V("assignment.is_trainee", "bool", "Trainee assignment"), V("assignment.is_acting", "bool", "Acting assignment"), V("assignment.action", "string", "Assigned, Changed or Ended"), V("assignment.effective_on", "datetime", "Effective (UTC)"), V("assignment.qualification_state", "string", "Qualified, Trainee, Exception, Waived or NotRequired") },
				[WorkflowTriggerEventType.IncidentParticipantCheckedIn] = new[] { V("participant.name", "string", "Display name"), V("participant.agency", "string", "Agency"), V("participant.kind", "string", "Person, Crew, Equipment or Overhead"), V("participant.request_number", "string", "Request number"), V("checkin.source", "string", "Desk, Import, PreRegistration, Scan, SelfService or Enrollment"), V("checkin.on", "datetime", "Checked in (UTC)") },
				[WorkflowTriggerEventType.IncidentSnapshotStale] = new[] { V("health.kind", "string", "Lag, SourceStale or Drift"), V("health.metric", "string", "Metric or source key"), V("health.lag_seconds", "int", "Lag in seconds"), V("health.oldest_source_utc", "datetime", "Oldest contributing source"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentRequestStatusChanged] = new[] { V("request.number", "string", "Request number"), V("request.kind", "string", "Resource kind"), V("request.type", "string", "Resource type"), V("request.priority", "string", "Priority"), V("request.need_by", "datetime", "Needed by (UTC)"), V("request.source_channel", "string", "Source channel"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("request.quantity", "decimal", "Quantity"), V("request.filled_quantity", "decimal", "Filled quantity"), V("fill.apparatus_count", "int", "Apparatus on the fill that caused it"), V("fill.personnel_count", "int", "Personnel on the fill that caused it"), V("fill.eta", "datetime", "Fill ETA (UTC)") },
				[WorkflowTriggerEventType.IncidentResourceArrivalDue] = new[] { V("resource.name", "string", "Resource"), V("resource.kind", "string", "Kind"), V("request.number", "string", "Request number"), V("eta.utc", "datetime", "ETA (UTC)"), V("eta.confidence", "string", "ETA confidence"), V("eta.is_late", "bool", "Past its ETA"), V("origin", "string", "Origin"), V("receiving_location", "string", "Receiving location"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentResourceStatusChanged] = new[] { V("resource.name", "string", "Resource"), V("resource.kind", "string", "Kind"), V("old_status", "string", "Previous support status"), V("new_status", "string", "New support status"), V("resource.request_number", "string", "Request number"), V("resource.agency", "string", "Provider agency") },
				[WorkflowTriggerEventType.IncidentDemobBlocked] = new[] { V("resource.name", "string", "Resource or participant"), V("blocker.category", "string", "Blocking category"), V("blocker.item", "string", "Checklist item"), V("blocker.owner", "string", "Owner position"), V("demob.planned_release", "datetime", "Planned release (UTC)") },
				[WorkflowTriggerEventType.IncidentDemobCompleted] = new[] { V("resource.name", "string", "Resource or participant"), V("resource.kind", "string", "Kind"), V("departed.on", "datetime", "Departed (UTC)"), V("destination", "string", "Destination"), V("waived_items_count", "int", "Waived checklist items") },
				[WorkflowTriggerEventType.IncidentFacilityCapacityThreshold] = new[] { V("facility.name", "string", "Facility"), V("facility.type", "string", "Facility type"), V("service.kind", "string", "Capacity dimension"), V("capacity.max", "decimal", "Maximum"), V("capacity.used", "decimal", "Used"), V("capacity.reserved", "decimal", "Reserved"), V("capacity.utilization_percent", "decimal", "Utilization percent"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentLodgingShortfall] = new[] { V("night.date", "datetime", "Night"), V("beds.required", "int", "Beds required"), V("beds.available", "int", "Beds available"), V("beds.shortfall", "int", "Shortfall"), V("properties.count", "int", "Properties"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentLodgingAssignmentChanged] = new[] { V("subject.name", "string", "Person or resource"), V("property.name", "string", "Property"), V("nights.first", "datetime", "First night"), V("nights.last", "datetime", "Last night"), V("assignment.action", "string", "Assigned, Moved, Extended, NoShow, Checkout, Cancelled, Declined or SelfArranged") },
				[WorkflowTriggerEventType.IncidentMealCutoffApproaching] = new[] { V("meal.window", "string", "Meal type"), V("site.name", "string", "Site"), V("cutoff.utc", "datetime", "Order cutoff (UTC)"), V("forecast.count", "int", "Forecast"), V("ordered.count", "int", "Ordered"), V("gap", "int", "Forecast minus ordered"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentWaterBelowThreshold] = new[] { V("site.name", "string", "Site"), V("on_hand.quantity", "decimal", "On hand"), V("on_hand.unit", "string", "Unit"), V("days_of_supply", "decimal", "Days of supply"), V("threshold.kind", "string", "Reorder or DaysOfSupply"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentServiceRequestStatusChanged] = new[] { V("ticket.number", "string", "Ticket number"), V("service.template", "string", "Service template"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("sla.breached", "bool", "SLA breached"), V("assigned_to", "string", "Assignee"), V("facility.name", "string", "Facility") },
				[WorkflowTriggerEventType.IncidentVendorEngagementChanged] = new[] { V("vendor.name", "string", "Vendor"), V("engagement.service", "string", "Service"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("capacity.committed", "decimal", "Committed capacity"), V("capacity.unit", "string", "Unit") },
				[WorkflowTriggerEventType.IncidentVendorCapacityConflict] = new[] { V("vendor.name", "string", "Vendor"), V("capability.kind", "string", "Capability"), V("capability.unit", "string", "Unit"), V("bucket.start", "datetime", "Bucket start"), V("bucket.end", "datetime", "Bucket end"), V("requested", "decimal", "Requested"), V("available", "decimal", "Available"), V("conflicting_incidents_count", "int", "Other incidents holding capacity"), V("was_override", "bool", "Authorized override") },
				[WorkflowTriggerEventType.IncidentVendorComplianceExpiring] = new[] { V("vendor.name", "string", "Vendor"), V("item.kind", "string", "Insurance, License, Agreement, TaxForm or Verification"), V("expires.on", "datetime", "Expires"), V("days_remaining", "int", "Days remaining"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentTransportMovementChanged] = new[] { V("movement.kind", "string", "Shuttle, CrewTransport, Cargo, Travel or Fuel"), V("movement.route", "string", "Route"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("passengers.count", "int", "Passengers"), V("exception.kind", "string", "Exception") },
				[WorkflowTriggerEventType.IncidentBudgetThreshold] = new[] { V("budget.line", "string", "Budget line"), V("budget.authorized_amount", "decimal", "Authorized amount"), V("budget.consumed_percent", "decimal", "Consumed percent"), V("budget.remaining_amount", "decimal", "Remaining amount"), V("measure", "string", "Measure"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentAgreementCeilingRisk] = new[] { V("vendor.name", "string", "Vendor"), V("agreement.number", "string", "Agreement number"), V("ceiling.amount", "decimal", "Ceiling"), V("consumed_percent", "decimal", "Consumed percent"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentUnapprovedSpendDetected] = new[] { V("exception.kind", "string", "ReceiptWithoutCommitment, OrderWithoutAuthority or ThresholdExceeded"), V("vendor.name", "string", "Vendor"), V("reference.number", "string", "Reference"), V("purchaser", "string", "Purchaser") },
				[WorkflowTriggerEventType.IncidentCommitmentRecorded] = new[] { V("vendor.name", "string", "Vendor"), V("commitment.number", "string", "Commitment number"), V("commitment.amount", "decimal", "Amount"), V("budget.line", "string", "Budget line"), V("agreement.number", "string", "Agreement number"), V("method", "string", "Purchase method") },
				[WorkflowTriggerEventType.IncidentInvoiceException] = new[] { V("vendor.name", "string", "Vendor"), V("invoice.reference", "string", "Invoice number"), V("exception.kind", "string", "MatchVariance, Duplicate, Unmatched or Aged"), V("variance.amount", "decimal", "Variance") },
				[WorkflowTriggerEventType.IncidentPaymentPackageStatusChanged] = new[] { V("package.number", "string", "Package number"), V("vendor.name", "string", "Vendor"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("line_count", "int", "Invoices") },
				[WorkflowTriggerEventType.IncidentTimeMissing] = new[] { V("period.number", "int", "Period"), V("missing.count", "int", "Subjects without time"), V("subjects.sample", "string", "Names (capped)"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentTimeApproved] = new[] { V("subject.name", "string", "Subject"), V("period.number", "int", "Period"), V("action", "string", "Certified or Approved"), V("spans.count", "int", "Spans") },
				[WorkflowTriggerEventType.IncidentShiftTicketMissing] = new[] { V("resource.name", "string", "Equipment"), V("vendor.name", "string", "Vendor"), V("agreement.number", "string", "Agreement number"), V("shift.date", "datetime", "Shift date"), V("consecutive_missing", "int", "Consecutive missing"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentCostThreshold] = new[] { V("measure", "string", "DailyBurn, Cumulative, ForecastAtCompletion or MissingData"), V("amount", "decimal", "Amount"), V("threshold.amount", "decimal", "Threshold"), V("period.number", "int", "Period"), V("missing_inputs.count", "int", "Missing inputs"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentClaimStatusChanged] = new[] { V("claim.number", "string", "Claim number"), V("claim.kind", "string", "Kind"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("deadline.on", "datetime", "Deadline"), V("deadline.approaching", "bool", "Deadline in lead window") },
				[WorkflowTriggerEventType.IncidentWorkRestViolation] = new[] { V("subject.name", "string", "Subject"), V("rule.name", "string", "Rule"), V("hours.worked", "decimal", "Hours worked"), V("hours.limit", "decimal", "Limit"), V("period.number", "int", "Period") },
				[WorkflowTriggerEventType.IncidentCloseoutMilestone] = new[] { V("milestone.kind", "string", "ItemBlocked, ItemComplete, Sealed, Reopened or FinalPackageGenerated"), V("item.name", "string", "Item"), V("open_items.count", "int", "Open items"), V("package.record_id", "string", "Package record id") },
				[WorkflowTriggerEventType.IncidentResourceTimeoutApproaching] = new[] { V("resource.name", "string", "Resource"), V("resource.kind", "string", "Kind"), V("assignment.start_on", "datetime", "Assignment start"), V("timeout.on", "datetime", "Timeout date"), V("days_remaining", "int", "Days remaining"), V("basis", "string", "RulePack, Agreement, OrderTerms or TentativeRelease"), V("has_extension", "bool", "Extended"), V("order_by.date", "datetime", "Order-by date for a replacement"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentActivationChanged] = new[] { V("activation.id", "string", "Activation id"), V("activation.name", "string", "Name"), V("activation.short_code", "string", "Short code"), V("activation.kind", "string", "Kind"), V("activation.is_exercise", "bool", "Exercise"), V("action", "string", "Activated, TeamActivated, JoinedComplex, LeftComplex, Closed, Sealed or Reopened"), V("team.name", "string", "Team"), V("complex.name", "string", "Complex"), V("assignments.count", "int", "Assignments made") },
				[WorkflowTriggerEventType.CoordinationLaneStale] = new[] { V("board.name", "string", "Board"), V("lane.type", "string", "Lane type"), V("lane.name", "string", "Lane"), V("stale_since", "datetime", "Stale since"), V("threshold_minutes", "int", "Threshold minutes"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentLifelineStatusChanged] = new[] { V("lifeline.name", "string", "Lifeline"), V("component", "string", "Component"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("source", "string", "Source") },
				[WorkflowTriggerEventType.IncidentCoordinationItemDue] = new[] { V("item.kind", "string", "Decision or OpenAction"), V("item.title", "string", "Title"), V("item.owner", "string", "Owner"), V("deadline.utc", "datetime", "Deadline (UTC)"), V("is_overdue", "bool", "Overdue"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentSituationalFeatureChanged] = new[] { V("feature.type", "string", "RoadClosure, Shelter, Healthcare, Infrastructure or EvacuationZone"), V("feature.name", "string", "Name"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("capacity.available", "int", "Available capacity"), V("capacity.total", "int", "Total capacity") },
				[WorkflowTriggerEventType.IncidentTeamReadinessChanged] = new[] { V("team.name", "string", "Team"), V("team.type", "string", "Team type"), V("window.start", "datetime", "On-call window start"), V("window.end", "datetime", "On-call window end"), V("readiness", "string", "Ready, Degraded or NotDeployable"), V("gaps.count", "int", "Gaps"), V("gap.positions", "string", "Position codes (capped)"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentEnrollmentBacklog] = new[] { V("unenrolled.count", "int", "Unenrolled subjects"), V("oldest_minutes", "int", "Oldest wait in minutes"), V("policy", "string", "Off, Suggest or AutoDraft"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentReceivingDiscrepancy] = new[] { V("request.number", "string", "Request number"), V("shipment.reference", "string", "Shipment"), V("discrepancy.kind", "string", "Shortage, Overage, Damage, WrongItem or Rejected"), V("quantity.expected", "decimal", "Expected"), V("quantity.accepted", "decimal", "Accepted"), V("quantity.rejected", "decimal", "Rejected"), V("receiving_location", "string", "Receiving location") },
				[WorkflowTriggerEventType.IncidentAccountabilityException] = new[] { V("item.kind", "string", "Radio, Kit, Battery or other"), V("item.label", "string", "Item"), V("holder.name", "string", "Holder"), V("holder.status", "string", "Holder support status"), V("days_overdue", "int", "Days overdue"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentHoldExpiring] = new[] { V("hold.kind", "string", "VendorCapacity or LodgingBlock"), V("vendor.name", "string", "Vendor or property"), V("capacity.held", "decimal", "Held"), V("capacity.unit", "string", "Unit"), V("expires.utc", "datetime", "Expires (UTC)"), V("would_release.count", "decimal", "Would release"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentProcurementStatusChanged] = new[] { V("document.kind", "string", "Requisition, PurchaseOrder, AgreementCall or Receipt"), V("document.number", "string", "Number"), V("vendor.name", "string", "Vendor"), V("old_status", "string", "Previous status"), V("new_status", "string", "New status"), V("amount", "decimal", "Amount"), V("method", "string", "Method") },
				[WorkflowTriggerEventType.IncidentBudgetAuthorityChanged] = new[] { V("budget.line", "string", "Budget line"), V("action", "string", "Created, Revised, Transferred or Delegated"), V("authorized_amount.old", "decimal", "Previous authorized amount"), V("authorized_amount.new", "decimal", "New authorized amount"), V("funding.code", "string", "Funding code"), V("delegate.position", "string", "Delegate position") },
				[WorkflowTriggerEventType.IncidentCostShareApportioned] = new[] { V("agreement.number", "string", "Agreement"), V("agreement.version", "int", "Version"), V("run.kind", "string", "Scheduled or Reapportionment"), V("parties.count", "int", "Parties"), V("period.through", "datetime", "Through"), V("total.apportioned", "decimal", "Total apportioned") },
				[WorkflowTriggerEventType.IncidentIntakeBacklog] = new[] { V("unfiled.count", "int", "Unfiled items"), V("oldest_hours", "int", "Oldest in hours"), V("quarantined.count", "int", "Quarantined"), V("resolved", "bool", "True on recovery") },
				[WorkflowTriggerEventType.IncidentRecoveryDeadlineApproaching] = new[] { V("declaration.number", "string", "Declaration"), V("obligation.kind", "string", "Obligation"), V("project.number", "string", "Project"), V("category", "string", "PA category"), V("due.on", "datetime", "Due"), V("days_remaining", "int", "Days remaining"), V("resolved", "bool", "True on recovery") }
			};

		public static readonly int[] Triggers = Variables.Keys.Select(x => (int)x).OrderBy(x => x).ToArray();

		/// <summary>Triggers that fire without an activation (department scope).</summary>
		public static readonly int[] DepartmentScoped = { (int)WorkflowTriggerEventType.IncidentVendorComplianceExpiring, (int)WorkflowTriggerEventType.IncidentTeamReadinessChanged };

		public static readonly int[] Business =
		{
			136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 203, 204, 205, 206, 207
		};

		public static readonly int[] Coordination = { 194, 195, 196, 197 };

		public static bool IsIncident(int trigger) => Variables.ContainsKey((WorkflowTriggerEventType)trigger);

		public static bool IsBusiness(int trigger) => Business.Contains(trigger);

		public static bool IsCoordination(int trigger) => Coordination.Contains(trigger);

		public static bool IsDepartmentScoped(int trigger) => DepartmentScoped.Contains(trigger);

		/// <summary>Common plus trigger-specific variables, in catalog order.</summary>
		public static IEnumerable<(string Variable, string Type, string Description)> For(WorkflowTriggerEventType trigger)
		{
			if (!Variables.TryGetValue(trigger, out var own))
				return Enumerable.Empty<(string, string, string)>();
			return IsDepartmentScoped((int)trigger) ? own : Common.Concat(own);
		}
	}
}
