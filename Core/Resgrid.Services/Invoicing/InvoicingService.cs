using System;
using System.Collections.Generic;
using System.Linq;
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
	/// Phase B customer invoicing (Workforce &amp; Business Operations plan, B4). Money math is server-side only
	/// (decision 12), numbers come from the atomic sequence (decision 7), status transitions happen only here
	/// (decision 6), and every transition publishes its Workflow trigger through the domain outbox (decision 22).
	/// Entitlement and feature-flag checks are the controllers' job; this service trusts its caller's authorization.
	/// </summary>
	public partial class InvoicingService : IInvoicingService
	{
		private readonly ICustomerBillingProfileRepository _profiles;
		private readonly IRateCardRepository _rateCards;
		private readonly IRateCardItemRepository _rateCardItems;
		private readonly IInvoiceRepository _invoices;
		private readonly IInvoiceLineItemRepository _lineItems;
		private readonly IInvoicePaymentRepository _payments;
		private readonly IInvoiceNumberSequenceRepository _sequence;
		private readonly IDepartmentBillingIdentityRepository _identities;
		/// <summary>Contractor billing (C-M2): the linked contract's terms override the profile's net days when the invoice issues.</summary>
		private readonly IServiceContractRepository _serviceContracts;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly IContactsService _contactsService;
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IEventAggregator _eventAggregator;
		private readonly IPdfProvider _pdfProvider;
		private readonly IEmailService _emailService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAddressService _addressService;
		private readonly IUnitOfWork _unitOfWork;

		public InvoicingService(ICustomerBillingProfileRepository profiles, IRateCardRepository rateCards, IRateCardItemRepository rateCardItems,
			IInvoiceRepository invoices, IInvoiceLineItemRepository lineItems, IInvoicePaymentRepository payments,
			IInvoiceNumberSequenceRepository sequence, IDepartmentBillingIdentityRepository identities,
			IContactsService contactsService, ICallsService callsService, IUnitsService unitsService,
			IDomainEventOutboxService outbox, IEventAggregator eventAggregator,
			IPdfProvider pdfProvider, IEmailService emailService, IDepartmentsService departmentsService, IAddressService addressService, IUnitOfWork unitOfWork,
			Lazy<IInvoicePaymentsService> paymentsService = null, Lazy<IProtectedReadService> protectedRead = null,
			IServiceContractRepository serviceContracts = null, Lazy<ISearchProjectionService> searchProjections = null)
		{
			_serviceContracts = serviceContracts;
			_searchProjections = searchProjections;
			_unitOfWork = unitOfWork;
			_paymentsService = paymentsService;
			_protectedRead = protectedRead;
			_pdfProvider = pdfProvider;
			_emailService = emailService;
			_departmentsService = departmentsService;
			_addressService = addressService;
			_profiles = profiles;
			_rateCards = rateCards;
			_rateCardItems = rateCardItems;
			_invoices = invoices;
			_lineItems = lineItems;
			_payments = payments;
			_sequence = sequence;
			_identities = identities;
			_contactsService = contactsService;
			_callsService = callsService;
			_unitsService = unitsService;
			_outbox = outbox;
			_eventAggregator = eventAggregator;
		}

		#region Billing profiles

		public async Task<CustomerBillingProfile> GetBillingProfileByContactIdAsync(string contactId, int departmentId)
		{
			return await _profiles.GetByContactIdAsync(contactId, departmentId);
		}

		public async Task<CustomerBillingProfile> GetBillingProfileByIdAsync(string customerBillingProfileId, int departmentId)
		{
			return await _profiles.GetByIdForDepartmentAsync(customerBillingProfileId, departmentId);
		}

		public async Task<List<CustomerBillingProfile>> GetBillingProfilesForDepartmentAsync(int departmentId)
		{
			return (await _profiles.GetAllForDepartmentAsync(departmentId))?.ToList() ?? new List<CustomerBillingProfile>();
		}

		public async Task<CustomerBillingProfile> SaveBillingProfileAsync(CustomerBillingProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (string.IsNullOrWhiteSpace(profile.ContactId)) throw new ArgumentException("ContactId is required.", nameof(profile));

			var contact = await _contactsService.GetContactByIdAsync(profile.ContactId);
			if (contact == null || contact.DepartmentId != profile.DepartmentId)
				throw new InvalidOperationException("invoicing_contact_not_found");

			ValidateTaxComponents(profile.TaxComponentsJson);
			ValidatePercent(profile.DefaultDiscountPercent, nameof(profile.DefaultDiscountPercent));
			ValidatePercent(profile.TaxRate, nameof(profile.TaxRate));
			if (profile.TermsNetDays < 0 || profile.TermsNetDays > 365) throw new ArgumentException("TermsNetDays must be between 0 and 365.", nameof(profile));

			if (!string.IsNullOrWhiteSpace(profile.DefaultRateCardId) && await _rateCards.GetByIdForDepartmentAsync(profile.DefaultRateCardId, profile.DepartmentId) == null)
				throw new InvalidOperationException("invoicing_rate_card_not_found");

			var existing = await _profiles.GetByContactIdAsync(profile.ContactId, profile.DepartmentId);
			var now = DateTime.UtcNow;
			var audit = NewAuditEvent(profile.DepartmentId, userId, AuditLogTypes.BillingProfileChanged, ipAddress, userAgent);

			if (existing != null)
			{
				audit.Before = Snapshot(existing);
				// One live profile per contact: an incoming save always lands on the existing row.
				profile.CustomerBillingProfileId = existing.CustomerBillingProfileId;
				profile.AddedOn = existing.AddedOn;
				profile.AddedByUserId = existing.AddedByUserId;
				profile.EditedOn = now;
				profile.EditedByUserId = userId;
			}
			else
			{
				profile.CustomerBillingProfileId = null;
				profile.AddedOn = now;
				profile.AddedByUserId = userId;
				profile.EditedOn = null;
				profile.EditedByUserId = null;
			}
			profile.IsDeleted = false;

			var saved = await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return saved;
		}

		public async Task<bool> DeleteBillingProfileAsync(string customerBillingProfileId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByIdForDepartmentAsync(customerBillingProfileId, departmentId);
			if (profile == null) return false;
			if (await _invoices.HasNonVoidInvoicesForContactAsync(profile.ContactId, departmentId))
				throw new InvalidOperationException("invoicing_profile_has_invoices");

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.BillingProfileChanged, ipAddress, userAgent);
			audit.Before = Snapshot(profile);
			profile.IsDeleted = true;
			profile.EditedOn = DateTime.UtcNow;
			profile.EditedByUserId = userId;
			await _profiles.SaveOrUpdateAsync(profile, cancellationToken);
			audit.After = Snapshot(profile);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return true;
		}

		public async Task<bool> ContactHasOpenBillingAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId)) return false;
			return await _invoices.HasNonVoidInvoicesForContactAsync(contactId, departmentId);
		}

		#endregion

		#region Rate cards

		public async Task<List<RateCard>> GetRateCardsForDepartmentAsync(int departmentId)
		{
			var cards = (await _rateCards.GetAllForDepartmentAsync(departmentId))?.ToList() ?? new List<RateCard>();
			foreach (var card in cards)
				card.Items = (await _rateCardItems.GetByRateCardIdAsync(card.RateCardId, departmentId))?.ToList() ?? new List<RateCardItem>();
			return cards;
		}

		public async Task<RateCard> GetRateCardByIdAsync(string rateCardId, int departmentId, bool includeInactiveItems = false)
		{
			var card = await _rateCards.GetByIdForDepartmentAsync(rateCardId, departmentId);
			if (card == null) return null;
			card.Items = (await _rateCardItems.GetByRateCardIdAsync(rateCardId, departmentId, includeInactiveItems))?.ToList() ?? new List<RateCardItem>();
			return card;
		}

		public async Task<RateCard> SaveRateCardAsync(RateCard rateCard, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (rateCard == null) throw new ArgumentNullException(nameof(rateCard));
			if (string.IsNullOrWhiteSpace(rateCard.Name)) throw new ArgumentException("Name is required.", nameof(rateCard));

			var existing = string.IsNullOrWhiteSpace(rateCard.RateCardId) ? null : await _rateCards.GetByIdForDepartmentAsync(rateCard.RateCardId, rateCard.DepartmentId);
			var now = DateTime.UtcNow;
			var audit = NewAuditEvent(rateCard.DepartmentId, userId, AuditLogTypes.RateCardChanged, ipAddress, userAgent);

			if (existing != null)
			{
				audit.Before = Snapshot(existing);
				rateCard.AddedOn = existing.AddedOn;
				rateCard.AddedByUserId = existing.AddedByUserId;
				rateCard.EditedOn = now;
				rateCard.EditedByUserId = userId;
			}
			else
			{
				rateCard.RateCardId = null;
				rateCard.AddedOn = now;
				rateCard.AddedByUserId = userId;
			}
			rateCard.IsDeleted = false;

			var saved = await _rateCards.SaveOrUpdateAsync(rateCard, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectRateCardAsync(rateCard, cancellationToken);
			if (saved.IsDefault)
				await _rateCards.ClearDefaultAsync(saved.DepartmentId, saved.RateCardId, cancellationToken);

			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			saved.Items = (await _rateCardItems.GetByRateCardIdAsync(saved.RateCardId, saved.DepartmentId, true))?.ToList() ?? new List<RateCardItem>();
			return saved;
		}

		public async Task<bool> DeleteRateCardAsync(string rateCardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var card = await _rateCards.GetByIdForDepartmentAsync(rateCardId, departmentId);
			if (card == null) return false;

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.RateCardChanged, ipAddress, userAgent);
			audit.Before = Snapshot(card);
			var now = DateTime.UtcNow;
			foreach (var item in (await _rateCardItems.GetByRateCardIdAsync(rateCardId, departmentId, true)) ?? Enumerable.Empty<RateCardItem>())
			{
				item.IsDeleted = true;
				item.EditedOn = now;
				item.EditedByUserId = userId;
				await _rateCardItems.SaveOrUpdateAsync(item, cancellationToken);
			}
			card.IsDeleted = true;
			card.IsDefault = false;
			card.EditedOn = now;
			card.EditedByUserId = userId;
			await _rateCards.SaveOrUpdateAsync(card, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectRateCardAsync(card, cancellationToken);
			audit.After = Snapshot(card);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return true;
		}

		public async Task<RateCardItem> SaveRateCardItemAsync(RateCardItem item, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (item == null) throw new ArgumentNullException(nameof(item));
			if (string.IsNullOrWhiteSpace(item.Name)) throw new ArgumentException("Name is required.", nameof(item));
			if (item.Rate < 0) throw new ArgumentException("Rate cannot be negative.", nameof(item));
			if (!Enum.IsDefined(typeof(RateCardItemTypes), item.ItemType)) throw new ArgumentException("Unknown item type.", nameof(item));
			var card = await _rateCards.GetByIdForDepartmentAsync(item.RateCardId, item.DepartmentId);
			if (card == null) throw new InvalidOperationException("invoicing_rate_card_not_found");

			var existing = string.IsNullOrWhiteSpace(item.RateCardItemId) ? null : await _rateCardItems.GetByIdForDepartmentAsync(item.RateCardItemId, item.DepartmentId);
			var now = DateTime.UtcNow;
			var audit = NewAuditEvent(item.DepartmentId, userId, AuditLogTypes.RateCardChanged, ipAddress, userAgent);
			if (existing != null)
			{
				audit.Before = Snapshot(existing);
				item.AddedOn = existing.AddedOn;
				item.AddedByUserId = existing.AddedByUserId;
				item.EditedOn = now;
				item.EditedByUserId = userId;
			}
			else
			{
				item.RateCardItemId = null;
				item.AddedOn = now;
				item.AddedByUserId = userId;
			}
			item.IsDeleted = false;

			var saved = await _rateCardItems.SaveOrUpdateAsync(item, cancellationToken);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return saved;
		}

		public async Task<bool> DeleteRateCardItemAsync(string rateCardItemId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var item = await _rateCardItems.GetByIdForDepartmentAsync(rateCardItemId, departmentId);
			if (item == null) return false;
			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.RateCardChanged, ipAddress, userAgent);
			audit.Before = Snapshot(item);
			item.IsDeleted = true;
			item.EditedOn = DateTime.UtcNow;
			item.EditedByUserId = userId;
			await _rateCardItems.SaveOrUpdateAsync(item, cancellationToken);
			audit.After = Snapshot(item);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return true;
		}

		public async Task<RateCard> GetEffectiveRateCardForContactAsync(string contactId, int departmentId)
		{
			var profile = string.IsNullOrWhiteSpace(contactId) ? null : await _profiles.GetByContactIdAsync(contactId, departmentId);
			if (profile != null && !string.IsNullOrWhiteSpace(profile.DefaultRateCardId))
			{
				var pinned = await GetRateCardByIdAsync(profile.DefaultRateCardId, departmentId);
				if (pinned != null && pinned.Active) return pinned;
			}
			var fallback = await _rateCards.GetDefaultForDepartmentAsync(departmentId);
			return fallback == null ? null : await GetRateCardByIdAsync(fallback.RateCardId, departmentId);
		}

		#endregion

		#region Invoices

		public async Task<List<Invoice>> GetInvoicesForDepartmentAsync(int departmentId, InvoiceListFilter filter)
		{
			var invoices = (await _invoices.GetForDepartmentAsync(departmentId, filter ?? new InvoiceListFilter()))?.ToList() ?? new List<Invoice>();
			return invoices;
		}

		public Task<int> CountInvoicesForDepartmentAsync(int departmentId, InvoiceListFilter filter) =>
			_invoices.CountForDepartmentAsync(departmentId, filter ?? new InvoiceListFilter());

		public async Task<List<Invoice>> GetInvoicesByContactIdAsync(string contactId, int departmentId)
		{
			var invoices = (await _invoices.GetByContactIdAsync(contactId, departmentId))?.ToList() ?? new List<Invoice>();
			return invoices;
		}

		public async Task<Invoice> GetInvoiceByIdAsync(string invoiceId, int departmentId)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) return null;
			await LoadChildrenAsync(invoice);
			return invoice;
		}

		public async Task<Invoice> CreateDraftInvoiceAsync(int departmentId, string contactId, string userId, string ipAddress, string userAgent, string currency = null, CancellationToken cancellationToken = default)
		{
			var profile = await _profiles.GetByContactIdAsync(contactId, departmentId);
			if (profile == null || !profile.Active) throw new InvalidOperationException("invoicing_profile_required");

			var now = DateTime.UtcNow;
			var invoice = new Invoice
			{
				InvoiceId = null,
				DepartmentId = departmentId,
				InvoiceNumber = await _sequence.GetNextNumberAsync(departmentId, cancellationToken),
				CustomerBillingProfileId = profile.CustomerBillingProfileId,
				ContactId = profile.ContactId,
				Status = (int)InvoiceStatus.Draft,
				Currency = NormalizeCurrency(currency),
				DiscountPercent = profile.DefaultDiscountPercent,
				TaxComponentsJson = null,
				AddedOn = now,
				AddedByUserId = userId
			};

			var saved = await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceCreated, ipAddress, userAgent);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await PublishAsync(saved, WorkflowTriggerEventType.InvoiceCreated, cancellationToken: cancellationToken);
			saved.LineItems = new List<InvoiceLineItem>();
			saved.Payments = new List<InvoicePayment>();
			return saved;
		}

		public async Task<Invoice> SaveInvoiceAsync(Invoice invoice, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (invoice == null) throw new ArgumentNullException(nameof(invoice));
			var existing = await RequireDraftAsync(invoice.InvoiceId, invoice.DepartmentId);
			ValidatePercent(invoice.DiscountPercent, nameof(invoice.DiscountPercent));

			var audit = NewAuditEvent(existing.DepartmentId, userId, AuditLogTypes.InvoiceUpdated, ipAddress, userAgent);
			audit.Before = Snapshot(existing);

			// Only the editable header fields move; numbers, status, money and provenance stay server-owned.
			existing.Notes = invoice.Notes;
			existing.TermsText = invoice.TermsText;
			existing.DiscountPercent = invoice.DiscountPercent;
			existing.DueOn = invoice.DueOn;
			existing.IssuedOn = invoice.IssuedOn;
			existing.Currency = NormalizeCurrency(invoice.Currency);
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;

			await _invoices.SaveOrUpdateAsync(existing, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(existing, cancellationToken);
			var recalculated = await RecalculateTotalsAsync(existing.InvoiceId, existing.DepartmentId, cancellationToken);
			audit.After = Snapshot(recalculated);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return recalculated;
		}

		public async Task<Invoice> LinkInvoiceToDeploymentAsync(string invoiceId, int departmentId, string deploymentId, string serviceContractId, int? termsNetDays, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var existing = await RequireDraftAsync(invoiceId, departmentId);
			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceUpdated, ipAddress, userAgent);
			audit.Before = Snapshot(existing);
			existing.DeploymentId = string.IsNullOrWhiteSpace(deploymentId) ? null : deploymentId.Trim();
			existing.ServiceContractId = string.IsNullOrWhiteSpace(serviceContractId) ? null : serviceContractId.Trim();
			// Contract terms override the profile's net days (decision 14 cascade); the due date is derived when the invoice issues.
			if (termsNetDays.HasValue && termsNetDays.Value > 0 && existing.IssuedOn.HasValue) existing.DueOn = existing.IssuedOn.Value.AddDays(termsNetDays.Value);
			existing.EditedOn = DateTime.UtcNow;
			existing.EditedByUserId = userId;
			await _invoices.SaveOrUpdateAsync(existing, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(existing, cancellationToken);
			var result = await GetInvoiceByIdAsync(invoiceId, departmentId);
			audit.After = Snapshot(result);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return result;
		}

		public Task<Invoice> SaveDraftAsync(Invoice invoice, List<InvoiceLineItem> lineItems, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (invoice == null) throw new ArgumentNullException(nameof(invoice));
			// Header and lines commit together: a line failure after the header write would otherwise leave the
			// draft with new header fields over stale lines and totals.
			return TransactionAsync(async () =>
			{
				await SaveInvoiceAsync(invoice, userId, ipAddress, userAgent, cancellationToken);
				return await SaveInvoiceLineItemsAsync(invoice.InvoiceId, invoice.DepartmentId, lineItems, userId, ipAddress, userAgent, cancellationToken);
			}, cancellationToken);
		}

		public Task<Invoice> SaveInvoiceLineItemsAsync(string invoiceId, int departmentId, List<InvoiceLineItem> lineItems, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			// Delete-then-insert must not be observable half done: one transaction, joined when the caller already owns one.
			return TransactionAsync(async () =>
			{
				var invoice = await RequireDraftAsync(invoiceId, departmentId);
				var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceUpdated, ipAddress, userAgent);
				audit.Before = Snapshot(invoice);

				// Lines keep their ids across a save (provenance links and audit history follow the row); stale lines are deleted.
				var current = (await _lineItems.GetByInvoiceIdAsync(invoiceId, departmentId))?.ToDictionary(l => l.InvoiceLineItemId, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, InvoiceLineItem>(StringComparer.OrdinalIgnoreCase);
				var incoming = (lineItems ?? new List<InvoiceLineItem>()).Where(x => x != null).ToList();
				var kept = new HashSet<string>(incoming.Where(l => !string.IsNullOrWhiteSpace(l.InvoiceLineItemId) && current.ContainsKey(l.InvoiceLineItemId)).Select(l => l.InvoiceLineItemId), StringComparer.OrdinalIgnoreCase);
				foreach (var line in incoming)
				{
					if (string.IsNullOrWhiteSpace(line.Description)) throw new ArgumentException("Every line needs a description.", nameof(lineItems));
				}
				foreach (var stale in current.Values.Where(l => !kept.Contains(l.InvoiceLineItemId)))
					await _lineItems.DeleteAsync(stale, cancellationToken);
				var sort = 0;
				var minimums = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
				foreach (var line in incoming)
				{
					var existingLine = !string.IsNullOrWhiteSpace(line.InvoiceLineItemId) && current.TryGetValue(line.InvoiceLineItemId, out var found) ? found : null;
					if (existingLine == null) line.InvoiceLineItemId = null;
					line.InvoiceId = invoiceId;
					line.DepartmentId = departmentId;
					line.Amount = RoundMoney(line.Quantity * line.UnitRate);
					// The rate card item's minimum charge is a floor the generator applies; the line carries no copy of
					// it, so it is re-applied here or every save (and the clerk's edit page) would silently under-bill.
					if (!string.IsNullOrWhiteSpace(line.RateCardItemId) && line.Quantity > 0)
					{
						if (!minimums.TryGetValue(line.RateCardItemId, out var minimum))
							minimums[line.RateCardItemId] = minimum = (await _rateCardItems.GetByIdForDepartmentAsync(line.RateCardItemId, departmentId))?.MinimumCharge;
						if (minimum.HasValue && line.Amount < RoundMoney(minimum.Value))
							line.Amount = RoundMoney(minimum.Value);
					}
					line.SortOrder = sort++;
					await _lineItems.SaveOrUpdateAsync(line, cancellationToken);
				}

				var recalculated = await RecalculateTotalsAsync(invoiceId, departmentId, cancellationToken);
				audit.After = Snapshot(recalculated);
				_eventAggregator.SendMessage<AuditEvent>(audit);
				return recalculated;
			}, cancellationToken);
		}

		public async Task<List<InvoiceLineItem>> GenerateLineItemsFromCallAsync(int callId, string rateCardId, int departmentId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != departmentId) throw new InvalidOperationException("invoicing_call_not_found");
			var card = await GetRateCardByIdAsync(rateCardId, departmentId);
			if (card == null) throw new InvalidOperationException("invoicing_rate_card_not_found");

			var lines = new List<InvoiceLineItem>();
			var callLabel = string.IsNullOrWhiteSpace(call.Number) ? call.CallId.ToString() : call.Number;
			var states = (await _unitsService.GetUnitStatesForCallAsync(departmentId, callId))?.Where(x => x != null).OrderBy(x => x.Timestamp).ToList() ?? new List<UnitState>();
			var fallbackEnd = call.ClosedOn ?? DateTime.UtcNow;

			foreach (var item in card.Items.Where(x => x.Active && !x.IsDeleted).OrderBy(x => x.SortOrder))
			{
				switch ((RateCardItemTypes)item.ItemType)
				{
					case RateCardItemTypes.HourlyUnit:
						foreach (var group in states.GroupBy(x => x.UnitId))
						{
							var unit = await _unitsService.GetUnitByIdAsync(group.Key);
							if (unit == null) continue;
							if (!string.IsNullOrWhiteSpace(item.UnitTypeFilter) && !string.Equals(unit.Type, item.UnitTypeFilter, StringComparison.OrdinalIgnoreCase)) continue;
							var minutes = OnSceneMinutes(group.ToList(), call.LoggedOn, fallbackEnd);
							if (minutes <= 0) continue;
							var billable = ApplyRounding(minutes, item);
							lines.Add(new InvoiceLineItem
							{
								CallId = callId,
								RateCardItemId = item.RateCardItemId,
								Description = $"{item.Name} — {unit.Name} — call {callLabel}",
								Quantity = billable,
								UnitRate = item.Rate,
								Amount = Math.Max(RoundMoney(billable * item.Rate), item.MinimumCharge.HasValue ? RoundMoney(item.MinimumCharge.Value) : 0m),
								Taxable = item.Taxable
							});
						}
						break;
					case RateCardItemTypes.HourlyPersonnel:
						// Personnel time on scene is not tracked per call today; emit an editable zero-quantity line so the
						// clerk fills hours in (plan decision 9: the generator drafts, the user finishes).
						lines.Add(new InvoiceLineItem { CallId = callId, RateCardItemId = item.RateCardItemId, Description = $"{item.Name} — call {callLabel}", Quantity = 0, UnitRate = item.Rate, Amount = 0, Taxable = item.Taxable });
						break;
					case RateCardItemTypes.FlatPerCall:
						lines.Add(new InvoiceLineItem { CallId = callId, RateCardItemId = item.RateCardItemId, Description = $"{item.Name} — call {callLabel}", Quantity = 1, UnitRate = item.Rate, Amount = RoundMoney(item.Rate), Taxable = item.Taxable });
						break;
					case RateCardItemTypes.FixedFee:
					case RateCardItemTypes.Mileage:
					case RateCardItemTypes.Material:
						// Quantity is the clerk's to enter; the line is offered with the rate prefilled.
						lines.Add(new InvoiceLineItem { CallId = callId, RateCardItemId = item.RateCardItemId, Description = $"{item.Name} — call {callLabel}", Quantity = (RateCardItemTypes)item.ItemType == RateCardItemTypes.FixedFee ? 1 : 0, UnitRate = item.Rate, Amount = (RateCardItemTypes)item.ItemType == RateCardItemTypes.FixedFee ? RoundMoney(item.Rate) : 0, Taxable = item.Taxable });
						break;
				}
			}

			var sort = 0;
			foreach (var line in lines) line.SortOrder = sort++;
			return lines;
		}

		public async Task<Invoice> AddCallToInvoiceAsync(string invoiceId, int callId, string rateCardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await RequireDraftAsync(invoiceId, departmentId);
			var generated = await GenerateLineItemsFromCallAsync(callId, rateCardId, departmentId);
			var current = (await _lineItems.GetByInvoiceIdAsync(invoiceId, departmentId))?.ToList() ?? new List<InvoiceLineItem>();
			current.AddRange(generated);
			return await SaveInvoiceLineItemsAsync(invoice.InvoiceId, departmentId, current, userId, ipAddress, userAgent, cancellationToken);
		}

		public async Task<Invoice> RecalculateTotalsAsync(string invoiceId, int departmentId, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			var lines = (await _lineItems.GetByInvoiceIdAsync(invoiceId, departmentId))?.ToList() ?? new List<InvoiceLineItem>();
			var profile = await _profiles.GetByIdForDepartmentAsync(invoice.CustomerBillingProfileId, departmentId);

			ComputeTotals(invoice, lines, profile);
			await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
			invoice.LineItems = lines;
			invoice.Payments = (await _payments.GetByInvoiceIdAsync(invoiceId, departmentId))?.ToList() ?? new List<InvoicePayment>();
			return invoice;
		}

		/// <summary>SubTotal → discount → tax → Total, then AmountPaid from effective payments (plan B4; decisions 12, 14, 23). Pure and unit-tested.</summary>
		public static void ComputeTotals(Invoice invoice, IReadOnlyCollection<InvoiceLineItem> lines, CustomerBillingProfile profile, IReadOnlyCollection<InvoicePayment> payments = null)
		{
			var subTotal = RoundMoney(lines.Sum(x => x.Amount));
			var taxableBase = RoundMoney(lines.Where(x => x.Taxable).Sum(x => x.Amount));
			var discount = invoice.DiscountPercent.HasValue && invoice.DiscountPercent.Value > 0 ? RoundMoney(subTotal * invoice.DiscountPercent.Value / 100m) : 0m;
			if (discount > subTotal) discount = subTotal;

			// The discount is applied pre-tax and pro rata to the taxable base.
			var taxableAfterDiscount = subTotal > 0 ? RoundMoney(taxableBase - discount * (taxableBase / subTotal)) : 0m;
			var tax = 0m;
			string componentsJson = null;
			if (profile != null && !profile.TaxExempt)
			{
				var components = ParseTaxComponents(profile.TaxComponentsJson);
				if (components.Count > 0)
				{
					foreach (var component in components)
					{
						component.Amount = RoundMoney(taxableAfterDiscount * component.RatePercent / 100m);
						tax += component.Amount.Value;
					}
					componentsJson = JsonConvert.SerializeObject(components);
				}
				else if (profile.TaxRate.HasValue && profile.TaxRate.Value > 0)
				{
					tax = RoundMoney(taxableAfterDiscount * profile.TaxRate.Value / 100m);
				}
			}

			invoice.SubTotal = subTotal;
			invoice.DiscountAmount = discount;
			invoice.TaxAmount = RoundMoney(tax);
			invoice.TaxComponentsJson = componentsJson;
			invoice.Total = RoundMoney(subTotal - discount + tax);
			if (payments != null)
				invoice.AmountPaid = RoundMoney(payments.Sum(x => x.EffectiveAmount));
		}

		public async Task<Invoice> MarkSentAsync(string invoiceId, int departmentId, string sentToEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			if (invoice.Status != (int)InvoiceStatus.Draft) throw new InvalidOperationException("invoicing_invoice_not_draft");
			var lines = (await _lineItems.GetByInvoiceIdAsync(invoiceId, departmentId))?.ToList() ?? new List<InvoiceLineItem>();
			if (lines.Count == 0) throw new InvalidOperationException("invoicing_invoice_has_no_lines");

			var profile = await _profiles.GetByIdForDepartmentAsync(invoice.CustomerBillingProfileId, departmentId);
			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceSent, ipAddress, userAgent);
			audit.Before = Snapshot(invoice);

			var now = DateTime.UtcNow;
			ComputeTotals(invoice, lines, profile);
			invoice.IssuedOn ??= now;
			invoice.DueOn ??= invoice.IssuedOn.Value.AddDays(await TermsNetDaysAsync(invoice, profile));
			invoice.SentOn = now;
			invoice.SentToEmail = string.IsNullOrWhiteSpace(sentToEmail) ? profile?.BillingEmail : sentToEmail.Trim();
			invoice.Status = (int)InvoiceStatus.Sent;
			invoice.EditedOn = now;
			invoice.EditedByUserId = userId;

			await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
			audit.After = Snapshot(invoice);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await PublishAsync(invoice, WorkflowTriggerEventType.InvoiceSent, oldStatus: (int)InvoiceStatus.Draft, cancellationToken: cancellationToken);
			await LoadChildrenAsync(invoice);
			return invoice;
		}

		public async Task<Invoice> VoidInvoiceAsync(string invoiceId, int departmentId, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			if (invoice.Status == (int)InvoiceStatus.Void) return await GetInvoiceByIdAsync(invoiceId, departmentId);
			if (invoice.Status == (int)InvoiceStatus.Paid) throw new InvalidOperationException("invoicing_invoice_paid_cannot_void");
			if (invoice.AmountPaid > 0) throw new InvalidOperationException("invoicing_invoice_has_payments_cannot_void");

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.InvoiceVoided, ipAddress, userAgent);
			audit.Before = Snapshot(invoice);
			var oldStatus = invoice.Status;
			var now = DateTime.UtcNow;
			invoice.Status = (int)InvoiceStatus.Void;
			invoice.VoidedOn = now;
			invoice.VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
			invoice.EditedOn = now;
			invoice.EditedByUserId = userId;
			await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
			audit.After = Snapshot(invoice);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await PublishAsync(invoice, WorkflowTriggerEventType.InvoiceVoided, oldStatus: oldStatus, cancellationToken: cancellationToken);
			await LoadChildrenAsync(invoice);
			return invoice;
		}

		public async Task<InvoicePayment> RecordPaymentAsync(InvoicePayment payment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (payment == null) throw new ArgumentNullException(nameof(payment));
			if (payment.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(payment));
			if (!Enum.IsDefined(typeof(InvoicePaymentMethods), payment.Method)) throw new ArgumentException("Unknown payment method.", nameof(payment));

			var invoice = await _invoices.GetByIdForDepartmentAsync(payment.InvoiceId, payment.DepartmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			if (invoice.Status is (int)InvoiceStatus.Draft or (int)InvoiceStatus.Void) throw new InvalidOperationException("invoicing_invoice_not_payable");

			// Online payments are idempotent on the provider's transaction id (Phase B2: the webhook may replay). A
			// transaction already recorded under another department is a conflict, never this department's payment.
			var online = !string.IsNullOrWhiteSpace(payment.GatewayTransactionId) && payment.Provider.HasValue;
			if (online)
			{
				var duplicate = await FindGatewayDuplicateAsync(payment);
				if (duplicate != null) return duplicate;
			}

			var audit = NewAuditEvent(payment.DepartmentId, userId, AuditLogTypes.InvoicePaymentRecorded, ipAddress, userAgent);
			audit.Before = Snapshot(invoice);

			var now = DateTime.UtcNow;
			payment.InvoicePaymentId = null;
			payment.Amount = RoundMoney(payment.Amount);
			payment.Status = (int)InvoicePaymentStatuses.Succeeded;
			payment.RefundedAmount = 0;
			payment.RecordedByUserId = userId;
			payment.AddedOn = now;
			if (payment.PaidOn == default) payment.PaidOn = now;
			InvoicePayment saved;
			try
			{
				saved = await _payments.SaveOrUpdateAsync(payment, cancellationToken);
			}
			catch (Exception ex) when (online && IsUniqueViolation(ex))
			{
				// Two deliveries passed the lookup together; the unique index (M0212) let exactly one insert through.
				var winner = await FindGatewayDuplicateAsync(payment);
				if (winner == null) throw;
				return winner;
			}

			var oldStatus = invoice.Status;
			await ApplyPaymentStateAsync(invoice, userId, now, cancellationToken);
			audit.After = Snapshot(invoice);
			_eventAggregator.SendMessage<AuditEvent>(audit);

			await PublishAsync(invoice, WorkflowTriggerEventType.InvoicePaymentRecorded, saved, oldStatus, cancellationToken);
			if (invoice.Status == (int)InvoiceStatus.Paid)
				await PublishAsync(invoice, WorkflowTriggerEventType.InvoicePaid, saved, oldStatus, cancellationToken);

			return saved;
		}

		public async Task<InvoicePayment> ApplyPaymentRefundAsync(string invoicePaymentId, int departmentId, decimal refundedAmount, bool disputeLost, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var payment = await _payments.GetByIdForDepartmentAsync(invoicePaymentId, departmentId);
			if (payment == null) throw new InvalidOperationException("invoicing_payment_not_found");
			if (refundedAmount < 0) throw new ArgumentException("Refunded amount cannot be negative.", nameof(refundedAmount));
			var invoice = await _invoices.GetByIdForDepartmentAsync(payment.InvoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");

			var audit = NewAuditEvent(departmentId, userId, disputeLost ? AuditLogTypes.InvoicePaymentDisputed : AuditLogTypes.InvoicePaymentRefunded, ipAddress, userAgent);
			audit.Before = Snapshot(invoice);

			payment.RefundedAmount = Math.Min(RoundMoney(refundedAmount), payment.Amount);
			payment.Status = disputeLost ? (int)InvoicePaymentStatuses.DisputeLost
				: payment.RefundedAmount >= payment.Amount ? (int)InvoicePaymentStatuses.Refunded
				: payment.RefundedAmount > 0 ? (int)InvoicePaymentStatuses.PartiallyRefunded
				: (int)InvoicePaymentStatuses.Succeeded;
			await _payments.SaveOrUpdateAsync(payment, cancellationToken);

			var oldStatus = invoice.Status;
			await ApplyPaymentStateAsync(invoice, userId, DateTime.UtcNow, cancellationToken);
			audit.After = Snapshot(invoice);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await PublishAsync(invoice, disputeLost ? WorkflowTriggerEventType.InvoicePaymentDisputed : WorkflowTriggerEventType.InvoicePaymentRefunded, payment, oldStatus, cancellationToken);
			return payment;
		}

		public async Task<int> MarkOverdueInvoicesAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			var candidates = (await _invoices.GetOverdueCandidatesAsync(asOfUtc, 1000))?.ToList() ?? new List<Invoice>();
			var count = 0;
			var enabledByDepartment = new Dictionary<int, bool>();
			foreach (var invoice in candidates)
			{
				if (invoice.Status is not ((int)InvoiceStatus.Sent or (int)InvoiceStatus.PartiallyPaid)) continue;
				if (departmentEnabled != null)
				{
					// The worker re-checks the Business Operations entitlement once per department per pass (plan decision 42).
					if (!enabledByDepartment.TryGetValue(invoice.DepartmentId, out var enabled))
						enabledByDepartment[invoice.DepartmentId] = enabled = await departmentEnabled(invoice.DepartmentId);
					if (!enabled) continue;
				}
				var audit = NewAuditEvent(invoice.DepartmentId, null, AuditLogTypes.InvoiceUpdated, null, null);
				audit.Before = Snapshot(invoice);
				var oldStatus = invoice.Status;
				invoice.Status = (int)InvoiceStatus.Overdue;
				invoice.EditedOn = asOfUtc;
				await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
				if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
				audit.After = Snapshot(invoice);
				_eventAggregator.SendMessage<AuditEvent>(audit);
				await PublishAsync(invoice, WorkflowTriggerEventType.InvoiceOverdue, oldStatus: oldStatus, cancellationToken: cancellationToken);
				count++;
			}
			return count;
		}

		public async Task<InvoiceAgingReport> GetAccountsReceivableAgingAsync(int departmentId, DateTime? asOfUtc = null)
		{
			var asOf = asOfUtc ?? DateTime.UtcNow;
			var rows = (await _invoices.GetAgingDataAsync(departmentId))?.Where(x => x.Balance > 0).ToList() ?? new List<InvoiceAgingRow>();
			var report = new InvoiceAgingReport { AsOfUtc = asOf };
			var buckets = new[]
			{
				new InvoiceAgingBucket { Label = "Current" }, new InvoiceAgingBucket { Label = "1-30" }, new InvoiceAgingBucket { Label = "31-60" },
				new InvoiceAgingBucket { Label = "61-90" }, new InvoiceAgingBucket { Label = "90+" }
			};
			foreach (var row in rows)
			{
				var daysPastDue = row.DueOn.HasValue ? (int)Math.Floor((asOf - row.DueOn.Value).TotalDays) : 0;
				var bucket = daysPastDue <= 0 ? buckets[0] : daysPastDue <= 30 ? buckets[1] : daysPastDue <= 60 ? buckets[2] : daysPastDue <= 90 ? buckets[3] : buckets[4];
				bucket.Invoices.Add(row);
				bucket.Count++;
				bucket.Balance = RoundMoney(bucket.Balance + row.Balance);
				InvoiceAgingReport.Accumulate(bucket.BalancesByCurrency, row.Currency, RoundMoney(row.Balance));
				InvoiceAgingReport.Accumulate(report.BalancesByCurrency, row.Currency, RoundMoney(row.Balance));
			}
			report.Buckets.AddRange(buckets);
			report.TotalCount = rows.Count;
			report.TotalBalance = RoundMoney(rows.Sum(x => x.Balance));
			return report;
		}

		#endregion

		#region Department billing identity

		public async Task<DepartmentBillingIdentity> GetDepartmentBillingIdentityAsync(int departmentId)
		{
			return await _identities.GetByDepartmentIdAsync(departmentId) ?? new DepartmentBillingIdentity { DepartmentId = departmentId, AllowedPaymentMethodsCsv = "card", PayLinkExpiryDays = 30, ShowPayOnlineOnDocuments = true };
		}

		public async Task<DepartmentBillingIdentity> SaveDepartmentBillingIdentityAsync(DepartmentBillingIdentity identity, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			if (identity.PayLinkExpiryDays < 1 || identity.PayLinkExpiryDays > 365) throw new ArgumentException("PayLinkExpiryDays must be between 1 and 365.", nameof(identity));
			var existing = await _identities.GetByDepartmentIdAsync(identity.DepartmentId);
			var audit = NewAuditEvent(identity.DepartmentId, userId, AuditLogTypes.DepartmentBillingIdentityChanged, ipAddress, userAgent);
			if (existing != null) audit.Before = Snapshot(existing);
			identity.UpdatedOn = DateTime.UtcNow;
			identity.UpdatedByUserId = userId;
			var saved = await _identities.UpsertAsync(identity, cancellationToken);
			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return saved;
		}

		#endregion

		#region Helpers

		private async Task LoadChildrenAsync(Invoice invoice)
		{
			invoice.LineItems = (await _lineItems.GetByInvoiceIdAsync(invoice.InvoiceId, invoice.DepartmentId))?.ToList() ?? new List<InvoiceLineItem>();
			invoice.Payments = (await _payments.GetByInvoiceIdAsync(invoice.InvoiceId, invoice.DepartmentId))?.ToList() ?? new List<InvoicePayment>();
		}

		/// <summary>Runs <paramref name="action"/> in the scope's transaction, joining one the caller already opened (tests pass no unit of work and run unwrapped).</summary>
		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null)
				return await action();

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

		/// <summary>The payment already recorded for this provider transaction in this department; a match owned by another department is a conflict.</summary>
		private async Task<InvoicePayment> FindGatewayDuplicateAsync(InvoicePayment payment)
		{
			var duplicate = await _payments.GetByGatewayTransactionIdAsync(payment.Provider.Value, payment.GatewayTransactionId);
			if (duplicate == null) return null;
			if (duplicate.DepartmentId != payment.DepartmentId) throw new InvalidOperationException("invoicing_payment_conflict");
			return duplicate;
		}

		/// <summary>PostgreSQL 23505 or SQL Server 2601/2627: the only insert failure the idempotent payment path absorbs.</summary>
		private static bool IsUniqueViolation(Exception ex)
		{
			if (ex is Npgsql.PostgresException postgres)
				return postgres.SqlState == "23505";
			if (ex is Microsoft.Data.SqlClient.SqlException sql)
				return sql.Number == 2601 || sql.Number == 2627;
			return false;
		}

		private async Task<int> TermsNetDaysAsync(Invoice invoice, CustomerBillingProfile profile)
		{
			if (_serviceContracts != null && !string.IsNullOrWhiteSpace(invoice.ServiceContractId))
			{
				var contract = await _serviceContracts.GetByIdForDepartmentAsync(invoice.ServiceContractId, invoice.DepartmentId);
				if (contract?.TermsNetDays > 0) return contract.TermsNetDays.Value;
			}
			return profile?.TermsNetDays ?? 30;
		}

		private async Task<Invoice> RequireDraftAsync(string invoiceId, int departmentId)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			if (invoice.Status != (int)InvoiceStatus.Draft) throw new InvalidOperationException("invoicing_invoice_not_draft");
			return invoice;
		}

		/// <summary>Re-derives AmountPaid and the Sent / PartiallyPaid / Paid / Overdue status from the effective payments.</summary>
		private async Task ApplyPaymentStateAsync(Invoice invoice, string userId, DateTime now, CancellationToken cancellationToken)
		{
			var payments = (await _payments.GetByInvoiceIdAsync(invoice.InvoiceId, invoice.DepartmentId))?.ToList() ?? new List<InvoicePayment>();
			invoice.AmountPaid = RoundMoney(payments.Sum(x => x.EffectiveAmount));
			invoice.Status = DeriveStatus(invoice, now);
			invoice.PaidOn = invoice.Status == (int)InvoiceStatus.Paid ? (invoice.PaidOn ?? payments.Where(x => x.EffectiveAmount > 0).Select(x => (DateTime?)x.PaidOn).DefaultIfEmpty(now).Max()) : null;
			invoice.EditedOn = now;
			invoice.EditedByUserId = userId;
			await _invoices.SaveOrUpdateAsync(invoice, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectInvoiceAsync(invoice, cancellationToken);
		}

		/// <summary>Paid when the balance is settled; PartiallyPaid when something is paid; otherwise Overdue if past due, else Sent. Void and Draft never change here.</summary>
		public static int DeriveStatus(Invoice invoice, DateTime now)
		{
			if (invoice.Status is (int)InvoiceStatus.Void or (int)InvoiceStatus.Draft) return invoice.Status;
			if (invoice.Total > 0 && invoice.AmountPaid >= invoice.Total) return (int)InvoiceStatus.Paid;
			if (invoice.AmountPaid > 0) return (int)InvoiceStatus.PartiallyPaid;
			return invoice.DueOn.HasValue && invoice.DueOn.Value < now ? (int)InvoiceStatus.Overdue : (int)InvoiceStatus.Sent;
		}

		/// <summary>Minutes between the unit's first OnScene state on the call and its next state; falls back to the call window when the unit never reported OnScene (plan decision 9).</summary>
		public static int OnSceneMinutes(IReadOnlyList<UnitState> unitStates, DateTime callLoggedOn, DateTime fallbackEnd)
		{
			var ordered = unitStates.OrderBy(x => x.Timestamp).ToList();
			var onScene = ordered.FirstOrDefault(x => x.State == (int)UnitStateTypes.OnScene);
			DateTime start, end;
			if (onScene != null)
			{
				start = onScene.Timestamp;
				var next = ordered.FirstOrDefault(x => x.Timestamp > onScene.Timestamp && x.State != (int)UnitStateTypes.OnScene);
				end = next?.Timestamp ?? fallbackEnd;
			}
			else
			{
				start = callLoggedOn;
				end = fallbackEnd;
			}
			var minutes = (int)Math.Ceiling((end - start).TotalMinutes);
			return minutes < 0 ? 0 : minutes;
		}

		/// <summary>Billable hours after the item's minimum and rounding increment (plan decision 9).</summary>
		public static decimal ApplyRounding(int minutes, RateCardItem item)
		{
			var billable = Math.Max(minutes, item.MinimumMinutes ?? 0);
			if (item.RoundingMinutes.HasValue && item.RoundingMinutes.Value > 0)
				billable = (int)Math.Ceiling(billable / (decimal)item.RoundingMinutes.Value) * item.RoundingMinutes.Value;
			return Math.Round(billable / 60m, 4, MidpointRounding.AwayFromZero);
		}

		public static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

		private static string NormalizeCurrency(string currency) =>
			string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant().Substring(0, Math.Min(3, currency.Trim().Length));

		private static void ValidatePercent(decimal? value, string name)
		{
			if (value.HasValue && (value.Value < 0 || value.Value > 100)) throw new ArgumentException($"{name} must be between 0 and 100.", name);
		}

		private static void ValidateTaxComponents(string json)
		{
			var components = ParseTaxComponents(json);
			if (components.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.RatePercent < 0 || x.RatePercent > 100))
				throw new ArgumentException("Every tax component needs a name and a rate between 0 and 100.", nameof(json));
		}

		public static List<TaxComponent> ParseTaxComponents(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<TaxComponent>();
			try
			{
				return JsonConvert.DeserializeObject<List<TaxComponent>>(json)?.Where(x => x != null).ToList() ?? new List<TaxComponent>();
			}
			catch (JsonException)
			{
				throw new ArgumentException("TaxComponentsJson is not a valid component list.", nameof(json));
			}
		}

		/// <summary>Publishes the lifecycle trigger through the domain outbox (plan decision 22). Payload = identifiers, status, amounts, dates; the contact name reads REDACTED when the contact row is protected.</summary>
		private async Task PublishAsync(Invoice invoice, WorkflowTriggerEventType trigger, InvoicePayment payment = null, int? oldStatus = null, CancellationToken cancellationToken = default)
		{
			string contactName = null;
			try
			{
				var contact = await _contactsService.GetContactByIdAsync(invoice.ContactId);
				contactName = contact == null ? null : ProtectedDataEnvelope.SafeDisplay(string.IsNullOrWhiteSpace(contact.CompanyName) ? $"{contact.FirstName} {contact.LastName}".Trim() : contact.CompanyName);
			}
			catch (Exception ex) { Logging.LogException(ex, "Invoice workflow payload: contact name could not be read."); }

			var payUrl = await PayUrlAsync(invoice);
			try
			{
				await _outbox.EnqueueAsync(invoice.DepartmentId, InvoiceWorkflowPayload.Producer, new DomainEventEnvelope
				{
					EventName = trigger.ToString(),
					AggregateType = "Invoice",
					AggregateId = invoice.InvoiceId,
					AggregateVersion = 0,
					Trigger = trigger,
					OccurredOn = DateTime.UtcNow,
					CorrelationId = invoice.InvoiceId,
					Payload = new
					{
						invoice.InvoiceId, invoice.InvoiceNumber, invoice.Status, invoice.ContactId,
						ContactName = contactName,
						invoice.Currency, invoice.SubTotal, invoice.DiscountAmount, invoice.TaxAmount, invoice.Total, invoice.AmountPaid, invoice.Balance,
						invoice.IssuedOn, invoice.DueOn, invoice.SentOn, invoice.PaidOn,
						PaymentAmount = payment?.Amount, PaymentMethod = payment == null ? null : ((InvoicePaymentMethods)payment.Method).ToString(), PaymentId = payment?.InvoicePaymentId,
						OldStatus = oldStatus,
						PayUrl = payUrl
					}
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				// A failed publish must never undo a committed money transition; it is logged and visible in the outbox health.
				Logging.LogException(ex, $"Invoice {invoice.InvoiceId} {trigger} could not be published.");
			}
		}

		private static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			switch (clone)
			{
				case Invoice invoice: invoice.LineItems = null; invoice.Payments = null; break;
				case RateCard card: card.Items = null; break;
			}
			return clone.CloneJsonToString();
		}

		private static AuditEvent NewAuditEvent(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent)
		{
			return new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Successful = true,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				ServerName = Environment.MachineName
			};
		}

		#endregion
	}
}
