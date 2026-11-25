using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>The LMS gate (§17.3) fails closed: flag, module switch and a paid Lms add-on window, built dark in wave 2.</summary>
	[TestFixture, NonParallelizable]
	public class LmsAccessServiceTests
	{
		private const int DepartmentId = 41;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ISubscriptionsService> _billing;
		private DepartmentModuleSettings _modules;
		private PaymentAddon _payment;
		private LmsAccessService _service;
		private string _billingUrl, _billingKey;

		[SetUp]
		public void SetUp()
		{
			_billingUrl = Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = Resgrid.Config.ApiConfig.BackendInternalApikey;
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			Resgrid.Config.ApiConfig.BackendInternalApikey = "unit-test-only";

			_flags = new Mock<IFeatureToggleService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_billing = new Mock<ISubscriptionsService>(MockBehavior.Strict);
			_modules = new DepartmentModuleSettings();
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _modules);
			SetFlag(true);
			_payment = new PaymentAddon
			{
				DepartmentId = DepartmentId, PlanAddonId = "lms-pack", TransactionId = "paid-invoice", EffectiveOn = DateTime.UtcNow.AddDays(-1), EndingOn = DateTime.UtcNow.AddDays(20)
			};
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false)).ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = "lms-pack", AddonType = (int)PlanAddonTypes.Lms } });
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == "lms-pack")))
				.ReturnsAsync(() => new List<PaymentAddon> { _payment });
			// Round 3 F: the gate also records what it saw (lapse tracking); no state row means no grace for an ended interval.
			_service = new LmsAccessService(_flags.Object, _settings.Object, _billing.Object, Mock.Of<Resgrid.Model.Repositories.ILmsAddonStatesRepository>(),
				Mock.Of<Resgrid.Model.Repositories.ILmsAddonLapsesRepository>(), Mock.Of<Resgrid.Model.Repositories.ILmsMemberSeatsRepository>(), Mock.Of<IDepartmentsService>());
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			Resgrid.Config.ApiConfig.BackendInternalApikey = _billingKey;
		}

		private void SetFlag(bool enabled) =>
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = enabled });

		[Test]
		public async Task With_flag_module_and_a_paid_window_the_department_authors_and_runs()
		{
			(await _service.IsFlagEnabledAsync(DepartmentId)).Should().BeTrue();
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeTrue();
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.Department)).Should().BeTrue();
		}

		[Test]
		public async Task The_flag_off_hides_everything_without_asking_billing()
		{
			SetFlag(false);
			(await _service.IsFlagEnabledAsync(DepartmentId)).Should().BeFalse();
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.Department)).Should().BeFalse();
			// A1 (R-S2): the Academy is the one LMS runtime that is not dark behind the flag; only the Trainings module closes it.
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.SystemPublished)).Should().BeTrue();
			_modules.TrainingDisabled = true;
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.SystemPublished)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task The_trainings_module_switch_closes_the_gate()
		{
			_modules.TrainingDisabled = true;
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task No_catalog_row_fails_closed_which_is_wave_two()
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false)).ReturnsAsync(new List<PlanAddon>());
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull();
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
		}

		[TestCase("SYSTEM")]
		[TestCase("expired")]
		[TestCase("forever")]
		public async Task Synthetic_expired_and_free_forever_payments_do_not_count(string kind)
		{
			if (kind == "SYSTEM") _payment.TransactionId = "SYSTEM";
			if (kind == "expired") _payment.EndingOn = DateTime.UtcNow.AddMinutes(-1);
			if (kind == "forever") _payment.EndingOn = DateTime.MaxValue;
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Unconfigured_billing_never_grants_the_add_on()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = null;
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task A_billing_failure_fails_closed()
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false)).ThrowsAsync(new InvalidOperationException("billing down"));
			(await _service.GetActiveAddonStateAsync(DepartmentId)).Should().BeNull();
			(await _service.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task System_published_content_runs_without_the_add_on_and_sponsored_sets_are_a_stub()
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false)).ReturnsAsync(new List<PlanAddon>());
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.SystemPublished)).Should().BeTrue("R-S2: Academy content runs for everyone");
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.Sponsored, 99)).Should().BeFalse("sponsorship is filled in by A1/T5");
			(await _service.CanRunItemAsync(DepartmentId, LmsRunnableOrigins.Department)).Should().BeFalse();
		}

		[Test]
		public async Task A_flag_evaluation_error_fails_closed()
		{
			_flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, DepartmentId)).ThrowsAsync(new InvalidOperationException("flags down"));
			(await _service.IsFlagEnabledAsync(DepartmentId)).Should().BeFalse();
			(await _service.IsFlagEnabledAsync(0)).Should().BeFalse();
		}
	}
}
