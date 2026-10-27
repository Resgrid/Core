using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;

namespace Resgrid.Services.IncidentLogistics
{
	/// <summary>
	/// The workflow triggers that fire on a ledger write (plan §13.7): a status transition, an insert, or a flagged exception on the
	/// row. Each is one <see cref="IncidentTriggerRule{T}"/> over a declarative spec — when it fires and which catalogued variables
	/// it carries — so every table's events run on the same write path and in the same transaction as the change itself. Triggers
	/// that depend on time or totals (arrival due, thresholds, backlogs) are evaluated by the workers instead
	/// (<see cref="IncidentAutomationService"/>).
	/// </summary>
	public sealed class IncidentTriggerSpec<T> where T : class, IIncidentLedgerEntity
	{
		public WorkflowTriggerEventType Trigger { get; set; }
		public Func<IncidentLedgerChange<T>, bool> When { get; set; }
		public Func<IncidentLedgerChange<T>, IncidentTriggerLabels, Task<Dictionary<string, object>>> Variables { get; set; }
	}

	/// <summary>Display labels for the ids a trigger names (vendor, facility, budget line), resolved through the lookup service.</summary>
	public sealed class IncidentTriggerLabels
	{
		private readonly IIncidentLookupService _lookups;
		private readonly IncidentActor _actor;
		private readonly CancellationToken _cancellationToken;

		public IncidentTriggerLabels(IIncidentLookupService lookups, IncidentActor actor, CancellationToken cancellationToken)
		{
			_lookups = lookups;
			_actor = actor;
			_cancellationToken = cancellationToken;
		}

