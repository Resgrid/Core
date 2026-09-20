using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Invoicing;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Customer invoices (Workforce &amp; Business Operations plan, Phase B, B7). Read-mostly: list, detail, PDF, and
	/// manual payment recording. Every endpoint except GetAccess answers 404 while the Invoicing.CustomerInvoicing
	/// flag is off or the department holds no Business Operations add-on, so a client cannot tell a disabled
	/// module from an absent one. Rate cards, billing profiles and drafting stay web-only in v1.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class InvoicesController : V4AuthenticatedApiControllerbase
	{
		private readonly IInvoicingService _invoicing;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IFeatureToggleService _flags;
		private readonly IContactsService _contacts;
		private readonly IInvoicePaymentsService _payments;

		public InvoicesController(IInvoicingService invoicing, IBusinessOperationsAccessService access, IFeatureToggleService flags, IContactsService contacts, IInvoicePaymentsService payments)
		{
			_invoicing = invoicing;
			_access = access;
			_flags = flags;
			_contacts = contacts;
			_payments = payments;
		}

		/// <summary>Whether invoicing is available to this department and what the add-on costs. Always answers.</summary>
		[HttpGet("GetAccess")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<InvoicingAccessResult>> GetAccess()
		{
			var flag = await _flags.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, DepartmentId);
			var addon = flag && await _access.HasActiveAddonAsync(DepartmentId);
			var result = new InvoicingAccessResult
			{
				Data = new InvoicingAccessData
				{
					FlagEnabled = flag,
					AddonActive = addon,
					InvoicingEnabled = flag && await _access.CanUseInvoicingAsync(DepartmentId),
					CanManage = ClaimsAuthorizationHelper.CanManageInvoicing()
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Invoices for the department, newest number first. Optional status filter (InvoiceStatus value) and contact filter.</summary>
		[HttpGet("GetInvoices")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<InvoicesResult>> GetInvoices(int? status = null, string contactId = null, int skip = 0, int take = 50)
		{
			if (!await EnabledAsync())
				return NotFound();

			var filter = new InvoiceListFilter { Statuses = status.HasValue ? new[] { status.Value } : null, ContactId = contactId, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 200) };
			var invoices = await _invoicing.GetInvoicesForDepartmentAsync(DepartmentId, filter);
			var names = await ContactNamesAsync(invoices.Select(x => x.ContactId));

			var result = new InvoicesResult { TotalCount = await _invoicing.CountInvoicesForDepartmentAsync(DepartmentId, filter) };
			result.Data = invoices.Select(x => Convert(x, names, includeChildren: false)).ToList();
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>One invoice with its line items and payments.</summary>
		[HttpGet("GetInvoice")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<InvoiceResult>> GetInvoice(string invoiceId)
		{
			if (!await EnabledAsync())
				return NotFound();

			var invoice = await _invoicing.GetInvoiceByIdAsync(invoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();

			var names = await ContactNamesAsync(new[] { invoice.ContactId });
			var result = new InvoiceResult { Data = Convert(invoice, names, includeChildren: true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The invoice as a PDF (application/pdf).</summary>
		[HttpGet("GetInvoicePdf")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> GetInvoicePdf(string invoiceId)
		{
			if (!await EnabledAsync())
				return NotFound();

			var invoice = await _invoicing.GetInvoiceByIdAsync(invoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();

			var pdf = await _invoicing.GetInvoicePdfAsync(invoiceId, DepartmentId);
			if (pdf == null || pdf.Length == 0)
				return StatusCode((int)HttpStatusCode.ServiceUnavailable);

			return File(pdf, "application/pdf", $"invoice-{invoice.InvoiceNumber}.pdf");
		}

		/// <summary>Record a manual payment (check, cash, ACH, external card, other). Online payments arrive through the provider path only.</summary>
		[HttpPost("RecordPayment")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<InvoiceResult>> RecordPayment([FromBody] RecordPaymentInput input)
		{
			if (!await EnabledAsync())
				return NotFound();
			if (input == null || string.IsNullOrWhiteSpace(input.InvoiceId) || input.Amount <= 0)
				return BadRequest();
			if (input.Method == (int)InvoicePaymentMethods.Online || !Enum.IsDefined(typeof(InvoicePaymentMethods), input.Method))
				return BadRequest();

			var invoice = await _invoicing.GetInvoiceByIdAsync(input.InvoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();

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
				}, UserId, Request.HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers["User-Agent"].ToString());
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("invoicing_", StringComparison.Ordinal))
			{
				var failed = new InvoiceResult { PageSize = 0, Status = ResponseHelper.Failure };
				ResponseHelper.PopulateV4ResponseData(failed);
				return BadRequest(failed);
			}

			return await GetInvoice(invoice.InvoiceId);
		}

		/// <summary>Phase B2: whether online payment collection is offered to this department and through which connection (masked).</summary>
		[HttpGet("GetOnlinePaymentsStatus")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<OnlinePaymentsStatusResult>> GetOnlinePaymentsStatus()
		{
			if (!await EnabledAsync())
				return NotFound();

			var status = await _payments.GetStatusAsync(DepartmentId);
			var result = new OnlinePaymentsStatusResult
			{
				Data = new OnlinePaymentsStatusData
				{
					AvailableInCluster = status.AvailableInCluster,
					FlagEnabled = status.FlagEnabled,
					EnabledByDepartment = status.EnabledByDepartment,
					CanCollect = status.CanCollect,
					BlockedReason = status.BlockedReason,
					AllowedPaymentMethods = status.AllowedPaymentMethods.ToArray(),
					Connection = status.Connection == null ? null : ConvertConnection(status.Connection)
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Phase B2: the department's payment-provider connections, masked. No OAuth on a phone (plan B2.5).</summary>
		[HttpGet("GetPaymentConnections")]
		[Authorize(Policy = ResgridResources.Invoicing_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<PaymentConnectionsResult>> GetPaymentConnections()
		{
			if (!await EnabledAsync())
				return NotFound();

			var result = new PaymentConnectionsResult { Data = (await _payments.GetConnectionsAsync(DepartmentId)).Select(ConvertConnection).ToList() };
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Phase B2: opens (or reuses) the invoice's online payment request and returns the shareable pay-page link. 400 with Status=Failure and the payments_* code when not offered.</summary>
		[HttpPost("CreatePaymentLink")]
		[Authorize(Policy = ResgridResources.Invoicing_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<PaymentLinkResult>> CreatePaymentLink([FromBody] CreatePaymentLinkInput input)
		{
			if (!await EnabledAsync())
				return NotFound();
			if (input == null || string.IsNullOrWhiteSpace(input.InvoiceId))
				return BadRequest();

			var invoice = await _invoicing.GetInvoiceByIdAsync(input.InvoiceId, DepartmentId);
			if (invoice == null)
				return NotFound();

			try
			{
				var request = await _payments.CreatePaymentRequestAsync(invoice.InvoiceId, DepartmentId, (int)PaymentRequestSources.Api, UserId,
					Request.HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers["User-Agent"].ToString());
				var payUrl = await _payments.BuildPayPageUrlAsync(invoice.InvoiceId, DepartmentId);
				var result = new PaymentLinkResult
				{
					Data = new PaymentLinkData
					{
						InvoiceId = invoice.InvoiceId,
						PayUrl = payUrl,
						InvoicePaymentRequestId = request.InvoicePaymentRequestId,
						RequestStatus = request.Status,
						RequestStatusName = ((PaymentRequestStatuses)request.Status).ToString(),
						Amount = request.Amount,
						Currency = request.Currency,
						ExpiresOn = request.ExpiresOn
					},
					PageSize = 1,
					Status = ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("payments_", StringComparison.Ordinal) || ex.Message.StartsWith("invoicing_", StringComparison.Ordinal))
			{
				var failed = new PaymentLinkResult { PageSize = 0, Status = ResponseHelper.Failure, Data = new PaymentLinkData { InvoiceId = invoice.InvoiceId } };
				ResponseHelper.PopulateV4ResponseData(failed);
				Response.Headers["X-Resgrid-Reason"] = ex.Message;
				return BadRequest(failed);
			}
		}

		private static PaymentConnectionData ConvertConnection(DepartmentPaymentConnection c)
		{
			return new PaymentConnectionData
			{
				DepartmentPaymentConnectionId = c.DepartmentPaymentConnectionId,
				Provider = c.Provider,
				ProviderName = Enum.IsDefined(typeof(PaymentProviders), c.Provider) ? ((PaymentProviders)c.Provider).ToString() : null,
				Status = c.Status,
				StatusName = Enum.IsDefined(typeof(PaymentConnectionStatuses), c.Status) ? ((PaymentConnectionStatuses)c.Status).ToString() : null,
				Environment = c.Environment,
				MaskedAccountId = c.ExternalAccountId,
				DisplayName = c.DisplayName,
				Country = c.Country,
				DefaultCurrency = c.DefaultCurrency,
				IsDefault = c.IsDefault,
				ConnectedOn = c.ConnectedOn,
				LastVerifiedOn = c.LastVerifiedOn
			};
		}

		private async Task<bool> EnabledAsync()
		{
			if (!await _flags.IsEnabledAsync(FeatureFlagKeys.CustomerInvoicing, DepartmentId))
				return false;
			return await _access.CanUseInvoicingAsync(DepartmentId);
		}

		private async Task<Dictionary<string, string>> ContactNamesAsync(IEnumerable<string> contactIds)
		{
			// One query for the page's contacts rather than a lookup per invoice.
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in await _contacts.GetContactsByIdsAsync(DepartmentId, contactIds))
				names[pair.Key] = pair.Value.Name;
			return names;
		}

		private static InvoiceResultData Convert(Invoice invoice, IReadOnlyDictionary<string, string> names, bool includeChildren)
		{
			var data = new InvoiceResultData
			{
				InvoiceId = invoice.InvoiceId,
				InvoiceNumber = invoice.InvoiceNumber,
				ContactId = invoice.ContactId,
				ContactName = names.TryGetValue(invoice.ContactId ?? string.Empty, out var name) ? name : null,
				Status = invoice.Status,
				StatusName = ((InvoiceStatus)invoice.Status).ToString(),
				Currency = invoice.Currency,
				SubTotal = invoice.SubTotal,
				DiscountPercent = invoice.DiscountPercent,
				DiscountAmount = invoice.DiscountAmount,
				TaxAmount = invoice.TaxAmount,
				Total = invoice.Total,
				AmountPaid = invoice.AmountPaid,
				Balance = invoice.Balance,
				IssuedOn = invoice.IssuedOn,
				DueOn = invoice.DueOn,
				SentOn = invoice.SentOn,
				PaidOn = invoice.PaidOn,
				VoidedOn = invoice.VoidedOn,
				Notes = invoice.Notes,
				TermsText = invoice.TermsText,
				AddedOn = invoice.AddedOn,
				UpdatedOn = invoice.EditedOn ?? invoice.AddedOn
			};

			if (includeChildren)
			{
				data.LineItems = (invoice.LineItems ?? new List<InvoiceLineItem>()).OrderBy(x => x.SortOrder).Select(x => new InvoiceLineItemData
				{
					InvoiceLineItemId = x.InvoiceLineItemId, CallId = x.CallId, Description = x.Description, Quantity = x.Quantity, UnitRate = x.UnitRate, Amount = x.Amount, Taxable = x.Taxable, SortOrder = x.SortOrder
				}).ToList();
				data.Payments = (invoice.Payments ?? new List<InvoicePayment>()).OrderBy(x => x.PaidOn).Select(x => new InvoicePaymentData
				{
					InvoicePaymentId = x.InvoicePaymentId, Amount = x.Amount, Method = x.Method, MethodName = ((InvoicePaymentMethods)x.Method).ToString(), Status = x.Status,
					RefundedAmount = x.RefundedAmount, Reference = x.Reference, PaymentMethodSummary = x.PaymentMethodSummary, PaidOn = x.PaidOn, AddedOn = x.AddedOn
				}).ToList();
			}

			return data;
		}
	}
}
