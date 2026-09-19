using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Stripe;
using Stripe.Checkout;

namespace Resgrid.Providers.Payments
{
	/// <summary>
	/// Stripe Connect adapter (Workforce &amp; Business Operations plan, B2.1/B2.3): OAuth for Standard accounts, direct
	/// charges through Checkout Sessions created as the connected account, and the Connect webhook endpoint. Uses its
	/// own StripeClient with the platform key so the SaaS billing path's global configuration is never touched;
	/// every connected-account call carries RequestOptions.StripeAccount. Request bodies are never logged.
	/// </summary>
	public sealed class StripeConnectPaymentProvider : IPaymentConnectProvider
	{
		public const string AuthorizeEndpoint = "https://connect.stripe.com/oauth/authorize";
		private const long SignatureToleranceSeconds = 300;

		public int Provider => (int)PaymentProviders.Stripe;

		public bool IsConfigured => !string.IsNullOrWhiteSpace(PaymentConnectConfig.StripeSecretKey) && !string.IsNullOrWhiteSpace(PaymentConnectConfig.StripeClientId);

		private static StripeClient Client() => new StripeClient(PaymentConnectConfig.StripeSecretKey);

		private static RequestOptions ForAccount(string externalAccountId, string idempotencyKey = null) =>
			new RequestOptions { StripeAccount = externalAccountId, IdempotencyKey = idempotencyKey };

