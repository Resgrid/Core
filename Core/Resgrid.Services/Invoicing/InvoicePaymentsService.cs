using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
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
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Phase B2 online payments (Workforce &amp; Business Operations plan, B2.4). Connect hand-off, pay links, the
	/// webhook truth path, worker 29 passes 2–4 and the webhook health read. Resgrid never holds funds: a request is
	/// a direct charge on the department's own account and only a verified provider event (or the reconciliation
	/// read-back of one) records money, always through <see cref="IInvoicingService.RecordPaymentAsync"/>.
	/// </summary>
	public class InvoicePaymentsService : IInvoicePaymentsService
	{
		/// <summary>Connect events the webhook receiver consumes (plan B2.1). The endpoint probe requires all of them.</summary>
		public static readonly IReadOnlyCollection<string> StripeConnectRequiredEvents = new[]
		{
			"checkout.session.completed",
			"checkout.session.async_payment_succeeded",
			"checkout.session.async_payment_failed",
			"checkout.session.expired",
			"payment_intent.succeeded",
			"charge.refunded",
			"charge.dispute.created",
			"charge.dispute.closed",
			"account.application.deauthorized",
			"account.updated"
		};

		/// <summary>Payment method types v1 can pass to a hosted request (plan B2.1).</summary>
		public static readonly IReadOnlyList<string> SupportedPaymentMethods = new[] { "card", "us_bank_account" };

		/// <summary>Currencies v1 settles (two-decimal only).</summary>
		public static readonly IReadOnlyList<string> SupportedCurrencies = new[] { "USD", "EUR", "GBP", "CAD", "AUD", "NZD" };

		public const string SystemUserId = "system";
		public const string PayTokenKind = "p";
		public const string StateKind = "s";
		public const string LastReconcileCacheKey = "PAYMENTS_LAST_RECONCILE_ON";
		private const string StateCachePrefix = "PAYCONNECT_STATE_";
		private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);
		private static readonly TimeSpan EndpointProbeCacheWindow = TimeSpan.FromMinutes(15);
		private static readonly object EndpointProbeLock = new object();
		private static DateTime _endpointProbeCheckedOnUtc = DateTime.MinValue;
		private static bool? _endpointProbeResult;
		private static string _endpointProbeUrl;

		private readonly IFeatureToggleService _featureToggleService;
		private readonly IStripeConnectEndpointProbe _endpointProbe;
		private readonly IPaymentConnectProvider _provider;
		private readonly IDepartmentPaymentConnectionRepository _connections;
		private readonly IInvoicePaymentRequestRepository _requests;
		private readonly IPaymentProviderEventRepository _events;
		private readonly IInvoiceRepository _invoices;
		private readonly ICustomerBillingProfileRepository _profiles;
		private readonly IInvoicePaymentRepository _payments;
		private readonly IDepartmentBillingIdentityRepository _identities;
		private readonly IInvoicingService _invoicing;
		private readonly IBusinessOperationsAccessService _access;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEmailService _emailService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;

		public InvoicePaymentsService(IFeatureToggleService featureToggleService, IStripeConnectEndpointProbe endpointProbe,
			IPaymentConnectProvider provider, IDepartmentPaymentConnectionRepository connections, IInvoicePaymentRequestRepository requests,
			IPaymentProviderEventRepository events, IInvoiceRepository invoices, ICustomerBillingProfileRepository profiles, IInvoicePaymentRepository payments,
			IDepartmentBillingIdentityRepository identities, IInvoicingService invoicing, IBusinessOperationsAccessService access,
			IDepartmentsService departmentsService, IEmailService emailService, ICacheProvider cacheProvider, IEventAggregator eventAggregator)
		{
			_featureToggleService = featureToggleService;
			_endpointProbe = endpointProbe;
			_provider = provider;
			_connections = connections;
			_requests = requests;
			_events = events;
			_invoices = invoices;
			_profiles = profiles;
			_payments = payments;
			_identities = identities;
			_invoicing = invoicing;
			_access = access;
			_departmentsService = departmentsService;
			_emailService = emailService;
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
		}

		#region Availability and status

		public async Task<bool> IsAvailableInClusterAsync()
		{
			return Config.PaymentConnectConfig.Enabled && await IsClusterSwitchOnAsync();
		}

		public async Task<OnlinePaymentsStatus> GetStatusAsync(int departmentId)
		{
			var status = new OnlinePaymentsStatus { AvailableInCluster = await IsAvailableInClusterAsync() };
			status.FlagEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.OnlinePayments, departmentId);

			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			status.EnabledByDepartment = identity?.OnlinePaymentsEnabled == true;
			status.ShowPayOnlineOnDocuments = identity?.ShowPayOnlineOnDocuments ?? true;
			status.PayLinkExpiryDays = identity?.PayLinkExpiryDays ?? Config.PaymentConnectConfig.PayLinkTokenTtlDays;
			status.AllowedPaymentMethods = ParseMethods(identity?.AllowedPaymentMethodsCsv);

			var connection = await ResolveDefaultConnectionAsync(departmentId, identity);
			if (connection != null)
			{
				status.Connection = Masked(connection);
				status.Capabilities = ParseCapabilities(connection.CapabilitiesJson);
			}

			if (!status.AvailableInCluster) status.BlockedReason = "payments_not_available_in_region";
			else if (!status.FlagEnabled) status.BlockedReason = "payments_flag_off";
			else if (!await _access.CanUseInvoicingAsync(departmentId)) status.BlockedReason = "payments_addon_required";
			else if (!status.EnabledByDepartment) status.BlockedReason = "payments_not_enabled_for_department";
			else if (connection == null) status.BlockedReason = "payments_no_connection";
			else if (!connection.IsUsable || (status.Capabilities.TryGetValue("charges_enabled", out var charges) && !charges)) status.BlockedReason = "payments_connection_not_ready";

			status.CanCollect = status.BlockedReason == null;
			return status;
		}

		private async Task<DepartmentPaymentConnection> ResolveDefaultConnectionAsync(int departmentId, DepartmentBillingIdentity identity)
		{
			if (!string.IsNullOrWhiteSpace(identity?.DefaultPaymentConnectionId))
			{
				var pinned = await _connections.GetByIdForDepartmentAsync(identity.DefaultPaymentConnectionId, departmentId);
				if (pinned != null) return pinned;
			}
			return await _connections.GetDefaultForDepartmentAsync(departmentId);
		}

		#endregion

		#region Connect

		public async Task<PaymentConnectBegin> BeginConnectAsync(int departmentId, int provider, string userId, string ipAddress, string userAgent)
		{
			if (!await IsAvailableInClusterAsync()) throw new InvalidOperationException("payments_not_available_in_region");
			if (provider != _provider.Provider) throw new InvalidOperationException("payments_provider_unavailable");
			if (!_provider.IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");
			if (string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)) throw new InvalidOperationException("payments_provider_unavailable");

			var nonce = Guid.NewGuid().ToString("N");
			var expires = DateTime.UtcNow.Add(StateLifetime);
			var state = Encode($"{StateKind}|{departmentId}|{userId}|{provider}|{nonce}|{expires:O}");
			if (!await _cacheProvider.SetStringAsync(StateCachePrefix + Hash(nonce), "1", StateLifetime))
				throw new InvalidOperationException("payments_provider_unavailable");

			return new PaymentConnectBegin
			{
				State = state,
				ExpiresOn = expires,
				AuthorizeUrl = _provider.BuildConnectUrl(state, BuildConnectRedirectUrl(provider))
			};
		}

		/// <summary>The OAuth callback lives on the authenticated web host (the administrator's session), so it is built from ResgridBaseUrl.</summary>
		public static string BuildConnectRedirectUrl(int provider)
		{
			var name = Enum.IsDefined(typeof(PaymentProviders), provider) ? ((PaymentProviders)provider).ToString().ToLowerInvariant() : provider.ToString(CultureInfo.InvariantCulture);
			return $"{(Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')}/User/Invoicing/PaymentConnectCallback/{name}";
		}

		public async Task<DepartmentPaymentConnection> CompleteConnectAsync(int departmentId, int provider, IReadOnlyDictionary<string, string> callbackParameters, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (!await IsAvailableInClusterAsync()) throw new InvalidOperationException("payments_not_available_in_region");
			if (provider != _provider.Provider) throw new InvalidOperationException("payments_provider_unavailable");
			if (callbackParameters == null) throw new InvalidOperationException("payments_state_invalid");

			// The provider's own error (the administrator declined) surfaces as a refusal, never as a redirect.
			if (callbackParameters.TryGetValue("error", out var providerError) && !string.IsNullOrWhiteSpace(providerError))
				throw new InvalidOperationException("payments_connect_declined");

			if (!callbackParameters.TryGetValue("state", out var state) || !TryDecodeState(state, out var stateDepartmentId, out var stateUserId, out var stateProvider, out var nonce, out var expires))
				throw new InvalidOperationException("payments_state_invalid");
			if (stateDepartmentId != departmentId || !string.Equals(stateUserId, userId, StringComparison.Ordinal) || stateProvider != provider || expires < DateTime.UtcNow)
				throw new InvalidOperationException("payments_state_invalid");

			// Single use: the nonce is consumed before the code is exchanged, so a replayed callback cannot connect twice.
			var stateKey = StateCachePrefix + Hash(nonce);
			if (string.IsNullOrEmpty(await _cacheProvider.GetStringAsync(stateKey)))
				throw new InvalidOperationException("payments_state_invalid");
			await _cacheProvider.RemoveAsync(stateKey);

			var facts = await _provider.CompleteConnectAsync(callbackParameters, cancellationToken);
			if (facts == null || string.IsNullOrWhiteSpace(facts.ExternalAccountId)) throw new InvalidOperationException("payments_connect_failed");

			var now = DateTime.UtcNow;
			var environment = facts.LiveMode ? (int)PaymentEnvironments.Live : (int)PaymentEnvironments.Sandbox;
			var existing = (await _connections.GetForDepartmentAsync(departmentId) ?? Enumerable.Empty<DepartmentPaymentConnection>())
				.FirstOrDefault(c => c.Provider == provider && c.Environment == environment);
			var isNew = existing == null;
			var connection = existing ?? new DepartmentPaymentConnection
			{
				DepartmentId = departmentId,
				Provider = provider,
				Environment = environment,
				ConnectedOn = now,
				ConnectedByUserId = userId,
				AddedOn = now,
				AddedByUserId = userId
			};

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.PaymentConnectionConnected, ipAddress, userAgent);
			if (!isNew) audit.Before = Snapshot(connection);

			connection.ExternalAccountId = facts.ExternalAccountId;
			connection.DisplayName = facts.DisplayName;
			connection.Country = facts.Country;
			connection.DefaultCurrency = facts.DefaultCurrency;
			connection.CapabilitiesJson = JsonConvert.SerializeObject(facts.Capabilities ?? new Dictionary<string, bool>());
			connection.ScopesCsv = facts.ScopesCsv;
			connection.Status = facts.ChargesEnabled ? (int)PaymentConnectionStatuses.Connected : (int)PaymentConnectionStatuses.ActionRequired;
			connection.LastVerifiedOn = now;
			connection.LastError = null;
			connection.DisconnectedOn = null;
			connection.ConnectedOn = now;
			connection.ConnectedByUserId = userId;
			connection.EditedOn = isNew ? null : now;
			connection.EditedByUserId = isNew ? null : userId;
			connection.AccessTokenCiphertext = string.IsNullOrEmpty(facts.AccessToken) ? null : Protect(facts.AccessToken);
			connection.RefreshTokenCiphertext = string.IsNullOrEmpty(facts.RefreshToken) ? null : Protect(facts.RefreshToken);
			connection.TokenExpiresOn = facts.TokenExpiresOn;

			var others = (await _connections.GetForDepartmentAsync(departmentId) ?? Enumerable.Empty<DepartmentPaymentConnection>()).Where(c => c.DepartmentPaymentConnectionId != connection.DepartmentPaymentConnectionId && c.IsUsable).ToList();
			connection.IsDefault = isNew ? others.Count == 0 : connection.IsDefault || others.Count == 0;

			var saved = await _connections.SaveOrUpdateAsync(connection, cancellationToken);
			if (saved.IsDefault) await _connections.ClearDefaultAsync(departmentId, saved.DepartmentPaymentConnectionId, cancellationToken);

			audit.After = Snapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await NotifyAdminsAsync(departmentId, $"Online payments: the department's {ProviderName(provider)} account ({saved.MaskedExternalAccountId}) was connected to Resgrid by {await UserDisplayAsync(departmentId, userId)}.");
			return Masked(saved);
		}

		public async Task<bool> DisconnectAsync(string departmentPaymentConnectionId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var connection = await _connections.GetByIdForDepartmentAsync(departmentPaymentConnectionId, departmentId);
			if (connection == null) return false;

			var audit = NewAuditEvent(departmentId, userId, AuditLogTypes.PaymentConnectionDisconnected, ipAddress, userAgent);
			audit.Before = Snapshot(connection);

			if (connection.Status is (int)PaymentConnectionStatuses.Connected or (int)PaymentConnectionStatuses.ActionRequired or (int)PaymentConnectionStatuses.Pending)
			{
				try { await _provider.DisconnectAsync(connection, cancellationToken); }
				catch (InvalidOperationException ex) when (ex.Message == "payments_disabled") { /* the cluster switch is off; local state still closes */ }
			}

			await CloseConnectionAsync(connection, (int)PaymentConnectionStatuses.Disconnected, userId, cancellationToken);
			audit.After = Snapshot(connection);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return true;
		}

		public async Task<IReadOnlyList<DepartmentPaymentConnection>> GetConnectionsAsync(int departmentId)
		{
			return (await _connections.GetForDepartmentAsync(departmentId) ?? Enumerable.Empty<DepartmentPaymentConnection>()).Select(Masked).ToList();
		}

		private async Task HandleConnectionRevokedAsync(DepartmentPaymentConnection connection, string reason, CancellationToken cancellationToken)
		{
			var audit = NewAuditEvent(connection.DepartmentId, SystemUserId, AuditLogTypes.PaymentConnectionRevoked, null, null);
			audit.Before = Snapshot(connection);
			connection.LastError = reason;
			await CloseConnectionAsync(connection, (int)PaymentConnectionStatuses.Revoked, SystemUserId, cancellationToken);
			audit.After = Snapshot(connection);
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await NotifyAdminsAsync(connection.DepartmentId, $"Online payments: access to the department's {ProviderName(connection.Provider)} account ({connection.MaskedExternalAccountId}) was revoked. New pay links are no longer offered until the account is connected again.");
		}

		private async Task CloseConnectionAsync(DepartmentPaymentConnection connection, int status, string userId, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			connection.Status = status;
			connection.IsDefault = false;
			connection.DisconnectedOn = now;
			connection.EditedOn = now;
			connection.EditedByUserId = userId;
			await _connections.SaveOrUpdateAsync(connection, cancellationToken);

			var identity = await _identities.GetByDepartmentIdAsync(connection.DepartmentId);
			if (identity != null && identity.DefaultPaymentConnectionId == connection.DepartmentPaymentConnectionId)
			{
				identity.DefaultPaymentConnectionId = null;
				identity.UpdatedOn = now;
				identity.UpdatedByUserId = userId;
				await _identities.SaveOrUpdateAsync(identity, cancellationToken);
			}

			foreach (var request in await _requests.GetOpenByConnectionAsync(connection.DepartmentPaymentConnectionId) ?? Enumerable.Empty<InvoicePaymentRequest>())
			{
				try { await _provider.CancelPaymentRequestAsync(connection, request.ExternalReference, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, $"Payment request {request.InvoicePaymentRequestId} could not be cancelled at the provider."); }
				await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Cancelled, "connection closed", cancellationToken);
			}
		}

		#endregion

		#region Pay links

		public async Task<InvoicePaymentRequest> CreatePaymentRequestAsync(string invoiceId, int departmentId, int source, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");

			var status = await GetStatusAsync(departmentId);
			if (!status.CanCollect) throw new InvalidOperationException(status.BlockedReason);

			if (invoice.Status is (int)InvoiceStatus.Draft or (int)InvoiceStatus.Void) throw new InvalidOperationException("payments_invoice_not_payable");
			if (invoice.Status == (int)InvoiceStatus.Paid) throw new InvalidOperationException("payments_invoice_paid");
			if (invoice.Balance <= 0) throw new InvalidOperationException("payments_zero_balance");
			if (!SupportedCurrencies.Contains((invoice.Currency ?? string.Empty).ToUpperInvariant())) throw new InvalidOperationException("payments_currency_mismatch");

			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			var connection = await ResolveDefaultConnectionAsync(departmentId, identity);
			if (connection == null || !connection.IsUsable) throw new InvalidOperationException("payments_connection_not_ready");
			if (!string.IsNullOrWhiteSpace(connection.DefaultCurrency) && !string.Equals(connection.DefaultCurrency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("payments_currency_mismatch");

			var now = DateTime.UtcNow;
			var open = await _requests.GetOpenByInvoiceIdAsync(invoiceId, departmentId);
			if (open != null)
			{
				if (open.ExpiresOn > now && !string.IsNullOrWhiteSpace(open.HostedUrl) && open.Amount == RoundMoney(invoice.Balance))
					return open;
				// Stale, amount changed or never opened: close it so the filtered unique index admits a fresh attempt.
				if (!string.IsNullOrWhiteSpace(open.ExternalReference))
				{
					try { await _provider.CancelPaymentRequestAsync(connection, open.ExternalReference, cancellationToken); }
					catch (Exception ex) { Logging.LogException(ex, $"Payment request {open.InvoicePaymentRequestId} could not be cancelled at the provider."); }
				}
				await TransitionRequestAsync(open, open.ExpiresOn <= now ? (int)PaymentRequestStatuses.Expired : (int)PaymentRequestStatuses.Cancelled, "superseded", cancellationToken);
			}

			var profile = await _profiles.GetByIdForDepartmentAsync(invoice.CustomerBillingProfileId, departmentId);

			// The row is allocated first so the provider receives the request id as its reference (plan B2.1 client_reference_id).
			var request = new InvoicePaymentRequest
			{
				InvoiceId = invoice.InvoiceId,
				DepartmentId = departmentId,
				DepartmentPaymentConnectionId = connection.DepartmentPaymentConnectionId,
				Provider = connection.Provider,
				Amount = RoundMoney(invoice.Balance),
				Currency = invoice.Currency.ToUpperInvariant(),
				Status = (int)PaymentRequestStatuses.Created,
				ExpiresOn = now.AddHours(24),
				Source = source,
				CreatedByUserId = userId,
				AddedOn = now,
				UpdatedOn = now
			};
			request = await _requests.SaveOrUpdateAsync(request, cancellationToken);

			var payToken = CreatePayPageToken(departmentId, invoice.InvoiceId, now.AddDays(Math.Max(1, status.PayLinkExpiryDays)));
			var payBase = PayPageBase(payToken);
			var methods = status.AllowedPaymentMethods.Where(m => status.Capabilities.Count == 0 || m != "us_bank_account" || (status.Capabilities.TryGetValue("us_bank_account_ach_payments", out var ach) && ach)).ToList();
			try
			{
				var creation = await _provider.CreatePaymentRequestAsync(connection, new PaymentRequestSpec
				{
					ExternalAccountId = connection.ExternalAccountId,
					PaymentRequestId = request.InvoicePaymentRequestId,
					DepartmentId = departmentId,
					InvoiceId = invoice.InvoiceId,
					InvoiceNumber = invoice.InvoiceNumber,
					Amount = request.Amount,
					Currency = request.Currency,
					CustomerEmail = profile?.BillingEmail,
					PaymentMethodTypes = methods,
					SuccessUrl = payBase + "/return",
					CancelUrl = payBase + "/cancel",
					ExpiresOn = request.ExpiresOn,
					IdempotencyKey = "invreq-" + request.InvoicePaymentRequestId
				}, cancellationToken);

				request.ExternalReference = creation.ExternalReference;
				request.PaymentIntentId = creation.PaymentIntentId;
				request.HostedUrl = creation.HostedUrl;
				if (creation.ExpiresOn > now) request.ExpiresOn = creation.ExpiresOn;
				request.Status = (int)PaymentRequestStatuses.Opened;
				request.UpdatedOn = DateTime.UtcNow;
				await _requests.SaveOrUpdateAsync(request, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Payment request {request.InvoicePaymentRequestId} could not be opened at the provider.");
				await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Failed, "provider refused: " + ex.Message, cancellationToken);
				throw new InvalidOperationException("payments_provider_unavailable");
			}

			var audit = NewAuditEvent(departmentId, userId ?? SystemUserId, AuditLogTypes.InvoicePaymentRequestCreated, ipAddress, userAgent);
			audit.After = JsonConvert.SerializeObject(new { request.InvoicePaymentRequestId, request.InvoiceId, request.Amount, request.Currency, request.Source, request.ExpiresOn, request.ExternalReference });
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return request;
		}

		public Task<InvoicePaymentRequest> GetOpenRequestAsync(string invoiceId, int departmentId) => _requests.GetOpenByInvoiceIdAsync(invoiceId, departmentId);

		public async Task<IReadOnlyList<InvoicePaymentRequest>> GetRequestsAsync(string invoiceId, int departmentId)
		{
			return (await _requests.GetByInvoiceIdAsync(invoiceId, departmentId) ?? Enumerable.Empty<InvoicePaymentRequest>()).ToList();
		}

		public async Task<string> BuildPayPageUrlAsync(string invoiceId, int departmentId)
		{
			var status = await GetStatusAsync(departmentId);
			if (!status.CanCollect) return null;
			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null || invoice.Status is (int)InvoiceStatus.Draft or (int)InvoiceStatus.Void or (int)InvoiceStatus.Paid || invoice.Balance <= 0) return null;
			if (string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.PublicBaseUrl) || string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase)) return null;
			return PayPageBase(CreatePayPageToken(departmentId, invoiceId, DateTime.UtcNow.AddDays(Math.Max(1, status.PayLinkExpiryDays))));
		}

		public string CreatePayPageToken(int departmentId, string invoiceId, DateTime expiresOnUtc)
		{
			return Encode($"{PayTokenKind}|{departmentId}|{invoiceId}|{expiresOnUtc:O}");
		}

		/// <summary>Decodes a pay-page token; false for any other token kind, a tampered token or a malformed one.</summary>
		public static bool TryDecodePayPageToken(string token, out int departmentId, out string invoiceId, out DateTime expiresOnUtc)
		{
			departmentId = 0; invoiceId = null; expiresOnUtc = DateTime.MinValue;
			var plain = Decode(token);
			if (plain == null) return false;
			var parts = plain.Split('|');
			if (parts.Length != 4 || parts[0] != PayTokenKind) return false;
			if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out departmentId) || departmentId <= 0) return false;
			if (string.IsNullOrWhiteSpace(parts[2])) return false;
			if (!DateTime.TryParse(parts[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expiresOnUtc)) return false;
			invoiceId = parts[2];
			return true;
		}

		public async Task<PayPageModel> GetPayPageModelAsync(string token)
		{
			var model = new PayPageModel { Available = false };
			if (!TryDecodePayPageToken(token, out var departmentId, out var invoiceId, out var expires))
			{
				model.UnavailableReason = "pay_link_invalid";
				return model;
			}
			model.DepartmentId = departmentId;
			model.InvoiceId = invoiceId;
			if (expires < DateTime.UtcNow)
			{
				model.UnavailableReason = "pay_link_expired";
				return model;
			}

			var invoice = await _invoices.GetByIdForDepartmentAsync(invoiceId, departmentId);
			if (invoice == null || invoice.IsDeleted || invoice.Status is (int)InvoiceStatus.Draft or (int)InvoiceStatus.Void)
			{
				model.UnavailableReason = "pay_invoice_unavailable";
				return model;
			}

			model.InvoiceNumber = invoice.InvoiceNumber;
			model.Currency = invoice.Currency;
			model.AmountDue = RoundMoney(Math.Max(0, invoice.Balance));
			model.DueOn = invoice.DueOn;
			model.DepartmentName = await DepartmentDisplayNameAsync(departmentId);
			if (invoice.Status == (int)InvoiceStatus.Paid || invoice.Balance <= 0)
			{
				model.Paid = true;
				model.UnavailableReason = "pay_invoice_paid";
				return model;
			}

			var status = await GetStatusAsync(departmentId);
			if (!status.CanCollect)
			{
				model.UnavailableReason = "pay_not_offered";
				return model;
			}

			var open = await _requests.GetOpenByInvoiceIdAsync(invoiceId, departmentId);
			if (open != null)
			{
				model.Processing = open.Status == (int)PaymentRequestStatuses.Processing;
				if (!model.Processing && open.ExpiresOn > DateTime.UtcNow) model.OpenHostedUrl = open.HostedUrl;
			}
			model.Available = !model.Processing;
			if (model.Processing) model.UnavailableReason = "pay_processing";
			return model;
		}

		private static string PayPageBase(string token) => $"{(Config.PaymentConnectConfig.PublicBaseUrl ?? string.Empty).TrimEnd('/')}/pay/{token}";

		#endregion

		#region Truth

		public async Task<PaymentWebhookReceipt> ReceiveWebhookAsync(int provider, string signatureHeader, string rawBody, string ipAddress, CancellationToken cancellationToken = default)
		{
			var receipt = new PaymentWebhookReceipt { Accepted = false, Outcome = PaymentEventOutcomes.Rejected };
			try
			{
				if (!Config.PaymentConnectConfig.Enabled)
				{
					receipt.Error = "payments_disabled";
					return receipt;
				}
				if (provider != _provider.Provider)
				{
					receipt.Error = "unknown_provider";
					return receipt;
				}

				if (!_provider.TryParseWebhook(rawBody, signatureHeader, out var envelope, out var error))
				{
					receipt.Error = error;
					await RecordRejectedAsync(provider, rawBody, error, ipAddress, cancellationToken);
					return receipt;
				}

				receipt.Outcome = await ApplyProviderEventAsync(envelope, rawBody, cancellationToken);
				receipt.Accepted = receipt.Outcome != PaymentEventOutcomes.Failed && receipt.Outcome != PaymentEventOutcomes.Rejected;
				return receipt;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Payment webhook could not be received.");
				receipt.Outcome = PaymentEventOutcomes.Failed;
				receipt.Error = ex.Message;
				return receipt;
			}
		}

		public async Task<PaymentEventOutcomes> ApplyProviderEventAsync(PaymentProviderEventEnvelope envelope, string rawBody, CancellationToken cancellationToken = default)
		{
			if (envelope == null || string.IsNullOrWhiteSpace(envelope.ExternalEventId)) return PaymentEventOutcomes.Ignored;

			var existing = await _events.GetByExternalEventIdAsync(envelope.Provider, envelope.ExternalEventId);
			if (existing != null && existing.Outcome != (int)PaymentEventOutcomes.Failed)
				return PaymentEventOutcomes.Duplicate;

			var now = DateTime.UtcNow;
			var record = existing ?? new PaymentConnectEvent
			{
				Provider = envelope.Provider,
				ExternalEventId = envelope.ExternalEventId,
				ExternalAccountId = envelope.ExternalAccountId,
				EventType = envelope.EventType,
				LiveMode = envelope.LiveMode,
				ReceivedOn = now,
				Outcome = (int)PaymentEventOutcomes.Ignored,
				// The ledger keeps the reconciliation shape only; payer PII is stripped before the row is written (ADP: no department at receipt time).
				PayloadJson = PaymentWebhookPayloadMinimizer.Minimize(rawBody)
			};
			try
			{
				record = await _events.SaveOrUpdateAsync(record, cancellationToken);
			}
			catch (Exception ex)
			{
				// The unique index lost a race with a concurrent delivery of the same event: that delivery owns it.
				Logging.LogException(ex, $"Payment event {envelope.ExternalEventId} could not be recorded; treated as a duplicate.");
				return PaymentEventOutcomes.Duplicate;
			}

			PaymentEventOutcomes outcome;
			string error = null;
			try
			{
				if (envelope.LiveMode != Config.PaymentConnectConfig.StripeLiveMode)
				{
					outcome = PaymentEventOutcomes.Rejected;
					error = "livemode mismatch";
					var audit = NewAuditEvent(0, SystemUserId, AuditLogTypes.PaymentWebhookRejected, null, null);
					audit.Successful = false;
					audit.After = JsonConvert.SerializeObject(new { envelope.ExternalEventId, envelope.EventType, Reason = error });
					_eventAggregator.SendMessage<AuditEvent>(audit);
				}
				else
				{
					(outcome, error) = await ApplyCoreAsync(envelope, record, cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Payment event {envelope.ExternalEventId} ({envelope.EventType}) failed to apply.");
				outcome = PaymentEventOutcomes.Failed;
				error = ex.Message;
			}

			record.Outcome = (int)outcome;
			record.Error = Truncate(error, 1000);
			record.ProcessedOn = DateTime.UtcNow;
			try { await _events.SaveOrUpdateAsync(record, cancellationToken); }
			catch (Exception ex) { Logging.LogException(ex, $"Payment event {envelope.ExternalEventId} outcome could not be stored."); }
			return outcome;
		}

		private async Task<(PaymentEventOutcomes Outcome, string Error)> ApplyCoreAsync(PaymentProviderEventEnvelope envelope, PaymentConnectEvent record, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(envelope.ExternalAccountId))
				return (PaymentEventOutcomes.Ignored, "no account on event");

			var connection = await _connections.GetByExternalAccountIdAsync(envelope.Provider, envelope.ExternalAccountId);
			if (connection == null)
				return (PaymentEventOutcomes.Ignored, "unknown account");
			record.DepartmentId = connection.DepartmentId;

			switch (envelope.Kind)
			{
				case PaymentEventKinds.ConnectionRevoked:
					if (connection.Status != (int)PaymentConnectionStatuses.Revoked && connection.Status != (int)PaymentConnectionStatuses.Disconnected)
						await HandleConnectionRevokedAsync(connection, "deauthorized at the provider", cancellationToken);
					return (PaymentEventOutcomes.Applied, null);

				case PaymentEventKinds.ConnectionUpdated:
					await ApplyConnectionFactsAsync(connection, envelope.ChargesEnabled, envelope.Capabilities, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);

				case PaymentEventKinds.PaymentProcessing:
				{
					var request = await ResolveRequestAsync(envelope);
					if (request == null) return (PaymentEventOutcomes.Ignored, "no request for event");
					if (!string.IsNullOrWhiteSpace(envelope.PaymentIntentId)) request.PaymentIntentId = envelope.PaymentIntentId;
					if (request.IsOpen) await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Processing, null, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.PaymentSucceeded:
				{
					var request = await ResolveRequestAsync(envelope);
					if (request == null) return (PaymentEventOutcomes.Ignored, "no request for event");
					await RecordOnlinePaymentAsync(connection, request, envelope, null, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.PaymentFailed:
				{
					var request = await ResolveRequestAsync(envelope);
					if (request == null) return (PaymentEventOutcomes.Ignored, "no request for event");
					if (request.IsOpen)
					{
						await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Failed, "payment failed at the provider", cancellationToken);
						AuditRequest(request, AuditLogTypes.InvoicePaymentRequestFailed);
					}
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.RequestExpired:
				{
					var request = await ResolveRequestAsync(envelope);
					if (request == null) return (PaymentEventOutcomes.Ignored, "no request for event");
					if (request.IsOpen)
					{
						await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Expired, null, cancellationToken);
						AuditRequest(request, AuditLogTypes.InvoicePaymentRequestExpired);
					}
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.PaymentRefunded:
				{
					var payment = await ResolvePaymentAsync(envelope, connection);
					if (payment == null) return (PaymentEventOutcomes.Ignored, "no payment for event");
					await _invoicing.ApplyPaymentRefundAsync(payment.InvoicePaymentId, payment.DepartmentId, envelope.RefundedAmount ?? payment.Amount, false, SystemUserId, null, null, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.PaymentDisputed:
				{
					var payment = await ResolvePaymentAsync(envelope, connection);
					if (payment == null) return (PaymentEventOutcomes.Ignored, "no payment for event");
					await _invoicing.ApplyPaymentDisputeAsync(payment.InvoicePaymentId, payment.DepartmentId, InvoiceDisputeStages.Opened, SystemUserId, null, null, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);
				}

				case PaymentEventKinds.DisputeClosed:
				{
					var payment = await ResolvePaymentAsync(envelope, connection);
					if (payment == null) return (PaymentEventOutcomes.Ignored, "no payment for event");
					await _invoicing.ApplyPaymentDisputeAsync(payment.InvoicePaymentId, payment.DepartmentId, envelope.DisputeLost == true ? InvoiceDisputeStages.Lost : InvoiceDisputeStages.Won, SystemUserId, null, null, cancellationToken);
					return (PaymentEventOutcomes.Applied, null);
				}

				default:
					return (PaymentEventOutcomes.Ignored, "event kind not consumed");
			}
		}

		private async Task RecordOnlinePaymentAsync(DepartmentPaymentConnection connection, InvoicePaymentRequest request, PaymentProviderEventEnvelope envelope, PaymentChargeFacts facts, CancellationToken cancellationToken)
		{
			var paymentIntentId = envelope?.PaymentIntentId ?? facts?.PaymentIntentId ?? request.PaymentIntentId;
			if (facts == null && !string.IsNullOrWhiteSpace(paymentIntentId))
			{
				try { facts = await _provider.GetChargeFactsAsync(connection, paymentIntentId, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, $"Charge facts for {request.InvoicePaymentRequestId} could not be read; recording without fee and receipt."); }
			}

			var gatewayId = paymentIntentId ?? facts?.ChargeId ?? envelope?.ChargeId ?? request.ExternalReference;
			var payment = await _invoicing.RecordPaymentAsync(new InvoicePayment
			{
				InvoiceId = request.InvoiceId,
				DepartmentId = request.DepartmentId,
				Amount = facts?.Amount > 0 ? facts.Amount : envelope?.Amount > 0 ? envelope.Amount.Value : request.Amount,
				Method = (int)InvoicePaymentMethods.Online,
				GatewayTransactionId = gatewayId,
				PaymentRequestId = request.InvoicePaymentRequestId,
				Provider = request.Provider,
				ProviderFeeAmount = facts?.Fee,
				NetAmount = facts?.Net,
				PayerEmail = facts?.PayerEmail ?? envelope?.PayerEmail,
				PaymentMethodSummary = facts?.MethodSummary ?? envelope?.MethodSummary,
				ReceiptUrl = facts?.ReceiptUrl ?? envelope?.ReceiptUrl,
				PaidOn = facts?.PaidOn ?? envelope?.OccurredOn ?? DateTime.UtcNow
			}, SystemUserId, null, null, cancellationToken);

			request.PaymentIntentId = paymentIntentId ?? request.PaymentIntentId;
			request.InvoicePaymentId = payment.InvoicePaymentId;
			request.CompletedOn = request.CompletedOn ?? DateTime.UtcNow;
			await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Completed, null, cancellationToken);
		}

		private async Task<InvoicePaymentRequest> ResolveRequestAsync(PaymentProviderEventEnvelope envelope)
		{
			InvoicePaymentRequest request = null;
			if (!string.IsNullOrWhiteSpace(envelope.ExternalReference))
				request = await _requests.GetByExternalReferenceAsync(envelope.Provider, envelope.ExternalReference);
			if (request == null && !string.IsNullOrWhiteSpace(envelope.PaymentIntentId))
				request = await _requests.GetByPaymentIntentIdAsync(envelope.Provider, envelope.PaymentIntentId);
			return request;
		}

		private async Task<InvoicePayment> ResolvePaymentAsync(PaymentProviderEventEnvelope envelope, DepartmentPaymentConnection connection)
		{
			InvoicePayment payment = null;
			if (!string.IsNullOrWhiteSpace(envelope.PaymentIntentId))
				payment = await _payments.GetByGatewayTransactionIdAsync(envelope.Provider, envelope.PaymentIntentId);
			if (payment == null && !string.IsNullOrWhiteSpace(envelope.ChargeId))
				payment = await _payments.GetByGatewayTransactionIdAsync(envelope.Provider, envelope.ChargeId);
			if (payment == null && !string.IsNullOrWhiteSpace(envelope.PaymentIntentId))
			{
				var request = await _requests.GetByPaymentIntentIdAsync(envelope.Provider, envelope.PaymentIntentId);
				if (!string.IsNullOrWhiteSpace(request?.InvoicePaymentId))
					payment = await _payments.GetByIdForDepartmentAsync(request.InvoicePaymentId, request.DepartmentId);
			}
			return payment != null && payment.DepartmentId == connection.DepartmentId ? payment : null;
		}

		private async Task ApplyConnectionFactsAsync(DepartmentPaymentConnection connection, bool? chargesEnabled, Dictionary<string, bool> capabilities, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			if (capabilities != null) connection.CapabilitiesJson = JsonConvert.SerializeObject(capabilities);
			connection.LastVerifiedOn = now;
			connection.EditedOn = now;
			connection.EditedByUserId = SystemUserId;

			if (chargesEnabled == false && connection.Status == (int)PaymentConnectionStatuses.Connected)
			{
				var audit = NewAuditEvent(connection.DepartmentId, SystemUserId, AuditLogTypes.PaymentConnectionActionRequired, null, null);
				audit.Before = Snapshot(connection);
				connection.Status = (int)PaymentConnectionStatuses.ActionRequired;
				connection.LastError = "charges disabled at the provider";
				audit.After = Snapshot(connection);
				_eventAggregator.SendMessage<AuditEvent>(audit);
				await NotifyAdminsAsync(connection.DepartmentId, $"Online payments: the department's {ProviderName(connection.Provider)} account ({connection.MaskedExternalAccountId}) can no longer take charges. Complete the provider's requirements to resume pay links.");
			}
			else if (chargesEnabled == true && connection.Status == (int)PaymentConnectionStatuses.ActionRequired)
			{
				connection.Status = (int)PaymentConnectionStatuses.Connected;
				connection.LastError = null;
			}
			await _connections.SaveOrUpdateAsync(connection, cancellationToken);
		}

		private async Task RecordRejectedAsync(int provider, string rawBody, string error, string ipAddress, CancellationToken cancellationToken)
		{
			var bodyHash = Hash(rawBody ?? string.Empty);
			try
			{
				await _events.SaveOrUpdateAsync(new PaymentConnectEvent
				{
					Provider = provider,
					ExternalEventId = "rejected:" + Guid.NewGuid().ToString("N"),
					EventType = "rejected",
					LiveMode = Config.PaymentConnectConfig.StripeLiveMode,
					ReceivedOn = DateTime.UtcNow,
					ProcessedOn = DateTime.UtcNow,
					Outcome = (int)PaymentEventOutcomes.Rejected,
					Error = Truncate(error, 1000),
					PayloadJson = null
				}, cancellationToken);
			}
			catch (Exception ex) { Logging.LogException(ex, "Rejected payment webhook could not be recorded."); }

			var audit = NewAuditEvent(0, SystemUserId, AuditLogTypes.PaymentWebhookRejected, ipAddress, null);
			audit.Successful = false;
			audit.After = JsonConvert.SerializeObject(new { Reason = error, BodySha256 = bodyHash });
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		private async Task TransitionRequestAsync(InvoicePaymentRequest request, int status, string error, CancellationToken cancellationToken)
		{
			request.Status = status;
			if (!string.IsNullOrWhiteSpace(error)) request.LastError = Truncate(error, 1000);
			request.UpdatedOn = DateTime.UtcNow;
			if (status == (int)PaymentRequestStatuses.Completed && !request.CompletedOn.HasValue) request.CompletedOn = request.UpdatedOn;
			await _requests.SaveOrUpdateAsync(request, cancellationToken);
		}

		private void AuditRequest(InvoicePaymentRequest request, AuditLogTypes type)
		{
			var audit = NewAuditEvent(request.DepartmentId, SystemUserId, type, null, null);
			audit.After = JsonConvert.SerializeObject(new { request.InvoicePaymentRequestId, request.InvoiceId, request.Amount, request.Currency, request.Status, request.LastError });
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion

		#region Worker passes

		public async Task<int> ReconcileOpenRequestsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			if (!Config.PaymentConnectConfig.Enabled) return 0;
			var applied = 0;
			var cutoff = asOfUtc.AddMinutes(-Math.Max(1, Config.PaymentConnectConfig.RequestReconcileAfterMinutes));
			foreach (var request in await _requests.GetOpenOlderThanAsync(cutoff, 200) ?? Enumerable.Empty<InvoicePaymentRequest>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(request.ExternalReference)) continue;
				var connection = string.IsNullOrWhiteSpace(request.DepartmentPaymentConnectionId) ? null : await _connections.GetByIdForDepartmentAsync(request.DepartmentPaymentConnectionId, request.DepartmentId);
				if (connection == null) continue;

				PaymentRequestState state;
				try { state = await _provider.GetPaymentRequestStateAsync(connection, request.ExternalReference, cancellationToken); }
				catch (Exception ex) { Logging.LogException(ex, $"Payment request {request.InvoicePaymentRequestId} could not be read back from the provider."); continue; }
				if (state == null) continue;

				var envelope = ReconciliationEnvelope(request, connection, state);
				if (envelope == null) continue;
				var outcome = await ApplyProviderEventAsync(envelope, JsonConvert.SerializeObject(new { source = "reconcile", request.InvoicePaymentRequestId, state.SessionStatus, state.PaymentStatus }), cancellationToken);
				if (outcome == PaymentEventOutcomes.Applied) applied++;
			}

			try { await _cacheProvider.SetStringAsync(LastReconcileCacheKey, DateTime.UtcNow.ToString("O"), TimeSpan.FromDays(7)); }
			catch (Exception ex) { Logging.LogException(ex, "Payments reconcile timestamp could not be cached."); }
			return applied;
		}

		/// <summary>Turns a read-back session state into the same envelope the webhook would have delivered (plan B2.4 reconciliation). Null when nothing changed.</summary>
		public static PaymentProviderEventEnvelope ReconciliationEnvelope(InvoicePaymentRequest request, DepartmentPaymentConnection connection, PaymentRequestState state)
		{
			var paid = string.Equals(state.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
			var complete = string.Equals(state.SessionStatus, "complete", StringComparison.OrdinalIgnoreCase);
			var expired = string.Equals(state.SessionStatus, "expired", StringComparison.OrdinalIgnoreCase);
			PaymentEventKinds kind;
			if (paid) kind = PaymentEventKinds.PaymentSucceeded;
			else if (complete) kind = PaymentEventKinds.PaymentProcessing;
			else if (expired) kind = PaymentEventKinds.RequestExpired;
			else return null;
			if (kind == PaymentEventKinds.PaymentProcessing && request.Status == (int)PaymentRequestStatuses.Processing) return null;

			return new PaymentProviderEventEnvelope
			{
				Provider = request.Provider,
				ExternalEventId = $"reconcile:{request.ExternalReference}:{kind}",
				ExternalAccountId = connection.ExternalAccountId,
				EventType = "reconcile." + kind.ToString().ToLowerInvariant(),
				Kind = kind,
				LiveMode = Config.PaymentConnectConfig.StripeLiveMode,
				OccurredOn = DateTime.UtcNow,
				ExternalReference = request.ExternalReference,
				PaymentIntentId = state.PaymentIntentId ?? state.Charge?.PaymentIntentId ?? request.PaymentIntentId,
				ChargeId = state.Charge?.ChargeId,
				Amount = state.Charge?.Amount ?? request.Amount,
				Currency = state.Charge?.Currency ?? request.Currency,
				PayerEmail = state.Charge?.PayerEmail,
				MethodSummary = state.Charge?.MethodSummary,
				ReceiptUrl = state.Charge?.ReceiptUrl
			};
		}

		public async Task<int> ExpireStaleRequestsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var request in await _requests.GetOpenExpiredAsync(asOfUtc, 500) ?? Enumerable.Empty<InvoicePaymentRequest>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (request.Status == (int)PaymentRequestStatuses.Processing) continue; // an asynchronous method is still clearing; the provider decides
				await TransitionRequestAsync(request, (int)PaymentRequestStatuses.Expired, null, cancellationToken);
				count++;
			}
			return count;
		}

		public async Task<int> ReverifyConnectionsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			if (!Config.PaymentConnectConfig.Enabled) return 0;
			var checkedCount = 0;
			foreach (var connection in await _connections.GetStaleVerifiedAsync(asOfUtc.AddDays(-1), 100) ?? Enumerable.Empty<DepartmentPaymentConnection>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				checkedCount++;
				try
				{
					var facts = await _provider.VerifyConnectionAsync(connection, cancellationToken);
					connection.DisplayName = facts.DisplayName ?? connection.DisplayName;
					connection.DefaultCurrency = facts.DefaultCurrency ?? connection.DefaultCurrency;
					await ApplyConnectionFactsAsync(connection, facts.ChargesEnabled, facts.Capabilities, cancellationToken);
				}
				catch (InvalidOperationException ex) when (ex.Message == "payments_connection_revoked")
				{
					await HandleConnectionRevokedAsync(connection, "verification: access revoked", cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Payment connection {connection.DepartmentPaymentConnectionId} could not be re-verified.");
				}
			}
			return checkedCount;
		}

		public Task<int> PurgeEventsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			return _events.PurgeReceivedBeforeAsync(asOfUtc.AddDays(-Math.Max(1, Config.PaymentConnectConfig.EventRetentionDays)), cancellationToken);
		}

		#endregion

		#region Health

		public async Task<PaymentsWebhookHealth> GetWebhookHealthAsync()
		{
			var health = new PaymentsWebhookHealth();

			try
			{
				health.Enabled = await IsAvailableInClusterAsync();
				if (!health.Enabled)
				{
					health.ComputeHealthy();
					return health;
				}

				health.WebhookConfigured = !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeSecretKey)
					&& !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.StripeConnectWebhookSecret)
					&& !string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.GetWebhookUrl());

				if (health.WebhookConfigured && Config.PaymentConnectConfig.WebhookEndpointProbeEnabled)
					health.EndpointRegistered = await ProbeEndpointRegisteredCachedAsync();

				var now = DateTime.UtcNow;
				if (_events != null)
				{
					health.LastEventReceivedOn = await _events.GetNewestReceivedOnAsync();
					health.LastEventAppliedOn = await _events.GetNewestAppliedOnAsync();
					health.RejectedLastHour = await _events.CountByOutcomeSinceAsync((int)PaymentEventOutcomes.Rejected, now.AddHours(-1));
					health.FailedLastHour = await _events.CountByOutcomeSinceAsync((int)PaymentEventOutcomes.Failed, now.AddHours(-1));
				}
				if (_requests != null)
				{
					health.OverdueOpenRequests = await _requests.CountOpenOlderThanAsync(now.AddMinutes(-Math.Max(1, Config.PaymentConnectConfig.RequestReconcileAfterMinutes)));
					var staleAfter = TimeSpan.FromHours(Math.Max(1, Config.PaymentConnectConfig.WebhookStaleAfterHours));
					var activity = await _requests.HasActivitySinceAsync(now.AddDays(-7));
					health.Stale = activity && (!health.LastEventReceivedOn.HasValue || now - health.LastEventReceivedOn.Value > staleAfter);
				}
				if (_cacheProvider != null)
				{
					var reconcile = await _cacheProvider.GetStringAsync(LastReconcileCacheKey);
					if (!string.IsNullOrWhiteSpace(reconcile) && DateTime.TryParse(reconcile, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var reconciledOn))
						health.LastReconcileOn = reconciledOn;
				}

				health.ComputeHealthy();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Payments webhook health could not be read.");
				// Unknown state, not a healthy disabled one: a cluster whose configuration switches payment collection on
				// reports the failed check as unhealthy so monitoring sees it; only a configured-off cluster stays healthy.
				health.Enabled = Config.PaymentConnectConfig.Enabled;
				health.EndpointRegistered = null;
				health.Healthy = !health.Enabled;
			}

			return health;
		}

		/// <summary>
		/// The Payments.StripeConnect operator flag is a per-cluster switch: only its global state counts, never a
		/// department override, so it is read as a flag row rather than evaluated for a department.
		/// </summary>
		private async Task<bool> IsClusterSwitchOnAsync()
		{
			var flag = await _featureToggleService.GetFlagByKeyAsync(FeatureFlagKeys.PaymentsStripeConnect);
			return flag != null && flag.IsEnabledGlobally && !flag.IsArchived;
		}

		private async Task<bool?> ProbeEndpointRegisteredCachedAsync()
		{
			var url = Config.PaymentConnectConfig.GetWebhookUrl();
			var now = DateTime.UtcNow;

			lock (EndpointProbeLock)
			{
				if (_endpointProbeUrl == url && now - _endpointProbeCheckedOnUtc < EndpointProbeCacheWindow)
					return _endpointProbeResult;
			}

			bool? result;
			try
			{
				result = await _endpointProbe.IsEndpointRegisteredAsync(url, Config.PaymentConnectConfig.StripeLiveMode, StripeConnectRequiredEvents);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Stripe Connect webhook endpoint probe failed.");
				result = null;
			}

			lock (EndpointProbeLock)
			{
				_endpointProbeUrl = url;
				_endpointProbeCheckedOnUtc = now;
				_endpointProbeResult = result;
			}

			return result;
		}

		/// <summary>Clears the in-process endpoint probe cache (tests, and an operator action after re-registering the endpoint).</summary>
		public static void ResetEndpointProbeCache()
		{
			lock (EndpointProbeLock)
			{
				_endpointProbeUrl = null;
				_endpointProbeCheckedOnUtc = DateTime.MinValue;
				_endpointProbeResult = null;
			}
		}

		#endregion

		#region Helpers

		public static IReadOnlyList<string> ParseMethods(string csv)
		{
			var list = (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(x => x.Trim().ToLowerInvariant()).Where(x => SupportedPaymentMethods.Contains(x)).Distinct().ToList();
			return list.Count == 0 ? new List<string> { "card" } : list;
		}

		public static Dictionary<string, bool> ParseCapabilities(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			try { return new Dictionary<string, bool>(JsonConvert.DeserializeObject<Dictionary<string, bool>>(json) ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase); }
			catch (JsonException) { return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); }
		}

		/// <summary>A DTO-safe copy: masked account id, no token ciphertext (plan B2.6).</summary>
		public static DepartmentPaymentConnection Masked(DepartmentPaymentConnection connection)
		{
			if (connection == null) return null;
			return new DepartmentPaymentConnection
			{
				DepartmentPaymentConnectionId = connection.DepartmentPaymentConnectionId,
				DepartmentId = connection.DepartmentId,
				Provider = connection.Provider,
				Status = connection.Status,
				Environment = connection.Environment,
				ExternalAccountId = DepartmentPaymentConnection.Mask(connection.ExternalAccountId),
				DisplayName = connection.DisplayName,
				Country = connection.Country,
				DefaultCurrency = connection.DefaultCurrency,
				CapabilitiesJson = connection.CapabilitiesJson,
				ScopesCsv = connection.ScopesCsv,
				ConnectedOn = connection.ConnectedOn,
				ConnectedByUserId = connection.ConnectedByUserId,
				DisconnectedOn = connection.DisconnectedOn,
				LastVerifiedOn = connection.LastVerifiedOn,
				LastError = connection.LastError,
				IsDefault = connection.IsDefault,
				AddedOn = connection.AddedOn,
				EditedOn = connection.EditedOn
			};
		}

		private static string Snapshot(DepartmentPaymentConnection connection)
		{
			return JsonConvert.SerializeObject(new
			{
				connection.DepartmentPaymentConnectionId, connection.Provider, connection.Status, connection.Environment,
				ExternalAccountId = connection.MaskedExternalAccountId, connection.DisplayName, connection.Country, connection.DefaultCurrency,
				connection.IsDefault, connection.ConnectedOn, connection.DisconnectedOn, connection.LastVerifiedOn, connection.LastError
			});
		}

		private static string ProviderName(int provider) => Enum.IsDefined(typeof(PaymentProviders), provider) ? ((PaymentProviders)provider).ToString() : "payment";

		private async Task<string> UserDisplayAsync(int departmentId, string userId)
		{
			try
			{
				var admins = await _departmentsService.GetAllAdminsForDepartmentAsync(departmentId);
				var match = admins?.FirstOrDefault(a => a.Id == userId);
				return match?.UserName ?? userId;
			}
			catch { return userId; }
		}

		private async Task NotifyAdminsAsync(int departmentId, string message)
		{
			try
			{
				foreach (var admin in await _departmentsService.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
					await _emailService.SendNotificationAsync(admin.Id, message, departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Online payments notification to department {departmentId} administrators could not be sent.");
			}
		}

		private async Task<string> DepartmentDisplayNameAsync(int departmentId)
		{
			var identity = await _identities.GetByDepartmentIdAsync(departmentId);
			if (!string.IsNullOrWhiteSpace(identity?.LegalBusinessName)) return identity.LegalBusinessName;
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);
			return department?.Name ?? "Resgrid";
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

		private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		private static string Hash(string value)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
		}

		/// <summary>Encrypts under the external-link passphrase and makes the cipher text URL safe (the call-link precedent, with a kind marker).</summary>
		private static string Encode(string plain)
		{
			var cipher = SymmetricEncryption.Encrypt(plain, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(cipher)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		private static string Decode(string token)
		{
			if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return null;
			try
			{
				var base64 = token.Replace('-', '+').Replace('_', '/');
				base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
				var cipher = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
				return SymmetricEncryption.Decrypt(cipher, Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			}
			catch { return null; }
		}

		private static bool TryDecodeState(string state, out int departmentId, out string userId, out int provider, out string nonce, out DateTime expiresOnUtc)
		{
			departmentId = 0; userId = null; provider = 0; nonce = null; expiresOnUtc = DateTime.MinValue;
			var plain = Decode(state);
			if (plain == null) return false;
			var parts = plain.Split('|');
			if (parts.Length != 6 || parts[0] != StateKind) return false;
			if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out departmentId)) return false;
			userId = parts[2];
			if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out provider)) return false;
			nonce = parts[4];
			return !string.IsNullOrWhiteSpace(nonce) && DateTime.TryParse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out expiresOnUtc);
		}

		/// <summary>Token ciphertext for v2 providers, under PaymentConnectConfig.CredentialPassphrase (never the link passphrase).</summary>
		private static string Protect(string secret)
		{
			if (string.IsNullOrWhiteSpace(Config.PaymentConnectConfig.CredentialPassphrase)) throw new InvalidOperationException("payments_provider_unavailable");
			return SymmetricEncryption.Encrypt(secret, Config.PaymentConnectConfig.CredentialPassphrase);
		}

		#endregion
	}
}
