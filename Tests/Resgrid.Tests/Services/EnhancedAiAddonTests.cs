using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace Resgrid.Tests.Services
{
	/// <summary>Enhanced AI add-on purchase and entitlement (enhanced-ai-addon-plan.md): catalog mapping, billing proxy and paid gate.</summary>
	[TestFixture, NonParallelizable]
	public class EnhancedAiAddonTests
	{
		private const int DepartmentId = 91;
		private bool _testMode;
		private string _paddleLive;
		private string _paddleTest;
		private string _billingUrl;
		private string _billingKey;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ISubscriptionsService> _subscriptions;
		private DepartmentModuleSettings _modules;
		private List<PaymentAddon> _payments;
		private EnhancedAiAccessService _access;
		private Mock<IDepartmentDataProtectionService> _protection;
		private DepartmentDataProtectionState _protectionState;

		[SetUp]
		public void SetUp()
		{
			_testMode = PaymentProviderConfig.IsTestMode;
			_paddleLive = PaymentProviderConfig.PaddleEnhancedAiAddon;
			_paddleTest = PaymentProviderConfig.PaddleEnhancedAiAddonTest;
			_billingUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = ApiConfig.BackendInternalApikey;
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			ApiConfig.BackendInternalApikey = "unit-test-only";

			_flags = new Mock<IFeatureToggleService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_subscriptions = new Mock<ISubscriptionsService>(MockBehavior.Strict);
			_modules = new DepartmentModuleSettings();
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _modules);
			SetFlag(FeatureFlagKeys.AiEnhanced, true);
			SetFlag(FeatureFlagKeys.AiNarratives, true);
			_payments = new List<PaymentAddon>
			{
				new PaymentAddon
				{
					DepartmentId = DepartmentId, PlanAddonId = AiAddonConfig.PlanAddonId, TransactionId = "in_paid",
					EffectiveOn = DateTime.UtcNow.AddDays(-2), EndingOn = DateTime.UtcNow.AddDays(28)
				}
			};
			_subscriptions.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.EnhancedAi, false)).ReturnsAsync(new List<PlanAddon>
			{
				new PlanAddon { PlanAddonId = AiAddonConfig.PlanAddonId, AddonType = (int)PlanAddonTypes.EnhancedAi }
			});
			_subscriptions.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId,
				It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == AiAddonConfig.PlanAddonId))).ReturnsAsync(() => _payments);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protectionState = DepartmentDataProtectionState.Disabled;
			_protection.Setup(p => p.GetStateAsync(DepartmentId, true)).ReturnsAsync(() => _protectionState);
			_access = new EnhancedAiAccessService(_flags.Object, _settings.Object, _subscriptions.Object, _protection.Object);
		}

		[TearDown]
		public void TearDown()
		{
			PaymentProviderConfig.IsTestMode = _testMode;
			PaymentProviderConfig.PaddleEnhancedAiAddon = _paddleLive;
			PaymentProviderConfig.PaddleEnhancedAiAddonTest = _paddleTest;
			SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			ApiConfig.BackendInternalApikey = _billingKey;
		}

		private void SetFlag(string key, bool enabled) =>
			_flags.Setup(f => f.EvaluateFreshAsync(key, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = enabled });

		[TestCase(PlanAddonTypes.ReadinessPro, true)]
		[TestCase(PlanAddonTypes.BusinessOperations, true)]
		[TestCase(PlanAddonTypes.EnhancedAi, true)]
		[TestCase(PlanAddonTypes.PTT, false)]
		[TestCase(PlanAddonTypes.ADP, false)]
		public void Only_the_monthly_add_ons_with_their_own_checkout_are_dedicated(PlanAddonTypes type, bool dedicated)
		{
			PlanAddon.IsDedicatedMonthlyAddon((int)type).Should().Be(dedicated);
		}

		[Test]
		public void Enhanced_ai_catalog_row_is_named_monthly_and_never_uses_a_live_price_in_test_mode()
		{
			var addon = new PlanAddon { AddonType = (int)PlanAddonTypes.EnhancedAi, ExternalId = "price_live_fixture", TestExternalId = "" };
			addon.GetAddonName().Should().Be("Enhanced AI");
			addon.GetEndDateFromNow().Should().BeCloseTo(DateTime.UtcNow.AddMonths(1), TimeSpan.FromMinutes(1));
			PaymentProviderConfig.IsTestMode = true;
			addon.GetExternalKey().Should().BeNull();
			PaymentProviderConfig.IsTestMode = false;
			addon.GetExternalKey().Should().Be("price_live_fixture");
		}

		[Test]
		public void Paddle_price_follows_the_environment_without_a_live_fallback()
		{
			PaymentProviderConfig.PaddleEnhancedAiAddon = "pri_live_fixture";
			PaymentProviderConfig.PaddleEnhancedAiAddonTest = "";
			PaymentProviderConfig.IsTestMode = true;
			PaymentProviderConfig.GetPaddleEnhancedAiAddonPriceId().Should().BeEmpty();
			PaymentProviderConfig.IsTestMode = false;
			PaymentProviderConfig.GetPaddleEnhancedAiAddonPriceId().Should().Be("pri_live_fixture");
		}

		[Test]
		public void Launch_prices_are_95_usd_and_145_eur_per_month()
		{
			AiAddonConfig.StripeMonthlyAmount.Should().Be(95m);
			AiAddonConfig.PaddleMonthlyAmount.Should().Be(145m);
		}

		[Test]
		public async Task Active_paid_window_grants_the_capability()
		{
			(await _access.HasActiveAddonAsync(DepartmentId)).Should().BeTrue();
			(await _access.CanUseAsync(DepartmentId, FeatureFlagKeys.AiNarratives)).Should().BeTrue();
		}

		[Test]
		public async Task Master_flag_module_switch_and_capability_flag_each_close_the_gate()
		{
			SetFlag(FeatureFlagKeys.AiEnhanced, false);
			(await _access.CanUseAsync(DepartmentId, FeatureFlagKeys.AiNarratives)).Should().BeFalse();
			(await _access.IsEnabledAsync(DepartmentId)).Should().BeFalse();
			SetFlag(FeatureFlagKeys.AiEnhanced, true);
			SetFlag(FeatureFlagKeys.AiNarratives, false);
			(await _access.CanUseAsync(DepartmentId, FeatureFlagKeys.AiNarratives)).Should().BeFalse();
			SetFlag(FeatureFlagKeys.AiNarratives, true);
			_modules.AiDisabled = true;
			(await _access.CanUseAsync(DepartmentId, FeatureFlagKeys.AiNarratives)).Should().BeFalse();
			(await _access.IsEnabledAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Synthesized_system_and_expired_payments_are_not_entitlements()
		{
			_payments[0].TransactionId = "SYSTEM";
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
			_payments[0].TransactionId = "in_paid";
			_payments[0].EndingOn = DateTime.UtcNow.AddMinutes(-1);
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
			_payments[0].EndingOn = DateTime.MaxValue;
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Unconfigured_billing_is_no_add_on_but_an_unreadable_billing_api_is_unknown()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = "";
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeFalse();
			_subscriptions.VerifyNoOtherCalls();

			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			_payments = null;
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull();
			(await _access.HasActiveAddonAsync(DepartmentId)).Should().BeFalse();

			_subscriptions.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.EnhancedAi, false)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await _access.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull();
		}

		[Test]
		public async Task Own_llm_provider_needs_the_add_on_on_the_hosted_service_but_not_on_an_open_source_install()
		{
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.Allowed, "the add-on window is active");

			SetFlag(FeatureFlagKeys.AiEnhanced, false);
			_modules.AiDisabled = true;
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.Allowed, "the department pays its own provider; only the add-on unlocks it");

			_payments[0].EndingOn = DateTime.UtcNow.AddMinutes(-1);
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.AddonRequired);

			_payments = null;
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.Unknown, "an unreadable Billing API never unlocks it");

			SystemBehaviorConfig.BillingApiBaseUrl = "";
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.Allowed, "an open-source install has no add-on to buy");
			(await _access.GetOwnLlmProviderStatusAsync(0)).Should().Be(OwnLlmProviderStatus.Unknown);
		}

		[TestCase(DepartmentDataProtectionState.EnrollmentQueued)]
		[TestCase(DepartmentDataProtectionState.Encrypting)]
		[TestCase(DepartmentDataProtectionState.Enabled)]
		[TestCase(DepartmentDataProtectionState.Rotating)]
		[TestCase(DepartmentDataProtectionState.OffboardingScheduled)]
		[TestCase(DepartmentDataProtectionState.Decrypting)]
		[TestCase(DepartmentDataProtectionState.Failed)]
		public async Task Advanced_data_protection_blocks_own_llm_provider_even_with_the_add_on_and_on_open_source(DepartmentDataProtectionState state)
		{
			_protectionState = state;
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.DataProtectionEnabled, "the add-on window is active but ADP wins");

			SystemBehaviorConfig.BillingApiBaseUrl = "";
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.DataProtectionEnabled);
		}

		[Test]
		public async Task An_unreadable_protection_state_never_allows_own_llm_provider()
		{
			_protection.Setup(p => p.GetStateAsync(DepartmentId, true)).ThrowsAsync(new InvalidOperationException("policy store down"));
			SystemBehaviorConfig.BillingApiBaseUrl = "";
			(await _access.GetOwnLlmProviderStatusAsync(DepartmentId)).Should().Be(OwnLlmProviderStatus.Unknown);
		}

		[Test]
		public void Live_provider_ids_are_set_and_test_mode_ids_stay_empty()
		{
			AiAddonConfig.StripeProductId.Should().Be("prod_VKDzUfEQokuRv5");
			AiAddonConfig.PaddleProductId.Should().Be("pro_01m3cdgjnjwgzyyg7pe0bz0526");
			_paddleLive.Should().Be("pri_01m3cdhy94qbcmdvjhqk3nkppt");
			AiAddonConfig.StripeTestProductId.Should().BeEmpty();
			AiAddonConfig.PaddleTestProductId.Should().BeEmpty();
			_paddleTest.Should().BeEmpty();
		}

		[Test]
		public async Task Billing_proxy_is_lazy_when_unconfigured_and_calls_the_ai_billing_routes()
		{
			var oldUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			try
			{
				SystemBehaviorConfig.BillingApiBaseUrl = "";
				var builder = new ContainerBuilder(); builder.RegisterModule<ServicesModule>();
				using var container = builder.Build(Autofac.Builder.ContainerBuildOptions.IgnoreStartableComponents);
				using var scope = container.BeginLifetimeScope();
				(await scope.Resolve<IAiBillingService>().GetAsync(DepartmentId)).Should().BeNull();

				SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.invalid";
				var handler = new BillingHandler();
				using var client = new RestClient(new RestClientOptions(SystemBehaviorConfig.BillingApiBaseUrl) { ConfigureMessageHandler = _ => handler }, configureSerialization: s => s.UseNewtonsoftJson());
				var service = new AiBillingService(() => client);
				(await service.GetAsync(DepartmentId)).Currency.Should().Be("USD");
				(await service.CancelRenewalAsync(DepartmentId)).Should().BeTrue();
				handler.Fail = true;
				(await service.BeginCheckoutAsync(DepartmentId)).Should().BeNull();
				handler.Paths.Should().Equal("/api/AiBilling/Status", "/api/AiBilling/CancelRenewal", "/api/AiBilling/Checkout");
			}
			finally { SystemBehaviorConfig.BillingApiBaseUrl = oldUrl; }
		}

		private sealed class BillingHandler : HttpMessageHandler
		{
			public readonly List<string> Paths = new List<string>();
			public bool Fail;
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Paths.Add(request.RequestUri.AbsolutePath);
				request.Headers.GetValues("X-API-Key").Should().ContainSingle().Which.Should().Be("unit-test-only");
				return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
				{
					Content = new StringContent(request.Method == HttpMethod.Post ? "true" : "{\"Provider\":\"Stripe\",\"Currency\":\"USD\",\"MonthlyAmount\":95}", System.Text.Encoding.UTF8, "application/json")
				});
			}
		}
	}
}