		public async Task<string> Of(string table, string id)
		{
			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(table) || _lookups == null)
				return null;
			var labels = await _lookups.GetLabelsAsync(_actor, table, new[] { id }, _cancellationToken);
			return labels.TryGetValue(id, out var label) ? label : null;
		}
	}

	public sealed class IncidentTriggerRule<T> : IIncidentLedgerRule<T> where T : class, IIncidentLedgerEntity
	{
		private readonly IIncidentEventPublisher _publisher;
		private readonly IIncidentLookupService _lookups;
		private readonly IncidentTriggerSpec<T> _spec;

		public IncidentTriggerRule(IIncidentEventPublisher publisher, IIncidentLookupService lookups, IncidentTriggerSpec<T> spec)
		{
			_publisher = publisher;
			_lookups = lookups;
			_spec = spec;
		}

		public Task BeforeSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken) => Task.CompletedTask;

		public async Task AfterSaveAsync(IncidentLedgerChange<T> change, CancellationToken cancellationToken)
		{
			if (change.IsDelete || !_spec.When(change))
				return;
			var variables = await _spec.Variables(change, new IncidentTriggerLabels(_lookups, change.Actor, cancellationToken));
			await _publisher.PublishAsync(change.Actor.DepartmentId, change.Activation, _spec.Trigger, variables, change.Entity.TableName, change.Entity.IdValue as string, cancellationToken);
		}
	}

	public static class IncidentTriggerRules
	{
		private static string N<TEnum>(int value) where TEnum : struct, Enum => Enum.GetName(typeof(TEnum), value) ?? value.ToString();

		private static bool Moved<T>(IncidentLedgerChange<T> c) where T : class, IIncidentLedgerEntity => c.IsTransition && c.OldStatus != c.NewStatus;

		private static Task<Dictionary<string, object>> Done(Dictionary<string, object> variables) => Task.FromResult(variables);

		/// <summary>The table a polymorphic subject id points at (participant, resource, facility); users have no label table here.</summary>
		public static string SubjectTable(int subjectType) => subjectType switch
		{
			(int)IncidentSubjectTypes.Participant => "IncidentParticipants",
			(int)IncidentSubjectTypes.Resource => "IncidentResources",
			(int)IncidentSubjectTypes.Facility => "IncidentFacilities",
			_ => null
		};

		private static void Add<T>(ContainerBuilder builder, WorkflowTriggerEventType trigger, Func<IncidentLedgerChange<T>, bool> when,
			Func<IncidentLedgerChange<T>, IncidentTriggerLabels, Task<Dictionary<string, object>>> variables) where T : class, IIncidentLedgerEntity
		{
			var spec = new IncidentTriggerSpec<T> { Trigger = trigger, When = when, Variables = variables };
			builder.Register(c => new IncidentTriggerRule<T>(c.Resolve<IIncidentEventPublisher>(), c.Resolve<IIncidentLookupService>(), spec))
				.As<IIncidentLedgerRule<T>>().InstancePerLifetimeScope();
		}

		public static void Register(ContainerBuilder builder)
		{
			// 121 — a request moved through its fixed lifecycle (fills recompute the request through IncidentRequestFillRule).
			Add<IncidentResourceRequest>(builder, WorkflowTriggerEventType.IncidentRequestStatusChanged, Moved, (c, _) => Done(new Dictionary<string, object>
			{
				["request.number"] = c.Entity.RequestNumber,
				["request.kind"] = N<IncidentResourceKinds>(c.Entity.Kind),
				["request.type"] = c.Entity.ResourceType,
				["request.priority"] = N<IncidentRequestPriorities>(c.Entity.Priority),
				["request.need_by"] = c.Entity.NeedBy,
				["request.source_channel"] = N<IncidentSourceChannels>(c.Entity.SourceChannel),
				["old_status"] = N<IncidentRequestStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentRequestStatuses>(c.NewStatus ?? 0),
				["request.quantity"] = c.Entity.Quantity
			}));

			// 123 — a resource's support status moved.
			Add<IncidentResource>(builder, WorkflowTriggerEventType.IncidentResourceStatusChanged, Moved, (c, _) => Done(new Dictionary<string, object>
			{
				["resource.name"] = c.Entity.Name,
				["resource.kind"] = N<IncidentResourceKinds>(c.Entity.Kind),
				["old_status"] = N<IncidentSupportStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentSupportStatuses>(c.NewStatus ?? 0),
				["resource.request_number"] = c.Entity.RequestNumber,
				["resource.agency"] = c.Entity.ProviderAgency
			}));

			// 125 — a demobilization plan reached Departed.
			Add<IncidentDemobPlan>(builder, WorkflowTriggerEventType.IncidentDemobCompleted, c => Moved(c) && c.NewStatus == (int)IncidentDemobStatuses.Departed, async (c, labels) => new Dictionary<string, object>
			{
				["resource.name"] = await labels.Of(SubjectTable(c.Entity.SubjectType), c.Entity.SubjectId),
				["resource.kind"] = N<IncidentSubjectTypes>(c.Entity.SubjectType),
				["departed.on"] = c.Entity.DepartedOn ?? DateTime.UtcNow,
				["destination"] = c.Entity.Destination
			});

			// 128 — a lodging assignment was made, moved or ended (the subject's name, never the room).
			Add<IncidentLodgingAssignment>(builder, WorkflowTriggerEventType.IncidentLodgingAssignmentChanged, c => c.IsNew || Moved(c) || (c.Existing != null && (c.Existing.FirstNight != c.Entity.FirstNight || c.Existing.LastNight != c.Entity.LastNight || c.Existing.IncidentLodgingBedId != c.Entity.IncidentLodgingBedId)),
				async (c, labels) => new Dictionary<string, object>
				{
					["subject.name"] = c.Entity.ReservationLabel ?? await labels.Of("IncidentParticipants", c.Entity.IncidentParticipantId) ?? await labels.Of("IncidentResources", c.Entity.IncidentResourceId),
					["property.name"] = await labels.Of("IncidentLodgingProperties", c.Entity.IncidentLodgingPropertyId),
					["nights.first"] = c.Entity.FirstNight,
					["nights.last"] = c.Entity.LastNight,
					["assignment.action"] = c.IsNew ? "Assigned"
						: c.IsTransition ? c.NewStatus switch
						{
							(int)IncidentLodgingAssignmentStatuses.NoShow => "NoShow",
							(int)IncidentLodgingAssignmentStatuses.Departed => "Checkout",
							(int)IncidentLodgingAssignmentStatuses.Cancelled => "Cancelled",
							_ => "Assigned"
						}
						: c.Existing.IncidentLodgingBedId != c.Entity.IncidentLodgingBedId ? "Moved" : "Extended"
				});

			// 131 — a service ticket moved.
			Add<IncidentServiceRequest>(builder, WorkflowTriggerEventType.IncidentServiceRequestStatusChanged, Moved, async (c, labels) => new Dictionary<string, object>
			{
				["ticket.number"] = c.Entity.TicketNumber,
				["service.template"] = await labels.Of("IncidentServiceTemplates", c.Entity.IncidentServiceTemplateId),
				["old_status"] = N<IncidentServiceRequestStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentServiceRequestStatuses>(c.NewStatus ?? 0),
				["sla.breached"] = c.Entity.SlaBreached,
				["assigned_to"] = c.Entity.AssignedPosition,
				["facility.name"] = await labels.Of("IncidentFacilities", c.Entity.IncidentFacilityId)
			});

			// 132 — a vendor engagement moved.
			Add<IncidentVendorEngagement>(builder, WorkflowTriggerEventType.IncidentVendorEngagementChanged, Moved, async (c, labels) => new Dictionary<string, object>
			{
				["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
				["engagement.service"] = c.Entity.ServiceDescription,
				["old_status"] = N<IncidentEngagementStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentEngagementStatuses>(c.NewStatus ?? 0)
			});

			// 135 — a transport movement moved or recorded an exception.
			Add<IncidentTransportMovement>(builder, WorkflowTriggerEventType.IncidentTransportMovementChanged,
				c => Moved(c) || (c.Existing != null && c.Existing.ExceptionKind != c.Entity.ExceptionKind && c.Entity.ExceptionKind != 0), (c, _) => Done(new Dictionary<string, object>
				{
					["movement.kind"] = N<IncidentTransportKinds>(c.Entity.Kind),
					["movement.route"] = c.Entity.Route,
					["old_status"] = N<IncidentTransportStatuses>(c.OldStatus ?? c.Existing?.Status ?? 0),
					["new_status"] = N<IncidentTransportStatuses>(c.NewStatus ?? c.Entity.Status),
					["passengers.count"] = c.Entity.PassengerCount,
					["exception.kind"] = c.Entity.ExceptionKind == 0 ? null : N<IncidentTransportExceptions>(c.Entity.ExceptionKind)
				}));

			// 139 — a commitment was recorded.
			Add<IncidentCommitment>(builder, WorkflowTriggerEventType.IncidentCommitmentRecorded, c => c.IsNew, async (c, labels) => new Dictionary<string, object>
			{
				["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
				["commitment.number"] = c.Entity.CommitmentNumber,
				["commitment.amount"] = c.Entity.Amount,
				["budget.line"] = await labels.Of("IncidentBudgetLines", c.Entity.IncidentBudgetLineId),
				["agreement.number"] = await labels.Of("VendorAgreements", c.Entity.VendorAgreementId),
				["method"] = N<IncidentPurchaseMethods>(c.Entity.Method)
			});

			// 138 — a commitment made without an approved requisition under a method that needs one.
			Add<IncidentCommitment>(builder, WorkflowTriggerEventType.IncidentUnapprovedSpendDetected,
				c => c.IsNew && string.IsNullOrWhiteSpace(c.Entity.PurchaseRequisitionId) && c.Entity.Method is not ((int)IncidentPurchaseMethods.PurchaseCard or (int)IncidentPurchaseMethods.MicroPurchase or (int)IncidentPurchaseMethods.Emergency),
				async (c, labels) => new Dictionary<string, object>
				{
					["exception.kind"] = "OrderWithoutAuthority",
					["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
					["reference.number"] = c.Entity.CommitmentNumber
				});

			// 140 — an invoice reference was flagged.
			Add<VendorInvoiceReference>(builder, WorkflowTriggerEventType.IncidentInvoiceException,
				c => c.Entity.ExceptionKind != (int)IncidentInvoiceExceptionKinds.None && (c.Existing == null || c.Existing.ExceptionKind != c.Entity.ExceptionKind),
				async (c, labels) => new Dictionary<string, object>
				{
					["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
					["invoice.reference"] = c.Entity.InvoiceNumber,
					["exception.kind"] = N<IncidentInvoiceExceptionKinds>(c.Entity.ExceptionKind),
					["variance.amount"] = c.Entity.MatchVariance
				});

			// 141 — a payment package moved.
			Add<PaymentPackage>(builder, WorkflowTriggerEventType.IncidentPaymentPackageStatusChanged, Moved, async (c, labels) => new Dictionary<string, object>
			{
				["package.number"] = c.Entity.PackageNumber,
				["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
				["old_status"] = N<IncidentPaymentPackageStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentPaymentPackageStatuses>(c.NewStatus ?? 0),
				["line_count"] = c.Entity.LineCount,
				["total.amount"] = c.Entity.TotalAmount
			});

			// 143 — time certified or approved.
			Add<IncidentTimeRecord>(builder, WorkflowTriggerEventType.IncidentTimeApproved,
				c => Moved(c) && c.NewStatus is (int)IncidentTimeStatuses.Certified or (int)IncidentTimeStatuses.Approved, async (c, labels) => new Dictionary<string, object>
				{
					["subject.name"] = await labels.Of("IncidentParticipants", c.Entity.IncidentParticipantId) ?? await labels.Of("IncidentResources", c.Entity.IncidentResourceId),
					["action"] = c.NewStatus == (int)IncidentTimeStatuses.Certified ? "Certified" : "Approved",
					["spans.count"] = 1
				});

			// 146 — a claim moved.
			Add<IncidentClaim>(builder, WorkflowTriggerEventType.IncidentClaimStatusChanged, Moved, (c, _) => Done(new Dictionary<string, object>
			{
				["claim.number"] = c.Entity.ClaimNumber,
				["claim.kind"] = N<IncidentClaimKinds>(c.Entity.Kind),
				["old_status"] = N<IncidentClaimStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentClaimStatuses>(c.NewStatus ?? 0),
				["deadline.on"] = c.Entity.DeadlineOn
			}));

			// 148 — a closeout item completed or waived.
			Add<IncidentCloseoutChecklistItem>(builder, WorkflowTriggerEventType.IncidentCloseoutMilestone,
				c => Moved(c) && c.NewStatus is (int)IncidentGateItemStatuses.Complete or (int)IncidentGateItemStatuses.Waived, (c, _) => Done(new Dictionary<string, object>
				{
					["milestone.kind"] = "ItemComplete",
					["item.name"] = c.Entity.Name
				}));

			// 195 — a lifeline component changed colour.
			Add<IncidentLifelineStatus>(builder, WorkflowTriggerEventType.IncidentLifelineStatusChanged,
				c => c.IsNew || (c.Existing != null && c.Existing.Status != c.Entity.Status), (c, _) => Done(new Dictionary<string, object>
				{
					["lifeline.name"] = N<IncidentLifelines>(c.Entity.Lifeline),
					["component"] = c.Entity.Component,
					["old_status"] = c.Existing == null ? null : N<IncidentLifelineColors>(c.Existing.Status),
					["new_status"] = N<IncidentLifelineColors>(c.Entity.Status),
					["source"] = c.Entity.Source
				}));

			// 197 — a situational feature (road closure, shelter, ...) changed status.
			Add<IncidentSituationalFeature>(builder, WorkflowTriggerEventType.IncidentSituationalFeatureChanged,
				c => c.IsNew || (c.Existing != null && c.Existing.Status != c.Entity.Status), (c, _) => Done(new Dictionary<string, object>
				{
					["feature.type"] = N<IncidentSituationalFeatureTypes>(c.Entity.FeatureType),
					["feature.name"] = c.Entity.Name,
					["old_status"] = c.Existing == null ? null : N<IncidentSituationalStatuses>(c.Existing.Status),
					["new_status"] = N<IncidentSituationalStatuses>(c.Entity.Status),
					["capacity.available"] = c.Entity.CapacityAvailable
				}));

			// 200 — a receipt recorded a discrepancy.
			Add<IncidentReceipt>(builder, WorkflowTriggerEventType.IncidentReceivingDiscrepancy,
				c => c.Entity.DiscrepancyKind != (int)IncidentDiscrepancyKinds.None && (c.Existing == null || c.Existing.DiscrepancyKind != c.Entity.DiscrepancyKind),
				async (c, labels) => new Dictionary<string, object>
				{
					["request.number"] = await labels.Of("IncidentResourceRequests", c.Entity.IncidentResourceRequestId),
					["shipment.reference"] = c.Entity.ShipmentReference,
					["discrepancy.kind"] = N<IncidentDiscrepancyKinds>(c.Entity.DiscrepancyKind)
				});

			// 201 — a custody item was lost or damaged.
			Add<IncidentCommunicationsResourceAssignment>(builder, WorkflowTriggerEventType.IncidentAccountabilityException,
				c => Moved(c) && c.NewStatus is (int)IncidentCustodyStatuses.Lost or (int)IncidentCustodyStatuses.Damaged, (c, _) => Done(new Dictionary<string, object>
				{
					["item.kind"] = N<IncidentCommsItemKinds>(c.Entity.ItemKind),
					["item.label"] = c.Entity.ItemLabel,
					["holder.name"] = c.Entity.HolderName,
					["exception.kind"] = N<IncidentCustodyStatuses>(c.NewStatus ?? 0)
				}));

			// 203 — a requisition, commitment or purchase receipt moved.
			Add<PurchaseRequisition>(builder, WorkflowTriggerEventType.IncidentProcurementStatusChanged, Moved, async (c, labels) => new Dictionary<string, object>
			{
				["document.kind"] = "Requisition",
				["document.number"] = c.Entity.RequisitionNumber,
				["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
				["old_status"] = N<IncidentRequisitionStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentRequisitionStatuses>(c.NewStatus ?? 0),
				["amount"] = c.Entity.EstimatedAmount
			});
			Add<IncidentCommitment>(builder, WorkflowTriggerEventType.IncidentProcurementStatusChanged, Moved, async (c, labels) => new Dictionary<string, object>
			{
				["document.kind"] = c.Entity.Method == (int)IncidentPurchaseMethods.AgreementCall ? "AgreementCall" : "PurchaseOrder",
				["document.number"] = c.Entity.CommitmentNumber,
				["vendor.name"] = await labels.Of("InventoryVendors", c.Entity.VendorId),
				["old_status"] = N<IncidentCommitmentStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentCommitmentStatuses>(c.NewStatus ?? 0),
				["amount"] = c.Entity.Amount
			});
			Add<PurchaseReceipt>(builder, WorkflowTriggerEventType.IncidentProcurementStatusChanged, c => c.IsNew || Moved(c), (c, _) => Done(new Dictionary<string, object>
			{
				["document.kind"] = "Receipt",
				["old_status"] = c.IsNew ? null : N<IncidentReceiptStatuses>(c.OldStatus ?? 0),
				["new_status"] = N<IncidentReceiptStatuses>(c.NewStatus ?? c.Entity.Status),
				["amount"] = c.Entity.Amount
			}));

			// 204 — budget authority created, revised or transferred.
			Add<IncidentFundingAuthorization>(builder, WorkflowTriggerEventType.IncidentBudgetAuthorityChanged, c => c.IsNew, async (c, labels) => new Dictionary<string, object>
			{
				["budget.line"] = await labels.Of("IncidentBudgetLines", c.Entity.IncidentBudgetLineId),
				["action"] = c.Entity.Kind switch { (int)IncidentFundingAuthorizationKinds.Initial => "Created", (int)IncidentFundingAuthorizationKinds.Transfer => "Transferred", _ => "Revised" },
				["authorized_amount.change"] = c.Entity.Amount
			});
			Add<IncidentSpendingDelegation>(builder, WorkflowTriggerEventType.IncidentBudgetAuthorityChanged, c => c.IsNew, (c, _) => Done(new Dictionary<string, object>
			{
				["action"] = "Delegated",
				["delegation.position"] = c.Entity.PositionCode,
				["delegation.threshold"] = c.Entity.ThresholdAmount
			}));

			// 205 — an apportionment run was recorded.
			Add<IncidentApportionmentRun>(builder, WorkflowTriggerEventType.IncidentCostShareApportioned, c => c.IsNew, async (c, labels) => new Dictionary<string, object>
			{
				["agreement.number"] = await labels.Of("IncidentCostShareAgreements", c.Entity.IncidentCostShareAgreementId),
				["agreement.version"] = c.Entity.AgreementVersion,
				["run.kind"] = N<IncidentApportionmentRunKinds>(c.Entity.RunKind),
				["through.date"] = c.Entity.ThroughDate,
				["total.amount"] = c.Entity.TotalApportioned
			});
		}
	}
}
