using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ReadinessAccessServiceTests
	{
		private const int DepartmentId = 77;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ISubscriptionsService> _billing;
		private ReadinessAccessService _service;
		private string _billingUrl;
		private string _billingKey;
		private DepartmentModuleSettings _modules;
		private PaymentAddon _payment;

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
			SetFlag(FeatureFlagKeys.ChecklistsSystem, true);
			SetFlag(FeatureFlagKeys.MaintenanceWorkOrders, true);
			_payment = new PaymentAddon
			{
				DepartmentId = DepartmentId, PlanAddonId = "readiness-monthly", TransactionId = "paid-invoice",
				EffectiveOn = DateTime.UtcNow.AddDays(-1), EndingOn = DateTime.UtcNow.AddDays(20)
			};
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro)).ReturnsAsync(new List<PlanAddon>
			{
				new PlanAddon { PlanAddonId = _payment.PlanAddonId, AddonType = (int)PlanAddonTypes.ReadinessPro }
			});
			_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId,
				It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == "readiness-monthly")))
				.ReturnsAsync(() => new List<PaymentAddon> { _payment });
			_service = new ReadinessAccessService(_flags.Object, _settings.Object, _billing.Object);
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			Resgrid.Config.ApiConfig.BackendInternalApikey = _billingKey;
		}

		private void SetFlag(string key, bool enabled)
		{
			_flags.Setup(f => f.IsEnabledAsync(key, DepartmentId, false, null)).ReturnsAsync(enabled);
			_flags.Setup(f => f.EvaluateFreshAsync(key, DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = enabled });
		}

		[Test]
		public async Task Free_checklists_never_query_billing_or_require_maintenance()
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = null;
			Resgrid.Config.ApiConfig.BackendInternalApikey = null;
			SetFlag(FeatureFlagKeys.MaintenanceWorkOrders, false);
			_modules.MaintenanceDisabled = true;
			(await _service.CanUseChecklistsAsync(DepartmentId)).Should().BeTrue();
			_billing.VerifyNoOtherCalls();
		}

		[TestCase(false, false, false)]
		[TestCase(true, true, false)]
		[TestCase(false, true, false)]
		[TestCase(true, false, true)]
		public async Task Checklist_flag_and_module_are_both_required(bool flag, bool disabled, bool expected)
		{
			SetFlag(FeatureFlagKeys.ChecklistsSystem, flag);
			_modules.ChecklistsDisabled = disabled;
			(await _service.CanUseChecklistsAsync(DepartmentId)).Should().Be(expected);
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Maintenance_is_independent_of_the_checklist_flag_and_module()
		{
			SetFlag(FeatureFlagKeys.ChecklistsSystem, false);
			_modules.ChecklistsDisabled = true;
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeTrue();
		}

		[TestCase(false, false)]
		[TestCase(true, true)]
		public async Task Maintenance_rollout_and_module_short_circuit_billing(bool flag, bool disabled)
		{
			SetFlag(FeatureFlagKeys.MaintenanceWorkOrders, flag);
			_modules.MaintenanceDisabled = disabled;
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[TestCase(null, "key")]
		[TestCase(" ", "key")]
		[TestCase("https://billing.example.invalid", null)]
		public async Task Unconfigured_billing_cannot_grant_a_synthetic_free_entitlement(string url, string key)
		{
			Resgrid.Config.SystemBehaviorConfig.BillingApiBaseUrl = url;
			Resgrid.Config.ApiConfig.BackendInternalApikey = key;
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[TestCase("wrong-department")]
		[TestCase("wrong-addon")]
		[TestCase("future")]
		[TestCase("expired")]
		[TestCase("missing-start")]
		[TestCase("system")]
		[TestCase("forever")]
		[TestCase("null")]
		public async Task Invalid_payment_does_not_grant_access(string kind)
		{
			switch (kind)
			{
				case "wrong-department": _payment.DepartmentId++; break;
				case "wrong-addon": _payment.PlanAddonId = "adp"; break;
				case "future": _payment.EffectiveOn = DateTime.UtcNow.AddDays(1); break;
				case "expired": _payment.EndingOn = DateTime.UtcNow.AddSeconds(-1); break;
				case "missing-start": _payment.EffectiveOn = default; break;
				case "system": _payment.TransactionId = "system"; break;
				case "forever": _payment.EndingOn = DateTime.MaxValue; break;
				case "null": _payment = null; break;
			}
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Cancellation_preserves_paid_time_but_not_expired_time()
		{
			_payment.IsCancelled = true;
			_payment.CancelledOn = DateTime.UtcNow.AddHours(-1);
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeTrue();
			_payment.EndingOn = DateTime.UtcNow.AddSeconds(-1);
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
		}

		[TestCase(1)]
		[TestCase(2)]
		public async Task Other_addon_catalogs_cannot_grant_maintenance(int addonType)
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro)).ReturnsAsync(new List<PlanAddon>
			{
				new PlanAddon { AddonType = addonType, PlanAddonId = "readiness-monthly" }, null
			});
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
			_billing.Verify(s => s.GetCurrentPaymentAddonsForDepartmentAsync(It.IsAny<int>(), It.IsAny<List<string>>()), Times.Never);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Missing_billing_payloads_fail_closed(bool missingCatalog)
		{
			if (missingCatalog)
				_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro)).ReturnsAsync((List<PlanAddon>)null);
			else
				_billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ReturnsAsync((List<PaymentAddon>)null);
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
		}

		[Test]
		public async Task Billing_outage_denies_maintenance_and_does_not_affect_checklists()
		{
			_billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.ReadinessPro)).ThrowsAsync(new TimeoutException("Test outage"));
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
			(await _service.CanUseChecklistsAsync(DepartmentId)).Should().BeTrue();
		}

		[TestCase(0)]
		[TestCase(-1)]
		public async Task Invalid_department_ids_do_not_query_dependencies(int departmentId)
		{
			(await _service.CanUseChecklistsAsync(departmentId)).Should().BeFalse();
			(await _service.CanUseMaintenanceAsync(departmentId)).Should().BeFalse();
			_flags.VerifyNoOtherCalls();
			_settings.VerifyNoOtherCalls();
			_billing.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Missing_module_settings_fail_closed()
		{
			_modules = null;
			(await _service.CanUseChecklistsAsync(DepartmentId)).Should().BeFalse();
			(await _service.CanUseMaintenanceAsync(DepartmentId)).Should().BeFalse();
			_billing.VerifyNoOtherCalls();
		}

		[TestCase(PlanFrequency.Yearly)]
		[TestCase(PlanFrequency.Monthly)]
		[TestCase(PlanFrequency.Never)]
		public void Readiness_period_estimate_is_monthly_regardless_of_base_plan(PlanFrequency frequency)
		{
			var addon = new PlanAddon { AddonType = (int)PlanAddonTypes.ReadinessPro, Plan = new Plan { Frequency = (int)frequency } };
			var before = DateTime.UtcNow.AddMonths(1);
			var actual = addon.GetEndDateFromNow();
			actual.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow.AddMonths(1));
			addon.GetAddonName().Should().Be("Readiness Pro");
		}

		[Test]
		public void Addon_identifiers_preserve_existing_products()
		{
			((int)PlanAddonTypes.PTT).Should().Be(1);
			((int)PlanAddonTypes.ADP).Should().Be(2);
			((int)PlanAddonTypes.ReadinessPro).Should().Be(3);
		}
	}
}
