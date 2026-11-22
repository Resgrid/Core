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
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// Dispatcher desk services (M0324) wired together over in-memory repositories: routing work to free dispatchers, busy and
	/// overflow, offers that are declined or expire, breaks as a length with auto return, and desk sign-in rules.
	/// </summary>
	[TestFixture]
	public partial class DispatcherDeskServiceTests
	{
		private const int Dept = 7;

		private sealed class Harness
		{
			public DateTime Now = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);
			public readonly InMemoryDispatcherDeskStore Store = new InMemoryDispatcherDeskStore();
			public readonly DispatcherDeskConfig Config = new DispatcherDeskConfig { Enabled = true, MinimumAvailableDispatchers = 1 };
			public readonly List<DispatcherDutyWindow> Duty = new List<DispatcherDutyWindow>();
			public readonly Dictionary<int, Call> Calls = new Dictionary<int, Call>();
			public readonly List<(string Kind, string UserId, string Code)> Pushes = new List<(string, string, string)>();
			public readonly Mock<IDispatcherVoiceProvider> Voice = new Mock<IDispatcherVoiceProvider>();
			public readonly Mock<ITextMessageProvider> Texts = new Mock<ITextMessageProvider>();
			public readonly Mock<IQueueService> Queue = new Mock<IQueueService>();
			public readonly Mock<IAiDispatchEnrichmentService> Ai = new Mock<IAiDispatchEnrichmentService>();
			public readonly Mock<IIntakeConversationService> Intake = new Mock<IIntakeConversationService>();
			/// <summary>Intake channel of an intake call, by call id (the sweep's chat-app check); a call not listed came from no channel.</summary>
			public readonly Dictionary<int, IntakeCallSource> IntakeSources = new Dictionary<int, IntakeCallSource>();
			/// <summary>Intake call screenings (M0326) by call id; a call not listed is unscreened.</summary>
			public readonly Dictionary<int, IntakeScreenings> Screenings = new Dictionary<int, IntakeScreenings>();
			public readonly HashSet<string> Supervisors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			/// <summary>How many of the next call saves throw.</summary>
			public int FailCallSaves;
			/// <summary>How many of the next call saves commit the call and then throw (a failure in the save's follow-up work).</summary>
			public int FailAfterCallSaves;
			/// <summary>Runs as a call save starts (another pass acting while this one saves).</summary>
			public Action<Call> BeforeCallSave;
			/// <summary>The routing service's unit of work (a transaction only in name: the in-memory store does not roll back).</summary>
			public readonly Mock<IUnitOfWork> UnitOfWork = new Mock<IUnitOfWork>();
			private int _nextCallId = 5000;

			public readonly DispatcherPresenceService Presence;
			public readonly DispatcherRoutingService Routing;
			public readonly DispatcherBreaksService Breaks;
			public readonly DispatcherConversationService Conversations;

			public Harness()
			{
				var configService = new Mock<IDispatcherDeskConfigService>();
				configService.Setup(x => x.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Config);

				var shiftDispatchers = new Mock<IShiftDispatchersService>();
				shiftDispatchers.Setup(x => x.IsEligibleDispatcherAsync(Dept, It.IsAny<string>())).ReturnsAsync(true);
				shiftDispatchers.Setup(x => x.GetOnDutyAsync(Dept, It.IsAny<DateTime>()))
					.ReturnsAsync((int d, DateTime at) => DispatcherDutyCalculator.OnDuty(Duty, at));
				shiftDispatchers.Setup(x => x.GetDutyForUserAsync(Dept, It.IsAny<string>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int d, string u, DateTime at) => DispatcherDutyCalculator.OnDuty(Duty, at).FirstOrDefault(x => x.UserId == u));
				shiftDispatchers.Setup(x => x.GetDutyWindowsAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int d, DateTime f, DateTime t) => Duty.Where(x => x.EndUtc > f && x.StartUtc < t).ToList());
				shiftDispatchers.Setup(x => x.GetSettingsForDepartmentAsync(Dept)).ReturnsAsync(new List<ShiftDispatchSetting>());
				// The duty shift (3) is a desk shift whose dispatchers plan their own breaks.
				shiftDispatchers.Setup(x => x.GetSettingsForShiftAsync(It.IsAny<int>()))
					.ReturnsAsync((int id) => new ShiftDispatchSetting { ShiftId = id, DepartmentId = Dept, IsEnabled = true, AllowSelfScheduledBreaks = true });

				var departments = new Mock<IDepartmentsService>();
				departments.Setup(x => x.GetDepartmentByIdAsync(Dept, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner", AdminUsers = new List<string>(), TimeZone = "Eastern Standard Time" });

				var profiles = new Mock<IUserProfileService>();
				profiles.Setup(x => x.GetAllProfilesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Dictionary<string, UserProfile>());

				var authorization = new Mock<IAuthorizationService>();
				authorization.Setup(x => x.GetShiftManagementScopeAsync(It.IsAny<string>(), Dept))
					.ReturnsAsync((string u, int d) => new ShiftManagementScope { AllGroups = Supervisors.Contains(u) });

				var notifier = new Mock<IDispatcherDeskNotifier>();
				notifier.Setup(x => x.NotifyOfferAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.Callback((DispatcherQueueItem i, string u, CancellationToken c) => Pushes.Add(("offer", u, "DQ:" + i.DispatcherQueueItemId))).Returns(Task.CompletedTask);
				notifier.Setup(x => x.NotifyBreakReminderAsync(It.IsAny<DispatcherBreak>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.Callback((DispatcherBreak b, bool due, CancellationToken c) => Pushes.Add((due ? "due" : "reminder", b.UserId, "DB:" + b.DispatcherBreakId))).ReturnsAsync(true);
				notifier.Setup(x => x.NotifyBreakOverAsync(It.IsAny<DispatcherBreak>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.Callback((DispatcherBreak b, IEnumerable<string> s, bool auto, CancellationToken c) =>
						Pushes.Add((s == null ? (auto ? "auto-returned" : "break-over") : "supervisors", b.UserId, "DB:" + b.DispatcherBreakId))).ReturnsAsync(true);

				var calls = new Mock<ICallsService>();
				calls.Setup(x => x.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => Calls.TryGetValue(id, out var c) ? c : null);
				calls.Setup(x => x.GetActiveCallPrioritiesForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
				calls.Setup(x => x.GetPendingCallsByDepartmentIdAsync(Dept)).ReturnsAsync(() => Calls.Values.Where(x => x.State == (int)CallStates.Pending).ToList());
				calls.Setup(x => x.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(Dept)).ReturnsAsync(() =>
					Calls.Values.Where(x => x.State == (int)CallStates.Active && x.DispatchOn.HasValue && x.HasBeenDispatched != true).ToList());
				calls.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken t) =>
				{
					BeforeCallSave?.Invoke(c);
					if (FailCallSaves > 0)
					{
						FailCallSaves--;
						throw new InvalidOperationException("The call could not be saved.");
					}
					if (c.CallId == 0)
						c.CallId = ++_nextCallId;
					Calls[c.CallId] = c;
					if (FailAfterCallSaves > 0)
					{
						FailAfterCallSaves--;
						throw new InvalidOperationException("The call was saved but its follow-up work failed.");
					}
					return c;
				});
				calls.Setup(x => x.GetAllCallsByDepartmentDateRangeAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int d, DateTime from, DateTime to) => Calls.Values.Where(x => x.LoggedOn >= from && x.LoggedOn <= to).ToList());

				Texts.Setup(x => x.SendTextMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int>())).ReturnsAsync(true);
				Ai.Setup(x => x.IsAvailableAsync(It.IsAny<int>())).ReturnsAsync(true);
				Queue.Setup(x => x.EnqueueAiDispatchTriageAsync(It.IsAny<AiDispatchQueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(x => x.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync("+15550000000");

				DispatcherRoutingService routing = null;
				DispatcherConversationService conversations = null;

				Presence = new DispatcherPresenceService(Store.PresencesRepository, Store.BreaksRepository, Store.ItemsRepository, shiftDispatchers.Object,
					configService.Object, departments.Object, profiles.Object, authorization.Object, notifier.Object, new Lazy<IDispatcherRoutingService>(() => routing))
				{ UtcNow = () => Now };

				Intake.Setup(x => x.ConnectDispatcherAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(IntakeConversationActionResults.Done);
				Intake.Setup(x => x.ReleaseDispatcherAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(IntakeConversationActionResults.Done);
				Intake.Setup(x => x.CloseAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(IntakeConversationActionResults.Done);
				Intake.Setup(x => x.SendDispatcherMessageAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(IntakeConversationActionResults.Done);
				Intake.Setup(x => x.SetDeskItemAsync(Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
				var intake = new Lazy<IEnumerable<IIntakeConversationService>>(() => new[] { Intake.Object });

				conversations = new DispatcherConversationService(Store.MessagesRepository, Store.ItemsRepository, Store.EventsRepository, Presence, notifier.Object,
					Texts.Object, new Mock<IEmailSender>().Object, settings.Object, intake)
				{ UtcNow = () => Now };

				var screening = new Mock<IIntakeScreeningService>();
				screening.Setup(x => x.GetScreeningsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) =>
					ids.Where(Screenings.ContainsKey).ToDictionary(id => id, id => Screenings[id]));

				var intakeCalls = new Mock<IIntakeService>();
				intakeCalls.Setup(x => x.GetCallSourcesAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync((int d, IEnumerable<int> ids) =>
					ids.Where(IntakeSources.ContainsKey).ToDictionary(id => id, id => IntakeSources[id]));

				routing = new DispatcherRoutingService(Store.ItemsRepository, Store.EventsRepository, Store.MessagesRepository, Store.PresencesRepository,
					Store.BreaksRepository, Presence, configService.Object, notifier.Object, Voice.Object, departments.Object, profiles.Object, Queue.Object,
					new Lazy<ICallsService>(() => calls.Object), new Lazy<IDispatcherConversationService>(() => conversations),
					new Lazy<IAiDispatchEnrichmentService>(() => Ai.Object), intake, new Lazy<IIntakeScreeningService>(() => screening.Object),
					new Lazy<IIntakeService>(() => intakeCalls.Object), UnitOfWork.Object)
				{ UtcNow = () => Now };

				Routing = routing;
				Conversations = conversations;
				Breaks = new DispatcherBreaksService(Store.BreaksRepository, Store.PresencesRepository, Store.ItemsRepository, shiftDispatchers.Object, Presence,
					configService.Object, notifier.Object, new Lazy<IDispatcherRoutingService>(() => routing))
				{ UtcNow = () => Now };
			}

			public void OnDuty(params string[] users)
			{
				foreach (var user in users)
					Duty.Add(new DispatcherDutyWindow { UserId = user, ShiftId = 3, ShiftDayId = 100, ShiftName = "Dispatch A", StartUtc = Now.AddHours(-4), EndUtc = Now.AddHours(8) });
			}

			public void SignedIn(string user, DispatcherPresenceStates state = DispatcherPresenceStates.Available, int minutesSinceOffer = 60) =>
				Store.AddPresence(Dept, user, state, Now, Now.AddMinutes(-minutesSinceOffer));

			public Call PendingCall(int id)
			{
				var call = new Call { CallId = id, DepartmentId = Dept, Name = "Pending " + id, NatureOfCall = "Fall", State = (int)CallStates.Pending, LoggedOn = Now.AddMinutes(-1) };
				Calls[id] = call;
				return call;
			}

			public DispatcherQueueItem Item(string id) => Store.Items.Single(x => x.DispatcherQueueItemId == id);

			/// <summary>A text to the department's dedicated number: intake saved the pending call and opened the thread.</summary>
			public (IntakeConversation Conversation, Call Call) IntakeThread(int callId, string conversationId = "ic-1")
			{
				var call = PendingCall(callId);
				var conversation = new IntakeConversation
				{
					IntakeConversationId = conversationId, DepartmentId = Dept, CallId = callId, ChannelType = 1, ParticipantAddress = "+15555550123",
					InboundAddress = "+15555550999", ReplyFromAddress = "+15555550000", State = (int)IntakeConversationStates.Open,
					Handler = (int)IntakeConversationHandlers.Assistant, StartedOn = Now, LastActivityOn = Now
				};
				Intake.Setup(x => x.GetByIdAsync(Dept, conversationId)).ReturnsAsync(conversation);
				return (conversation, call);
			}

			public bool TextsSent() => Texts.Invocations.Any(x => x.Method.Name == nameof(ITextMessageProvider.SendTextMessage));

			public DispatcherPresence PresenceOf(string user) => Store.Presences.Single(x => x.UserId == user);
		}

		// ------------------------------------------------------------------ routing

		[Test]
		public async Task A_pending_call_is_offered_to_the_longest_idle_free_dispatcher()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 5);
			h.SignedIn("bob", minutesSinceOffer: 30);
			h.PendingCall(100);

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Offered);
			result.UserId.Should().Be("bob");
			h.Pushes.Should().ContainSingle(x => x.Kind == "offer" && x.UserId == "bob" && x.Code == "DQ:" + result.Item.DispatcherQueueItemId);
			h.Item(result.Item.DispatcherQueueItemId).OfferExpiresOn.Should().Be(h.Now.AddSeconds(DispatcherDeskConfig.DefaultOfferTimeoutSeconds));
		}

		[Test]
		public async Task A_screened_intake_call_waits_in_the_queue_and_is_never_offered()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);
			h.Screenings[100] = IntakeScreenings.LikelyNotIncident;

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Queued);
			h.Pushes.Should().BeEmpty("nobody is pushed or timed for a screened call");
			(await h.Routing.RouteDepartmentAsync(Dept)).Should().Be(0, "the routing pass skips it too");
			h.Item(result.Item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Queued);
			(await h.Routing.AcceptAsync(Dept, result.Item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue("a dispatcher can still take it from the queue");
		}

		[Test]
		public async Task An_offer_out_for_a_call_screened_afterwards_comes_back_to_the_queue_without_a_miss()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);
			var offered = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });
			offered.Outcome.Should().Be(DispatcherRouteOutcomes.Offered);

			// Enhanced AI's reading lands after the call was saved and offered.
			h.Screenings[100] = IntakeScreenings.LikelyNotIncident;
			await h.Routing.OnCallChangedAsync(Dept, 100);

			var item = h.Item(offered.Item.DispatcherQueueItemId);
			item.State.Should().Be((int)DispatcherQueueItemStates.Queued);
			item.AssignedUserId.Should().BeNull();
			(item.DeclinedUserIds ?? "").Should().NotContain("ann", "a withdrawn offer is no decline");
			h.PresenceOf("ann").MissedOffers.Should().Be(0);
			h.Store.Events.Should().Contain(e => e.DispatcherQueueItemId == item.DispatcherQueueItemId && e.EventType == (int)DispatcherQueueEventTypes.HeldForReview);

			// An unscreened waiting call keeps its offer.
			h.PendingCall(101);
			var other = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 101 });
			await h.Routing.OnCallChangedAsync(Dept, 101);
			h.Item(other.Item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Offered);
		}

		[Test]
		public async Task Busy_dispatchers_get_nothing_and_the_next_text_goes_to_the_other_one_then_overflows()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 30);
			h.SignedIn("bob", minutesSinceOffer: 5);
			h.PendingCall(100);

			// Ann takes the pending call and is busy.
			var first = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });
			first.UserId.Should().Be("ann");
			(await h.Routing.AcceptAsync(Dept, first.Item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue();
			(await h.Presence.GetMemberAsync(Dept, "ann", h.Now)).DisplayState.Should().Be(DispatcherDisplayStates.Busy);

			// A text comes in: it goes to Bob, who takes it; both are busy.
			var text = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230001", TargetAddress = "+15550000000", Body = "Fire in the barn", ProviderMessageId = "SM1"
			});
			text.Outcome.Should().Be(DispatcherRouteOutcomes.Offered);
			text.UserId.Should().Be("bob");
			(await h.Routing.AcceptAsync(Dept, text.Item.DispatcherQueueItemId, "bob")).Success.Should().BeTrue();

			// Another text from someone else: nobody is free, so it overflows into a pending call that waits in the queue.
			h.Config.TextOverflowMode = (int)DispatcherTextOverflowModes.EnhancedAi;
			var third = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230002", TargetAddress = "+15550000000", Body = "Car off the road on Route 9", ProviderMessageId = "SM2"
			});

			third.Outcome.Should().Be(DispatcherRouteOutcomes.Overflow);
			var stored = h.Item(third.Item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Queued, "it waits for a free dispatcher as a pending call");
			stored.CallId.Should().HaveValue();
			stored.OverflowedOn.Should().Be(h.Now);
			var call = h.Calls[stored.CallId.Value];
			call.State.Should().Be((int)CallStates.Pending);
			call.NatureOfCall.Should().Be("Car off the road on Route 9");
			call.SourceIdentifier.Should().Be("DQ:" + stored.DispatcherQueueItemId);
			h.Queue.Verify(x => x.EnqueueAiDispatchTriageAsync(It.Is<AiDispatchQueueItem>(q => q.CallId == call.CallId), It.IsAny<CancellationToken>()), Times.Once);

			// The texter still hears it arrived, worded for a pending call rather than promising a dispatcher's reply.
			h.Store.Messages.Where(x => x.DispatcherQueueItemId == stored.DispatcherQueueItemId && x.Direction == (int)DispatcherMessageDirections.Outbound)
				.Select(x => x.Body).Should().ContainSingle().Which.Should().Contain("passed it to the dispatchers").And.NotContain("will reply");
		}

		[Test]
		public async Task The_text_acknowledgement_and_voice_prompts_name_the_configured_emergency_number()
		{
			var saved = Resgrid.Config.IntakeConfig.EmergencyNumber;
			try
			{
				Resgrid.Config.IntakeConfig.EmergencyNumber = " 112 ";
				var h = new Harness();
				h.OnDuty("ann");
				h.SignedIn("ann");

				var result = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
				{
					DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230001", TargetAddress = "+15550000000", Body = "Help", ProviderMessageId = "SM1"
				});

				h.Store.Messages.Single(x => x.DispatcherQueueItemId == result.Item.DispatcherQueueItemId && x.Direction == (int)DispatcherMessageDirections.Outbound)
					.Body.Should().EndWith("call 112.").And.NotContain("911");
				TwilioVoicePromptCatalog.VoiceIntakeGreeting.Should().EndWith("dial 112.");
				TwilioVoicePromptCatalog.VoiceIntakeTextOnly.Should().Contain("dial 112.");

				Resgrid.Config.IntakeConfig.EmergencyNumber = "";
				EmergencyNumber.Current.Should().Be("911", "a blank setting falls back to 911");
			}
			finally
			{
				Resgrid.Config.IntakeConfig.EmergencyNumber = saved;
			}
		}

		[Test]
		public async Task A_follow_up_text_joins_the_open_conversation_and_reaches_its_holder()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var first = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "(555) 123-0001", TargetAddress = "+15550000000", Body = "Help", ProviderMessageId = "SM1"
			});
			await h.Routing.AcceptAsync(Dept, first.Item.DispatcherQueueItemId, "ann");

			h.Now = h.Now.AddMinutes(5);
			var second = await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230001", TargetAddress = "+15550000000", Body = "It's at 12 Main St", ProviderMessageId = "SM2"
			});

			second.Outcome.Should().Be(DispatcherRouteOutcomes.Joined);
			second.UserId.Should().Be("ann");
			h.Store.Items.Should().ContainSingle();
			h.Store.Messages.Where(x => x.Direction == (int)DispatcherMessageDirections.Inbound).Select(x => x.Body).Should().Equal("Help", "It's at 12 Main St");

			// A provider retry of the same message is ignored.
			(await h.Routing.RouteInboundMessageAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = Dept, Kind = DispatcherQueueItemKinds.Text, SourceAddress = "+15551230001", Body = "It's at 12 Main St", ProviderMessageId = "SM2"
			})).Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
		}

		[Test]
		public async Task A_declined_offer_moves_to_the_next_dispatcher_and_never_back()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 30);
			h.SignedIn("bob", minutesSinceOffer: 5);
			h.PendingCall(100);

			var offered = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });
			(await h.Routing.DeclineAsync(Dept, offered.Item.DispatcherQueueItemId, "ann", "On the radio")).Success.Should().BeTrue();

			var item = h.Item(offered.Item.DispatcherQueueItemId);
			item.State.Should().Be((int)DispatcherQueueItemStates.Offered);
			item.AssignedUserId.Should().Be("bob");

			await h.Routing.DeclineAsync(Dept, item.DispatcherQueueItemId, "bob", null);
			item = h.Item(offered.Item.DispatcherQueueItemId);
			item.State.Should().Be((int)DispatcherQueueItemStates.Queued, "a call waits in the shared queue when everyone passed");
			item.GetDeclinedUserIds().Should().BeEquivalentTo(new[] { "ann", "bob" });

			// Anyone can still take it from the queue.
			(await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue();
		}

		[Test]
		public async Task Expired_offers_move_on_and_repeated_misses_set_the_dispatcher_away()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);
			h.PendingCall(101);

			var first = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });
			first.UserId.Should().Be("ann");

			h.Now = h.Now.AddSeconds(DispatcherDeskConfig.DefaultOfferTimeoutSeconds + 1);
			await h.Routing.ProcessTimeoutsAsync(h.Now);
			h.Item(first.Item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Queued);
			h.PresenceOf("ann").MissedOffers.Should().Be(1);
			h.Store.Events.Should().Contain(x => x.EventType == (int)DispatcherQueueEventTypes.OfferExpired && x.UserId == "ann");

			// The second call is offered to her and she misses it too: two misses in a row set her Away.
			var second = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 101 });
			second.UserId.Should().Be("ann");
			h.Now = h.Now.AddSeconds(DispatcherDeskConfig.DefaultOfferTimeoutSeconds + 1);
			await h.Routing.ProcessTimeoutsAsync(h.Now);

			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.Away);
			h.PresenceOf("ann").AwayReason.Should().Be(DispatcherRoutingService.AwayAfterMissedOffers);
		}

		[Test]
		public async Task Waiting_work_escalates_once_to_on_duty_dispatchers_and_supervisors()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			h.PendingCall(100);
			var item = (await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 })).Item;

			h.Now = h.Now.AddMinutes(DispatcherDeskConfig.DefaultQueueEscalationMinutes + 1);
			await h.Routing.ProcessTimeoutsAsync(h.Now);
			await h.Routing.ProcessTimeoutsAsync(h.Now.AddMinutes(1));

			h.Item(item.DispatcherQueueItemId).EscalatedOn.Should().Be(h.Now);
			h.Store.Events.Count(x => x.EventType == (int)DispatcherQueueEventTypes.Escalated).Should().Be(1);
		}

		[Test]
		public async Task A_pending_call_dispatched_elsewhere_leaves_the_desk()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var call = h.PendingCall(100);
			var item = (await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 })).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");

			call.State = (int)CallStates.Active;
			call.HasBeenDispatched = true;
			await h.Routing.OnCallChangedAsync(Dept, 100);

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Completed);
			stored.Outcome.Should().Be((int)DispatcherQueueItemOutcomes.Dispatched);
			stored.CompletedByUserId.Should().Be("ann");
			(await h.Presence.GetMemberAsync(Dept, "ann", h.Now)).DisplayState.Should().Be(DispatcherDisplayStates.Available);
		}

		[Test]
		public async Task The_sweep_adds_pending_and_due_scheduled_calls_once()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			h.PendingCall(100);
			h.Calls[200] = new Call { CallId = 200, DepartmentId = Dept, State = (int)CallStates.Active, DispatchOn = h.Now.AddMinutes(10), LoggedOn = h.Now.AddDays(-1) };
			h.Calls[201] = new Call { CallId = 201, DepartmentId = Dept, State = (int)CallStates.Active, DispatchOn = h.Now.AddHours(5), LoggedOn = h.Now.AddDays(-1) };

			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(2);
			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(0);

			h.Store.Items.Select(x => x.CallId).Should().BeEquivalentTo(new int?[] { 100, 200 });
			h.Store.Items.Single(x => x.CallId == 200).Kind.Should().Be((int)DispatcherQueueItemKinds.ScheduledCall);
			h.Store.Items.Single(x => x.CallId == 200).DueOn.Should().Be(h.Now.AddMinutes(10));
		}

		[Test]
		public async Task The_sweep_recovers_chats_and_emails_under_their_own_kind_when_the_desk_takes_no_pending_calls()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			h.Config.RoutePendingCalls = false;
			h.Config.RouteChat = true;
			h.Config.RouteEmail = true;

			// A WhatsApp intake call and an emailed call whose first hand-off to the desk failed, the desk's own overflow call, and a plain
			// pending call (which this desk does not take).
			h.Calls[300] = new Call { CallId = 300, DepartmentId = Dept, State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake, LoggedOn = h.Now.AddHours(-1) };
			h.IntakeSources[300] = new IntakeCallSource { CallId = 300, ChannelType = (int)IntakeChannelTypes.WhatsApp };
			h.Calls[301] = new Call { CallId = 301, DepartmentId = Dept, State = (int)CallStates.Pending, CallSource = (int)CallSources.EmailImport, LoggedOn = h.Now.AddHours(-1) };
			h.Calls[302] = new Call { CallId = 302, DepartmentId = Dept, State = (int)CallStates.Pending, CallSource = (int)CallSources.EmailImport, SourceIdentifier = "DQ:item-1", LoggedOn = h.Now.AddHours(-1) };
			h.Calls[303] = new Call { CallId = 303, DepartmentId = Dept, State = (int)CallStates.Pending, LoggedOn = h.Now.AddHours(-1) };

			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(2);
			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(0, "each call goes on the desk once");

			h.Store.Items.Select(x => x.CallId).Should().BeEquivalentTo(new int?[] { 300, 301 });
			h.Store.Items.Single(x => x.CallId == 300).Kind.Should().Be((int)DispatcherQueueItemKinds.Chat);
			h.Store.Items.Single(x => x.CallId == 301).Kind.Should().Be((int)DispatcherQueueItemKinds.Email);
		}

		[Test]
		public async Task The_sweep_takes_a_chat_as_a_pending_call_when_chats_are_unticked()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			h.Config.RouteChat = false;

			h.Calls[300] = new Call { CallId = 300, DepartmentId = Dept, State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake, LoggedOn = h.Now.AddHours(-1) };
			h.IntakeSources[300] = new IntakeCallSource { CallId = 300, ChannelType = (int)IntakeChannelTypes.WhatsApp };

			(await h.Routing.SyncCallsAsync(Dept, h.Now)).Should().Be(1);
			h.Store.Items.Single(x => x.CallId == 300).Kind.Should().Be((int)DispatcherQueueItemKinds.PendingCall);
		}

		[Test]
		public async Task Nothing_is_routed_while_the_desk_is_off()
		{
			var h = new Harness();
			h.Config.Enabled = false;
			h.PendingCall(100);

			(await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 }))
				.Outcome.Should().Be(DispatcherRouteOutcomes.NotRouted);
			h.Store.Items.Should().BeEmpty();
		}

		// ------------------------------------------------------------------ breaks

		[Test]
		public async Task A_break_started_early_is_due_back_its_length_after_it_started()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			h.SignedIn("bob");
			var planned = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak
			{
				UserId = "ann", BreakType = (int)DispatcherBreakTypes.Meal, ScheduledStartOn = h.Now.AddMinutes(30), DurationMinutes = 60
			}, "ann");
			planned.Success.Should().BeTrue();

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", planned.Value.DispatcherBreakId, null, null, false, "ann");

			started.Success.Should().BeTrue();
			started.Value.ExpectedReturnOn.Should().Be(h.Now.AddMinutes(60), "the lunch is 60 minutes from when it starts, not until its planned end");
			started.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.StartedEarly && x.Minutes == 30);
			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.OnBreak);
		}

		[Test]
		public async Task On_break_a_dispatcher_gets_no_routed_work()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 60);
			h.SignedIn("bob", minutesSinceOffer: 1);
			(await h.Breaks.StartBreakAsync(Dept, "ann", null, (int)DispatcherBreakTypes.Break, 15, false, "ann")).Success.Should().BeTrue();
			h.PendingCall(100);

			var result = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 });

			result.UserId.Should().Be("bob");
		}

		[Test]
		public async Task Holding_work_blocks_a_break_unless_it_is_returned_to_the_queue_and_then_it_goes_to_someone_else()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 60);
			h.SignedIn("bob", minutesSinceOffer: 1);
			h.PendingCall(100);
			var item = (await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 })).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");
			h.Config.MinimumAvailableDispatchers = 0;

			var refused = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "ann");
			refused.Success.Should().BeFalse();
			refused.Error.Should().Be(DispatcherDeskErrors.HoldingWork);

			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, true, "ann");
			started.Success.Should().BeTrue();
			started.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.WorkReturnedToQueue);

			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Offered);
			stored.AssignedUserId.Should().Be("bob", "work handed back is never offered straight back to the dispatcher going on break");
		}

		[Test]
		public async Task Coverage_below_the_minimum_warns_or_refuses_unless_a_supervisor_starts_it()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			h.SignedIn("bob", DispatcherPresenceStates.OnBreak);
			h.Config.MinimumAvailableDispatchers = 1;

			var warned = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "ann");
			warned.Success.Should().BeTrue();
			warned.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.NoCoverage && x.Minimum == 1 && x.Available == 0);
			await h.Breaks.EndBreakAsync(Dept, "ann", "ann", DispatcherBreakEndReasons.Returned);

			h.Config.PreventBreakBelowMinimum = true;
			var refused = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "ann");
			refused.Error.Should().Be(DispatcherDeskErrors.CoverageTooLow);

			h.Supervisors.Add("sup");
			var overridden = await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "sup");
			overridden.Success.Should().BeTrue();
		}

		[Test]
		public async Task The_department_auto_return_makes_a_late_dispatcher_available_after_the_override_minutes()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			h.SignedIn("bob");
			h.Config.AutoReturnAfterMinutes = 10;
			var started = await h.Breaks.StartBreakAsync(Dept, "ann", null, (int)DispatcherBreakTypes.Break, 15, false, "ann");

			h.Now = h.Now.AddMinutes(15);
			await h.Breaks.ProcessBreakTimersAsync(h.Now);
			h.Pushes.Should().Contain(x => x.Kind == "break-over" && x.UserId == "ann");
			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.OnBreak, "she is due back but the override has not run out");

			h.Now = h.Now.AddMinutes(10);
			await h.Breaks.ProcessBreakTimersAsync(h.Now);

			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.Available);
			var row = h.Store.Breaks.Single(x => x.DispatcherBreakId == started.Value.DispatcherBreakId);
			row.State.Should().Be((int)DispatcherBreakStates.Completed);
			row.EndReason.Should().Be((int)DispatcherBreakEndReasons.AutoReturn);
			h.Pushes.Should().Contain(x => x.Kind == "auto-returned" && x.UserId == "ann");
			h.Pushes.Should().Contain(x => x.Kind == "supervisors");
		}

		[Test]
		public async Task Without_auto_return_the_dispatcher_stays_on_break_and_supervisors_hear_once()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			h.SignedIn("bob");
			await h.Breaks.StartBreakAsync(Dept, "ann", null, null, 15, false, "ann");

			for (var minute = 15; minute <= 60; minute += 5)
				await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(minute));

			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.OnBreak);
			h.Pushes.Count(x => x.Kind == "supervisors").Should().Be(1);
			h.Pushes.Count(x => x.Kind == "break-over").Should().Be(1);
		}

		[Test]
		public async Task Planned_breaks_remind_once_before_and_once_when_due()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var planned = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { UserId = "ann", ScheduledStartOn = h.Now.AddMinutes(20), DurationMinutes = 15 }, "ann");

			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(10));
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(16));
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(17));
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(20));
			await h.Breaks.ProcessBreakTimersAsync(h.Now.AddMinutes(21));

			h.Pushes.Where(x => x.Code == "DB:" + planned.Value.DispatcherBreakId).Select(x => x.Kind).Should().Equal("reminder", "due");
		}

		[Test]
		public async Task Planning_refuses_overlapping_your_own_break_and_warns_about_others()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			h.SignedIn("bob");
			await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { UserId = "ann", ScheduledStartOn = h.Now.AddHours(1), DurationMinutes = 30 }, "ann");

			var own = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { UserId = "ann", ScheduledStartOn = h.Now.AddHours(1).AddMinutes(15), DurationMinutes = 30 }, "ann");
			own.Error.Should().Be(DispatcherDeskErrors.BreakOverlaps);

			var other = await h.Breaks.SaveBreakAsync(Dept, new DispatcherBreak { UserId = "bob", ScheduledStartOn = h.Now.AddHours(1).AddMinutes(15), DurationMinutes = 30 }, "bob");
			other.Success.Should().BeTrue();
			other.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.BreaksOverlap);
			other.Warnings.Should().Contain(x => x.Kind == DispatcherDeskWarningKinds.NoCoverage);
		}

		// ------------------------------------------------------------------ presence

		[Test]
		public async Task Signing_in_needs_a_shift_unless_the_department_allows_unscheduled_duty()
		{
			var h = new Harness();

			(await h.Presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest { DeviceKind = "phone" })).Error.Should().Be(DispatcherDeskErrors.NotOnDuty);

			h.Config.AllowUnscheduledDuty = true;
			var signedIn = await h.Presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest
			{
				DeviceKind = "phone", Unscheduled = true, DutyEndsOn = h.Now.AddHours(6), CallbackNumber = "555-123-0009"
			});

			signedIn.Success.Should().BeTrue();
			signedIn.Value.IsOnDuty.Should().BeTrue();
			signedIn.Value.DisplayState.Should().Be(DispatcherDisplayStates.Available);
			h.PresenceOf("ann").CallbackNumber.Should().Be("+15551230009");
			h.PresenceOf("ann").DeviceKind.Should().Be("phone");
		}

		[Test]
		public async Task Signing_off_returns_held_work_to_the_queue_and_ends_the_break()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			h.PendingCall(100);
			var item = (await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 })).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");

			(await h.Presence.SignOffAsync(Dept, "ann", "ann")).Value.Should().BeTrue();

			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.Offline);
			var stored = h.Item(item.DispatcherQueueItemId);
			stored.State.Should().Be((int)DispatcherQueueItemStates.Queued);
			stored.AssignedUserId.Should().BeNull();
		}

		[Test]
		public async Task Duty_end_signs_the_dispatcher_off()
		{
			var h = new Harness();
			h.OnDuty("ann");
			(await h.Presence.SignInAsync(Dept, "ann", new DispatcherSignInRequest())).Success.Should().BeTrue();

			h.Now = h.Now.AddHours(9);
			h.Duty.Clear();
			await h.Presence.ProcessPresenceAsync(h.Now);

			h.PresenceOf("ann").State.Should().Be((int)DispatcherPresenceStates.Offline);
		}

		[Test]
		public async Task Stats_count_offers_accepts_and_breaks()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", minutesSinceOffer: 60);
			h.SignedIn("bob", minutesSinceOffer: 1);
			h.PendingCall(100);
			var item = (await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 100 })).Item;
			h.Now = h.Now.AddSeconds(20);
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");
			h.Now = h.Now.AddMinutes(2);
			await h.Routing.CompleteAsync(Dept, item.DispatcherQueueItemId, "ann", DispatcherQueueItemOutcomes.Dispatched, null, null);

			var stats = await h.Routing.GetStatsAsync(Dept, h.Now.AddHours(-1), h.Now.AddHours(1));

			stats.Created.Should().Be(1);
			stats.Completed.Should().Be(1);
			var ann = stats.Dispatchers.Single(x => x.UserId == "ann");
			ann.Offered.Should().Be(1);
			ann.Accepted.Should().Be(1);
			ann.Dispatched.Should().Be(1);
			ann.AverageSecondsToAccept.Should().Be(20);
			ann.AverageSecondsToComplete.Should().Be(120);
		}

		// ------------------------------------------------------------------ intake text threads (plan F2)

		[Test]
		public async Task An_intake_text_thread_is_offered_as_a_text_item_linked_to_its_call_without_a_desk_acknowledgement()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var (conversation, call) = h.IntakeThread(300);

			var result = await h.Routing.RouteIntakeConversationAsync(conversation, call);

			result.Outcome.Should().Be(DispatcherRouteOutcomes.Offered);
			var item = h.Store.Items.Single();
			item.Kind.Should().Be((int)DispatcherQueueItemKinds.Text);
			item.CallId.Should().Be(300);
			item.ExternalReference.Should().Be("IC:ic-1");
			item.ConversationKey.Should().BeNull("replies to the master number reach the thread through intake, not the desk's text key");
			item.AssignedUserId.Should().Be("ann");
			h.TextsSent().Should().BeFalse("intake already sent its hand-off welcome");
			h.Store.Messages.Should().BeEmpty("the thread's messages live in intake (call notes)");
			h.Intake.Verify(x => x.SetDeskItemAsync(Dept, "ic-1", item.DispatcherQueueItemId, It.IsAny<CancellationToken>()), Times.Once);

			// Routing the same thread again is not a second item.
			(await h.Routing.RouteIntakeConversationAsync(conversation, call)).Outcome.Should().Be(DispatcherRouteOutcomes.Duplicate);
			h.Store.Items.Should().HaveCount(1);
		}

		[Test]
		public async Task When_the_call_reached_the_desk_first_its_pending_call_item_becomes_the_threads()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var (conversation, call) = h.IntakeThread(301);
			var first = await h.Routing.EnqueueAsync(new DispatcherEnqueueRequest { DepartmentId = Dept, Kind = DispatcherQueueItemKinds.PendingCall, CallId = 301 });

			var result = await h.Routing.RouteIntakeConversationAsync(conversation, call);

			result.Item.DispatcherQueueItemId.Should().Be(first.Item.DispatcherQueueItemId);
			h.Store.Items.Should().HaveCount(1);
			var item = h.Store.Items.Single();
			item.Kind.Should().Be((int)DispatcherQueueItemKinds.Text);
			item.ExternalReference.Should().Be("IC:ic-1");
			item.SourceAddress.Should().Be("+15555550123");
		}

		[Test]
		public async Task Accepting_connects_the_dispatcher_and_a_thread_taken_on_the_call_page_first_is_refused()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann");
			var (conversation, call) = h.IntakeThread(302);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;

			(await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue();
			h.Intake.Verify(x => x.ConnectDispatcherAsync(Dept, "ic-1", "ann", true, It.IsAny<CancellationToken>()), Times.Once);

			// A second thread: bob connected from the call page while it waited, so ann's take from the queue is refused.
			var (other, otherCall) = h.IntakeThread(303, "ic-2");
			h.SignedIn("bob", DispatcherPresenceStates.Away);
			var waiting = (await h.Routing.RouteIntakeConversationAsync(other, otherCall)).Item;
			other.AssignedUserId = "bob";
			other.Handler = (int)IntakeConversationHandlers.Dispatcher;
			h.Intake.Setup(x => x.ConnectDispatcherAsync(Dept, "ic-2", "ann", false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.HeldByAnotherDispatcher);

			var refused = await h.Routing.AcceptAsync(Dept, waiting.DispatcherQueueItemId, "ann");

			refused.Success.Should().BeFalse();
			refused.Error.Should().Be(DispatcherDeskErrors.ItemNotAvailable);
			h.Item(waiting.DispatcherQueueItemId).AssignedUserId.Should().Be("bob");
			h.Item(waiting.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Accepted);
		}

		[Test]
		public async Task Releasing_hands_the_thread_back_and_completing_closes_it()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var (conversation, call) = h.IntakeThread(304);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");

			(await h.Routing.ReleaseAsync(Dept, item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue();
			h.Intake.Verify(x => x.ReleaseDispatcherAsync(Dept, "ic-1", "ann", It.IsAny<CancellationToken>()), Times.Once);

			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");
			(await h.Routing.CompleteAsync(Dept, item.DispatcherQueueItemId, "ann", DispatcherQueueItemOutcomes.CallCreated, null, null, notifyParticipant: false))
				.Success.Should().BeTrue();
			h.Intake.Verify(x => x.CloseAsync(Dept, "ic-1", "ann", It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);

			// Intake then reports the close back: the item is already done.
			await h.Routing.OnIntakeConversationClosedAsync(conversation);
			h.Item(item.DispatcherQueueItemId).Outcome.Should().Be((int)DispatcherQueueItemOutcomes.CallCreated);
		}

		[Test]
		public async Task Intake_callbacks_move_the_item_a_connect_from_the_call_page_and_a_release()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var (conversation, call) = h.IntakeThread(305);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			h.Item(item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Queued);

			// Intake raises the callback after its own connect committed: the thread is carol's.
			conversation.AssignedUserId = "carol";
			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			h.Item(item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("carol");

			conversation.AssignedUserId = null;
			await h.Routing.OnIntakeDispatcherReleasedAsync(conversation, "carol");

			h.Item(item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Queued);
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().BeNull();
			h.Intake.Verify(x => x.ReleaseDispatcherAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"intake already released the thread itself");
		}

		[Test]
		public async Task A_connect_from_the_call_page_that_loses_to_a_message_is_retried_while_intake_still_has_that_dispatcher()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var (conversation, call) = h.IntakeThread(320);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.AssignedUserId = "carol";

			// A participant's message touches the item between the callback's read and its write.
			h.Store.BeforeStoredItemUpdate = stored => { h.Store.BeforeStoredItemUpdate = null; stored.Version++; };
			await h.Routing.OnIntakeDispatcherConnectedAsync(conversation, "carol");

			h.Item(item.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Accepted);
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("carol");

			// The same race after carol stepped away again on intake: the stale connect is not recorded.
			var (other, otherCall) = h.IntakeThread(321, "ic-2");
			var second = (await h.Routing.RouteIntakeConversationAsync(other, otherCall)).Item;
			other.AssignedUserId = null;
			h.Store.BeforeStoredItemUpdate = stored => { h.Store.BeforeStoredItemUpdate = null; stored.Version++; };
			await h.Routing.OnIntakeDispatcherConnectedAsync(other, "carol");

			h.Item(second.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Queued);
		}

		[Test]
		public async Task A_closed_thread_never_turns_an_item_a_dispatcher_took_meanwhile_into_a_pending_call()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(322);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			conversation.State = (int)IntakeConversationStates.Closed;

			// ann takes the item between the close callback's state check and its write.
			h.Store.BeforeStoredItemUpdate = stored =>
			{
				h.Store.BeforeStoredItemUpdate = null;
				stored.Version++;
				stored.State = (int)DispatcherQueueItemStates.Accepted;
				stored.AssignedUserId = "ann";
			};
			await h.Routing.OnIntakeConversationClosedAsync(conversation);

			h.Item(item.DispatcherQueueItemId).Kind.Should().Be((int)DispatcherQueueItemKinds.Text, "the dispatcher keeps the item as they took it");
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("ann");
		}

		[Test]
		public async Task A_release_that_never_reached_intake_is_finished_by_the_next_dispatchers_accept()
		{
			var h = new Harness();
			h.OnDuty("ann", "bob");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var (conversation, call) = h.IntakeThread(323);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			(await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue();
			conversation.AssignedUserId = "ann";

			h.Now = h.Now.AddMinutes(2);
			h.Intake.Setup(x => x.ReleaseDispatcherAsync(Dept, "ic-1", "ann", It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("intake store unavailable"));
			(await h.Routing.ReleaseAsync(Dept, item.DispatcherQueueItemId, "ann")).Success.Should().BeTrue("the desk's own state changed");
			h.Intake.Verify(x => x.ReleaseDispatcherAsync(Dept, "ic-1", "ann", It.IsAny<CancellationToken>()), Times.Exactly(3), "an idempotent hand-back is retried");

			// Intake still has ann on the thread; bob takes the item from the queue.
			h.Intake.Setup(x => x.ReleaseDispatcherAsync(Dept, "ic-1", "ann", It.IsAny<CancellationToken>()))
				.Callback(() => conversation.AssignedUserId = null).ReturnsAsync(IntakeConversationActionResults.Done);
			h.Intake.SetupSequence(x => x.ConnectDispatcherAsync(Dept, "ic-1", "bob", false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.HeldByAnotherDispatcher)
				.ReturnsAsync(IntakeConversationActionResults.Done);
			h.Now = h.Now.AddMinutes(2);
			h.SignedIn("bob", DispatcherPresenceStates.Away);

			var taken = await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "bob");

			taken.Success.Should().BeTrue();
			h.Item(item.DispatcherQueueItemId).AssignedUserId.Should().Be("bob");
			h.Intake.Verify(x => x.ReleaseDispatcherAsync(Dept, "ic-1", "ann", It.IsAny<CancellationToken>()), Times.Exactly(4), "the owed release, against ann only");
			h.Intake.Verify(x => x.ConnectDispatcherAsync(Dept, "ic-1", "bob", false, It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task A_completion_whose_thread_close_failed_closes_the_thread_on_its_next_message()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann", DispatcherPresenceStates.Away);
			var (conversation, call) = h.IntakeThread(324);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");
			h.Intake.Setup(x => x.CloseAsync(Dept, "ic-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new TimeoutException("intake store unavailable"));

			(await h.Routing.CompleteAsync(Dept, item.DispatcherQueueItemId, "ann", DispatcherQueueItemOutcomes.CallCreated, null, null, notifyParticipant: true))
				.Success.Should().BeTrue();
			h.Intake.Verify(x => x.CloseAsync(Dept, "ic-1", "ann", It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Exactly(3));

			h.Intake.Setup(x => x.CloseAsync(Dept, "ic-1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.Done);
			await h.Routing.OnIntakeParticipantMessageAsync(conversation);

			h.Intake.Verify(x => x.CloseAsync(Dept, "ic-1", "ann", It.Is<string>(r => r.Contains("CallCreated")), false, It.IsAny<CancellationToken>()), Times.Once,
				"the owed close is finished without a text the dispatcher may not have asked for");
		}

		[Test]
		public async Task A_message_on_a_thread_whose_item_ended_because_the_call_went_out_leaves_the_thread_open()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(325);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			call.State = (int)CallStates.Active;
			await h.Routing.OnCallChangedAsync(Dept, call.CallId);
			h.Item(item.DispatcherQueueItemId).IsOpen().Should().BeFalse();

			await h.Routing.OnIntakeParticipantMessageAsync(conversation);

			h.Intake.Verify(x => x.CloseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_closed_thread_leaves_a_still_pending_call_on_the_desk_and_completes_an_item_whose_call_went_out()
		{
			var h = new Harness();
			var (conversation, call) = h.IntakeThread(306);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;

			conversation.State = (int)IntakeConversationStates.Closed;
			conversation.CloseReason = "Idle";
			await h.Routing.OnIntakeConversationClosedAsync(conversation);

			h.Item(item.DispatcherQueueItemId).IsOpen().Should().BeTrue();
			h.Item(item.DispatcherQueueItemId).Kind.Should().Be((int)DispatcherQueueItemKinds.PendingCall, "the call still waits for a dispatcher");

			var (other, otherCall) = h.IntakeThread(307, "ic-2");
			var second = (await h.Routing.RouteIntakeConversationAsync(other, otherCall)).Item;
			otherCall.State = (int)CallStates.Active;
			await h.Routing.OnIntakeConversationClosedAsync(other);

			h.Item(second.DispatcherQueueItemId).State.Should().Be((int)DispatcherQueueItemStates.Completed);
			h.Item(second.DispatcherQueueItemId).Outcome.Should().Be((int)DispatcherQueueItemOutcomes.Dispatched);
		}

		[Test]
		public async Task Replies_on_a_thread_go_through_intake_and_an_opted_out_texter_cannot_be_texted()
		{
			var h = new Harness();
			h.OnDuty("ann");
			h.SignedIn("ann");
			var (conversation, call) = h.IntakeThread(308);
			var item = (await h.Routing.RouteIntakeConversationAsync(conversation, call)).Item;
			await h.Routing.AcceptAsync(Dept, item.DispatcherQueueItemId, "ann");

			var sent = await h.Conversations.SendReplyAsync(Dept, item.DispatcherQueueItemId, "ann", "Units are on the way.");

			sent.Success.Should().BeTrue();
			h.Intake.Verify(x => x.SendDispatcherMessageAsync(Dept, "ic-1", "ann", "Units are on the way.", It.IsAny<CancellationToken>()), Times.Once);
			h.TextsSent().Should().BeFalse("intake texts from the master number");
			h.Store.Messages.Should().BeEmpty();

			h.Intake.Setup(x => x.SendDispatcherMessageAsync(Dept, "ic-1", "ann", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(IntakeConversationActionResults.OptedOut);
			var refused = await h.Conversations.SendReplyAsync(Dept, item.DispatcherQueueItemId, "ann", "Are you there?");

			refused.Success.Should().BeFalse();
			refused.Error.Should().Be(DispatcherDeskErrors.ChannelUnavailable);
		}

		[Test]
		public async Task The_router_never_throws_into_intake()
		{
			var routing = new Mock<IDispatcherRoutingService>();
			routing.Setup(x => x.RouteIntakeConversationAsync(It.IsAny<IntakeConversation>(), It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("database down"));
			routing.Setup(x => x.OnIntakeParticipantMessageAsync(It.IsAny<IntakeConversation>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("database down"));
			var router = new DispatcherIntakeConversationRouter(new Lazy<IDispatcherRoutingService>(() => routing.Object));

			(await router.RouteNewConversationAsync(new IntakeConversation { IntakeConversationId = "x" }, new Call(), CancellationToken.None))
				.Should().Be(IntakeConversationRouting.NotRouted);
			Func<Task> message = () => router.OnParticipantMessageAsync(new IntakeConversation(), new IntakeConversationMessage(), CancellationToken.None);
			await message.Should().NotThrowAsync();
		}
	}
}
