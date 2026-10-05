using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.FeatureToggles;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05, item 2.9: a department's own admins can write feature-flag overrides, so an override
	/// written from inside the department may switch a flag off but cannot switch on a flag the operator has off globally
	/// (the kill switch) or one above the department's plan. Operator overrides keep rolling a globally-off flag out one
	/// department at a time, and v4 refuses the self-service enable up front. Also 3.15 on v4: hazard delete needs
	/// Contact Delete.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class AdminContentFeatureToggleTests
	{
		private const int DepartmentId = 12;
		private const string FlagKey = "Records.System";
		private const string DepartmentAdminId = "dept-admin-1";
		private const string OperatorId = "operator-1";

		private Mock<IFeatureFlagRepository> _flags;
		private Mock<IFeatureFlagOverrideRepository> _overrides;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<IDepartmentsService> _departments;
		private FeatureFlag _flag;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_flag = new FeatureFlag { FeatureFlagId = 3, FlagKey = FlagKey, Name = "Records", IsEnabledGlobally = false };

			_flags = new Mock<IFeatureFlagRepository>();
			_flags.Setup(x => x.GetAllAsync()).ReturnsAsync(() => new List<FeatureFlag> { _flag });
			_overrides = new Mock<IFeatureFlagOverrideRepository>();
			_subscriptions = new Mock<ISubscriptionsService>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department
			{
				DepartmentId = DepartmentId,
				ManagingUserId = DepartmentAdminId,
				Members = new List<DepartmentMember> { new DepartmentMember { DepartmentId = DepartmentId, UserId = DepartmentAdminId, IsAdmin = true } }
			});

			_activity = new Activity(nameof(AdminContentFeatureToggleTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private FeatureToggleService Service()
		{
			var rules = new Mock<IFeatureFlagTargetingRuleRepository>();
			rules.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<FeatureFlagTargetingRule>());
			var prerequisites = new Mock<IFeatureFlagPrerequisiteRepository>();
			prerequisites.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<FeatureFlagPrerequisite>());

			return new FeatureToggleService(_flags.Object, _overrides.Object, rules.Object, prerequisites.Object, new Mock<IFeatureFlagUsageRepository>().Object,
				new Mock<ICacheProvider>().Object, new Mock<IEventAggregator>().Object, _subscriptions.Object, _departments.Object);
		}

		private void Override(bool enabled, string writtenBy) =>
			_overrides.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<FeatureFlagOverride>
			{
				new FeatureFlagOverride { FeatureFlagOverrideId = 1, FeatureFlagId = _flag.FeatureFlagId, DepartmentId = DepartmentId, IsEnabled = enabled, CreatedByUserId = writtenBy }
			});

		[Test]
		public async Task A_department_admins_override_cannot_switch_on_a_flag_that_is_off_globally()
		{
			Override(enabled: true, writtenBy: DepartmentAdminId);

			var evaluation = await Service().EvaluateFreshAsync(FlagKey, DepartmentId);

			evaluation.IsEnabled.Should().BeFalse();
			evaluation.Source.Should().NotBe(FeatureFlagEvaluationSource.Override);
		}

		[Test]
		public async Task An_operator_override_still_rolls_a_globally_off_flag_out_to_the_department()
		{
			Override(enabled: true, writtenBy: OperatorId);

			var evaluation = await Service().EvaluateFreshAsync(FlagKey, DepartmentId);

			evaluation.IsEnabled.Should().BeTrue();
			evaluation.Source.Should().Be(FeatureFlagEvaluationSource.Override);
		}

		[Test]
		public async Task A_department_admins_override_cannot_get_past_the_plan_gate()
		{
			_flag.IsEnabledGlobally = true;
			_flag.MinimumPlanType = 30;
			_subscriptions.Setup(x => x.GetCurrentPlanForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Plan { PlanId = 1 });
			Override(enabled: true, writtenBy: DepartmentAdminId);

			var evaluation = await Service().EvaluateFreshAsync(FlagKey, DepartmentId);

			evaluation.IsEnabled.Should().BeFalse();
			evaluation.Source.Should().Be(FeatureFlagEvaluationSource.PlanGate);
		}

		[Test]
		public async Task A_department_admins_override_opts_in_when_the_flag_is_on_and_the_plan_qualifies()
		{
			_flag.IsEnabledGlobally = true;
			_flag.RolloutPercentage = 0;
			Override(enabled: true, writtenBy: DepartmentAdminId);

			var evaluation = await Service().EvaluateFreshAsync(FlagKey, DepartmentId);

			evaluation.IsEnabled.Should().BeTrue();
			evaluation.Source.Should().Be(FeatureFlagEvaluationSource.Override);
		}

		[Test]
		public async Task A_department_admins_override_can_always_switch_a_flag_off()
		{
			_flag.IsEnabledGlobally = true;
			Override(enabled: false, writtenBy: DepartmentAdminId);

			var evaluation = await Service().EvaluateFreshAsync(FlagKey, DepartmentId);

			evaluation.IsEnabled.Should().BeFalse();
			evaluation.Source.Should().Be(FeatureFlagEvaluationSource.Override);
		}

		private static FeatureTogglesController Api(IFeatureToggleService service, bool systemAdmin)
		{
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.PrimarySid, systemAdmin ? OperatorId : DepartmentAdminId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
				new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
			};
			if (systemAdmin)
				claims.Add(new Claim(ClaimTypes.Role, "Admins"));

			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			return new FeatureTogglesController(service) { ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[Test]
		public async Task V4_SetOverride_refuses_a_department_admin_switching_on_a_globally_off_flag()
		{
			var service = new Mock<IFeatureToggleService>();
			service.Setup(x => x.GetFlagByKeyAsync(FlagKey, It.IsAny<bool>())).ReturnsAsync(_flag);

			var result = await Api(service.Object, systemAdmin: false).SetOverride(new SetFeatureFlagOverrideInput { Key = FlagKey, IsEnabled = true });

			result.Result.Should().BeOfType<BadRequestObjectResult>();
			service.Verify(x => x.SetDepartmentOverrideAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<DateTime?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task V4_SetOverride_lets_a_department_admin_opt_out()
		{
			var service = new Mock<IFeatureToggleService>();
			service.Setup(x => x.GetFlagByKeyAsync(FlagKey, It.IsAny<bool>())).ReturnsAsync(_flag);
			service.Setup(x => x.SetDepartmentOverrideAsync(FlagKey, DepartmentId, false, null, null, null, DepartmentAdminId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new FeatureFlagOverride { FeatureFlagId = 3, DepartmentId = DepartmentId, IsEnabled = false });

			await Api(service.Object, systemAdmin: false).SetOverride(new SetFeatureFlagOverrideInput { Key = FlagKey, IsEnabled = false });

			service.Verify(x => x.SetDepartmentOverrideAsync(FlagKey, DepartmentId, false, null, null, null, DepartmentAdminId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task V4_SetOverride_keeps_the_system_admin_rollout_path()
		{
			var service = new Mock<IFeatureToggleService>();
			service.Setup(x => x.GetFlagByKeyAsync(FlagKey, It.IsAny<bool>())).ReturnsAsync(_flag);
			service.Setup(x => x.SetDepartmentOverrideAsync(FlagKey, 55, true, null, null, null, OperatorId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new FeatureFlagOverride { FeatureFlagId = 3, DepartmentId = 55, IsEnabled = true });

			await Api(service.Object, systemAdmin: true).SetOverride(new SetFeatureFlagOverrideInput { Key = FlagKey, DepartmentId = 55, IsEnabled = true });

			service.Verify(x => x.SetDepartmentOverrideAsync(FlagKey, 55, true, null, null, null, OperatorId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void V4_DeleteContactHazard_needs_Contact_Delete_like_the_other_contact_deletes()
		{
			typeof(Resgrid.Web.Services.Controllers.v4.ContactsController).GetMethod("DeleteContactHazard")!.GetCustomAttribute<AuthorizeAttribute>()!.Policy
				.Should().Be(ResgridResources.Contacts_Delete);
		}
	}
}
