using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using PostmarkDotNet;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.CallEmailTemplates;
using Resgrid.Tests.Mocks;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// The department's email call mode (DispatcherDeskConfig.EmailCallMode, setting 128, dispatchers-on-duty-plan.md D10) decides
	/// what an email to the dispatch address becomes: dispatched at once (the behaviour before the setting), a pending call waiting
	/// for a dispatcher (put on the dispatcher desk as an email), or a pending call Enhanced AI reads whatever the email format.
	/// </summary>
	[TestFixture]
	public class EmailControllerDispatchModeTests
	{
		private const int DepartmentId = 10;
		private const string DispatchCode = "dept10";
		private const string Sender = "cad@county.example";

		private const string Active911Body = @"CALL: Structure Fire
ADDR1: 100 OAK ST
ID: 2026-000777-FIRE
NARR: SMOKE SHOWING";

		private List<Call> _saved;
		private int _broadcasts;
		private bool _failBroadcast;
		private DispatcherDeskConfig _desk;
		private bool _aiAvailable;
		private int _formatType;
		private Call _existing;
		private MockInboundEmailReceiptsRepository _receipts;
		private Mock<IQueueService> _queue;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IEventAggregator> _events;
		private EmailController _controller;
		private bool _aiEnrichEnabled;

		[SetUp]
		public void SetUp()
		{
			_aiEnrichEnabled = Resgrid.Config.AiDispatchConfig.EnrichEnabled;
			Resgrid.Config.AiDispatchConfig.EnrichEnabled = true;
			_saved = new List<Call>();
			_broadcasts = 0;
			_failBroadcast = false;
			_desk = new DispatcherDeskConfig();
			_aiAvailable = true;
			_formatType = (int)CallEmailTypes.Active911;
			_existing = null;
			_receipts = new MockInboundEmailReceiptsRepository();
			var department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" };

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentIdForDispatchEmailAsync(DispatchCode)).ReturnsAsync(DepartmentId);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "member" } });
			departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId))
				.ReturnsAsync(() => new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = _formatType });
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);

			var factory = new CallEmailFactory();
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetLatest10ActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
					It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()))
				.Returns((int type, CallEmail email, string manager, List<IdentityUser> users, Department dept, List<Call> active, List<Unit> units, int priority,
					List<DepartmentCallPriority> priorities, List<CallType> types) => _existing != null
					? Task.FromResult(_existing)
					: factory.GenerateCallFromEmailText(CallEmailTypes.Active911, email, manager, users, dept, active, units, priority, priorities, types, Mock.Of<IGeoLocationProvider>()));
			calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken ct) =>
				{
					if (call.CallId <= 0)
					{
						call.CallId = 900 + _saved.Count;
						_saved.Add(call);
					}

					return call;
				});
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool bypass) => _saved.SingleOrDefault(c => c.CallId == id));
			calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());

			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallQueueItem item, CancellationToken ct) =>
				{
					_broadcasts++;
					if (_failBroadcast)
						throw new TimeoutException("bus unavailable");
					return true;
				});

			var deskConfig = new Mock<IDispatcherDeskConfigService>();
			deskConfig.Setup(d => d.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _desk);
			_routing = new Mock<IDispatcherRoutingService>();
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(DispatcherRouteResult.NotRouted());
			var ai = new Mock<IAiDispatchEnrichmentService>();
			ai.Setup(a => a.IsAvailableAsync(DepartmentId)).ReturnsAsync(() => _aiAvailable);
			_events = new Mock<IEventAggregator>();
			var emailCalls = new DispatcherEmailCallService(deskConfig.Object, _routing.Object, ai.Object, _events.Object);

			var aiAdmin = new Mock<IAiDispatchAdminService>();
			aiAdmin.Setup(a => a.IsSenderAllowedAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);

			_controller = new EmailController(settings.Object, Mock.Of<INumbersService>(), Mock.Of<ILimitsService>(), calls.Object, _queue.Object,
				departments.Object, profiles.Object, Mock.Of<ITextCommandService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<ICommunicationService>(), Mock.Of<IDistributionListsService>(), Mock.Of<IUsersService>(), Mock.Of<IEmailService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IMessageService>(), Mock.Of<IFileService>(), units.Object, Mock.Of<IGeoLocationProvider>(), Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), aiAdmin.Object,
				new InboundEmailReceiptsService(_receipts, TimeProvider.System), Mock.Of<IIntakeService>(), Mock.Of<Resgrid.Model.Providers.IEmailSender>(), emailCalls);
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Config.AiDispatchConfig.EnrichEnabled = _aiEnrichEnabled;
		}

		private static PostmarkInboundMessage Email(string messageId = null)
		{
			return new PostmarkInboundMessage
			{
				MessageID = messageId ?? Guid.NewGuid().ToString(),
				Subject = "ACTIVE 9-1-1",
				TextBody = Active911Body,
				FromFull = new FromFull { Email = Sender, Name = "County CAD" },
				ToFull = new List<ToFull> { new ToFull { Email = DispatchCode + "@" + Resgrid.Config.InboundEmailConfig.DispatchDomain, Name = "Dispatch" } },
				Attachments = new List<Attachment>()
			};
		}

		private async Task<int?> ReceiveAsync(PostmarkInboundMessage email) =>
			((await _controller.Receive(email, CancellationToken.None)) as IStatusCodeActionResult)?.StatusCode;

		private void VerifyAiQueued(Times times, string because = "") =>
			_queue.Verify(q => q.EnqueueAiDispatchTriageAsync(It.Is<AiDispatchQueueItem>(i => i.CallId == _saved[0].CallId && i.Channel == 1), It.IsAny<CancellationToken>()), times, because);

		[Test]
		public async Task Auto_dispatch_sends_the_call_at_once_and_never_touches_the_desk()
		{
			(await ReceiveAsync(Email())).Should().Be(200);

			_saved.Should().ContainSingle();
			_saved[0].State.Should().Be((int)CallStates.Active);
			_broadcasts.Should().Be(1);
			_routing.Verify(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyAiQueued(Times.Never());
		}

		[Test]
		public async Task Pending_mode_holds_the_call_for_a_dispatcher_and_puts_it_on_the_desk_as_an_email()
		{
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.Pending;
			var id = Guid.NewGuid().ToString();

			(await ReceiveAsync(Email(id))).Should().Be(200);

			var call = _saved.Single();
			call.State.Should().Be((int)CallStates.Pending);
			call.DispatchOn.Should().BeNull();
			call.HasBeenDispatched.Should().BeNull();
			call.NatureOfCall.Should().Contain("SMOKE SHOWING", "the template's call is kept as parsed");
			_broadcasts.Should().Be(0, "nobody is paged for a pending call");
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.DepartmentId == DepartmentId && q.Kind == DispatcherQueueItemKinds.Email &&
				q.CallId == call.CallId && q.SourceAddress == Sender && q.Subject == "ACTIVE 9-1-1"), It.IsAny<CancellationToken>()), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<CallUpdatedEvent>(u => u.Call.CallId == call.CallId)), Times.Once);
			_events.Verify(e => e.SendMessage(It.IsAny<CallAddedEvent>()), Times.Never);
			VerifyAiQueued(Times.Never());
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task Enhanced_ai_mode_holds_the_call_and_queues_ai_enrichment_whatever_the_email_format()
		{
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi;

			(await ReceiveAsync(Email())).Should().Be(200);

			_saved.Single().State.Should().Be((int)CallStates.Pending);
			_broadcasts.Should().Be(0);
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.Kind == DispatcherQueueItemKinds.Email), It.IsAny<CancellationToken>()), Times.Once);
			VerifyAiQueued(Times.Once());
		}

		[Test]
		public async Task Enhanced_ai_mode_without_enhanced_ai_still_holds_the_call_but_queues_no_enrichment()
		{
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi;
			_aiAvailable = false;

			(await ReceiveAsync(Email())).Should().Be(200);

			_saved.Single().State.Should().Be((int)CallStates.Pending);
			VerifyAiQueued(Times.Never());
		}

		[Test]
		public async Task A_retry_of_a_pending_call_never_pages_and_finishes_its_desk_entry()
		{
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.Pending;
			var id = Guid.NewGuid().ToString();
			using var cancelled = new CancellationTokenSource();
			var attempts = 0;
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherEnqueueRequest request, CancellationToken ct) =>
				{
					// The first delivery's request is cancelled while the desk step runs, after the call was saved.
					if (++attempts == 1)
					{
						cancelled.Cancel();
						throw new OperationCanceledException(cancelled.Token);
					}

					return Task.FromResult(DispatcherRouteResult.NotRouted());
				});

			((await _controller.Receive(Email(id), cancelled.Token)) as IStatusCodeActionResult)?.StatusCode.Should().Be(400);
			_saved.Should().ContainSingle();
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Saved);

			(await ReceiveAsync(Email(id))).Should().Be(200);

			_saved.Should().ContainSingle("the retry finished the first call");
			_broadcasts.Should().Be(0);
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.CallId == _saved[0].CallId), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task A_failed_desk_entry_keeps_the_email_retryable_and_the_retry_puts_it_on_the_desk()
		{
			// Only Emails ticked: neither the call-event hand-off nor the desk's sweep (both Pending Calls routing) would add it later.
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi;
			_desk.RoutePendingCalls = false;
			var id = Guid.NewGuid().ToString();
			var attempts = 0;
			_routing.Setup(r => r.EnqueueAsync(It.IsAny<DispatcherEnqueueRequest>(), It.IsAny<CancellationToken>()))
				.Returns((DispatcherEnqueueRequest request, CancellationToken ct) => ++attempts == 1
					? throw new TimeoutException("desk store unavailable")
					: Task.FromResult(DispatcherRouteResult.NotRouted()));

			(await ReceiveAsync(Email(id))).Should().Be(400, "Postmark redelivers");
			_saved.Should().ContainSingle();
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Saved);
			VerifyAiQueued(Times.Once());

			(await ReceiveAsync(Email(id))).Should().Be(200);

			_saved.Should().ContainSingle("the retry finished the first call");
			_broadcasts.Should().Be(0);
			_routing.Verify(r => r.EnqueueAsync(It.Is<DispatcherEnqueueRequest>(q => q.CallId == _saved[0].CallId && q.Kind == DispatcherQueueItemKinds.Email),
				It.IsAny<CancellationToken>()), Times.Exactly(2));
			VerifyAiQueued(Times.Once(), "the AI step went out on the first delivery");
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task Only_a_held_call_is_queued_for_ai_as_held_for_a_dispatcher()
		{
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.EnhancedAi;
			(await ReceiveAsync(Email())).Should().Be(200);
			_queue.Verify(q => q.EnqueueAiDispatchTriageAsync(It.Is<AiDispatchQueueItem>(i => i.CallId == _saved[0].CallId && i.HeldForDispatcher),
				It.IsAny<CancellationToken>()), Times.Once);

			_saved.Clear();
			_desk.EmailCallMode = (int)DispatcherEmailCallModes.AutoDispatch;
			_formatType = (int)CallEmailTypes.AI;
			(await ReceiveAsync(Email())).Should().Be(200);
			_queue.Verify(q => q.EnqueueAiDispatchTriageAsync(It.Is<AiDispatchQueueItem>(i => i.CallId == _saved[0].CallId && !i.HeldForDispatcher),
				It.IsAny<CancellationToken>()), Times.Once, "the AI email format's live call keeps the active-call rule");
		}

		[Test]
		public async Task An_update_to_a_pending_call_is_not_redispatched_when_its_alarm_count_changes()
		{
			_existing = new Call { CallId = 77, DepartmentId = DepartmentId, State = (int)CallStates.Pending, Name = "Structure Fire", DispatchCount = 3, PreviousDispatchCount = 2 };

			(await ReceiveAsync(Email())).Should().Be(200);

			_broadcasts.Should().Be(0, "a pending call waits for its dispatcher");

			_existing = new Call { CallId = 78, DepartmentId = DepartmentId, State = (int)CallStates.Active, Name = "Structure Fire", DispatchCount = 3, PreviousDispatchCount = 2 };
			(await ReceiveAsync(Email())).Should().Be(200);

			_broadcasts.Should().Be(1, "a running call is still redispatched on a new alarm");
		}
	}
}
