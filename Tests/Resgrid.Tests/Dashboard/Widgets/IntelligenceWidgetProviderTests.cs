using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Intelligence;
using Resgrid.Model.Services;
using Resgrid.Services.AiDispatch;
using Resgrid.Services.Dashboard.Widgets.Intelligence;
using static Resgrid.Tests.Dashboard.Widgets.AgentDWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Intelligence widgets (plan §7.11, §7.1 aiDispatch.triage).</summary>
	[TestFixture]
	public class IntelligenceWidgetProviderTests
	{
		[Test]
		public void The_assistant_needs_the_enhanced_ai_capability_and_the_chat_system_it_rides()
		{
			var flags = new[] { FeatureFlagKeys.AiAssistant, FeatureFlagKeys.ChatSystem };
			Visible("ai.assistant", Viewer(flags: flags)).Should().BeFalse();
			Visible("ai.assistant", Viewer(flags: new[] { FeatureFlagKeys.AiAssistant }, access: new[] { DashboardAccessChecks.EnhancedAiAssistant })).Should().BeFalse();
			Visible("ai.assistant", Viewer(flags: flags, access: new[] { DashboardAccessChecks.EnhancedAiAssistant })).Should().BeTrue();
		}

		[Test]
		public async Task The_assistant_provider_only_confirms_availability()
		{
			var data = (AssistantWidgetData)await new AssistantWidgetProvider().GetDataAsync(Context("ai.assistant"), CancellationToken.None);
			data.Available.Should().BeTrue();
		}

		[Test]
		public void Triage_is_for_dispatchers_in_an_ai_dispatch_department()
		{
			var flags = new[] { FeatureFlagKeys.AiDispatchTemplate, FeatureFlagKeys.AiEnhanced };
			Visible("aiDispatch.triage", Viewer(flags: flags, access: new[] { DashboardAccessChecks.AiDispatchAvailable })).Should().BeFalse("Call_Create is the dispatcher rule");
			Visible("aiDispatch.triage", Viewer(policies: new[] { "Call_Create" }, flags: flags, access: new[] { DashboardAccessChecks.AiDispatchAvailable })).Should().BeTrue();
		}

		[TestCase(AiDispatchOutcomes.Applied, null, false)]
		[TestCase(AiDispatchOutcomes.InProgress, null, false)]
		[TestCase(AiDispatchOutcomes.Applied, 12, true)]
		[TestCase(AiDispatchOutcomes.LowConfidence, null, true)]
		[TestCase(AiDispatchOutcomes.BudgetExhausted, null, true)]
		public void Needs_review_when_the_proposal_was_not_applied_or_names_a_related_call(string outcome, int? related, bool expected)
		{
			AiDispatchTriage.NeedsReview(outcome, related).Should().Be(expected);
		}

		private static AiDispatchTriageItem Item(int callId, string outcome, int minutesAgo, int? related = null, decimal? confidence = 0.42m) =>
			new AiDispatchTriageItem { CallId = callId, CallNumber = "C-" + callId, Outcome = outcome, Confidence = confidence, RelatedCallId = related, CreatedOnUtc = Now.AddMinutes(-minutesAgo) };

		[Test]
		public async Task Triage_lists_review_items_first_and_only_calls_in_the_dispatchers_scope()
		{
			var admin = new Mock<IAiDispatchAdminService>(MockBehavior.Strict);
			admin.Setup(a => a.GetTriageSummaryAsync(Dept, 24, 50, It.IsAny<CancellationToken>())).ReturnsAsync(new AiDispatchTriageSummary
			{
				OutcomeCounts = new Dictionary<string, int> { [AiDispatchOutcomes.Applied] = 5, [AiDispatchOutcomes.LowConfidence] = 2, [AiDispatchOutcomes.InProgress] = 1 },
				ActiveCalls = new List<AiDispatchTriageItem>
				{
					Item(1, AiDispatchOutcomes.Applied, 1), Item(2, AiDispatchOutcomes.LowConfidence, 10), Item(3, AiDispatchOutcomes.LowConfidence, 2)
				}
			});
			var narrow = new DispatchScope { DepartmentId = Dept, UserId = User, IsDepartmentWide = false };
			var scope = new Mock<IDispatchScopeService>(MockBehavior.Strict);
			scope.Setup(s => s.GetScopeForUserAsync(Dept, User)).ReturnsAsync(narrow);
			scope.Setup(s => s.FilterCallsAsync(narrow, It.IsAny<List<Call>>())).ReturnsAsync((DispatchScope s, List<Call> calls) => calls.Where(c => c.CallId != 3).ToList());
			var calls = new Mock<ICallsService>(MockBehavior.Strict);
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(new List<Call>
			{
				new Call { CallId = 1, DepartmentId = Dept }, new Call { CallId = 2, DepartmentId = Dept }, new Call { CallId = 3, DepartmentId = Dept }
			});

			var data = (AiDispatchTriageWidgetData)await new AiDispatchTriageWidgetProvider(admin.Object, scope.Object, calls.Object)
				.GetDataAsync(Context("aiDispatch.triage", Viewer(policies: new[] { "Call_Create" })), CancellationToken.None);

			data.Rows.Select(r => r.CallId).Should().Equal(2, 1);
			data.NeedsReview.Should().Be(1, "call 3 is outside the dispatcher's scope");
			data.Rows[0].ConfidencePercent.Should().Be(42);
			data.Applied.Should().Be(5);
			data.InProgress.Should().Be(1);
			data.Total.Should().Be(8);
		}

		[Test]
		public async Task A_department_wide_dispatcher_sees_every_active_call_without_loading_calls()
		{
			var admin = new Mock<IAiDispatchAdminService>();
			admin.Setup(a => a.GetTriageSummaryAsync(Dept, 6, 50, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AiDispatchTriageSummary { ActiveCalls = new List<AiDispatchTriageItem> { Item(4, AiDispatchOutcomes.Unavailable, 3) } });
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(s => s.GetScopeForUserAsync(Dept, User)).ReturnsAsync(DispatchScope.DepartmentWide(Dept, User, DispatchScopeReasons.ScopingDisabled));

			var data = (AiDispatchTriageWidgetData)await new AiDispatchTriageWidgetProvider(admin.Object, scope.Object, new Mock<ICallsService>(MockBehavior.Strict).Object)
				.GetDataAsync(Context("aiDispatch.triage", settings: new Dictionary<string, object> { ["hours"] = 6 }), CancellationToken.None);

			data.Hours.Should().Be(6);
			data.NeedsReview.Should().Be(1);
		}

		[Test]
		public async Task The_triage_summary_reads_the_window_from_the_clock_and_bounds_its_inputs()
		{
			var audits = new Mock<IAiDispatchAuditRepository>(MockBehavior.Strict);
			var since = Now.AddHours(-168);
			audits.Setup(a => a.GetOutcomeCountsAsync(Dept, since, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int>());
			audits.Setup(a => a.GetActiveCallTriageAsync(Dept, since, 200, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AiDispatchTriageItem>());
			var clock = new Mock<TimeProvider>();
			clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(Now));
			var service = new AiDispatchAdminService(null, audits.Object, null, null, null, null, clock.Object);

			var summary = await service.GetTriageSummaryAsync(Dept, 10000, 10000, CancellationToken.None);

			summary.SinceUtc.Should().Be(since, "hours are clamped to a week");
			summary.ActiveCalls.Should().BeEmpty();
		}
	}
}
