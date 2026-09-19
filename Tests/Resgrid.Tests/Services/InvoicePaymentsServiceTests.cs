using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;
using PaymentConnectConfig = Resgrid.Config.PaymentConnectConfig;
using SystemBehaviorConfig = Resgrid.Config.SystemBehaviorConfig;

namespace Resgrid.Tests.Services
{
	/// <summary>Plan B2.7 unit list: tokens, state, the refusal matrix, request reuse, the truth path and reconciliation.</summary>
	[TestFixture]
	public class InvoicePaymentsServiceTests
	{
		private const int Dept = 7;
		private const string Acct = "acct_1ABCDEF123456789";

		private Mock<IFeatureToggleService> _toggles;
		private Mock<IStripeConnectEndpointProbe> _probe;
		private Mock<IPaymentConnectProvider> _provider;
		private Mock<IDepartmentPaymentConnectionRepository> _connections;
		private Mock<IInvoicePaymentRequestRepository> _requests;
		private Mock<IPaymentProviderEventRepository> _events;
		private Mock<IInvoiceRepository> _invoices;
		private Mock<ICustomerBillingProfileRepository> _profiles;
		private Mock<IInvoicePaymentRepository> _payments;
		private Mock<IDepartmentBillingIdentityRepository> _identities;
		private Mock<IInvoicingService> _invoicing;
		private Mock<IBusinessOperationsAccessService> _access;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEmailService> _email;
		private Mock<ICacheProvider> _cache;
		private Mock<IEventAggregator> _aggregator;

		private readonly List<AuditEvent> _audits = new List<AuditEvent>();
		private readonly Dictionary<string, string> _cacheStore = new Dictionary<string, string>();
		private readonly List<InvoicePaymentRequest> _savedRequests = new List<InvoicePaymentRequest>();
		private readonly List<PaymentConnectEvent> _savedEvents = new List<PaymentConnectEvent>();
		private readonly List<DepartmentPaymentConnection> _savedConnections = new List<DepartmentPaymentConnection>();

		private bool _enabled; private string _passphrase; private string _publicBase; private bool _live; private string _secret; private string _clientId;

