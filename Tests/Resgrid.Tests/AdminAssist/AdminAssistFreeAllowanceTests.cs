using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	/// <summary>Admin Assist free allowance (enhanced-ai-addon-plan.md §5.4): window arithmetic and the conversation gate's tier paths.</summary>
	[TestFixture, NonParallelizable]
	public class AdminAssistFreeAllowanceTests
	{
		private static DateTime Utc(int year, int month, int day, int hour = 0) => new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);

		[Test]
		public void Before_any_answered_question_everything_counts_against_the_starter_allowance()
		{
			var window = AdminAssistFreeAllowance.Current(null, Utc(2026, 10, 5), 20, 30, 4);
			window.Starter.Should().BeTrue();
			window.Allowance.Should().Be(20);
			window.StartUtc.Should().BeBefore(Utc(2000, 1, 1));
			window.EndUtc.Should().Be(Utc(2026, 11, 4));
		}

		[Test]
		public void Starter_window_runs_thirty_days_from_the_first_answered_question_across_a_month_boundary()
		{
			var first = Utc(2026, 10, 20, 15);
			var window = AdminAssistFreeAllowance.Current(first, Utc(2026, 11, 10), 20, 30, 4);
			window.Should().Be(new AdminAssistFreeWindow(first, first.AddDays(30), 20, true));
		}

		[Test]
		public void The_month_the_starter_window_ends_in_only_counts_questions_after_it_ended()
		{
			var first = Utc(2026, 10, 20, 15);
			var window = AdminAssistFreeAllowance.Current(first, Utc(2026, 11, 25), 20, 30, 4);
			window.Should().Be(new AdminAssistFreeWindow(first.AddDays(30), Utc(2026, 12, 1), 4, false));
		}

		[Test]
		public void Later_months_reset_on_the_first_without_rollover()
		{
			var window = AdminAssistFreeAllowance.Current(Utc(2026, 1, 3), Utc(2026, 12, 31, 23), 20, 30, 4);
			window.Should().Be(new AdminAssistFreeWindow(Utc(2026, 12, 1), Utc(2027, 1, 1), 4, false));
		}

		[Test]
		public void Local_clock_values_are_rejected()
		{
			Action act = () => AdminAssistFreeAllowance.Current(null, DateTime.Now, 20, 30, 4);
			act.Should().Throw<ArgumentException>();
		}

		private sealed class FixedClock : TimeProvider
		{
			public DateTimeOffset Now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
			public override DateTimeOffset GetUtcNow() => Now;
		}

		private const int DepartmentId = 812;
		private readonly AdminAssistActor _actor = new AdminAssistActor(DepartmentId, "admin-1");
		private Mock<IEnhancedAiAccessService> _enhancedAi;
		private Mock<IAiUsageMeter> _usage;
		private Mock<IAiFreeAllowanceStore> _allowance;
		private AiAccessService _gate;
		private (bool, string, bool, string, string, string, string, string, string, int) _savedAi;
		private (string, string) _savedSecurity;
		private bool _savedFree;

		[SetUp]
		public void SetUp()
		{
			_savedAi = (AiConfig.AdminAssistEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model,
				AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.AuditHmacKey, AiConfig.SelfHostedDepartmentIds, AiConfig.TurnTokenLimit);
			_savedSecurity = (SecurityConfig.EncryptionKey, SecurityConfig.EncryptionSaltValue);
			_savedFree = AiAddonConfig.AdminAssistFreeEnabled;
			AiConfig.AdminAssistEnabled = true;
			AiConfig.Endpoint = "https://inference.example.invalid/v1/chat/completions";
			AiConfig.AllowPrivateEndpoint = false;
			AiConfig.ApiKey = "unit-test-only";
			AiConfig.Model = "Qwen/Qwen3-14B-AWQ";
			AiConfig.ModelRevision = new string('a', 40);
			AiConfig.RuntimeDigest = "sha256:" + new string('b', 64);
			AiConfig.AuditHmacKey = Convert.ToBase64String(new byte[32]);
			AiConfig.SelfHostedDepartmentIds = "";
			AiConfig.TurnTokenLimit = 8192;
			SecurityConfig.EncryptionKey = "unit-test-encryption-key-0123456789";
			SecurityConfig.EncryptionSaltValue = "unit-test-salt";
			AiAddonConfig.AdminAssistFreeEnabled = true;

			var access = new Mock<IAdminAssistAccessService>();
			access.Setup(a => a.CanAccessAsync(_actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new FeatureFlagEvaluation { IsEnabled = true });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.ShouldEncryptNewWritesAsync(DepartmentId)).ReturnsAsync(false);
			_enhancedAi = new Mock<IEnhancedAiAccessService>();
			_enhancedAi.Setup(e => e.GetActiveAddonStateAsync(DepartmentId)).ReturnsAsync(false);
			_usage = new Mock<IAiUsageMeter>();
			_usage.Setup(u => u.IsDisabledAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			_usage.Setup(u => u.RemainingAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000_000);
			_allowance = new Mock<IAiFreeAllowanceStore>();
			_allowance.Setup(a => a.GetFirstAnsweredAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
			SetFreeUsage(5, 5);
			_gate = new AiAccessService(access.Object, flags.Object, settings.Object, _enhancedAi.Object, _usage.Object, _allowance.Object, protection.Object, new FixedClock());
		}

		[TearDown]
		public void TearDown()
		{
			(AiConfig.AdminAssistEnabled, AiConfig.Endpoint, AiConfig.AllowPrivateEndpoint, AiConfig.ApiKey, AiConfig.Model,
				AiConfig.ModelRevision, AiConfig.RuntimeDigest, AiConfig.AuditHmacKey, AiConfig.SelfHostedDepartmentIds, AiConfig.TurnTokenLimit) = _savedAi;
			(SecurityConfig.EncryptionKey, SecurityConfig.EncryptionSaltValue) = _savedSecurity;
			AiAddonConfig.AdminAssistFreeEnabled = _savedFree;
		}

		private void SetFreeUsage(int answered, int attempts) =>
			_allowance.Setup(a => a.GetFreeUsageAsync(DepartmentId, It.IsAny<AdminAssistFreeWindow>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AiFreeUsage(answered, attempts));

		[Test]
		public async Task Department_without_the_add_on_gets_the_starter_allowance_with_questions_remaining()
		{
			var status = await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None);
			status.Available.Should().BeTrue();
			status.Tier.Should().Be(AdminAssistAskTiers.Free);
			status.FreeQuestionsAllowance.Should().Be(AiAddonConfig.AdminAssistFreeStarterQuestions);
			status.FreeQuestionsRemaining.Should().Be(AiAddonConfig.AdminAssistFreeStarterQuestions - 5);
			status.FreeWindowEndsUtc.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(AiAddonConfig.AdminAssistFreeStarterDays));
			_usage.Verify(u => u.RemainingAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Used_up_allowance_is_refused_before_the_turn_but_not_on_the_post_reservation_recheck()
		{
			SetFreeUsage(AiAddonConfig.AdminAssistFreeStarterQuestions, AiAddonConfig.AdminAssistFreeStarterQuestions);
			var before = await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None);
			before.Available.Should().BeFalse();
			before.Reason.Should().Be("FreeAllowanceExhausted");
			before.FreeQuestionsRemaining.Should().Be(0);
			(await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None, requireBudget: false)).Available.Should().BeTrue();
		}

		[Test]
		public async Task Repeated_unanswered_attempts_hit_the_daily_attempt_limit()
		{
			SetFreeUsage(0, AiAddonConfig.AdminAssistFreeDailyAttemptLimit);
			(await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None)).Reason.Should().Be("FreeAttemptLimit");
		}

		[Test]
		public async Task Unknown_billing_is_never_presented_as_the_free_allowance()
		{
			_enhancedAi.Setup(e => e.GetActiveAddonStateAsync(DepartmentId)).ReturnsAsync((bool?)null);
			var status = await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None);
			status.Available.Should().BeFalse();
			status.Reason.Should().Be("EntitlementUnavailable");
			_allowance.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Free_allowance_can_be_switched_off_by_the_operator()
		{
			AiAddonConfig.AdminAssistFreeEnabled = false;
			(await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None)).Reason.Should().Be("EntitlementUnavailable");
		}

		[Test]
		public async Task Paid_department_uses_the_token_budget_and_never_reads_the_allowance()
		{
			_enhancedAi.Setup(e => e.GetActiveAddonStateAsync(DepartmentId)).ReturnsAsync(true);
			var status = await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None);
			status.Available.Should().BeTrue();
			status.Tier.Should().Be(AdminAssistAskTiers.EnhancedAi);
			status.FreeQuestionsRemaining.Should().BeNull();
			_allowance.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Unreviewed_models_leave_the_gate_unconfigured()
		{
			AiConfig.Model = "some/unreviewed-model";
			(await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None)).Reason.Should().Be("Unconfigured");
		}

		[Test]
		public async Task Turns_reserve_on_the_path_that_admitted_them()
		{
			var reservation = new AiUsageReservation("r1", DepartmentId, "admin-1", 8192, DateTime.UtcNow.AddMinutes(2));
			_allowance.Setup(a => a.ReserveFreeAsync(_actor, It.IsAny<DateTime>(), 8192, It.Is<AdminAssistFreeWindow>(w => w.Starter && w.Allowance == AiAddonConfig.AdminAssistFreeStarterQuestions),
				AiAddonConfig.AdminAssistFreeDailyAttemptLimit, It.IsAny<CancellationToken>())).ReturnsAsync(new AiFreeReservationResult(reservation, "Reserved"));
			var free = await _gate.CanUseAdminAssistAsync(_actor, CancellationToken.None);
			(await _gate.ReserveTurnAsync(_actor, free, CancellationToken.None)).Should().Be(reservation);
			_usage.Verify(u => u.ReserveAsync(It.IsAny<AdminAssistActor>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

			_usage.Setup(u => u.ReserveAsync(_actor, It.IsAny<DateTime>(), 8192, AiConfig.MonthlyTokenLimit, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
			var paid = new AdminAssistAskStatus(true, "Available", 1_000_000, AdminAssistAskTiers.EnhancedAi);
			(await _gate.ReserveTurnAsync(_actor, paid, CancellationToken.None)).Should().Be(reservation);

			Func<Task> refused = () => _gate.ReserveTurnAsync(_actor, new AdminAssistAskStatus(false, "FreeAllowanceExhausted", 0, AdminAssistAskTiers.Free), CancellationToken.None);
			await refused.Should().ThrowAsync<UnauthorizedAccessException>();
		}
	}
}
