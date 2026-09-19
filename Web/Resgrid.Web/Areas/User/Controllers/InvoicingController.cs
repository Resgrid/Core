using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Invoicing;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Customer invoicing (Workforce &amp; Business Operations plan, Phase B, B6): invoices, rate cards, customer billing
	/// profiles, the accounts-receivable aging report and the department billing identity. The Invoicing.CustomerInvoicing
	/// flag and the Business Operations module switch gate the whole controller (404 when either is off, like the v4 API).
	/// A lapsed add-on leaves every page readable and refuses each mutation with 402 (plan decision 42), so a department
	/// that stops paying keeps its records. Every write posts a form with the antiforgery token; JSON answers carry a
	/// <c>message</c> the page shows through toastr.
	/// </summary>
	[Area("User"), Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), RequestSizeLimit(1024 * 1024)]
	public sealed class InvoicingController : SecureBaseController
	{
		private const int PageSize = 50;
		private static readonly string[] Currencies = { "USD", "EUR", "GBP", "CAD", "AUD", "NZD" };

		private readonly IInvoicingService _invoicing;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IFeatureToggleService _flags;
		private readonly IContactsService _contacts;
		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IAddressService _addresses;
		private readonly IProtectedReadService _protectedRead;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> _strings;

		private bool _canWrite;

		public InvoicingController(IInvoicingService invoicing, IBusinessOperationsAccessService access, IFeatureToggleService flags, IContactsService contacts,
			ICallsService calls, IUnitsService units, IAddressService addresses, IProtectedReadService protectedRead,
			IStringLocalizer<Resgrid.Localization.Areas.User.Invoicing.Invoicing> strings)
		{
			_invoicing = invoicing;
			_access = access;
			_flags = flags;
			_contacts = contacts;
			_calls = calls;
			_units = units;
			_addresses = addresses;
			_protectedRead = protectedRead;
			_strings = strings;
		}

		#region Gating

		public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
		{
			Response.Headers["Cache-Control"] = "no-store";

			if (!await _flags.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, DepartmentId) || !SettingsHelper.IsBusinessOperationsEnabled())
			{
				context.Result = NotFound();
				return;
			}

			_canWrite = await _access.CanUseInvoicingAsync(DepartmentId);
			ViewBag.InvoicingCanWrite = _canWrite;

			if (HttpMethods.IsPost(Request.Method) && !_canWrite)
			{
				context.Result = Refused(402, "AddonRequired");
				return;
			}

			await next();
		}

		/// <summary>402 / 400 / 404 answers: JSON for AJAX callers, a redirect back with a message otherwise.</summary>
		private IActionResult Refused(int statusCode, string key, string redirectAction = null, object routeValues = null)
		{
			if (IsAjax())
				return StatusCode(statusCode, new { message = _strings[key].Value, code = key });
			TempData["InvoicingMessage"] = _strings[key].Value;
			return redirectAction == null ? RedirectToAction(nameof(Index)) : RedirectToAction(redirectAction, routeValues);
		}

		private bool IsAjax() => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string UserAgent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		private T Page<T>(T view) where T : InvoicingPageView
		{
			view.CanWrite = _canWrite;
			view.CanManage = ClaimsAuthorizationHelper.CanManageInvoicing();
			return view;
		}

		/// <summary>Display names for the contacts referenced, resolved for Advanced Data Protection (REDACTED without a grant).</summary>
		private async Task<Dictionary<string, string>> ContactNamesAsync(IEnumerable<string> contactIds)
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var contacts = new List<Contact>();
			foreach (var id in contactIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var contact = await _contacts.GetContactByIdAsync(id);
				if (contact != null && contact.DepartmentId == DepartmentId)
					contacts.Add(contact);
			}
			if (contacts.Count > 0)
				await _protectedRead.ResolveContactsForReadAsync(DepartmentId, contacts, null, UserId);
			foreach (var contact in contacts)
				names[contact.ContactId] = contact.Name;
			return names;
		}

		private async Task<Contact> ContactAsync(string contactId)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return null;
			var contact = await _contacts.GetContactByIdAsync(contactId);
			return contact != null && contact.DepartmentId == DepartmentId ? contact : null;
		}

		private static bool IsInvoicingError(Exception ex) => ex is InvalidOperationException && ex.Message.StartsWith("invoicing_", StringComparison.Ordinal);

		/// <summary>Service error codes (<c>invoicing_*</c>) map to resource keys of the same name so the page shows a translated message.</summary>
		private string ErrorText(Exception ex)
		{
			var key = ex.Message.Split(':')[0].Trim();
			var localized = _strings[key];
			return localized.ResourceNotFound ? _strings["SaveFailed"].Value : localized.Value;
		}

		#endregion

		#region Invoices

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public async Task<IActionResult> Index(int? status = null, string contactId = null, int page = 0)
		{
			var filter = new InvoiceListFilter { Statuses = status.HasValue ? new[] { status.Value } : null, ContactId = contactId, Skip = Math.Max(0, page) * PageSize, Take = PageSize };
			var invoices = await _invoicing.GetInvoicesForDepartmentAsync(DepartmentId, filter);
			var aging = await _invoicing.GetAccountsReceivableAgingAsync(DepartmentId);
			var overdue = aging.Buckets.Where(b => b.Label != "Current").ToList();

			var model = Page(new InvoiceIndexView
			{
				Invoices = invoices,
				Status = status,
				ContactId = contactId,
				Page = Math.Max(0, page),
				PageSize = PageSize,
				TotalCount = await _invoicing.CountInvoicesForDepartmentAsync(DepartmentId, filter),
				OutstandingBalance = aging.TotalBalance,
				OverdueBalance = overdue.Sum(b => b.Balance),
				OverdueCount = overdue.Sum(b => b.Count),
				ContactNames = await ContactNamesAsync(invoices.Select(x => x.ContactId))
			});
			ViewBag.Message = TempData["InvoicingMessage"];
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Create)]
		public async Task<IActionResult> New(string contactId = null)
		{
			if (!_canWrite)
				return Refused(402, "AddonRequired");
			return View(await BuildNewAsync(contactId));
		}

		private async Task<InvoiceNewView> BuildNewAsync(string contactId)
		{
			var contacts = (await _contacts.GetAllContactsForDepartmentAsync(DepartmentId) ?? new List<Contact>()).Where(x => !x.IsDeleted).ToList();
			await _protectedRead.ResolveContactsForReadAsync(DepartmentId, contacts, null, UserId);
			var profiles = (await _invoicing.GetBillingProfilesForDepartmentAsync(DepartmentId) ?? new List<CustomerBillingProfile>()).Where(x => x.Active).Select(x => x.ContactId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var customers = contacts.Select(c => new InvoiceCustomerChoice { ContactId = c.ContactId, Name = c.Name, HasBillingProfile = profiles.Contains(c.ContactId) })
				.OrderByDescending(c => c.HasBillingProfile).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
			return Page(new InvoiceNewView { ContactId = contactId, Customers = customers });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Create)]
		public async Task<IActionResult> New(string contactId, string currency, CancellationToken cancellationToken)
		{
			var contact = await ContactAsync(contactId);
			if (contact == null)
			{
				var model = await BuildNewAsync(null);
				model.Message = _strings["CustomerRequired"].Value;
				return View(model);
			}
			if (!Currencies.Contains(currency ?? string.Empty))
				currency = null;

			try
			{
				var invoice = await _invoicing.CreateDraftInvoiceAsync(DepartmentId, contact.ContactId, UserId, Ip, UserAgent, currency, cancellationToken);
				return RedirectToAction(nameof(Edit), new { id = invoice.InvoiceId });
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				var model = await BuildNewAsync(contactId);
				model.Message = ErrorText(ex);
				return View(model);
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> Edit(string id)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			if (invoice.Status != (int)InvoiceStatus.Draft)
				return RedirectToAction(nameof(View), new { id });
			if (!_canWrite)
				return Refused(402, "AddonRequired", nameof(View), new { id });

			var effective = await _invoicing.GetEffectiveRateCardForContactAsync(invoice.ContactId, DepartmentId);
			var model = Page(new InvoiceEditView
			{
				Invoice = invoice,
				Profile = await _invoicing.GetBillingProfileByContactIdAsync(invoice.ContactId, DepartmentId),
				RateCards = (await _invoicing.GetRateCardsForDepartmentAsync(DepartmentId) ?? new List<RateCard>()).Where(x => x.Active).ToList(),
				DefaultRateCardId = effective?.RateCardId,
				ContactNames = await ContactNamesAsync(new[] { invoice.ContactId })
			});
			model.Message = TempData["InvoicingMessage"] as string;
			return View(model);
		}

		/// <summary>Saves a draft's header and replaces its line list in one step; answers JSON for the editor.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> Save(InvoiceHeaderInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.InvoiceId))
				return Refused(400, "InvalidInput");
			var invoice = await _invoicing.GetInvoiceByIdAsync(input.InvoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();

			List<InvoiceLineInput> lines;
			try { lines = string.IsNullOrWhiteSpace(input.LinesJson) ? new List<InvoiceLineInput>() : JsonConvert.DeserializeObject<List<InvoiceLineInput>>(input.LinesJson) ?? new List<InvoiceLineInput>(); }
			catch (JsonException) { return Refused(400, "InvalidInput"); }
			if (lines.Count > 500 || lines.Any(l => string.IsNullOrWhiteSpace(l.Description) || l.Quantity < 0 || l.UnitRate < 0))
				return Refused(400, "InvalidInput");

			try
			{
				invoice.DueOn = input.DueOn;
				invoice.DiscountPercent = input.DiscountPercent;
				invoice.Notes = input.Notes;
				invoice.TermsText = input.TermsText;
				if (Currencies.Contains(input.Currency ?? string.Empty))
					invoice.Currency = input.Currency;
				await _invoicing.SaveInvoiceAsync(invoice, UserId, Ip, UserAgent, cancellationToken);

				var order = 0;
				var lineItems = lines.Select(l => new InvoiceLineItem
				{
					InvoiceLineItemId = string.IsNullOrWhiteSpace(l.InvoiceLineItemId) ? null : l.InvoiceLineItemId,
					InvoiceId = invoice.InvoiceId,
					DepartmentId = DepartmentId,
					CallId = l.CallId,
					RateCardItemId = string.IsNullOrWhiteSpace(l.RateCardItemId) ? null : l.RateCardItemId,
					Description = l.Description.Trim(),
					Quantity = l.Quantity,
					UnitRate = l.UnitRate,
					Taxable = l.Taxable,
					SortOrder = order++
				}).ToList();
				var saved = await _invoicing.SaveInvoiceLineItemsAsync(invoice.InvoiceId, DepartmentId, lineItems, UserId, Ip, UserAgent, cancellationToken);
				return Json(new { id = saved.InvoiceId, subTotal = saved.SubTotal, discountAmount = saved.DiscountAmount, taxAmount = saved.TaxAmount, total = saved.Total, message = _strings["Saved"].Value });
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				return StatusCode(400, new { message = ErrorText(ex) });
			}
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public new async Task<IActionResult> View(string id)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();

			var model = Page(new InvoiceDetailView
			{
				Invoice = invoice,
				Profile = await _invoicing.GetBillingProfileByContactIdAsync(invoice.ContactId, DepartmentId),
				RenderedHtml = await _invoicing.RenderInvoiceHtmlAsync(id, DepartmentId),
				ContactNames = await ContactNamesAsync(new[] { invoice.ContactId })
			});
			model.Message = TempData["InvoicingMessage"] as string;
			return View("View", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public async Task<IActionResult> Pdf(string id)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			var pdf = await _invoicing.GetInvoicePdfAsync(id, DepartmentId);
			if (pdf == null || pdf.Length == 0)
				return Refused(503, "PdfUnavailable", nameof(View), new { id });
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			return File(pdf, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> Send(string id, string toEmail, CancellationToken cancellationToken)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			try
			{
				await _invoicing.SendInvoiceAsync(id, DepartmentId, string.IsNullOrWhiteSpace(toEmail) ? null : toEmail.Trim(), UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["InvoiceSentMessage"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(View), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> MarkSent(string id, CancellationToken cancellationToken)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			try
			{
				await _invoicing.MarkSentAsync(id, DepartmentId, null, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["InvoiceMarkedSent"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(View), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Delete)]
		public async Task<IActionResult> Void(string id, string reason, CancellationToken cancellationToken)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			try
			{
				await _invoicing.VoidInvoiceAsync(id, DepartmentId, reason, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["InvoiceVoidedMessage"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(View), new { id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> RecordPayment(RecordPaymentInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.InvoiceId))
				return Refused(400, "InvalidInput");
			var invoice = await _invoicing.GetInvoiceByIdAsync(input.InvoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();
			// Online payments only ever arrive through the provider path (Phase B2); the form cannot claim one.
			if (input.Amount <= 0 || input.Method == (int)InvoicePaymentMethods.Online || !Enum.IsDefined(typeof(InvoicePaymentMethods), input.Method))
				return Refused(400, "InvalidInput", nameof(View), new { id = input.InvoiceId });

			try
			{
				await _invoicing.RecordPaymentAsync(new InvoicePayment
				{
					InvoiceId = invoice.InvoiceId,
					DepartmentId = DepartmentId,
					Amount = input.Amount,
					Method = input.Method,
					Reference = input.Reference,
					Notes = input.Notes,
					PaidOn = input.PaidOn ?? DateTime.UtcNow
				}, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["PaymentRecorded"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(View), new { id = input.InvoiceId });
		}

		/// <summary>Calls logged against the invoice's customer, newest first, for the "Add call" dialog.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> CallsForInvoice(string id)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			var calls = await _calls.GetCallsByContactIdAsync(invoice.ContactId, DepartmentId) ?? new List<Call>();
			var invoiced = (invoice.LineItems ?? new List<InvoiceLineItem>()).Where(x => x.CallId.HasValue).Select(x => x.CallId.Value).ToHashSet();
			var rows = calls.Where(c => !c.IsDeleted).OrderByDescending(c => c.LoggedOn).Take(200)
				.Select(c => new InvoiceCallChoice { CallId = c.CallId, Number = c.Number, Name = c.Name, LoggedOn = c.LoggedOn, AlreadyInvoiced = invoiced.Contains(c.CallId) }).ToList();
			return Json(rows);
		}

		/// <summary>The draft lines a rate card produces for a call; nothing is saved until the editor posts Save.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> PreviewCallLines(string id, int callId, string rateCardId)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(id, DepartmentId);
			if (invoice == null)
				return NotFound();
			var call = await _calls.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();
			try
			{
				var lines = await _invoicing.GenerateLineItemsFromCallAsync(callId, rateCardId, DepartmentId);
				return Json(lines.Select(l => new { callId = l.CallId, rateCardItemId = l.RateCardItemId, description = l.Description, quantity = l.Quantity, unitRate = l.UnitRate, taxable = l.Taxable }));
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				return StatusCode(400, new { message = ErrorText(ex) });
			}
		}

		#endregion

		#region Rate cards

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public async Task<IActionResult> RateCards()
		{
			var model = Page(new RateCardsView { RateCards = await _invoicing.GetRateCardsForDepartmentAsync(DepartmentId) ?? new List<RateCard>() });
			ViewBag.Message = TempData["InvoicingMessage"];
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> EditRateCard(string id = null)
		{
			var model = Page(new RateCardEditView());
			if (!string.IsNullOrWhiteSpace(id))
			{
				var card = await _invoicing.GetRateCardByIdAsync(id, DepartmentId, includeInactiveItems: true);
				if (card == null)
					return NotFound();
				model.RateCard = card;
				model.Items = (card.Items ?? new List<RateCardItem>()).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();
			}
			else if (!_canWrite)
				return Refused(402, "AddonRequired", nameof(RateCards));

			model.UnitTypes = (await _units.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>()).Select(x => x.Type).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList();
			model.Message = TempData["InvoicingMessage"] as string;
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> SaveRateCard(RateCardInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Name))
				return Refused(400, "NameRequired", nameof(EditRateCard), new { id = input?.RateCardId });

			RateCard card;
			if (string.IsNullOrWhiteSpace(input.RateCardId))
				card = new RateCard { DepartmentId = DepartmentId };
			else
			{
				card = await _invoicing.GetRateCardByIdAsync(input.RateCardId, DepartmentId);
				if (card == null)
					return NotFound();
			}
			card.Name = input.Name.Trim();
			card.Description = input.Description;
			card.IsDefault = input.IsDefault;
			card.Active = input.Active;

			try
			{
				var saved = await _invoicing.SaveRateCardAsync(card, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["Saved"].Value;
				return RedirectToAction(nameof(EditRateCard), new { id = saved.RateCardId });
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
				return RedirectToAction(nameof(EditRateCard), new { id = input.RateCardId });
			}
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Delete)]
		public async Task<IActionResult> DeleteRateCard(string id, CancellationToken cancellationToken)
		{
			try
			{
				if (!await _invoicing.DeleteRateCardAsync(id, DepartmentId, UserId, Ip, UserAgent, cancellationToken))
					return NotFound();
				TempData["InvoicingMessage"] = _strings["RateCardDeleted"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(RateCards));
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> SaveRateCardItem(RateCardItemInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.RateCardId) || string.IsNullOrWhiteSpace(input.Name) || input.Rate < 0 || !Enum.IsDefined(typeof(RateCardItemTypes), input.ItemType))
				return Refused(400, "InvalidInput", nameof(EditRateCard), new { id = input?.RateCardId });
			var card = await _invoicing.GetRateCardByIdAsync(input.RateCardId, DepartmentId, includeInactiveItems: true);
			if (card == null)
				return NotFound();

			var item = string.IsNullOrWhiteSpace(input.RateCardItemId) ? null : (card.Items ?? new List<RateCardItem>()).FirstOrDefault(x => x.RateCardItemId == input.RateCardItemId);
			if (item == null && !string.IsNullOrWhiteSpace(input.RateCardItemId))
				return NotFound();
			item ??= new RateCardItem { RateCardId = card.RateCardId, DepartmentId = DepartmentId };

			item.ItemType = input.ItemType;
			item.Name = input.Name.Trim();
			item.Description = input.Description;
			item.Rate = input.Rate;
			item.UnitLabel = input.UnitLabel;
			item.MinimumMinutes = input.MinimumMinutes;
			item.RoundingMinutes = input.RoundingMinutes;
			item.MinimumCharge = input.MinimumCharge;
			item.UnitTypeFilter = string.IsNullOrWhiteSpace(input.UnitTypeFilter) ? null : input.UnitTypeFilter;
			item.Taxable = input.Taxable;
			item.SortOrder = input.SortOrder;
			item.Active = input.Active;

			try
			{
				await _invoicing.SaveRateCardItemAsync(item, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingMessage"] = _strings["Saved"].Value;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(EditRateCard), new { id = card.RateCardId });
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> DeleteRateCardItem(string rateCardId, string id, CancellationToken cancellationToken)
		{
			try
			{
				if (!await _invoicing.DeleteRateCardItemAsync(id, DepartmentId, UserId, Ip, UserAgent, cancellationToken))
					return NotFound();
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(EditRateCard), new { id = rateCardId });
		}

		#endregion

		#region Aging

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public async Task<IActionResult> Aging()
		{
			var report = await _invoicing.GetAccountsReceivableAgingAsync(DepartmentId);
			var model = Page(new InvoiceAgingView
			{
				Report = report,
				ContactNames = await ContactNamesAsync(report.Buckets.SelectMany(b => b.Invoices).Select(x => x.ContactId))
			});
			return View(model);
		}

		#endregion

		#region Department billing identity

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> Settings()
		{
			var model = Page(new BillingSettingsView());
			model.Identity = await _invoicing.GetDepartmentBillingIdentityAsync(DepartmentId) ?? new DepartmentBillingIdentity { DepartmentId = DepartmentId };
			if (model.Identity.RemitToAddressId.HasValue)
				model.RemitTo = await _addresses.GetAddressByIdAsync(model.Identity.RemitToAddressId.Value) ?? new Address();
			model.OnlinePaymentsClusterEnabled = PaymentConnectConfig.Enabled && await _flags.IsEnabledAsync(FeatureFlagKeys.PaymentsStripeConnect, DepartmentId);
			model.OnlinePaymentsFlagEnabled = await _flags.IsEnabledAsync(FeatureFlagKeys.OnlinePayments, DepartmentId);
			model.Message = TempData["InvoicingMessage"] as string;
			model.SaveSuccess = TempData["InvoicingSaved"] as bool? ?? false;
			return View(model);
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> Settings(BillingSettingsInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return Refused(400, "InvalidInput", nameof(Settings));
			var identity = await _invoicing.GetDepartmentBillingIdentityAsync(DepartmentId) ?? new DepartmentBillingIdentity { DepartmentId = DepartmentId };

			var hasAddress = !string.IsNullOrWhiteSpace(input.Address1) || !string.IsNullOrWhiteSpace(input.City) || !string.IsNullOrWhiteSpace(input.PostalCode);
			if (hasAddress)
			{
				var address = identity.RemitToAddressId.HasValue ? await _addresses.GetAddressByIdAsync(identity.RemitToAddressId.Value) ?? new Address() : new Address();
				address.Address1 = input.Address1;
				address.City = input.City;
				address.State = input.State;
				address.PostalCode = input.PostalCode;
				address.Country = input.Country;
				var saved = await _addresses.SaveAddressAsync(address, cancellationToken);
				identity.RemitToAddressId = saved?.AddressId;
			}

			identity.LegalBusinessName = input.LegalBusinessName;
			identity.TaxRegistrationNumber = input.TaxRegistrationNumber;
			identity.SecondaryTaxRegistrationNumber = input.SecondaryTaxRegistrationNumber;
			identity.SamUei = input.SamUei;
			identity.CageCode = input.CageCode;
			identity.WorkersCompAccountNumber = input.WorkersCompAccountNumber;
			identity.InvoiceFooterText = input.InvoiceFooterText;
			identity.PayLinkExpiryDays = Math.Clamp(input.PayLinkExpiryDays, 1, 365);
			identity.ShowPayOnlineOnDocuments = input.ShowPayOnlineOnDocuments;

			try
			{
				await _invoicing.SaveDepartmentBillingIdentityAsync(identity, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingSaved"] = true;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(Settings));
		}

		#endregion

		#region Customer billing profiles

		[HttpGet]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		public async Task<IActionResult> BillingProfile(string contactId)
		{
			var contact = await ContactAsync(contactId);
			if (contact == null)
				return NotFound();
			var model = await BuildBillingProfileAsync(contact);
			model.Message = TempData["InvoicingMessage"] as string;
			model.SaveSuccess = TempData["InvoicingSaved"] as bool? ?? false;
			return View(model);
		}

		private async Task<BillingProfileView> BuildBillingProfileAsync(Contact contact)
		{
			var protectedRead = await _protectedRead.ResolveContactsForReadAsync(DepartmentId, new List<Contact> { contact }, null, UserId);
			var model = Page(new BillingProfileView { Contact = contact, IsProtectedContact = protectedRead.IsProtected });
			model.ContactNames[contact.ContactId] = contact.Name;
			model.Profile = await _invoicing.GetBillingProfileByContactIdAsync(contact.ContactId, DepartmentId) ?? new CustomerBillingProfile { DepartmentId = DepartmentId, ContactId = contact.ContactId, BillingEmail = contact.Email };
			if (model.Profile.BillingAddressId.HasValue)
				model.BillingAddress = await _addresses.GetAddressByIdAsync(model.Profile.BillingAddressId.Value) ?? new Address();
			model.TaxComponents = ParseTaxComponents(model.Profile.TaxComponentsJson);
			model.RateCards = (await _invoicing.GetRateCardsForDepartmentAsync(DepartmentId) ?? new List<RateCard>()).Where(x => x.Active).ToList();
			model.Invoices = (await _invoicing.GetInvoicesByContactIdAsync(contact.ContactId, DepartmentId) ?? new List<Invoice>()).OrderByDescending(x => x.InvoiceNumber).ToList();
			return model;
		}

		[HttpPost, ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		public async Task<IActionResult> BillingProfile(BillingProfileInput input, CancellationToken cancellationToken)
		{
			var contact = await ContactAsync(input?.ContactId);
			if (contact == null)
				return NotFound();

			var profile = await _invoicing.GetBillingProfileByContactIdAsync(contact.ContactId, DepartmentId) ?? new CustomerBillingProfile { DepartmentId = DepartmentId, ContactId = contact.ContactId };
			if (input.TermsNetDays < 0 || input.TermsNetDays > 365 || (input.TaxRate.HasValue && (input.TaxRate < 0 || input.TaxRate > 100)) || (input.DefaultDiscountPercent.HasValue && (input.DefaultDiscountPercent < 0 || input.DefaultDiscountPercent > 100)))
				return Refused(400, "InvalidInput", nameof(BillingProfile), new { contactId = contact.ContactId });

			if (!input.UseContactMailingAddress && (!string.IsNullOrWhiteSpace(input.Address1) || !string.IsNullOrWhiteSpace(input.City) || !string.IsNullOrWhiteSpace(input.PostalCode)))
			{
				var address = profile.BillingAddressId.HasValue ? await _addresses.GetAddressByIdAsync(profile.BillingAddressId.Value) ?? new Address() : new Address();
				address.Address1 = input.Address1;
				address.City = input.City;
				address.State = input.State;
				address.PostalCode = input.PostalCode;
				address.Country = input.Country;
				var saved = await _addresses.SaveAddressAsync(address, cancellationToken);
				profile.BillingAddressId = saved?.AddressId;
			}

			var components = new List<TaxComponent>();
			for (var i = 0; i < (input.TaxName?.Length ?? 0) && i < 3; i++)
			{
				if (string.IsNullOrWhiteSpace(input.TaxName[i]))
					continue;
				var percent = i < (input.TaxPercent?.Length ?? 0) ? input.TaxPercent[i] : null;
				if (!percent.HasValue || percent < 0 || percent > 100)
					return Refused(400, "InvalidInput", nameof(BillingProfile), new { contactId = contact.ContactId });
				components.Add(new TaxComponent { Name = input.TaxName[i].Trim(), RatePercent = percent.Value, RegistrationNumber = i < (input.TaxRegistration?.Length ?? 0) ? input.TaxRegistration[i] : null });
			}

			profile.BillingEmail = string.IsNullOrWhiteSpace(input.BillingEmail) ? null : input.BillingEmail.Trim();
			profile.UseContactMailingAddress = input.UseContactMailingAddress;
			profile.TermsNetDays = input.TermsNetDays;
			profile.TaxExempt = input.TaxExempt;
			profile.TaxRate = input.TaxRate;
			profile.TaxComponentsJson = components.Count == 0 ? null : JsonConvert.SerializeObject(components);
			profile.DefaultRateCardId = string.IsNullOrWhiteSpace(input.DefaultRateCardId) ? null : input.DefaultRateCardId;
			profile.DefaultDiscountPercent = input.DefaultDiscountPercent;
			profile.PurchaseOrderRequired = input.PurchaseOrderRequired;
			profile.Notes = input.Notes;
			profile.Active = input.Active;

			try
			{
				await _invoicing.SaveBillingProfileAsync(profile, UserId, Ip, UserAgent, cancellationToken);
				TempData["InvoicingSaved"] = true;
			}
			catch (Exception ex) when (IsInvoicingError(ex))
			{
				TempData["InvoicingMessage"] = ErrorText(ex);
			}
			return RedirectToAction(nameof(BillingProfile), new { contactId = contact.ContactId });
		}

		private static List<TaxComponent> ParseTaxComponents(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<TaxComponent>();
			try { return JsonConvert.DeserializeObject<List<TaxComponent>>(json) ?? new List<TaxComponent>(); }
			catch (JsonException) { return new List<TaxComponent>(); }
		}

		#endregion
	}
}