		public string BuildConnectUrl(string state, string redirectUrl)
		{
			if (!IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");
			return AuthorizeEndpoint
				+ "?response_type=code"
				+ "&client_id=" + Uri.EscapeDataString(PaymentConnectConfig.StripeClientId)
				+ "&scope=read_write"
				+ "&state=" + Uri.EscapeDataString(state ?? string.Empty)
				+ "&redirect_uri=" + Uri.EscapeDataString(redirectUrl ?? string.Empty);
		}

		public async Task<PaymentConnectionFacts> CompleteConnectAsync(IReadOnlyDictionary<string, string> callbackParameters, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");
			if (callbackParameters == null || !callbackParameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
				throw new InvalidOperationException("payments_connect_failed");

			OAuthToken token;
			try
			{
				token = await new OAuthTokenService(Client()).CreateAsync(new OAuthTokenCreateOptions { GrantType = "authorization_code", Code = code }, null, cancellationToken);
			}
			catch (StripeException ex)
			{
				Logging.LogException(ex, "Stripe Connect OAuth token exchange failed.");
				throw new InvalidOperationException("payments_connect_failed");
			}

			if (string.IsNullOrWhiteSpace(token?.StripeUserId))
				throw new InvalidOperationException("payments_connect_failed");

			var facts = await ReadAccountAsync(token.StripeUserId, cancellationToken);
			facts.ScopesCsv = token.Scope;
			facts.LiveMode = token.Livemode;
			return facts;
		}

		public async Task<PaymentConnectionFacts> VerifyConnectionAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			var facts = await ReadAccountAsync(connection.ExternalAccountId, cancellationToken);
			facts.LiveMode = PaymentConnectConfig.StripeLiveMode;
			return facts;
		}

		public async Task DisconnectAsync(DepartmentPaymentConnection connection, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			if (!IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");
			try
			{
				await new OAuthTokenService(Client()).DeauthorizeAsync(new OAuthDeauthorizeOptions { ClientId = PaymentConnectConfig.StripeClientId, StripeUserId = connection.ExternalAccountId }, null, cancellationToken);
			}
			catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.BadRequest || ex.HttpStatusCode == HttpStatusCode.Unauthorized || ex.HttpStatusCode == HttpStatusCode.NotFound)
			{
				// Already deauthorized from the Stripe dashboard, or the account no longer exists: the local state wins.
				Logging.LogInfo($"Stripe Connect deauthorize answered {(int)ex.HttpStatusCode}; treating the connection as already revoked.");
			}
		}

		public async Task<PaymentRequestCreation> CreatePaymentRequestAsync(DepartmentPaymentConnection connection, PaymentRequestSpec spec, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			if (spec == null) throw new ArgumentNullException(nameof(spec));
			if (!IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");

			var metadata = new Dictionary<string, string>
			{
				["departmentId"] = spec.DepartmentId.ToString(CultureInfo.InvariantCulture),
				["invoiceId"] = spec.InvoiceId,
				["paymentRequestId"] = spec.PaymentRequestId
			};
			var label = $"Invoice {spec.InvoiceNumber}";
			var options = new SessionCreateOptions
			{
				Mode = "payment",
				ClientReferenceId = spec.PaymentRequestId,
				CustomerEmail = string.IsNullOrWhiteSpace(spec.CustomerEmail) ? null : spec.CustomerEmail,
				PaymentMethodTypes = spec.PaymentMethodTypes == null || spec.PaymentMethodTypes.Count == 0 ? null : spec.PaymentMethodTypes.ToList(),
				SuccessUrl = spec.SuccessUrl,
				CancelUrl = spec.CancelUrl,
				ExpiresAt = spec.ExpiresOn,
				Metadata = metadata,
				LineItems = new List<SessionLineItemOptions>
				{
					new SessionLineItemOptions
					{
						Quantity = 1,
						PriceData = new SessionLineItemPriceDataOptions
						{
							Currency = (spec.Currency ?? "usd").ToLowerInvariant(),
							UnitAmount = ToMinorUnits(spec.Amount),
							ProductData = new SessionLineItemPriceDataProductDataOptions { Name = label }
						}
					}
				},
				// No application_fee_amount, ever (plan B2.1): the department pays Stripe's fees and Resgrid takes nothing.
				PaymentIntentData = new SessionPaymentIntentDataOptions { Description = label, Metadata = metadata }
			};

			Session session;
			try
			{
				session = await new SessionService(Client()).CreateAsync(options, ForAccount(connection.ExternalAccountId, spec.IdempotencyKey), cancellationToken);
			}
			catch (StripeException ex)
			{
				Logging.LogException(ex, "Stripe Checkout Session could not be created on the connected account.");
				throw new InvalidOperationException("payments_provider_unavailable");
			}

			return new PaymentRequestCreation
			{
				ExternalReference = session.Id,
				PaymentIntentId = session.PaymentIntentId,
				HostedUrl = session.Url,
				ExpiresOn = session.ExpiresAt
			};
		}

		public async Task<PaymentRequestState> GetPaymentRequestStateAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			var options = new SessionGetOptions();
			options.AddExpand("payment_intent");
			options.AddExpand("payment_intent.latest_charge");
			options.AddExpand("payment_intent.latest_charge.balance_transaction");
			var session = await new SessionService(Client()).GetAsync(externalReference, options, ForAccount(connection.ExternalAccountId), cancellationToken);
			var charge = session.PaymentIntent?.LatestCharge;
			return new PaymentRequestState
			{
				SessionStatus = session.Status,
				PaymentStatus = session.PaymentStatus,
				PaymentIntentId = session.PaymentIntentId ?? session.PaymentIntent?.Id,
				Charge = charge == null ? null : ChargeFacts(charge, session.CustomerDetails?.Email)
			};
		}

		public async Task CancelPaymentRequestAsync(DepartmentPaymentConnection connection, string externalReference, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			try
			{
				await new SessionService(Client()).ExpireAsync(externalReference, null, ForAccount(connection.ExternalAccountId), cancellationToken);
			}
			catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.BadRequest || ex.HttpStatusCode == HttpStatusCode.NotFound)
			{
				// Already complete or expired at Stripe: nothing to expire.
			}
		}

		public async Task<PaymentChargeFacts> GetChargeFactsAsync(DepartmentPaymentConnection connection, string paymentIntentId, CancellationToken cancellationToken = default)
		{
			if (connection == null) throw new ArgumentNullException(nameof(connection));
			if (string.IsNullOrWhiteSpace(paymentIntentId)) return null;
			var options = new PaymentIntentGetOptions();
			options.AddExpand("latest_charge");
			options.AddExpand("latest_charge.balance_transaction");
			var intent = await new PaymentIntentService(Client()).GetAsync(paymentIntentId, options, ForAccount(connection.ExternalAccountId), cancellationToken);
			return intent?.LatestCharge == null ? null : ChargeFacts(intent.LatestCharge, intent.ReceiptEmail);
		}

