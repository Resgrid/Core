using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>The email call mode (setting 128, D10) shared by the Postmark dispatch branch and the POP importer.</summary>
	[TestFixture]
	public class DispatcherEmailCallServiceTests
	{
		private const int DepartmentId = 12;

		private Mock<IDispatcherDeskConfigService> _deskConfig;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IAiDispatchEnrichmentService> _ai;
		private Mock<IEventAggregator> _events;
		private DispatcherDeskConfig _desk;
		private DispatcherEmailCallService _service;

		[SetUp]
		public void SetUp()
		{
			_desk = new DispatcherDeskConfig();
			_deskConfig = new Mock<IDispatcherDeskConfigService>();
			_deskConfig.Setup(d => d.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _desk);
			_routing = new Mock<IDispatcherRoutingService>();
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherRouteResult.NotRouted());
			_ai = new Mock<IAiDispatchEnrichmentService>();
			_events = new Mock<IEventAggregator>();
			_service = new DispatcherEmailCallService(_deskConfig.Object, _routing.Object, _ai.Object, _events.Object);
		}

		[TestCase(0, DispatcherEmailCallModes.AutoDispatch)]
		[TestCase(1, DispatcherEmailCallModes.Pending)]
		[TestCase(2, DispatcherEmailCallModes.EnhancedAi)]
		[TestCase(9, DispatcherEmailCallModes.AutoDispatch)]
		public async Task The_mode_comes_from_the_desk_config(int stored, DispatcherEmailCallModes expected)
		{
			_desk.EmailCallMode = stored;

			(await _service.GetModeAsync(DepartmentId)).Should().Be(expected);
		}

		[Test]
		public async Task A_missing_or_unreadable_setting_dispatches_as_before()
		{
			_desk = null;
			(await _service.GetModeAsync(DepartmentId)).Should().Be(DispatcherEmailCallModes.AutoDispatch);

			_deskConfig.Setup(d => d.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("settings store down"));
			(await _service.GetModeAsync(DepartmentId)).Should().Be(DispatcherEmailCallModes.AutoDispatch);
		}

		[Test]
		public void Holding_a_call_makes_it_pending_and_keeps_its_dispatch_lists()
		{
			var call = new Call
			{
				State = (int)CallStates.Active,
				DispatchOn = DateTime.UtcNow,
				HasBeenDispatched = false,
				Dispatches = new System.Collections.Generic.List<CallDispatch> { new CallDispatch { UserId = "u1" } }
			};

			_service.HoldAsPending(call);

			call.State.Should().Be((int)CallStates.Pending);
			call.DispatchOn.Should().BeNull();
			call.HasBeenDispatched.Should().BeNull();
			call.Dispatches.Should().ContainSingle(d => d.UserId == "u1");
			_service.Invoking(s => s.HoldAsPending(null)).Should().NotThrow();
		}

		[Test]
		public async Task Ai_enrichment_follows_the_ai_format_or_the_enhanced_ai_mode_when_available()
		{
			_ai.Setup(a => a.IsAvailableAsync(DepartmentId)).ReturnsAsync(true);

			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.AI, DispatcherEmailCallModes.AutoDispatch)).Should().BeTrue("the AI format enriches as before");
			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.Generic, DispatcherEmailCallModes.AutoDispatch)).Should().BeFalse();
			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.Generic, DispatcherEmailCallModes.Pending)).Should().BeFalse();
			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.Generic, DispatcherEmailCallModes.EnhancedAi)).Should().BeTrue();

			_ai.Setup(a => a.IsAvailableAsync(DepartmentId)).ReturnsAsync(false);
			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.Generic, DispatcherEmailCallModes.EnhancedAi)).Should().BeFalse("no add-on, no enrichment");

			_ai.Setup(a => a.IsAvailableAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException());
			(await _service.ShouldEnrichAsync(DepartmentId, (int)CallEmailTypes.Generic, DispatcherEmailCallModes.EnhancedAi)).Should().BeFalse();
		}

		[Test]
		public async Task A_saved_pending_call_goes_on_the_desk_as_an_email_and_refreshes_screens()
		{
			var call = new Call { CallId = 40, DepartmentId = DepartmentId, NatureOfCall = "SMOKE SHOWING", ReportingUserId = "manager", State = (int)CallStates.Pending };

			(await _service.OnPendingCallSavedAsync(call, " cad@county.example ", new string('s', 300))).Should().BeTrue();

			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.DepartmentId == DepartmentId && q.Kind == DispatcherQueueItemKinds.Email &&
				q.CallId == 40 && q.SourceAddress == "cad@county.example" && q.Subject.Length == 250 && q.Body == "SMOKE SHOWING" && q.CreatedByUserId == "manager"),
				It.IsAny<CancellationToken>()), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(u => u.DepartmentId == DepartmentId && u.Call == call)), Times.Once);
		}

		[Test]
		public async Task A_desk_failure_never_reaches_the_email_but_a_real_cancellation_does()
		{
			var call = new Call { CallId = 41, DepartmentId = DepartmentId, State = (int)CallStates.Pending };
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("desk down"));

			(await _service.OnPendingCallSavedAsync(call, null, null)).Should().BeFalse("the desk hand-off is still owed, so the delivery stays retryable");
			_events.Verify(e => e.SendMessage(It.IsAny<CallUpdatedEvent>()), Times.Once, "the screens still refresh");

			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherRouteResult.NotRouted());
			(await _service.OnPendingCallSavedAsync(call, null, null)).Should().BeTrue("a desk that does not take emails owes nothing");

			using var cts = new CancellationTokenSource();
			cts.Cancel();
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));
			await _service.Invoking(s => s.OnPendingCallSavedAsync(call, null, null, cts.Token)).Should().ThrowAsync<OperationCanceledException>();

			(await _service.OnPendingCallSavedAsync(new Call(), null, null)).Should().BeTrue("an unsaved call is ignored");
		}
	}
}
