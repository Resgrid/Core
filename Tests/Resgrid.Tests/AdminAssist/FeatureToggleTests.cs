using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class FeatureToggleTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private Mock<IFeatureToggleService> _flags;
		private Mock<IRecordsAuthorizationService> _membership;
		private AdminAssistAccessService _access;
		[SetUp]
		public void SetUp()
		{
			_flags = new(); _membership = new();
			_membership.Setup(a => a.IsActiveMemberAsync("admin", 7)).ReturnsAsync(true);
			_membership.Setup(a => a.IsDepartmentAdminAsync("admin", 7)).ReturnsAsync(true);
			_access = new(_membership.Object, _flags.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<ISubscriptionsService>(),
				Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IReadinessAccessService>(), Mock.Of<IBusinessOperationsAccessService>(),
				new ConfigurationCatalog(), TimeProvider.System, Mock.Of<IAuthorizationService>());
		}
		private void Set(string key, bool enabled) => _flags.Setup(f => f.EvaluateFreshAsync(key, Actor.DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { Key = key, IsEnabled = enabled });

		[TestCase(false,false,false)] [TestCase(false,false,true)]
		[TestCase(true,false,false)] [TestCase(true,false,true)]
		[TestCase(false,true,false)] [TestCase(false,true,true)]
		[TestCase(true,true,false)] [TestCase(true,true,true)]
		public async Task Setup_and_assist_are_independent_and_AI_does_not_enable_either(bool setup, bool assist, bool ai)
		{
			Set(FeatureFlagKeys.AdminSetup,setup); Set(FeatureFlagKeys.AdminAssist,assist); Set(FeatureFlagKeys.AiAdminAssist,ai);
			Assert.That(await _access.CanAccessAsync(Actor,true), Is.EqualTo(setup));
			Assert.That(await _access.CanAccessAsync(Actor,false), Is.EqualTo(assist));
			Assert.That(await AdminAssistFeatureAvailability.CanConfigureOperatingProfileAsync(_flags.Object,7), Is.EqualTo(setup || assist));
			_flags.Verify(f => f.EvaluateFreshAsync(FeatureFlagKeys.AiAdminAssist,7), Times.Never);
		}
		[TestCase(true)] [TestCase(false)]
		public async Task Missing_flag_and_store_outage_fail_closed(bool setup)
		{
			Assert.That(await _access.CanAccessAsync(Actor,setup), Is.False);
			_flags.Setup(f => f.EvaluateFreshAsync(It.IsAny<string>(),7)).ThrowsAsync(new InvalidOperationException("Store unavailable"));
			Assert.That(await _access.CanAccessAsync(Actor,setup), Is.False);
		}
		[TestCase(true)] [TestCase(false)]
		public async Task Revocation_takes_effect_on_the_next_access_check(bool setup)
		{
			_flags.SetupSequence(f => f.EvaluateFreshAsync(setup ? FeatureFlagKeys.AdminSetup : FeatureFlagKeys.AdminAssist,7))
				.ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true }).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = false });
			Assert.That(await _access.CanAccessAsync(Actor,setup), Is.True);
			Assert.That(await _access.CanAccessAsync(Actor,setup), Is.False);
		}
		[TestCase(true)] [TestCase(false)]
		public async Task Enabling_a_flag_does_not_authorize_an_ordinary_member(bool setup)
		{
			Set(FeatureFlagKeys.AdminSetup,true); Set(FeatureFlagKeys.AdminAssist,true);
			_membership.Setup(a => a.IsDepartmentAdminAsync("admin",7)).ReturnsAsync(false);
			Assert.That(await _access.CanAccessAsync(Actor,setup), Is.False);
			_flags.VerifyNoOtherCalls();
		}
		[TestCase(true)] [TestCase(false)]
		public void Disabled_overview_does_not_read_department_evidence_or_progress(bool setup)
		{
			var snapshots = new Mock<IConfigurationSnapshotProvider>(MockBehavior.Strict);
			var repository = new Mock<IAdminAssistRepository>(MockBehavior.Strict);
			var service = new AdminAssistService(_access,new ConfigurationCatalog(),snapshots.Object,repository.Object,TimeProvider.System);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.GetOverviewAsync(Actor,setup));
			snapshots.VerifyNoOtherCalls(); repository.VerifyNoOtherCalls();
		}
		[Test]
		public void Caller_cancellation_is_not_swallowed_as_a_disabled_flag()
		{
			using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
			Assert.ThrowsAsync<OperationCanceledException>(async () => await AdminAssistFeatureAvailability.IsEnabledAsync(_flags.Object,7,true,cancellation.Token));
		}
	}
}