		public bool TryParseWebhook(string rawBody, string signatureHeader, out PaymentProviderEventEnvelope envelope, out string error)
		{
			envelope = null;
			error = null;
			if (string.IsNullOrWhiteSpace(PaymentConnectConfig.StripeConnectWebhookSecret))
			{
				error = "webhook_secret_missing";
				return false;
			}
			if (string.IsNullOrWhiteSpace(rawBody) || string.IsNullOrWhiteSpace(signatureHeader))
			{
				error = "signature_missing";
				return false;
			}

			Event stripeEvent;
			try
			{
				stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader, PaymentConnectConfig.StripeConnectWebhookSecret, SignatureToleranceSeconds, throwOnApiVersionMismatch: false);
			}
			catch (StripeException ex)
			{
				error = "signature_invalid: " + ex.Message;
				return false;
			}
			catch (Exception ex)
			{
				error = "body_invalid: " + ex.Message;
				return false;
			}

			envelope = ToEnvelope(stripeEvent);
			return envelope != null;
		}

		/// <summary>Maps a verified Stripe event to the provider-neutral envelope (plan B2.3). Pure; the webhook receiver and the tests share it.</summary>
		public static PaymentProviderEventEnvelope ToEnvelope(Event stripeEvent)
		{
			if (stripeEvent == null) return null;
			var envelope = new PaymentProviderEventEnvelope
			{
				Provider = (int)PaymentProviders.Stripe,
				ExternalEventId = stripeEvent.Id,
				ExternalAccountId = stripeEvent.Account,
				EventType = stripeEvent.Type,
				LiveMode = stripeEvent.Livemode,
				OccurredOn = stripeEvent.Created,
				Kind = PaymentEventKinds.Unknown
			};

			switch (stripeEvent.Type)
			{
				case "checkout.session.completed":
				case "checkout.session.async_payment_succeeded":
					if (stripeEvent.Data?.Object is Session completed)
					{
						FillFromSession(envelope, completed);
						// A completed session with an unpaid status is an asynchronous method (ACH) still clearing.
						envelope.Kind = string.Equals(completed.PaymentStatus, "unpaid", StringComparison.OrdinalIgnoreCase) && stripeEvent.Type == "checkout.session.completed"
							? PaymentEventKinds.PaymentProcessing
							: PaymentEventKinds.PaymentSucceeded;
					}
					break;
				case "checkout.session.async_payment_failed":
					if (stripeEvent.Data?.Object is Session failed) FillFromSession(envelope, failed);
					envelope.Kind = PaymentEventKinds.PaymentFailed;
					break;
				case "checkout.session.expired":
					if (stripeEvent.Data?.Object is Session expired) FillFromSession(envelope, expired);
					envelope.Kind = PaymentEventKinds.RequestExpired;
					break;
				case "payment_intent.succeeded":
					if (stripeEvent.Data?.Object is PaymentIntent intent)
					{
						envelope.PaymentIntentId = intent.Id;
						envelope.Amount = FromMinorUnits(intent.AmountReceived > 0 ? intent.AmountReceived : intent.Amount);
						envelope.Currency = intent.Currency?.ToUpperInvariant();
						envelope.ChargeId = intent.LatestChargeId;
						envelope.PayerEmail = intent.ReceiptEmail;
					}
					envelope.Kind = PaymentEventKinds.PaymentSucceeded;
					break;
				case "charge.refunded":
					if (stripeEvent.Data?.Object is Charge refunded)
					{
						envelope.ChargeId = refunded.Id;
						envelope.PaymentIntentId = refunded.PaymentIntentId;
						envelope.Amount = FromMinorUnits(refunded.Amount);
						envelope.RefundedAmount = FromMinorUnits(refunded.AmountRefunded);
						envelope.Currency = refunded.Currency?.ToUpperInvariant();
					}
					envelope.Kind = PaymentEventKinds.PaymentRefunded;
					break;
				case "charge.dispute.created":
				case "charge.dispute.closed":
					if (stripeEvent.Data?.Object is Dispute dispute)
					{
						envelope.ChargeId = dispute.ChargeId;
						envelope.PaymentIntentId = dispute.PaymentIntentId;
						envelope.Amount = FromMinorUnits(dispute.Amount);
						envelope.Currency = dispute.Currency?.ToUpperInvariant();
						envelope.DisputeLost = string.Equals(dispute.Status, "lost", StringComparison.OrdinalIgnoreCase);
					}
					envelope.Kind = stripeEvent.Type == "charge.dispute.created" ? PaymentEventKinds.PaymentDisputed : PaymentEventKinds.DisputeClosed;
					break;
				case "account.application.deauthorized":
					envelope.Kind = PaymentEventKinds.ConnectionRevoked;
					break;
				case "account.updated":
					if (stripeEvent.Data?.Object is Account account)
					{
						envelope.ExternalAccountId = string.IsNullOrWhiteSpace(envelope.ExternalAccountId) ? account.Id : envelope.ExternalAccountId;
						envelope.ChargesEnabled = account.ChargesEnabled;
						envelope.Capabilities = ReadCapabilities(account);
					}
					envelope.Kind = PaymentEventKinds.ConnectionUpdated;
					break;
			}

			return envelope;
		}

