using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Contractor bids (Workforce &amp; Business Operations plan, C4; decisions 14, 17, 22). Numbers come from the
	/// per-department sequence, rates are snapshotted onto lines when authored, the discount cascades contract →
	/// profile → bid, lifecycle changes publish registry 74–78 through the domain outbox, and an accepted bid converts
	/// transactionally into a Call + Deployment (+ calendar item). Bid rows are not under Advanced Data Protection —
	/// the customer receives the bid — and the delivery render decrypts only the customer's own Contact row (Contacts
	/// family) through the invoicing workload lane. Callers authorize.
	/// </summary>
	public class BidsService : IBidsService
	{
		private readonly IBidRepository _bids;
		private readonly IBidLineItemRepository _lines;
		private readonly IBidNumberSequenceRepository _sequence;
		private readonly IServiceContractRepository _contracts;
		private readonly ICustomerBillingProfileRepository _profiles;
		private readonly IDepartmentBillingIdentityRepository _identities;
		private readonly IRateScheduleService _rateSchedules;
		private readonly IDeploymentService _deployments;
		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly ICalendarService _calendarService;
		private readonly IEmailService _emailService;
		private readonly IPdfProvider _pdfProvider;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly Lazy<IProtectedReadService> _protectedRead;

		public BidsService(IBidRepository bids, IBidLineItemRepository lines, IBidNumberSequenceRepository sequence, IServiceContractRepository contracts,
			ICustomerBillingProfileRepository profiles, IDepartmentBillingIdentityRepository identities, IRateScheduleService rateSchedules, IDeploymentService deployments,
			IContactsService contactsService, IDepartmentsService departmentsService, ICallsService callsService, ICalendarService calendarService, IEmailService emailService,
			IPdfProvider pdfProvider, IDomainEventOutboxService outbox, IEventAggregator eventAggregator, IUnitOfWork unitOfWork,
			Lazy<IProtectedReadService> protectedRead = null)
		{
			_bids = bids;
			_lines = lines;
			_sequence = sequence;
			_contracts = contracts;
			_profiles = profiles;
			_identities = identities;
			_rateSchedules = rateSchedules;
			_deployments = deployments;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_calendarService = calendarService;
			_emailService = emailService;
			_pdfProvider = pdfProvider;
			_outbox = outbox;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_protectedRead = protectedRead;
		}

		#region Reads

		public async Task<List<Bid>> GetBidsForDepartmentAsync(int departmentId, BidStatuses? status = null, int skip = 0, int take = 100)
		{
			return (await _bids.GetForDepartmentAsync(departmentId, status.HasValue ? (int?)status.Value : null, skip, take))?.ToList() ?? new List<Bid>();
		}

		public Task<int> CountBidsForDepartmentAsync(int departmentId, BidStatuses? status = null) => _bids.CountForDepartmentAsync(departmentId, status.HasValue ? (int?)status.Value : null);

		public async Task<List<Bid>> GetBidsByContactIdAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId)) return new List<Bid>();
			return (await _bids.GetByContactIdAsync(departmentId, contactId))?.ToList() ?? new List<Bid>();
		}

		public Task<Bid> GetBidByIdAsync(string bidId, int departmentId) => LoadAsync(bidId, departmentId);

		private async Task<Bid> LoadAsync(string bidId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(bidId)) return null;
			var bid = await _bids.GetByIdForDepartmentAsync(bidId, departmentId);
			if (bid == null || bid.IsDeleted) return null;
			bid.LineItems = (await _lines.GetByBidAsync(bidId))?.OrderBy(l => l.SortOrder).ToList() ?? new List<BidLineItem>();
			return bid;
		}

		private async Task<Bid> RequireEditableAsync(string bidId, int departmentId)
		{
			var bid = await _bids.GetByIdForDepartmentAsync(bidId, departmentId);
			if (bid == null || bid.IsDeleted) throw new InvalidOperationException("bids_not_found");
			if (!bid.IsEditable) throw new InvalidOperationException("bids_locked");
			return bid;
		}

		#endregion

		#region Draft, header, lines

		public async Task<Bid> CreateDraftBidAsync(int departmentId, string contactId, string serviceContractId, string title, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(contactId)) throw new InvalidOperationException("bids_contact_required");
			var contact = await _contactsService.GetContactByIdAsync(contactId);
			if (contact == null || contact.DepartmentId != departmentId || contact.IsDeleted) throw new InvalidOperationException("bids_contact_not_found");
			ServiceContract contract = null;
			if (!string.IsNullOrWhiteSpace(serviceContractId))
			{
				contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
				if (contract == null || contract.IsDeleted) throw new InvalidOperationException("bids_contract_not_found");
				if (!string.Equals(contract.ContactId, contactId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bids_contract_contact_mismatch");
			}
			var profile = await _profiles.GetByContactIdAsync(contactId, departmentId);
			var schedule = await _rateSchedules.GetEffectiveScheduleForContactAsync(contactId, departmentId, contract?.ServiceContractId);

			var now = DateTime.UtcNow;
			var bid = new Bid
			{
				DepartmentId = departmentId,
				BidNumber = await _sequence.GetNextNumberAsync(departmentId, cancellationToken),
				ContactId = contactId,
				CustomerBillingProfileId = profile?.CustomerBillingProfileId,
				ServiceContractId = contract?.ServiceContractId,
				RateScheduleId = schedule?.RateScheduleId,
				Title = string.IsNullOrWhiteSpace(title) ? $"Bid for {contact.Name}" : title.Trim(),
				Status = (int)BidStatuses.Draft,
				// Decision 14: the discount cascades contract → profile; the bid may still override it.
				DiscountPercent = contract?.DiscountPercent ?? profile?.DefaultDiscountPercent,
				ValidUntil = now.Date.AddDays(30),
				AddedOn = now,
				AddedByUserId = userId
			};
			var saved = await _bids.SaveOrUpdateAsync(bid, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.BidCreated, ipAddress, userAgent, null, saved);
			await PublishAsync(saved, WorkflowTriggerEventType.BidCreated, null, cancellationToken);
			return await GetBidByIdAsync(saved.BidId, departmentId);
		}

		public async Task<Bid> SaveBidAsync(Bid bid, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (bid == null) throw new ArgumentNullException(nameof(bid));
			if (string.IsNullOrWhiteSpace(bid.Title)) throw new InvalidOperationException("bids_title_required");
			if (bid.DiscountPercent.HasValue && (bid.DiscountPercent < 0 || bid.DiscountPercent > 100)) throw new InvalidOperationException("bids_discount_invalid");
			if (bid.RequestedStartOn.HasValue && bid.RequestedEndOn.HasValue && bid.RequestedEndOn < bid.RequestedStartOn) throw new InvalidOperationException("bids_dates_invalid");
			var existing = await RequireEditableAsync(bid.BidId, bid.DepartmentId);
			var before = Snapshot(existing);

			if (!string.IsNullOrWhiteSpace(bid.ServiceContractId) && !string.Equals(bid.ServiceContractId, existing.ServiceContractId, StringComparison.OrdinalIgnoreCase))
			{
				var contract = await _contracts.GetByIdForDepartmentAsync(bid.ServiceContractId, bid.DepartmentId);
				if (contract == null || contract.IsDeleted || !string.Equals(contract.ContactId, existing.ContactId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bids_contract_not_found");
			}
			if (!string.IsNullOrWhiteSpace(bid.RateScheduleId) && !string.Equals(bid.RateScheduleId, existing.RateScheduleId, StringComparison.OrdinalIgnoreCase))
			{
				if (await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, bid.DepartmentId, includeInactive: true) == null) throw new InvalidOperationException("bids_schedule_not_found");
			}

			existing.ServiceContractId = Trim(bid.ServiceContractId);
			existing.RateScheduleId = Trim(bid.RateScheduleId);
			existing.Title = bid.Title.Trim();
			existing.Description = Trim(bid.Description);
			existing.ValidUntil = bid.ValidUntil;
			existing.RequestedStartOn = bid.RequestedStartOn;
			existing.RequestedEndOn = bid.RequestedEndOn;
			existing.IncidentNumber = Trim(bid.IncidentNumber);
			existing.DeliveryLocation = Trim(bid.DeliveryLocation);
			existing.DiscountPercent = bid.DiscountPercent;
			existing.Notes = Trim(bid.Notes);
			existing.TermsText = Trim(bid.TermsText);
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;

			var saved = await _bids.SaveOrUpdateAsync(existing, cancellationToken);
			var recalculated = await RecalculateEstimatesAsync(saved.BidId, saved.DepartmentId, cancellationToken);
			Audit(existing.DepartmentId, userId, AuditLogTypes.BidUpdated, ipAddress, userAgent, before, recalculated);
			return recalculated;
		}

		public async Task<Bid> SaveBidLineItemsAsync(string bidId, int departmentId, List<BidLineItem> lineItems, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var bid = await RequireEditableAsync(bidId, departmentId);
			var incoming = (lineItems ?? new List<BidLineItem>()).Where(l => l != null).ToList();
			if (incoming.Any(l => string.IsNullOrWhiteSpace(l.Description) || l.Quantity <= 0 || l.UnitRate < 0 || !Enum.IsDefined(typeof(BidLineTypes), l.LineType)))
				throw new InvalidOperationException("bids_line_invalid");

			var schedule = string.IsNullOrWhiteSpace(bid.RateScheduleId) ? null : await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, departmentId, includeInactive: true);
			var existing = (await _lines.GetByBidAsync(bidId))?.ToList() ?? new List<BidLineItem>();
			var before = existing.CloneJsonToString();
			var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var order = 0;
			foreach (var line in incoming.OrderBy(l => l.SortOrder))
			{
				var target = string.IsNullOrWhiteSpace(line.BidLineItemId) ? null : existing.FirstOrDefault(e => string.Equals(e.BidLineItemId, line.BidLineItemId, StringComparison.OrdinalIgnoreCase));
				target ??= new BidLineItem { BidId = bidId, DepartmentId = departmentId };
				target.RateScheduleEntryId = Trim(line.RateScheduleEntryId);
				target.LineType = line.LineType;
				target.Description = line.Description.Trim();
				target.CrewSize = line.CrewSize;
				target.Quantity = line.Quantity;
				target.EstimatedHoursPerDay = line.EstimatedHoursPerDay;
				target.EstimatedDays = line.EstimatedDays;
				target.PremiumIdsJson = line.PremiumIds.Count == 0 ? null : JsonConvert.SerializeObject(line.PremiumIds.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList());
				target.Taxable = line.Taxable;
				target.SortOrder = order++;
				// The rate snapshots at authoring (a moving schedule never changes a sent bid); a blank rate is filled from the entry.
				target.UnitRate = line.UnitRate > 0 || schedule == null ? line.UnitRate : SnapshotRate(schedule, target);
				target.EstimatedAmount = Estimate(target);
				var saved = await _lines.SaveOrUpdateAsync(target, cancellationToken);
				keep.Add(saved.BidLineItemId);
			}
			foreach (var stale in existing.Where(e => !keep.Contains(e.BidLineItemId)))
				await _lines.DeleteAsync(stale, cancellationToken);

			var recalculated = await RecalculateEstimatesAsync(bidId, departmentId, cancellationToken);
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.BidUpdated, ipAddress, userAgent);
			audit.Before = before;
			audit.After = recalculated.LineItems.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return recalculated;
		}

		/// <summary>Deployment-band rate (hourly) or daily-deployment rate (daily) plus the selected premiums' deployment adders.</summary>
		public static decimal SnapshotRate(RateSchedule schedule, BidLineItem line)
		{
			var entry = schedule?.Entries?.FirstOrDefault(e => string.Equals(e.RateScheduleEntryId, line.RateScheduleEntryId, StringComparison.OrdinalIgnoreCase));
			if (entry == null) return 0m;
			var band = (BillingBases)entry.BillingBasis switch
			{
				BillingBases.Daily or BillingBases.PerPersonPerDay => entry.Band(RateBandTypes.DailyDeployment) ?? entry.Band(RateBandTypes.Deployment),
				BillingBases.PerKilometer => entry.Band(RateBandTypes.MileagePerKm),
				_ => entry.Band(RateBandTypes.Deployment) ?? entry.Band(RateBandTypes.DailyDeployment) ?? entry.Bands.FirstOrDefault()
			};
			var rate = band?.Rate ?? 0m;
			foreach (var id in line.PremiumIds)
			{
				var premium = schedule.Premiums?.FirstOrDefault(p => string.Equals(p.RatePremiumId, id, StringComparison.OrdinalIgnoreCase) && p.IsActive && !p.IsDeleted);
				if (premium != null) rate += premium.DeploymentAdder;
			}
			return rate;
		}

		/// <summary>Quantity × rate × hours/day (when given) × days (when given); basis-agnostic so daily lines leave hours blank.</summary>
		public static decimal Estimate(BidLineItem line) =>
			Math.Round(line.Quantity * line.UnitRate * (line.EstimatedHoursPerDay ?? 1m) * (line.EstimatedDays ?? 1m), 2, MidpointRounding.AwayFromZero);

		public async Task<Bid> RecalculateEstimatesAsync(string bidId, int departmentId, CancellationToken cancellationToken = default)
		{
			var bid = await LoadAsync(bidId, departmentId);
			if (bid == null) throw new InvalidOperationException("bids_not_found");
			var profile = string.IsNullOrWhiteSpace(bid.CustomerBillingProfileId) ? null : await _profiles.GetByIdForDepartmentAsync(bid.CustomerBillingProfileId, departmentId);

			// Same rules as the invoice (decision 14/23): subtotal → discount → tax on the taxable base.
			var shadow = new Invoice { DiscountPercent = bid.DiscountPercent };
			var lines = bid.LineItems.Select(l => new InvoiceLineItem { Amount = l.EstimatedAmount, Taxable = l.Taxable }).ToList();
			InvoicingService.ComputeTotals(shadow, lines, profile);
			bid.EstimatedSubTotal = shadow.SubTotal;
			bid.EstimatedDiscountAmount = shadow.DiscountAmount;
			bid.EstimatedTaxAmount = shadow.TaxAmount;
			bid.EstimatedTotal = shadow.Total;
			var lineItems = bid.LineItems;
			var saved = await _bids.SaveOrUpdateAsync(bid, cancellationToken);
			saved.LineItems = lineItems;
			return saved;
		}

		#endregion

		#region Lifecycle

		public static bool IsValidTransition(BidStatuses from, BidStatuses to) => (from, to) switch
		{
			(BidStatuses.Draft, BidStatuses.Submitted) => true,
			(BidStatuses.Draft, BidStatuses.Withdrawn) => true,
			(BidStatuses.Submitted, BidStatuses.Accepted) => true,
			(BidStatuses.Submitted, BidStatuses.Declined) => true,
			(BidStatuses.Submitted, BidStatuses.Withdrawn) => true,
			(BidStatuses.Submitted, BidStatuses.Expired) => true,
			(BidStatuses.Expired, BidStatuses.Submitted) => true,
			(BidStatuses.Declined, BidStatuses.Submitted) => true,
			_ => false
		};

		private async Task<Bid> TransitionAsync(string bidId, int departmentId, BidStatuses to, AuditLogTypes auditType, WorkflowTriggerEventType? trigger, string userId, string ipAddress, string userAgent, Action<Bid> apply, CancellationToken cancellationToken)
		{
			var bid = await _bids.GetByIdForDepartmentAsync(bidId, departmentId);
			if (bid == null || bid.IsDeleted) throw new InvalidOperationException("bids_not_found");
			var from = (BidStatuses)bid.Status;
			if (!IsValidTransition(from, to)) throw new InvalidOperationException("bids_status_transition_invalid");
			if (to == BidStatuses.Submitted && !(await _lines.GetByBidAsync(bidId))?.Any() == true) throw new InvalidOperationException("bids_no_lines");

			var before = Snapshot(bid);
			bid.Status = (int)to;
			apply?.Invoke(bid);
			bid.EditedOn = DateTime.UtcNow;
			bid.EditedByUserId = userId;
			var saved = await _bids.SaveOrUpdateAsync(bid, cancellationToken);
			Audit(departmentId, userId, auditType, ipAddress, userAgent, before, saved);
			if (trigger.HasValue) await PublishAsync(saved, trigger.Value, (int)from, cancellationToken);
			return await GetBidByIdAsync(bidId, departmentId);
		}

		public Task<Bid> SubmitBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			TransitionAsync(bidId, departmentId, BidStatuses.Submitted, AuditLogTypes.BidSent, WorkflowTriggerEventType.BidSent, userId, ipAddress, userAgent, b => { b.SentOn ??= DateTime.UtcNow; }, cancellationToken);

		public async Task<Bid> SendBidAsync(string bidId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var bid = await LoadAsync(bidId, departmentId);
			if (bid == null) throw new InvalidOperationException("bids_not_found");
			if (bid.Status is not ((int)BidStatuses.Draft or (int)BidStatuses.Submitted)) throw new InvalidOperationException("bids_status_transition_invalid");
			if (bid.LineItems.Count == 0) throw new InvalidOperationException("bids_no_lines");

			var profile = string.IsNullOrWhiteSpace(bid.CustomerBillingProfileId) ? null : await _profiles.GetByIdForDepartmentAsync(bid.CustomerBillingProfileId, departmentId);
			var contact = await _contactsService.GetContactByIdAsync(bid.ContactId);
			await ResolveContactForWorkloadAsync(contact, departmentId);
			var recipient = !string.IsNullOrWhiteSpace(toEmail) ? toEmail.Trim() : profile?.BillingEmail ?? contact?.Email;
			if (string.IsNullOrWhiteSpace(recipient) || ProtectedDataEnvelope.HasEnvelopePrefix(recipient) || recipient == ProtectedDataEnvelope.RedactionValue) throw new InvalidOperationException("bids_no_recipient_email");

			var pdf = await GetBidPdfCoreAsync(bidId, departmentId, workload: true);
			if (pdf == null || pdf.Length == 0) throw new InvalidOperationException("bids_pdf_unavailable");

			var label = $"Bid #{bid.BidNumber}";
			var notification = new EmailNotification
			{
				To = recipient,
				Subject = $"{label} from {await DepartmentDisplayNameAsync(departmentId)}: {bid.Title}",
				Body = $"{label} — {bid.Title} — estimated {InvoicingService.FormatMoney(bid.EstimatedTotal, await CurrencyAsync(bid))} is attached." + (bid.ValidUntil.HasValue ? $" This bid is valid until {bid.ValidUntil.Value:yyyy-MM-dd}." : string.Empty),
				AttachmentName = $"bid-{bid.BidNumber}.pdf",
				AttachmentData = pdf
			};
			var sent = await _emailService.SendInvoiceAsync(notification, departmentId, null, null, label);
			if (!sent)
			{
				Logging.LogError($"Bid {bidId} e-mail to the customer was not sent (department {departmentId}).");
				throw new InvalidOperationException("bids_email_not_sent");
			}

			if (bid.Status == (int)BidStatuses.Draft)
				return await TransitionAsync(bidId, departmentId, BidStatuses.Submitted, AuditLogTypes.BidSent, WorkflowTriggerEventType.BidSent, userId, ipAddress, userAgent, b => { b.SentOn = DateTime.UtcNow; b.SentToEmail = recipient; }, cancellationToken);

			// Already submitted: record the (re)send without a status change.
			var before = Snapshot(bid);
			bid.SentOn = DateTime.UtcNow;
			bid.SentToEmail = recipient;
			bid.EditedOn = bid.SentOn;
			bid.EditedByUserId = userId;
			var saved = await _bids.SaveOrUpdateAsync(bid, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.BidSent, ipAddress, userAgent, before, saved);
			await PublishAsync(saved, WorkflowTriggerEventType.BidSent, (int)BidStatuses.Submitted, cancellationToken);
			return await GetBidByIdAsync(bidId, departmentId);
		}

		public Task<Bid> AcceptBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			TransitionAsync(bidId, departmentId, BidStatuses.Accepted, AuditLogTypes.BidAccepted, WorkflowTriggerEventType.BidAccepted, userId, ipAddress, userAgent, b => { b.AcceptedOn = DateTime.UtcNow; }, cancellationToken);

		public Task<Bid> DeclineBidAsync(string bidId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			TransitionAsync(bidId, departmentId, BidStatuses.Declined, AuditLogTypes.BidDeclined, WorkflowTriggerEventType.BidDeclined, userId, ipAddress, userAgent, b => { b.DeclinedOn = DateTime.UtcNow; b.DeclineReason = Trim(reason); }, cancellationToken);

		public Task<Bid> WithdrawBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			TransitionAsync(bidId, departmentId, BidStatuses.Withdrawn, AuditLogTypes.BidWithdrawn, null, userId, ipAddress, userAgent, null, cancellationToken);

		public Task<Bid> ExpireBidAsync(string bidId, int departmentId, CancellationToken cancellationToken = default) =>
			TransitionAsync(bidId, departmentId, BidStatuses.Expired, AuditLogTypes.BidExpired, WorkflowTriggerEventType.BidExpired, null, null, null, null, cancellationToken);

		public async Task<bool> DeleteBidAsync(string bidId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var bid = await _bids.GetByIdForDepartmentAsync(bidId, departmentId);
			if (bid == null || bid.IsDeleted) return false;
			if (bid.Status != (int)BidStatuses.Draft) throw new InvalidOperationException("bids_locked");
			var before = Snapshot(bid);
			bid.IsDeleted = true;
			bid.EditedOn = DateTime.UtcNow;
			bid.EditedByUserId = userId;
			await _bids.SaveOrUpdateAsync(bid, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.BidDeleted, ipAddress, userAgent, before, bid);
			return true;
		}

		public async Task<int> RunExpirySweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			var expired = 0;
			var enabled = new Dictionary<int, bool>();
			foreach (var bid in (await _bids.GetExpiryCandidatesAsync(asOfUtc))?.ToList() ?? new List<Bid>())
			{
				if (!enabled.TryGetValue(bid.DepartmentId, out var ok)) { ok = departmentEnabled == null || await departmentEnabled(bid.DepartmentId); enabled[bid.DepartmentId] = ok; }
				if (!ok) continue;
				try { await ExpireBidAsync(bid.BidId, bid.DepartmentId, cancellationToken); expired++; }
				catch (Exception ex) { Logging.LogException(ex, $"Bid {bid.BidId} could not be expired."); }
			}
			return expired;
		}

		#endregion

		#region Rendering

		public Task<string> RenderBidHtmlAsync(string bidId, int departmentId) => RenderBidHtmlCoreAsync(bidId, departmentId, workload: false);
		public Task<byte[]> GetBidPdfAsync(string bidId, int departmentId) => GetBidPdfCoreAsync(bidId, departmentId, workload: false);

		private async Task<byte[]> GetBidPdfCoreAsync(string bidId, int departmentId, bool workload)
		{
			var html = await RenderBidHtmlCoreAsync(bidId, departmentId, workload);
			return html == null ? null : _pdfProvider.ConvertHtmlToPdf(html);
		}

		private async Task<string> RenderBidHtmlCoreAsync(string bidId, int departmentId, bool workload)
		{
			var bid = await LoadAsync(bidId, departmentId);
			if (bid == null) return null;

			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			var contact = await _contactsService.GetContactByIdAsync(bid.ContactId);
			// Bid rows are never protected; the customer's Contact row may be, so the delivery render decrypts it for the PDF.
			if (workload) await ResolveContactForWorkloadAsync(contact, departmentId);
			var contract = string.IsNullOrWhiteSpace(bid.ServiceContractId) ? null : await _contracts.GetByIdForDepartmentAsync(bid.ServiceContractId, departmentId);
			var model = new BidRenderModel
			{
				Bid = bid,
				Currency = await CurrencyAsync(bid),
				DepartmentName = string.IsNullOrWhiteSpace(identity?.LegalBusinessName) ? department?.Name : identity.LegalBusinessName,
				CustomerName = ProtectedDataEnvelope.SafeDisplay(contact?.Name),
				ContractName = contract?.Name,
				ContractNumber = contract?.ContractNumber,
				FooterText = identity?.InvoiceFooterText
			};
			return RenderBidHtml(model);
		}

		public sealed class BidRenderModel
		{
			public Bid Bid { get; set; }
			public string Currency { get; set; }
			public string DepartmentName { get; set; }
			public string CustomerName { get; set; }
			public string ContractName { get; set; }
			public string ContractNumber { get; set; }
			public string FooterText { get; set; }
		}

		/// <summary>Pure HTML rendering; HTML-encodes every user value.</summary>
		public static string RenderBidHtml(BidRenderModel model)
		{
			var bid = model.Bid;
			var currency = model.Currency ?? "USD";
			var sb = new StringBuilder();
			sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E($"Bid #{bid.BidNumber}")).Append("</title>");
			sb.Append("<style>body{font-family:Helvetica,Arial,sans-serif;font-size:12px;color:#222;margin:32px}h1{font-size:22px;margin:0 0 4px}h2{font-size:14px;margin:18px 0 6px}table{border-collapse:collapse;width:100%}th,td{padding:6px 8px;text-align:left;vertical-align:top}th{border-bottom:2px solid #444;font-size:11px;text-transform:uppercase}td.num,th.num{text-align:right;white-space:nowrap}tr.line td{border-bottom:1px solid #ddd}table.totals{width:auto;margin-left:auto;margin-top:12px}table.totals td{padding:4px 8px}table.totals tr.grand td{border-top:2px solid #444;font-weight:bold;font-size:14px}.meta td{padding:2px 8px 2px 0}.muted{color:#666}.status{display:inline-block;padding:2px 8px;border:1px solid #444;border-radius:3px;font-size:11px;text-transform:uppercase}.footer{margin-top:28px;font-size:11px;color:#555;white-space:pre-wrap}</style></head><body>");
			sb.Append("<h1>").Append(E(model.DepartmentName)).Append("</h1>");
			sb.Append("<h2>Bid #").Append(bid.BidNumber).Append(" <span class=\"status\">").Append(E(((BidStatuses)bid.Status).ToString())).Append("</span></h2>");
			sb.Append("<p><strong>").Append(E(bid.Title)).Append("</strong></p>");
			if (!string.IsNullOrWhiteSpace(bid.Description)) sb.Append("<p>").Append(E(bid.Description)).Append("</p>");
			sb.Append("<table class=\"meta\">");
			Row(sb, "Prepared for", model.CustomerName);
			Row(sb, "Contract", string.IsNullOrWhiteSpace(model.ContractName) ? null : $"{model.ContractName}{(string.IsNullOrWhiteSpace(model.ContractNumber) ? string.Empty : $" ({model.ContractNumber})")}");
			Row(sb, "Incident", bid.IncidentNumber);
			Row(sb, "Delivery location", bid.DeliveryLocation);
			Row(sb, "Requested", bid.RequestedStartOn.HasValue ? $"{bid.RequestedStartOn:yyyy-MM-dd}{(bid.RequestedEndOn.HasValue ? $" to {bid.RequestedEndOn:yyyy-MM-dd}" : string.Empty)}" : null);
			Row(sb, "Valid until", bid.ValidUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			Row(sb, "Sent", bid.SentOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
			sb.Append("</table>");

			sb.Append("<table><thead><tr><th>Description</th><th class=\"num\">Qty</th><th class=\"num\">Hours/day</th><th class=\"num\">Days</th><th class=\"num\">Rate</th><th class=\"num\">Estimate</th></tr></thead><tbody>");
			foreach (var line in bid.LineItems ?? new List<BidLineItem>())
				sb.Append("<tr class=\"line\"><td>").Append(E(line.Description)).Append(line.CrewSize.HasValue ? $" <span class=\"muted\">({line.CrewSize}-person)</span>" : string.Empty)
				  .Append("</td><td class=\"num\">").Append(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture))
				  .Append("</td><td class=\"num\">").Append(line.EstimatedHoursPerDay?.ToString("0.##", CultureInfo.InvariantCulture) ?? "")
				  .Append("</td><td class=\"num\">").Append(line.EstimatedDays?.ToString("0.##", CultureInfo.InvariantCulture) ?? "")
				  .Append("</td><td class=\"num\">").Append(InvoicingService.FormatMoney(line.UnitRate, currency, 4))
				  .Append("</td><td class=\"num\">").Append(InvoicingService.FormatMoney(line.EstimatedAmount, currency)).Append("</td></tr>");
			sb.Append("</tbody></table>");

			sb.Append("<table class=\"totals\">");
			sb.Append("<tr><td>Subtotal</td><td class=\"num\">").Append(InvoicingService.FormatMoney(bid.EstimatedSubTotal, currency)).Append("</td></tr>");
			if (bid.EstimatedDiscountAmount > 0) sb.Append("<tr><td>Discount").Append(bid.DiscountPercent.HasValue ? $" ({bid.DiscountPercent.Value.ToString("0.##", CultureInfo.InvariantCulture)}%)" : string.Empty).Append("</td><td class=\"num\">-").Append(InvoicingService.FormatMoney(bid.EstimatedDiscountAmount, currency)).Append("</td></tr>");
			if (bid.EstimatedTaxAmount > 0) sb.Append("<tr><td>Tax (estimated)</td><td class=\"num\">").Append(InvoicingService.FormatMoney(bid.EstimatedTaxAmount, currency)).Append("</td></tr>");
			sb.Append("<tr class=\"grand\"><td>Estimated total</td><td class=\"num\">").Append(InvoicingService.FormatMoney(bid.EstimatedTotal, currency)).Append("</td></tr>");
			sb.Append("</table>");
			sb.Append("<p class=\"muted\">Estimates are based on the requested hours and days; actual charges follow the daily time reports and the rate schedule in force.</p>");
			if (!string.IsNullOrWhiteSpace(bid.TermsText)) sb.Append("<h2>Terms</h2><div class=\"footer\">").Append(E(bid.TermsText)).Append("</div>");
			if (!string.IsNullOrWhiteSpace(model.FooterText)) sb.Append("<div class=\"footer\">").Append(E(model.FooterText)).Append("</div>");
			sb.Append("</body></html>");
			return sb.ToString();
		}

		private static void Row(StringBuilder sb, string label, string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return;
			sb.Append("<tr><td class=\"muted\">").Append(E(label)).Append("</td><td>").Append(E(value)).Append("</td></tr>");
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

		#endregion

		#region Conversion

		public async Task<BidConversionContext> GetBidConversionContextAsync(string bidId, int departmentId)
		{
			var bid = await GetBidByIdAsync(bidId, departmentId);
			if (bid == null) return null;
			var contract = string.IsNullOrWhiteSpace(bid.ServiceContractId) ? null : await _contracts.GetByIdForDepartmentAsync(bid.ServiceContractId, departmentId);
			var schedule = string.IsNullOrWhiteSpace(bid.RateScheduleId) ? await _rateSchedules.GetEffectiveScheduleForContactAsync(bid.ContactId, departmentId, bid.ServiceContractId) : await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, departmentId);
			var contact = await _contactsService.GetContactByIdAsync(bid.ContactId);
			return new BidConversionContext
			{
				Bid = bid,
				Contract = contract,
				Schedule = schedule,
				ContactName = ProtectedDataEnvelope.SafeDisplay(contact?.Name),
				EffectiveDiscountPercent = bid.DiscountPercent ?? contract?.DiscountPercent,
				Currency = await CurrencyAsync(bid)
			};
		}

		public async Task<BidConversionResult> ConvertBidToDeploymentAsync(BidConversionRequest request, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			var bid = await LoadAsync(request.BidId, departmentId);
			if (bid == null) throw new InvalidOperationException("bids_not_found");
			if (bid.Status != (int)BidStatuses.Accepted) throw new InvalidOperationException("bids_not_accepted");
			if (bid.IsConverted) throw new InvalidOperationException("bids_already_converted");
			if (string.IsNullOrWhiteSpace(request.CallName)) throw new InvalidOperationException("bids_call_name_required");
			if (request.StartOn.HasValue && request.EndOn.HasValue && request.EndOn < request.StartOn) throw new InvalidOperationException("bids_dates_invalid");

			var contract = string.IsNullOrWhiteSpace(bid.ServiceContractId) ? null : await _contracts.GetByIdForDepartmentAsync(bid.ServiceContractId, departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			return await TransactionAsync(async () =>
			{
				var call = new Call
				{
					DepartmentId = departmentId,
					ReportingUserId = userId,
					Name = request.CallName.Trim(),
					NatureOfCall = string.IsNullOrWhiteSpace(request.CallNature) ? bid.Title : request.CallNature.Trim(),
					IncidentNumber = Trim(request.IncidentNumber) ?? bid.IncidentNumber,
					Priority = request.CallPriority,
					Type = request.CallTypeId?.ToString(),
					Address = Trim(request.Address),
					GeoLocationData = Trim(request.GeoLocation),
					LoggedOn = DateTime.UtcNow,
					State = (int)CallStates.Active,
					CallSource = (int)CallSources.User,
					ExternalIdentifier = $"bid:{bid.BidNumber}",
					Notes = Trim(request.Notes),
					Contacts = new List<CallContact> { new CallContact { DepartmentId = departmentId, ContactId = bid.ContactId, CallContactType = 0 } }
				};
				var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

				var deployment = await _deployments.SaveDeploymentAsync(new Deployment
				{
					DepartmentId = departmentId,
					CallId = savedCall.CallId,
					FinanceMode = (int)DeploymentFinanceModes.Billable,
					BidId = bid.BidId,
					ServiceContractId = bid.ServiceContractId,
					RateScheduleId = bid.RateScheduleId,
					ContactId = bid.ContactId,
					Name = request.CallName.Trim(),
					Status = (int)DeploymentStatuses.Planned,
					IncidentNumber = Trim(request.IncidentNumber) ?? bid.IncidentNumber,
					ServiceRequestNumber = Trim(request.ServiceRequestNumber),
					PointOfHire = Trim(request.PointOfHire) ?? contract?.PointOfHire,
					StartOn = request.StartOn ?? bid.RequestedStartOn,
					EndOn = request.EndOn ?? bid.RequestedEndOn,
					MaxDays = request.MaxDays ?? contract?.MaxDeploymentDays,
					OutOfProvince = request.OutOfProvince,
					TravelViaAir = request.TravelViaAir,
					LocalTimeZoneId = Trim(request.LocalTimeZoneId) ?? department?.TimeZone,
					Currency = await CurrencyAsync(bid),
					DiscountPercent = bid.DiscountPercent ?? contract?.DiscountPercent,
					Notes = Trim(request.Notes)
				}, userId, ipAddress, userAgent, cancellationToken);

				var result = new BidConversionResult { CallId = savedCall.CallId, Deployment = deployment };
				foreach (var unit in request.Units ?? new List<BidConversionUnit>())
				{
					var unitRow = await _deployments.AddUnitAsync(deployment.DeploymentId, departmentId, unit.UnitId, unit.CallSign, null, userId, ipAddress, userAgent, cancellationToken, unit.RateScheduleEntryId);
					result.Warnings.AddRange(unitRow.Warnings);
					if (unitRow.Unit == null) continue;
					foreach (var seat in unit.Seats ?? new List<BidConversionSeat>())
					{
						var seated = await _deployments.AddPersonnelAsync(deployment.DeploymentId, departmentId, new DeploymentPersonnelInput
						{
							UserId = seat.UserId, DeploymentUnitId = unitRow.Unit.DeploymentUnitId, UnitRoleId = seat.UnitRoleId, CertificationCode = seat.CertificationCode, CallSign = seat.CallSign,
							RateScheduleEntryId = seat.RateScheduleEntryId, PremiumIds = seat.PremiumIds, Force = true
						}, userId, ipAddress, userAgent, cancellationToken);
						result.Warnings.AddRange(seated.Warnings);
					}
					foreach (var equipment in unit.Equipment ?? new List<BidConversionEquipment>())
					{
						var issued = await _deployments.AddEquipmentAsync(deployment.DeploymentId, departmentId, new DeploymentEquipmentInput
						{
							DeploymentUnitId = unitRow.Unit.DeploymentUnitId, InventoryAssetId = equipment.InventoryAssetId, InventoryItemId = equipment.InventoryItemId, FreeTextName = equipment.FreeTextName, RateScheduleEntryId = equipment.RateScheduleEntryId
						}, userId, ipAddress, userAgent, cancellationToken);
						result.Warnings.AddRange(issued.Warnings);
					}
				}
				foreach (var seat in request.UnassignedPersonnel ?? new List<BidConversionSeat>())
				{
					var seated = await _deployments.AddPersonnelAsync(deployment.DeploymentId, departmentId, new DeploymentPersonnelInput
					{
						UserId = seat.UserId, CertificationCode = seat.CertificationCode, CallSign = seat.CallSign, RateScheduleEntryId = seat.RateScheduleEntryId, PremiumIds = seat.PremiumIds, Force = true
					}, userId, ipAddress, userAgent, cancellationToken);
					result.Warnings.AddRange(seated.Warnings);
				}

				if (request.CreateCalendarItem && deployment.StartOn.HasValue)
				{
					try
					{
						var timeZone = department?.TimeZone ?? "UTC";
						var start = DateTimeHelpers.GetLocalDateTime(deployment.StartOn.Value, timeZone);
						var end = DateTimeHelpers.GetLocalDateTime(deployment.EndOn ?? deployment.StartOn.Value.AddHours(8), timeZone);
						var item = await _calendarService.AddNewCalendarItemAsync(new CalendarItem
						{
							DepartmentId = departmentId, Title = deployment.Name, Start = start, End = end <= start ? start.AddHours(1) : end,
							Description = $"Deployment for bid #{bid.BidNumber}" + (string.IsNullOrWhiteSpace(deployment.IncidentNumber) ? string.Empty : $" — incident {deployment.IncidentNumber}"),
							Location = Trim(request.Address), CreatorUserId = userId, IsAllDay = false, ItemType = 0, Public = false
						}, timeZone, cancellationToken);
						if (item != null && item.CalendarItemId > 0)
						{
							result.CalendarItemId = item.CalendarItemId;
							deployment.CalendarItemId = item.CalendarItemId;
							deployment = await _deployments.SaveDeploymentAsync(deployment, userId, ipAddress, userAgent, cancellationToken);
						}
					}
					catch (Exception ex) { Logging.LogException(ex, $"Bid {bid.BidId}: calendar item was not created."); }
				}

				var before = Snapshot(bid);
				bid.ConvertedCallId = savedCall.CallId;
				bid.ConvertedDeploymentId = deployment.DeploymentId;
				bid.EditedOn = DateTime.UtcNow;
				bid.EditedByUserId = userId;
				var lineItems = bid.LineItems;
				var savedBid = await _bids.SaveOrUpdateAsync(bid, cancellationToken);
				savedBid.LineItems = lineItems;
				Audit(departmentId, userId, AuditLogTypes.BidConverted, ipAddress, userAgent, before, savedBid);
				result.Bid = await GetBidByIdAsync(bid.BidId, departmentId);
				result.Deployment = await _deployments.GetDeploymentByIdAsync(deployment.DeploymentId, departmentId) ?? deployment;
				return result;
			}, cancellationToken);
		}

		#endregion

		#region Helpers

		private async Task<string> CurrencyAsync(Bid bid)
		{
			var schedule = string.IsNullOrWhiteSpace(bid.RateScheduleId) ? null : await _rateSchedules.GetScheduleByIdAsync(bid.RateScheduleId, bid.DepartmentId, includeInactive: true);
			return schedule?.Currency ?? "USD";
		}

		/// <summary>Decrypts a protected customer contact for a system workload (bid delivery). Never throws; leaves the row as-is on failure.</summary>
		private async Task ResolveContactForWorkloadAsync(Contact contact, int departmentId)
		{
			if (contact == null || _protectedRead?.Value == null) return;
			try { await _protectedRead.Value.ResolveRecordsEntitiesForWorkloadAsync(departmentId, "invoicing", new[] { (contact, contact.ContactId) }, ProtectedReadService.ContactFieldAccessors); }
			catch (Exception ex) { Logging.LogException(ex, $"Contact {contact.ContactId} could not be resolved for the bid workload."); }
		}

		private async Task<string> DepartmentDisplayNameAsync(int departmentId)
		{
			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			if (!string.IsNullOrWhiteSpace(identity?.LegalBusinessName)) return identity.LegalBusinessName;
			return (await _departmentsService.GetDepartmentByIdAsync(departmentId))?.Name ?? "Resgrid";
		}

		private async Task PublishAsync(Bid bid, WorkflowTriggerEventType trigger, int? oldStatus, CancellationToken cancellationToken)
		{
			try
			{
				string contactName = null;
				try { contactName = (await _contactsService.GetContactByIdAsync(bid.ContactId))?.Name; } catch (Exception ex) { Logging.LogException(ex, "Bid event: contact name lookup failed."); }
				await _outbox.EnqueueAsync(bid.DepartmentId, ContractorWorkflowPayload.Producer, new DomainEventEnvelope
				{
					EventName = trigger.ToString(),
					AggregateType = "Bid",
					AggregateId = bid.BidId,
					AggregateVersion = 0,
					Trigger = trigger,
					OccurredOn = DateTime.UtcNow,
					CorrelationId = bid.BidId,
					Payload = new
					{
						bid.BidId, bid.BidNumber, bid.Title, bid.Status, OldStatus = oldStatus, bid.ContactId,
						ContactName = ProtectedDataEnvelope.SafeDisplay(contactName),
						bid.ServiceContractId, bid.IncidentNumber, bid.ValidUntil, bid.RequestedStartOn, bid.RequestedEndOn, bid.EstimatedTotal,
						Currency = await CurrencyAsync(bid), bid.SentOn, bid.AcceptedOn, bid.DeclinedOn, bid.ConvertedDeploymentId, bid.ConvertedCallId
					}
				}, cancellationToken);
			}
			catch (Exception ex) { Logging.LogException(ex, $"Bid {bid.BidId} {trigger} could not be published."); }
		}

		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null) return await action();
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var result = await action();
				_unitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		internal static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			if (clone is Bid bid) bid.LineItems = null;
			return clone.CloneJsonToString();
		}

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