		[SetUp]
		public void SetUp()
		{
			_enabled = PaymentConnectConfig.Enabled; _passphrase = SystemBehaviorConfig.ExternalLinkUrlParamPassphrase; _publicBase = PaymentConnectConfig.PublicBaseUrl;
			_live = PaymentConnectConfig.StripeLiveMode; _secret = PaymentConnectConfig.StripeSecretKey; _clientId = PaymentConnectConfig.StripeClientId;
			PaymentConnectConfig.Enabled = true;
			PaymentConnectConfig.PublicBaseUrl = "https://pay.example.test";
			PaymentConnectConfig.StripeLiveMode = false;
			PaymentConnectConfig.StripeSecretKey = "sk_test_platform";
			PaymentConnectConfig.StripeClientId = "ca_test";
			SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = "unit-test-passphrase";
			_audits.Clear(); _cacheStore.Clear(); _savedRequests.Clear(); _savedEvents.Clear(); _savedConnections.Clear();

			_toggles = new Mock<IFeatureToggleService>();
			_toggles.Setup(t => t.GetFlagByKeyAsync(FeatureFlagKeys.PaymentsStripeConnect, It.IsAny<bool>())).ReturnsAsync(new FeatureFlag { FlagKey = FeatureFlagKeys.PaymentsStripeConnect, IsEnabledGlobally = true });
			_toggles.Setup(t => t.IsEnabledAsync(FeatureFlagKeys.OnlinePayments, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			_probe = new Mock<IStripeConnectEndpointProbe>();
			_provider = new Mock<IPaymentConnectProvider>();
			_provider.SetupGet(p => p.Provider).Returns((int)PaymentProviders.Stripe);
			_provider.SetupGet(p => p.IsConfigured).Returns(true);
			_provider.Setup(p => p.BuildConnectUrl(It.IsAny<string>(), It.IsAny<string>())).Returns((string state, string redirect) => "https://connect.stripe.com/oauth/authorize?state=" + state + "&redirect_uri=" + Uri.EscapeDataString(redirect));

			_connections = new Mock<IDepartmentPaymentConnectionRepository>();
			_connections.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentPaymentConnection c, CancellationToken _, bool __) => { c.DepartmentPaymentConnectionId ??= Guid.NewGuid().ToString(); _savedConnections.Add(c); return c; });
			_connections.Setup(r => r.GetForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentPaymentConnection>());
			_requests = new Mock<IInvoicePaymentRequestRepository>();
			_requests.Setup(r => r.SaveOrUpdateAsync(It.IsAny<InvoicePaymentRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((InvoicePaymentRequest r, CancellationToken _, bool __) => { r.InvoicePaymentRequestId ??= Guid.NewGuid().ToString(); if (!_savedRequests.Contains(r)) _savedRequests.Add(r); return r; });
			_requests.Setup(r => r.GetOpenByConnectionAsync(It.IsAny<string>())).ReturnsAsync(new List<InvoicePaymentRequest>());
			_events = new Mock<IPaymentProviderEventRepository>();
			_events.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PaymentConnectEvent>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((PaymentConnectEvent e, CancellationToken _, bool __) => { e.PaymentConnectEventId ??= Guid.NewGuid().ToString(); if (!_savedEvents.Contains(e)) _savedEvents.Add(e); return e; });
			_invoices = new Mock<IInvoiceRepository>();
			_profiles = new Mock<ICustomerBillingProfileRepository>();
			_payments = new Mock<IInvoicePaymentRepository>();
			_identities = new Mock<IDepartmentBillingIdentityRepository>();
			_identities.Setup(r => r.GetByDepartmentIdAsync(Dept)).ReturnsAsync(new DepartmentBillingIdentity { DepartmentId = Dept, OnlinePaymentsEnabled = true, PayLinkExpiryDays = 30, AllowedPaymentMethodsCsv = "card,us_bank_account" });
			_identities.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentBillingIdentity>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DepartmentBillingIdentity i, CancellationToken _, bool __) => i);
			_invoicing = new Mock<IInvoicingService>();
			_access = new Mock<IBusinessOperationsAccessService>();
			_access.Setup(a => a.CanUseInvoicingAsync(Dept)).ReturnsAsync(true);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetAllAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<Model.Identity.IdentityUser> { new Model.Identity.IdentityUser { Id = "admin", UserName = "chief", Email = "chief@example.test" } });
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Test Fire" });
			_email = new Mock<IEmailService>();
			_email.Setup(e => e.SendNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<UserProfile>())).ReturnsAsync(true);
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync((string k, string v, TimeSpan _) => { _cacheStore[k] = v; return true; });
			_cache.Setup(c => c.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string k) => _cacheStore.TryGetValue(k, out var v) ? v : null);
			_cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ReturnsAsync((string k) => _cacheStore.Remove(k));
			_aggregator = new Mock<IEventAggregator>();
			_aggregator.Setup(a => a.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));
			InvoicePaymentsService.ResetEndpointProbeCache();
		}

		[TearDown]
		public void TearDown()
		{
			PaymentConnectConfig.Enabled = _enabled; SystemBehaviorConfig.ExternalLinkUrlParamPassphrase = _passphrase; PaymentConnectConfig.PublicBaseUrl = _publicBase;
			PaymentConnectConfig.StripeLiveMode = _live; PaymentConnectConfig.StripeSecretKey = _secret; PaymentConnectConfig.StripeClientId = _clientId;
		}

		private InvoicePaymentsService Build() => new InvoicePaymentsService(_toggles.Object, _probe.Object, _provider.Object, _connections.Object, _requests.Object, _events.Object,
			_invoices.Object, _profiles.Object, _payments.Object, _identities.Object, _invoicing.Object, _access.Object, _departments.Object, _email.Object, _cache.Object, _aggregator.Object);

		private DepartmentPaymentConnection Connected(string capabilities = "{\"charges_enabled\":true,\"card_payments\":true,\"us_bank_account_ach_payments\":true}")
		{
			var connection = new DepartmentPaymentConnection
			{
				DepartmentPaymentConnectionId = "conn-1", DepartmentId = Dept, Provider = (int)PaymentProviders.Stripe, Status = (int)PaymentConnectionStatuses.Connected,
				ExternalAccountId = Acct, DefaultCurrency = "USD", CapabilitiesJson = capabilities, IsDefault = true, ConnectedOn = DateTime.UtcNow.AddDays(-2)
			};
			_connections.Setup(r => r.GetDefaultForDepartmentAsync(Dept)).ReturnsAsync(connection);
			_connections.Setup(r => r.GetByIdForDepartmentAsync("conn-1", Dept)).ReturnsAsync(connection);
			_connections.Setup(r => r.GetByExternalAccountIdAsync((int)PaymentProviders.Stripe, Acct)).ReturnsAsync(connection);
			return connection;
		}

		private Invoice OpenInvoice(int status = (int)InvoiceStatus.Sent, decimal total = 250m, decimal paid = 0m, string currency = "USD")
		{
			var invoice = new Invoice { InvoiceId = "inv-1", DepartmentId = Dept, InvoiceNumber = 42, ContactId = "c-1", CustomerBillingProfileId = "p-1", Status = status, Total = total, AmountPaid = paid, Currency = currency, DueOn = DateTime.UtcNow.AddDays(10) };
			_invoices.Setup(r => r.GetByIdForDepartmentAsync("inv-1", Dept)).ReturnsAsync(invoice);
			_profiles.Setup(r => r.GetByIdForDepartmentAsync("p-1", Dept)).ReturnsAsync(new CustomerBillingProfile { CustomerBillingProfileId = "p-1", DepartmentId = Dept, ContactId = "c-1", BillingEmail = "ap@customer.test" });
			return invoice;
		}

		private InvoicePaymentRequest OpenRequest(string reference = "cs_1", string intent = "pi_1", int status = (int)PaymentRequestStatuses.Opened)
		{
			var request = new InvoicePaymentRequest
			{
				InvoicePaymentRequestId = "req-1", InvoiceId = "inv-1", DepartmentId = Dept, DepartmentPaymentConnectionId = "conn-1", Provider = (int)PaymentProviders.Stripe,
				Amount = 250m, Currency = "USD", Status = status, ExternalReference = reference, PaymentIntentId = intent, HostedUrl = "https://checkout.stripe.com/c/pay/cs_1",
				ExpiresOn = DateTime.UtcNow.AddHours(20), AddedOn = DateTime.UtcNow.AddMinutes(-30), UpdatedOn = DateTime.UtcNow.AddMinutes(-30)
			};
			_requests.Setup(r => r.GetOpenByInvoiceIdAsync("inv-1", Dept)).ReturnsAsync(request);
			_requests.Setup(r => r.GetByExternalReferenceAsync((int)PaymentProviders.Stripe, reference)).ReturnsAsync(request);
			_requests.Setup(r => r.GetByPaymentIntentIdAsync((int)PaymentProviders.Stripe, intent)).ReturnsAsync(request);
			return request;
		}

		private static PaymentProviderEventEnvelope Envelope(PaymentEventKinds kind, string id = "evt_1", string account = Acct, bool live = false) => new PaymentProviderEventEnvelope
		{
			Provider = (int)PaymentProviders.Stripe, ExternalEventId = id, ExternalAccountId = account, EventType = kind.ToString(), Kind = kind, LiveMode = live, OccurredOn = DateTime.UtcNow,
			ExternalReference = "cs_1", PaymentIntentId = "pi_1", Amount = 250m, Currency = "USD"
		};

		// ---- tokens and state -------------------------------------------------------------------------------------

		[Test]
		public void Pay_page_token_round_trips_and_carries_the_kind_marker()
		{
			var service = Build();
			var expires = DateTime.UtcNow.AddDays(30);
			var token = service.CreatePayPageToken(Dept, "inv-1", expires);
			token.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
			InvoicePaymentsService.TryDecodePayPageToken(token, out var dept, out var invoiceId, out var decodedExpiry).Should().BeTrue();
			dept.Should().Be(Dept); invoiceId.Should().Be("inv-1"); decodedExpiry.Should().BeCloseTo(expires, TimeSpan.FromMilliseconds(1));
		}

		[Test]
		public void A_call_style_token_never_resolves_as_a_pay_token()
		{
			// The call-link precedent encrypts "{callId}|{type}|{station}" without a kind marker; the pay page must refuse it.
			var cipher = Resgrid.Framework.SymmetricEncryption.Encrypt("12345|t|1", SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);
			var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cipher)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			InvoicePaymentsService.TryDecodePayPageToken(token, out _, out _, out _).Should().BeFalse();
			InvoicePaymentsService.TryDecodePayPageToken("not-a-token", out _, out _, out _).Should().BeFalse();
			InvoicePaymentsService.TryDecodePayPageToken(Build().CreatePayPageToken(Dept, "inv-1", DateTime.UtcNow) + "x", out _, out _, out _).Should().BeFalse();
		}

		[Test]
		public async Task Begin_connect_refuses_while_the_cluster_switch_is_off()
		{
			_toggles.Setup(t => t.GetFlagByKeyAsync(FeatureFlagKeys.PaymentsStripeConnect, It.IsAny<bool>())).ReturnsAsync(new FeatureFlag { IsEnabledGlobally = false });
			Func<Task> act = () => Build().BeginConnectAsync(Dept, (int)PaymentProviders.Stripe, "u1", "ip", "ua");
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("payments_not_available_in_region");
		}

		[Test]
		public async Task Begin_connect_signs_a_single_use_state_and_complete_connect_consumes_it()
		{
			_provider.Setup(p => p.CompleteConnectAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PaymentConnectionFacts { ExternalAccountId = Acct, DisplayName = "Test Fire District", Country = "US", DefaultCurrency = "USD", ChargesEnabled = true, Capabilities = new Dictionary<string, bool> { ["card_payments"] = true } });
			var service = Build();

			var begin = await service.BeginConnectAsync(Dept, (int)PaymentProviders.Stripe, "u1", "ip", "ua");
			begin.AuthorizeUrl.Should().Contain("state=").And.Contain(Uri.EscapeDataString("/User/Invoicing/PaymentConnectCallback/stripe"));
			_cacheStore.Should().HaveCount(1);

			var callback = new Dictionary<string, string> { ["code"] = "ac_123", ["state"] = begin.State };
			var connection = await service.CompleteConnectAsync(Dept, (int)PaymentProviders.Stripe, callback, "u1", "ip", "ua");
			connection.Status.Should().Be((int)PaymentConnectionStatuses.Connected);
			connection.IsDefault.Should().BeTrue();
			connection.ExternalAccountId.Should().Be("acct_…6789", "the DTO carries the masked id");
			_savedConnections.Single().ExternalAccountId.Should().Be(Acct);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.PaymentConnectionConnected).Which.After.Should().NotContain(Acct);
			_email.Verify(e => e.SendNotificationAsync("admin", It.Is<string>(m => m.Contains("connected")), Dept, null), Times.Once);

			Func<Task> replay = () => service.CompleteConnectAsync(Dept, (int)PaymentProviders.Stripe, callback, "u1", "ip", "ua");
			await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("payments_state_invalid");
		}

		[Test]
		public async Task Complete_connect_binds_the_state_to_department_and_user_and_refuses_a_declined_hand_off()
		{
			var service = Build();
			var begin = await service.BeginConnectAsync(Dept, (int)PaymentProviders.Stripe, "u1", "ip", "ua");

			Func<Task> otherUser = () => service.CompleteConnectAsync(Dept, (int)PaymentProviders.Stripe, new Dictionary<string, string> { ["code"] = "x", ["state"] = begin.State }, "u2", "ip", "ua");
			await otherUser.Should().ThrowAsync<InvalidOperationException>().WithMessage("payments_state_invalid");
			_cacheStore.Should().HaveCount(1, "a refused callback does not consume the state");

			Func<Task> declined = () => service.CompleteConnectAsync(Dept, (int)PaymentProviders.Stripe, new Dictionary<string, string> { ["error"] = "access_denied", ["state"] = begin.State }, "u1", "ip", "ua");
			await declined.Should().ThrowAsync<InvalidOperationException>().WithMessage("payments_connect_declined");
			_provider.Verify(p => p.CompleteConnectAsync(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- gates ------------------------------------------------------------------------------------------------

		[Test]
		public async Task Status_reports_the_first_failing_gate_in_order()
		{
			var service = Build();
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_no_connection");

			Connected("{\"charges_enabled\":false}");
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_connection_not_ready");

			Connected();
			var ok = await service.GetStatusAsync(Dept);
			ok.CanCollect.Should().BeTrue(); ok.BlockedReason.Should().BeNull(); ok.Connection.ExternalAccountId.Should().Be("acct_…6789");
			ok.AllowedPaymentMethods.Should().BeEquivalentTo(new[] { "card", "us_bank_account" });

			_identities.Setup(r => r.GetByDepartmentIdAsync(Dept)).ReturnsAsync(new DepartmentBillingIdentity { DepartmentId = Dept, OnlinePaymentsEnabled = false });
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_not_enabled_for_department");

			_access.Setup(a => a.CanUseInvoicingAsync(Dept)).ReturnsAsync(false);
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_addon_required");

			_toggles.Setup(t => t.IsEnabledAsync(FeatureFlagKeys.OnlinePayments, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_flag_off");

			PaymentConnectConfig.Enabled = false;
			(await service.GetStatusAsync(Dept)).BlockedReason.Should().Be("payments_not_available_in_region");
		}

		[TestCase((int)InvoiceStatus.Draft, 250, 0, "USD", "payments_invoice_not_payable")]
		[TestCase((int)InvoiceStatus.Void, 250, 0, "USD", "payments_invoice_not_payable")]
		[TestCase((int)InvoiceStatus.Paid, 250, 250, "USD", "payments_invoice_paid")]
		[TestCase((int)InvoiceStatus.Sent, 250, 250, "USD", "payments_zero_balance")]
		[TestCase((int)InvoiceStatus.Sent, 250, 0, "JPY", "payments_currency_mismatch")]
		[TestCase((int)InvoiceStatus.Sent, 250, 0, "EUR", "payments_currency_mismatch")]
		public async Task Create_request_refusal_matrix(int status, decimal total, decimal paid, string currency, string expected)
		{
			Connected();
			OpenInvoice(status, total, paid, currency);
			Func<Task> act = () => Build().CreatePaymentRequestAsync("inv-1", Dept, (int)PaymentRequestSources.Web, "u1", "ip", "ua");
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expected);
			_provider.Verify(p => p.CreatePaymentRequestAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<PaymentRequestSpec>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Create_request_reuses_an_open_unexpired_request_for_the_same_balance()
		{
			Connected(); OpenInvoice(); var open = OpenRequest();
			var result = await Build().CreatePaymentRequestAsync("inv-1", Dept, (int)PaymentRequestSources.Web, "u1", "ip", "ua");
			result.Should().BeSameAs(open);
			_provider.Verify(p => p.CreatePaymentRequestAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<PaymentRequestSpec>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Create_request_opens_a_hosted_request_for_the_full_balance_with_pay_page_return_urls()
		{
			Connected(); OpenInvoice(total: 250m, paid: 100m);
			PaymentRequestSpec captured = null;
			_provider.Setup(p => p.CreatePaymentRequestAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<PaymentRequestSpec>(), It.IsAny<CancellationToken>()))
				.Callback<DepartmentPaymentConnection, PaymentRequestSpec, CancellationToken>((_, s, __) => captured = s)
				.ReturnsAsync(new PaymentRequestCreation { ExternalReference = "cs_new", PaymentIntentId = "pi_new", HostedUrl = "https://checkout.stripe.com/c/pay/cs_new", ExpiresOn = DateTime.UtcNow.AddHours(23) });

			var request = await Build().CreatePaymentRequestAsync("inv-1", Dept, (int)PaymentRequestSources.PayPage, null, "ip", "ua");

			request.Status.Should().Be((int)PaymentRequestStatuses.Opened);
			request.Amount.Should().Be(150m);
			request.ExternalReference.Should().Be("cs_new");
			captured.Amount.Should().Be(150m);
			captured.PaymentRequestId.Should().Be(request.InvoicePaymentRequestId, "the row is allocated before the provider sees it");
			captured.CustomerEmail.Should().Be("ap@customer.test");
			captured.SuccessUrl.Should().StartWith("https://pay.example.test/pay/").And.EndWith("/return");
			captured.CancelUrl.Should().EndWith("/cancel");
			captured.PaymentMethodTypes.Should().BeEquivalentTo(new[] { "card", "us_bank_account" });
			captured.IdempotencyKey.Should().Be("invreq-" + request.InvoicePaymentRequestId);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.InvoicePaymentRequestCreated);
		}

		[Test]
		public async Task Create_request_marks_the_row_failed_when_the_provider_refuses()
		{
			Connected(); OpenInvoice();
			_provider.Setup(p => p.CreatePaymentRequestAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<PaymentRequestSpec>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("payments_provider_unavailable"));
			Func<Task> act = () => Build().CreatePaymentRequestAsync("inv-1", Dept, (int)PaymentRequestSources.Web, "u1", "ip", "ua");
			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("payments_provider_unavailable");
			_savedRequests.Single().Status.Should().Be((int)PaymentRequestStatuses.Failed);
		}

		[Test]
		public async Task ACH_is_dropped_from_the_request_when_the_account_lacks_the_capability()
		{
			Connected("{\"charges_enabled\":true,\"card_payments\":true,\"us_bank_account_ach_payments\":false}"); OpenInvoice();
			PaymentRequestSpec captured = null;
			_provider.Setup(p => p.CreatePaymentRequestAsync(It.IsAny<DepartmentPaymentConnection>(), It.IsAny<PaymentRequestSpec>(), It.IsAny<CancellationToken>()))
				.Callback<DepartmentPaymentConnection, PaymentRequestSpec, CancellationToken>((_, s, __) => captured = s)
				.ReturnsAsync(new PaymentRequestCreation { ExternalReference = "cs_new", HostedUrl = "https://checkout.stripe.com/x", ExpiresOn = DateTime.UtcNow.AddHours(23) });
			await Build().CreatePaymentRequestAsync("inv-1", Dept, (int)PaymentRequestSources.Web, "u1", "ip", "ua");
			captured.PaymentMethodTypes.Should().BeEquivalentTo(new[] { "card" });
		}

		[Test]
		public async Task Pay_page_model_degrades_for_expired_links_paid_invoices_and_unoffered_payment()
		{
			var service = Build();
			(await service.GetPayPageModelAsync(service.CreatePayPageToken(Dept, "inv-1", DateTime.UtcNow.AddMinutes(-1)))).UnavailableReason.Should().Be("pay_link_expired");
			(await service.GetPayPageModelAsync("garbage")).UnavailableReason.Should().Be("pay_link_invalid");

			OpenInvoice(total: 250m, paid: 250m, status: (int)InvoiceStatus.Paid);
			var paid = await service.GetPayPageModelAsync(service.CreatePayPageToken(Dept, "inv-1", DateTime.UtcNow.AddDays(1)));
			paid.Paid.Should().BeTrue(); paid.Available.Should().BeFalse(); paid.UnavailableReason.Should().Be("pay_invoice_paid"); paid.DepartmentName.Should().Be("Test Fire");

			OpenInvoice();
			(await service.GetPayPageModelAsync(service.CreatePayPageToken(Dept, "inv-1", DateTime.UtcNow.AddDays(1)))).UnavailableReason.Should().Be("pay_not_offered", "no connection yet");

			Connected(); OpenRequest();
			var ready = await service.GetPayPageModelAsync(service.CreatePayPageToken(Dept, "inv-1", DateTime.UtcNow.AddDays(1)));
			ready.Available.Should().BeTrue(); ready.AmountDue.Should().Be(250m); ready.InvoiceNumber.Should().Be(42); ready.OpenHostedUrl.Should().Be("https://checkout.stripe.com/c/pay/cs_1");
		}

		[Test]
		public async Task Build_pay_page_url_is_null_when_payment_is_not_offered_and_a_pay_link_otherwise()
		{
			OpenInvoice();
			(await Build().BuildPayPageUrlAsync("inv-1", Dept)).Should().BeNull();
			Connected();
			var url = await Build().BuildPayPageUrlAsync("inv-1", Dept);
			url.Should().StartWith("https://pay.example.test/pay/");
			InvoicePaymentsService.TryDecodePayPageToken(url.Substring(url.LastIndexOf('/') + 1), out _, out var invoiceId, out var expires).Should().BeTrue();
			invoiceId.Should().Be("inv-1"); expires.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
		}

		// ---- truth ------------------------------------------------------------------------------------------------

		[Test]
		public async Task Duplicate_events_are_absorbed_but_a_failed_event_may_be_retried()
		{
			_events.Setup(r => r.GetByExternalEventIdAsync((int)PaymentProviders.Stripe, "evt_1")).ReturnsAsync(new PaymentConnectEvent { ExternalEventId = "evt_1", Outcome = (int)PaymentEventOutcomes.Applied });
			(await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded), "{}")).Should().Be(PaymentEventOutcomes.Duplicate);

			Connected(); OpenInvoice(); OpenRequest();
			_invoicing.Setup(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoicePayment p, string _, string __, string ___, CancellationToken ____) => { p.InvoicePaymentId = "pay-1"; return p; });
			_events.Setup(r => r.GetByExternalEventIdAsync((int)PaymentProviders.Stripe, "evt_1")).ReturnsAsync(new PaymentConnectEvent { ExternalEventId = "evt_1", Outcome = (int)PaymentEventOutcomes.Failed });
			(await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded), "{}")).Should().Be(PaymentEventOutcomes.Applied);
		}

		[Test]
		public async Task Livemode_mismatch_is_rejected_and_audited_without_touching_money()
		{
			Connected(); OpenInvoice(); OpenRequest();
			var outcome = await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded, live: true), "{}");
			outcome.Should().Be(PaymentEventOutcomes.Rejected);
			_savedEvents.Single().Outcome.Should().Be((int)PaymentEventOutcomes.Rejected);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.PaymentWebhookRejected && !a.Successful);
			_invoicing.Verify(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Unknown_accounts_and_unmatched_requests_are_ignored_and_counted()
		{
			(await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded, account: "acct_other"), "{}")).Should().Be(PaymentEventOutcomes.Ignored);
			Connected();
			(await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded, id: "evt_2"), "{}")).Should().Be(PaymentEventOutcomes.Ignored, "no request carries cs_1");
			_savedEvents.Should().HaveCount(2).And.OnlyContain(e => e.Outcome == (int)PaymentEventOutcomes.Ignored);
		}

		[Test]
		public async Task Succeeded_records_an_online_payment_through_the_invoicing_choke_point_and_completes_the_request()
		{
			Connected(); OpenInvoice(); var request = OpenRequest();
			_provider.Setup(p => p.GetChargeFactsAsync(It.IsAny<DepartmentPaymentConnection>(), "pi_1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PaymentChargeFacts { ChargeId = "ch_1", PaymentIntentId = "pi_1", Amount = 250m, Currency = "USD", Fee = 7.55m, Net = 242.45m, PayerEmail = "payer@customer.test", MethodSummary = "Visa •••• 4242", ReceiptUrl = "https://pay.stripe.com/receipts/x", PaidOn = DateTime.UtcNow });
			InvoicePayment recorded = null;
			_invoicing.Setup(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync((InvoicePayment p, string _, string __, string ___, CancellationToken ____) => { p.InvoicePaymentId = "pay-1"; recorded = p; return p; });

			var outcome = await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded), "{\"id\":\"evt_1\"}");

			outcome.Should().Be(PaymentEventOutcomes.Applied);
			recorded.Method.Should().Be((int)InvoicePaymentMethods.Online);
			recorded.GatewayTransactionId.Should().Be("pi_1");
			recorded.PaymentRequestId.Should().Be("req-1");
			recorded.Provider.Should().Be((int)PaymentProviders.Stripe);
			recorded.ProviderFeeAmount.Should().Be(7.55m); recorded.NetAmount.Should().Be(242.45m);
			recorded.PaymentMethodSummary.Should().Be("Visa •••• 4242"); recorded.ReceiptUrl.Should().StartWith("https://");
			request.Status.Should().Be((int)PaymentRequestStatuses.Completed); request.InvoicePaymentId.Should().Be("pay-1"); request.CompletedOn.Should().NotBeNull();
			_savedEvents.Single().Outcome.Should().Be((int)PaymentEventOutcomes.Applied);
			_savedEvents.Single().DepartmentId.Should().Be(Dept);
			_savedEvents.Single().PayloadJson.Should().Be("{\"id\":\"evt_1\"}");
		}

		[Test]
		public async Task ACH_moves_the_request_to_processing_then_succeeds_and_a_failure_closes_it()
		{
			Connected(); OpenInvoice(); var request = OpenRequest();
			var service = Build();
			(await service.ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentProcessing, id: "evt_p"), "{}")).Should().Be(PaymentEventOutcomes.Applied);
			request.Status.Should().Be((int)PaymentRequestStatuses.Processing);

			(await service.ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentFailed, id: "evt_f"), "{}")).Should().Be(PaymentEventOutcomes.Applied);
			request.Status.Should().Be((int)PaymentRequestStatuses.Failed);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.InvoicePaymentRequestFailed);

			// A late success for a closed request still records the money: the invoicing choke point dedupes on the gateway id.
			_invoicing.Setup(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoicePayment p, string _, string __, string ___, CancellationToken ____) => { p.InvoicePaymentId = "pay-1"; return p; });
			(await service.ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentSucceeded, id: "evt_s"), "{}")).Should().Be(PaymentEventOutcomes.Applied);
			request.Status.Should().Be((int)PaymentRequestStatuses.Completed);
		}

		[Test]
		public async Task Expiry_refund_and_dispute_events_route_to_the_invoicing_service()
		{
			Connected(); OpenInvoice(); var request = OpenRequest();
			var payment = new InvoicePayment { InvoicePaymentId = "pay-1", InvoiceId = "inv-1", DepartmentId = Dept, Amount = 250m, Provider = (int)PaymentProviders.Stripe, GatewayTransactionId = "pi_1" };
			_payments.Setup(r => r.GetByGatewayTransactionIdAsync((int)PaymentProviders.Stripe, "pi_1")).ReturnsAsync(payment);
			_invoicing.Setup(i => i.ApplyPaymentRefundAsync("pay-1", Dept, 100m, false, InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
			_invoicing.Setup(i => i.ApplyPaymentDisputeAsync("pay-1", Dept, It.IsAny<InvoiceDisputeStages>(), InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(payment);
			var service = Build();

			(await service.ApplyProviderEventAsync(Envelope(PaymentEventKinds.RequestExpired, id: "evt_e"), "{}")).Should().Be(PaymentEventOutcomes.Applied);
			request.Status.Should().Be((int)PaymentRequestStatuses.Expired);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.InvoicePaymentRequestExpired);

			var refund = Envelope(PaymentEventKinds.PaymentRefunded, id: "evt_r"); refund.RefundedAmount = 100m;
			(await service.ApplyProviderEventAsync(refund, "{}")).Should().Be(PaymentEventOutcomes.Applied);
			_invoicing.Verify(i => i.ApplyPaymentRefundAsync("pay-1", Dept, 100m, false, InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>()), Times.Once);

			(await service.ApplyProviderEventAsync(Envelope(PaymentEventKinds.PaymentDisputed, id: "evt_d"), "{}")).Should().Be(PaymentEventOutcomes.Applied);
			_invoicing.Verify(i => i.ApplyPaymentDisputeAsync("pay-1", Dept, InvoiceDisputeStages.Opened, InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>()), Times.Once);

			var lost = Envelope(PaymentEventKinds.DisputeClosed, id: "evt_l"); lost.DisputeLost = true;
			(await service.ApplyProviderEventAsync(lost, "{}")).Should().Be(PaymentEventOutcomes.Applied);
			_invoicing.Verify(i => i.ApplyPaymentDisputeAsync("pay-1", Dept, InvoiceDisputeStages.Lost, InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>()), Times.Once);

			var won = Envelope(PaymentEventKinds.DisputeClosed, id: "evt_w"); won.DisputeLost = false;
			(await service.ApplyProviderEventAsync(won, "{}")).Should().Be(PaymentEventOutcomes.Applied);
			_invoicing.Verify(i => i.ApplyPaymentDisputeAsync("pay-1", Dept, InvoiceDisputeStages.Won, InvoicePaymentsService.SystemUserId, null, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Revoked_connection_is_closed_open_requests_cancelled_and_admins_told()
		{
			var connection = Connected(); OpenInvoice(); var request = OpenRequest();
			_requests.Setup(r => r.GetOpenByConnectionAsync("conn-1")).ReturnsAsync(new List<InvoicePaymentRequest> { request });
			_identities.Setup(r => r.GetByDepartmentIdAsync(Dept)).ReturnsAsync(new DepartmentBillingIdentity { DepartmentId = Dept, OnlinePaymentsEnabled = true, DefaultPaymentConnectionId = "conn-1" });

			(await Build().ApplyProviderEventAsync(Envelope(PaymentEventKinds.ConnectionRevoked), "{}")).Should().Be(PaymentEventOutcomes.Applied);

			connection.Status.Should().Be((int)PaymentConnectionStatuses.Revoked); connection.IsDefault.Should().BeFalse(); connection.DisconnectedOn.Should().NotBeNull();
			request.Status.Should().Be((int)PaymentRequestStatuses.Cancelled);
			_provider.Verify(p => p.CancelPaymentRequestAsync(connection, "cs_1", It.IsAny<CancellationToken>()), Times.Once);
			_identities.Verify(r => r.SaveOrUpdateAsync(It.Is<DepartmentBillingIdentity>(i => i.DefaultPaymentConnectionId == null), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.PaymentConnectionRevoked);
			_email.Verify(e => e.SendNotificationAsync("admin", It.Is<string>(m => m.Contains("revoked")), Dept, null), Times.Once);
		}

		[Test]
		public async Task Account_update_with_charges_disabled_downgrades_the_connection_and_re_enables_it_later()
		{
			var connection = Connected();
			var down = Envelope(PaymentEventKinds.ConnectionUpdated, id: "evt_1"); down.ChargesEnabled = false; down.Capabilities = new Dictionary<string, bool> { ["charges_enabled"] = false };
			(await Build().ApplyProviderEventAsync(down, "{}")).Should().Be(PaymentEventOutcomes.Applied);
			connection.Status.Should().Be((int)PaymentConnectionStatuses.ActionRequired);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.PaymentConnectionActionRequired);

			var up = Envelope(PaymentEventKinds.ConnectionUpdated, id: "evt_2"); up.ChargesEnabled = true;
			(await Build().ApplyProviderEventAsync(up, "{}")).Should().Be(PaymentEventOutcomes.Applied);
			connection.Status.Should().Be((int)PaymentConnectionStatuses.Connected);
		}

		[Test]
		public async Task A_throwing_apply_records_a_failed_outcome_and_the_receipt_is_not_accepted()
		{
			Connected(); OpenInvoice(); OpenRequest();
			_invoicing.Setup(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("database down"));
			_provider.Setup(p => p.TryParseWebhook("body", "sig", out It.Ref<PaymentProviderEventEnvelope>.IsAny, out It.Ref<string>.IsAny))
				.Returns((string _, string __, out PaymentProviderEventEnvelope env, out string err) => { env = Envelope(PaymentEventKinds.PaymentSucceeded); err = null; return true; });

			var receipt = await Build().ReceiveWebhookAsync((int)PaymentProviders.Stripe, "sig", "body", "1.2.3.4");
			receipt.Accepted.Should().BeFalse(); receipt.Outcome.Should().Be(PaymentEventOutcomes.Failed);
			_savedEvents.Single().Outcome.Should().Be((int)PaymentEventOutcomes.Failed);
			_savedEvents.Single().Error.Should().Contain("database down");
		}

		[Test]
		public async Task Receive_webhook_refuses_when_disabled_and_records_a_rejected_signature_with_a_hashed_body()
		{
			PaymentConnectConfig.Enabled = false;
			var disabled = await Build().ReceiveWebhookAsync((int)PaymentProviders.Stripe, "sig", "body", "ip");
			disabled.Accepted.Should().BeFalse(); disabled.Error.Should().Be("payments_disabled");
			_savedEvents.Should().BeEmpty();

			PaymentConnectConfig.Enabled = true;
			_provider.Setup(p => p.TryParseWebhook("body", "bad", out It.Ref<PaymentProviderEventEnvelope>.IsAny, out It.Ref<string>.IsAny))
				.Returns((string _, string __, out PaymentProviderEventEnvelope env, out string err) => { env = null; err = "signature_invalid"; return false; });
			var rejected = await Build().ReceiveWebhookAsync((int)PaymentProviders.Stripe, "bad", "body", "ip");
			rejected.Accepted.Should().BeFalse(); rejected.Outcome.Should().Be(PaymentEventOutcomes.Rejected);
			_savedEvents.Single().Outcome.Should().Be((int)PaymentEventOutcomes.Rejected);
			_savedEvents.Single().PayloadJson.Should().BeNull("an unverified body is never stored");
			var audit = _audits.Should().ContainSingle(a => a.Type == AuditLogTypes.PaymentWebhookRejected).Which;
			audit.Successful.Should().BeFalse(); audit.After.Should().Contain("BodySha256").And.NotContain("body\"");
		}

		// ---- reconciliation ---------------------------------------------------------------------------------------

		[Test]
		public void Reconciliation_envelopes_mirror_what_the_webhook_would_have_said()
		{
			var connection = new DepartmentPaymentConnection { ExternalAccountId = Acct };
			var request = new InvoicePaymentRequest { ExternalReference = "cs_1", Provider = (int)PaymentProviders.Stripe, Amount = 250m, Currency = "USD", Status = (int)PaymentRequestStatuses.Opened };

			var paid = InvoicePaymentsService.ReconciliationEnvelope(request, connection, new PaymentRequestState { SessionStatus = "complete", PaymentStatus = "paid", PaymentIntentId = "pi_1", Charge = new PaymentChargeFacts { Amount = 250m, Fee = 1m } });
			paid.Kind.Should().Be(PaymentEventKinds.PaymentSucceeded); paid.ExternalEventId.Should().Be("reconcile:cs_1:PaymentSucceeded"); paid.PaymentIntentId.Should().Be("pi_1");

			InvoicePaymentsService.ReconciliationEnvelope(request, connection, new PaymentRequestState { SessionStatus = "expired", PaymentStatus = "unpaid" }).Kind.Should().Be(PaymentEventKinds.RequestExpired);
			InvoicePaymentsService.ReconciliationEnvelope(request, connection, new PaymentRequestState { SessionStatus = "complete", PaymentStatus = "unpaid" }).Kind.Should().Be(PaymentEventKinds.PaymentProcessing);
			InvoicePaymentsService.ReconciliationEnvelope(request, connection, new PaymentRequestState { SessionStatus = "open", PaymentStatus = "unpaid" }).Should().BeNull();
			request.Status = (int)PaymentRequestStatuses.Processing;
			InvoicePaymentsService.ReconciliationEnvelope(request, connection, new PaymentRequestState { SessionStatus = "complete", PaymentStatus = "unpaid" }).Should().BeNull("nothing changed");
		}

		[Test]
		public async Task Reconcile_applies_a_missed_webhook_once_and_stamps_the_pass()
		{
			Connected(); OpenInvoice(); var request = OpenRequest();
			_requests.Setup(r => r.GetOpenOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<InvoicePaymentRequest> { request });
			_provider.Setup(p => p.GetPaymentRequestStateAsync(It.IsAny<DepartmentPaymentConnection>(), "cs_1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PaymentRequestState { SessionStatus = "complete", PaymentStatus = "paid", PaymentIntentId = "pi_1", Charge = new PaymentChargeFacts { ChargeId = "ch_1", PaymentIntentId = "pi_1", Amount = 250m, Currency = "USD" } });
			_invoicing.Setup(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((InvoicePayment p, string _, string __, string ___, CancellationToken ____) => { p.InvoicePaymentId = "pay-1"; return p; });
			var service = Build();

			(await service.ReconcileOpenRequestsAsync(DateTime.UtcNow)).Should().Be(1);
			request.Status.Should().Be((int)PaymentRequestStatuses.Completed);
			_cacheStore.Should().ContainKey(InvoicePaymentsService.LastReconcileCacheKey);

			// The same read-back next cycle is a duplicate of the same synthetic event id.
			_events.Setup(r => r.GetByExternalEventIdAsync((int)PaymentProviders.Stripe, "reconcile:cs_1:PaymentSucceeded")).ReturnsAsync(_savedEvents.Single());
			(await service.ReconcileOpenRequestsAsync(DateTime.UtcNow)).Should().Be(0);
			_invoicing.Verify(i => i.RecordPaymentAsync(It.IsAny<InvoicePayment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Expire_pass_closes_open_requests_but_leaves_asynchronous_ones_to_the_provider()
		{
			var open = new InvoicePaymentRequest { InvoicePaymentRequestId = "a", Status = (int)PaymentRequestStatuses.Opened, ExpiresOn = DateTime.UtcNow.AddHours(-1) };
			var processing = new InvoicePaymentRequest { InvoicePaymentRequestId = "b", Status = (int)PaymentRequestStatuses.Processing, ExpiresOn = DateTime.UtcNow.AddHours(-1) };
			_requests.Setup(r => r.GetOpenExpiredAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<InvoicePaymentRequest> { open, processing });
			(await Build().ExpireStaleRequestsAsync(DateTime.UtcNow)).Should().Be(1);
			open.Status.Should().Be((int)PaymentRequestStatuses.Expired);
			processing.Status.Should().Be((int)PaymentRequestStatuses.Processing);
		}

		[Test]
		public async Task Reverify_downgrades_a_revoked_account_and_refreshes_a_healthy_one()
		{
			var connection = Connected();
			_connections.Setup(r => r.GetStaleVerifiedAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<DepartmentPaymentConnection> { connection });
			_provider.Setup(p => p.VerifyConnectionAsync(connection, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("payments_connection_revoked"));
			(await Build().ReverifyConnectionsAsync(DateTime.UtcNow)).Should().Be(1);
			connection.Status.Should().Be((int)PaymentConnectionStatuses.Revoked);

			var healthy = Connected();
			_connections.Setup(r => r.GetStaleVerifiedAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<DepartmentPaymentConnection> { healthy });
			_provider.Setup(p => p.VerifyConnectionAsync(healthy, It.IsAny<CancellationToken>())).ReturnsAsync(new PaymentConnectionFacts { ExternalAccountId = Acct, DisplayName = "Renamed", ChargesEnabled = true, Capabilities = new Dictionary<string, bool> { ["charges_enabled"] = true } });
			(await Build().ReverifyConnectionsAsync(DateTime.UtcNow)).Should().Be(1);
			healthy.DisplayName.Should().Be("Renamed"); healthy.LastVerifiedOn.Should().NotBeNull(); healthy.Status.Should().Be((int)PaymentConnectionStatuses.Connected);
		}

		// ---- health with the ledger -------------------------------------------------------------------------------

		[Test]
		public async Task Health_reads_the_ledger_for_staleness_rejections_and_overdue_requests()
		{
			PaymentConnectConfig.StripeConnectWebhookSecret = "whsec_x"; PaymentConnectConfig.WebhookEndpointProbeEnabled = false;
			_requests.Setup(r => r.HasActivitySinceAsync(It.IsAny<DateTime>())).ReturnsAsync(true);
			_requests.Setup(r => r.CountOpenOlderThanAsync(It.IsAny<DateTime>())).ReturnsAsync(2);
			_events.Setup(r => r.GetNewestReceivedOnAsync()).ReturnsAsync(DateTime.UtcNow.AddHours(-30));
			_events.Setup(r => r.CountByOutcomeSinceAsync((int)PaymentEventOutcomes.Rejected, It.IsAny<DateTime>())).ReturnsAsync(1);
			_cacheStore[InvoicePaymentsService.LastReconcileCacheKey] = DateTime.UtcNow.AddMinutes(-5).ToString("O");

			var health = await Build().GetWebhookHealthAsync();
			health.Enabled.Should().BeTrue(); health.WebhookConfigured.Should().BeTrue();
			health.Stale.Should().BeTrue(); health.OverdueOpenRequests.Should().Be(2); health.RejectedLastHour.Should().Be(1);
			health.LastReconcileOn.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-5), TimeSpan.FromSeconds(5));
			health.Healthy.Should().BeFalse();

			_requests.Setup(r => r.HasActivitySinceAsync(It.IsAny<DateTime>())).ReturnsAsync(false);
			_requests.Setup(r => r.CountOpenOlderThanAsync(It.IsAny<DateTime>())).ReturnsAsync(0);
			_events.Setup(r => r.CountByOutcomeSinceAsync((int)PaymentEventOutcomes.Rejected, It.IsAny<DateTime>())).ReturnsAsync(0);
			(await Build().GetWebhookHealthAsync()).Healthy.Should().BeTrue("no activity is never stale");
		}

		[Test]
		public void Helpers_mask_ids_and_parse_methods_and_capabilities_defensively()
		{
			DepartmentPaymentConnection.Mask(Acct).Should().Be("acct_…6789");
			DepartmentPaymentConnection.Mask("abc").Should().Be("…abc");
			InvoicePaymentsService.ParseMethods(null).Should().BeEquivalentTo(new[] { "card" });
			InvoicePaymentsService.ParseMethods("us_bank_account, paypal,CARD").Should().BeEquivalentTo(new[] { "us_bank_account", "card" });
			InvoicePaymentsService.ParseCapabilities("not json").Should().BeEmpty();
			InvoicePaymentsService.ParseCapabilities("{\"card_payments\":true}")["CARD_PAYMENTS"].Should().BeTrue();
			var masked = InvoicePaymentsService.Masked(new DepartmentPaymentConnection { ExternalAccountId = Acct, AccessTokenCiphertext = "secret", RefreshTokenCiphertext = "secret" });
			masked.AccessTokenCiphertext.Should().BeNull(); masked.RefreshTokenCiphertext.Should().BeNull(); masked.ExternalAccountId.Should().Be("acct_…6789");
		}
	}
}