		private static void FillFromSession(PaymentProviderEventEnvelope envelope, Session session)
		{
			envelope.ExternalReference = session.Id;
			envelope.PaymentIntentId = session.PaymentIntentId ?? session.PaymentIntent?.Id;
			envelope.Amount = FromMinorUnits(session.AmountTotal);
			envelope.Currency = session.Currency?.ToUpperInvariant();
			envelope.PayerEmail = session.CustomerDetails?.Email ?? session.CustomerEmail;
		}

		private async Task<PaymentConnectionFacts> ReadAccountAsync(string externalAccountId, CancellationToken cancellationToken)
		{
			if (!IsConfigured) throw new InvalidOperationException("payments_provider_unavailable");
			Account account;
			try
			{
				account = await new AccountService(Client()).GetAsync(externalAccountId, null, null, cancellationToken);
			}
			catch (StripeException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized || ex.HttpStatusCode == HttpStatusCode.Forbidden || ex.HttpStatusCode == HttpStatusCode.NotFound)
			{
				throw new InvalidOperationException("payments_connection_revoked");
			}
			catch (StripeException ex)
			{
				Logging.LogException(ex, "Stripe connected account could not be read.");
				throw new InvalidOperationException("payments_provider_unavailable");
			}

			return new PaymentConnectionFacts
			{
				ExternalAccountId = account.Id,
				DisplayName = account.Settings?.Dashboard?.DisplayName ?? account.BusinessProfile?.Name ?? account.Email,
				Country = account.Country,
				DefaultCurrency = account.DefaultCurrency?.ToUpperInvariant(),
				ChargesEnabled = account.ChargesEnabled,
				Capabilities = ReadCapabilities(account),
				LiveMode = PaymentConnectConfig.StripeLiveMode
			};
		}

		private static Dictionary<string, bool> ReadCapabilities(Account account)
		{
			return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["charges_enabled"] = account.ChargesEnabled,
				["card_payments"] = string.Equals(account.Capabilities?.CardPayments, "active", StringComparison.OrdinalIgnoreCase),
				["us_bank_account_ach_payments"] = string.Equals(account.Capabilities?.UsBankAccountAchPayments, "active", StringComparison.OrdinalIgnoreCase)
			};
		}

		private static PaymentChargeFacts ChargeFacts(Charge charge, string fallbackEmail)
		{
			var details = charge.PaymentMethodDetails;
			string summary;
			if (details?.Card != null)
				summary = $"{Capitalize(details.Card.Brand)} •••• {details.Card.Last4}";
			else if (details?.UsBankAccount != null)
				summary = $"ACH {details.UsBankAccount.BankName} •••• {details.UsBankAccount.Last4}".Replace("  ", " ");
			else
				summary = details?.Type;

			return new PaymentChargeFacts
			{
				ChargeId = charge.Id,
				PaymentIntentId = charge.PaymentIntentId,
				Amount = FromMinorUnits(charge.Amount) ?? 0,
				Currency = charge.Currency?.ToUpperInvariant(),
				Fee = FromMinorUnits(charge.BalanceTransaction?.Fee),
				Net = FromMinorUnits(charge.BalanceTransaction?.Net),
				PayerEmail = charge.BillingDetails?.Email ?? charge.ReceiptEmail ?? fallbackEmail,
				MethodSummary = summary,
				ReceiptUrl = charge.ReceiptUrl,
				PaidOn = charge.Created
			};
		}

		private static string Capitalize(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

		/// <summary>v1 settles two-decimal currencies only (USD, EUR, GBP, CAD, AUD, NZD); zero-decimal currencies are refused upstream.</summary>
		public static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

		public static decimal? FromMinorUnits(long? minor) => minor.HasValue ? minor.Value / 100m : (decimal?)null;
	}
}
