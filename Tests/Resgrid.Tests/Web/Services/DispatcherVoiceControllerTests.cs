using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;
using Resgrid.Web.Services.Twilio;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>The Twilio webhooks of the dispatcher desk voice flow (plan D8): what each returns and what it records.</summary>
	[TestFixture]
	public class DispatcherVoiceControllerTests
	{
		private const int DepartmentId = 42;
		private const string ItemId = "item-1";
		private const string CallerSid = "CA-caller";
		private const string DispatcherId = "dispatcher-1";
		private const string QueueName = "rgdesk-item-1";

		private Mock<IDispatcherQueueItemsRepository> _items;
		private Mock<IDispatcherRoutingService> _routing;
		private Mock<IDispatcherVoiceService> _voice;
		private Mock<IDispatcherDeskConfigService> _config;
		private Mock<IDispatcherDeskPendingCalls> _pendingCalls;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ITwilioVoiceResponseService> _tts;
		private DispatcherQueueItem _item;
		private List<DispatcherQueueEvent> _events;
		private DispatcherDeskConfig _deskConfig;

		[SetUp]
		public void SetUp()
		{
			_item = new DispatcherQueueItem
			{
				DispatcherQueueItemId = ItemId,
				DepartmentId = DepartmentId,
				Kind = (int)DispatcherQueueItemKinds.Voice,
				State = (int)DispatcherQueueItemStates.Offered,
				AssignedUserId = DispatcherId,
				SourceAddress = "+15551234567",
				TargetAddress = "+15559990000",
				ExternalReference = CallerSid,
				CreatedOn = DateTime.UtcNow,
				Version = 3
			};
			_events = new List<DispatcherQueueEvent>();
			_deskConfig = new DispatcherDeskConfig { Enabled = true, VoiceHoldSeconds = 30 };

			_items = new Mock<IDispatcherQueueItemsRepository>();
			_items.Setup(x => x.GetItemAsync(ItemId)).ReturnsAsync(() => _item);
			_items.Setup(x => x.TryUpdateAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_routing = new Mock<IDispatcherRoutingService>();
			_routing.Setup(x => x.GetItemDetailAsync(DepartmentId, ItemId, It.IsAny<string>()))
				.ReturnsAsync(() => new DispatcherQueueItemDetail { Item = _item, Events = _events });
			_routing.Setup(x => x.AcceptAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					_item.State = (int)DispatcherQueueItemStates.Accepted;
					return DispatcherDeskResult<DispatcherQueueItem>.Ok(_item);
				});
			_routing.Setup(x => x.ReleaseAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => DispatcherDeskResult<DispatcherQueueItem>.Ok(_item));
			_routing.Setup(x => x.TryMarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_voice = new Mock<IDispatcherVoiceService>();
			_voice.Setup(x => x.QueueNameFor(It.IsAny<DispatcherQueueItem>())).Returns(QueueName);

			_config = new Mock<IDispatcherDeskConfigService>();
			_config.Setup(x => x.GetConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _deskConfig);

			_pendingCalls = new Mock<IDispatcherDeskPendingCalls>();
			_calls = new Mock<ICallsService>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = "admin-1" });
			_settings = new Mock<IDepartmentSettingsService>();

			_tts = new Mock<ITwilioVoiceResponseService>();
			_tts.Setup(x => x.AppendPromptAsync(It.IsAny<VoiceResponse>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<VoiceResponse, string, CancellationToken, string>((response, text, _, __) =>
				{
					response.Append(new Play { Url = PromptUri(text) });
					return System.Threading.Tasks.Task.CompletedTask;
				});
			_tts.Setup(x => x.AppendPromptAsync(It.IsAny<Gather>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Returns<Gather, string, CancellationToken, string>((gather, text, _, __) =>
				{
					gather.Append(new Play { Url = PromptUri(text) });
					return System.Threading.Tasks.Task.CompletedTask;
				});
		}

		private static Uri PromptUri(string text) => new Uri($"https://tts.example/{Uri.EscapeDataString(text)}.wav");

		private static string Prompt(string text) => Uri.EscapeDataString(text);

		private DispatcherVoiceController Controller(params (string Name, string Value)[] form)
		{
			var context = new DefaultHttpContext();
			context.Request.Method = "POST";
			context.Request.ContentType = "application/x-www-form-urlencoded";
			context.Request.Form = new FormCollection(form.ToDictionary(x => x.Name, x => new StringValues(x.Value)));

			return new DispatcherVoiceController(_items.Object, _routing.Object, _voice.Object, _config.Object, _pendingCalls.Object, _calls.Object,
				_departments.Object, _settings.Object, _tts.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = context }
			};
		}

		private static string Content(ActionResult result) => ((ContentResult)result).Content;

		private void VerifyVoiceEvent(DispatcherQueueEventTypes type, string userId, string detail, Times times)
		{
			_routing.Verify(x => x.RecordVoiceEventAsync(It.Is<DispatcherQueueItem>(i => i.DispatcherQueueItemId == ItemId), type, userId,
				It.Is<string>(d => detail == null || d == detail), It.IsAny<CancellationToken>()), times);
		}

		#region Hold

		[Test]
		public async System.Threading.Tasks.Task Hold_greets_a_new_caller_and_pauses_before_the_next_check()
		{
			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "0")).Hold(ItemId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.HoldGreeting));
			content.Should().Contain($"<Pause length=\"{DispatcherVoiceController.HoldPauseSeconds}\"");
			content.Should().NotContain("<Leave");
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_asks_a_caller_already_waiting_to_continue_holding()
		{
			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "25")).Hold(ItemId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.HoldContinue));
			content.Should().NotContain(Prompt(DispatcherDeskVoicePrompts.HoldGreeting));
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_leaves_when_the_item_overflowed()
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;

			var content = Content(await Controller(("CallSid", CallerSid)).Hold(ItemId));

			content.Should().Contain("<Leave");
			_routing.Verify(x => x.TryMarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_overflows_a_queued_caller_held_past_the_department_hold_time()
		{
			_item.State = (int)DispatcherQueueItemStates.Queued;
			_item.AssignedUserId = null;
			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-45);

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "45")).Hold(ItemId));

			content.Should().Contain("<Leave");
			_routing.Verify(x => x.TryMarkOverflowedAsync(_item, It.Is<string>(n => n.Contains("voicemail")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_keeps_the_caller_when_a_dispatcher_takes_them_as_the_overflow_is_written()
		{
			_item.State = (int)DispatcherQueueItemStates.Queued;
			_item.AssignedUserId = null;
			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-45);
			var taken = new DispatcherQueueItem
			{
				DispatcherQueueItemId = ItemId, DepartmentId = DepartmentId, Kind = _item.Kind, State = (int)DispatcherQueueItemStates.Accepted,
				AssignedUserId = DispatcherId, ExternalReference = CallerSid, CreatedOn = _item.CreatedOn, Version = _item.Version + 1
			};
			// Webhook read and the recheck both see Queued; the accept lands just before the conditional overflow write.
			_items.SetupSequence(x => x.GetItemAsync(ItemId)).ReturnsAsync(_item).ReturnsAsync(_item).ReturnsAsync(taken);
			_routing.Setup(x => x.TryMarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "45")).Hold(ItemId));

			content.Should().NotContain("<Leave", "the dispatcher who answered keeps the caller; the next hold poll decides on the accepted item");
			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_keeps_a_queued_caller_within_the_hold_time()
		{
			_item.State = (int)DispatcherQueueItemStates.Queued;
			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-5);

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "5")).Hold(ItemId));

			content.Should().NotContain("<Leave");
			_routing.Verify(x => x.TryMarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_with_no_hold_time_overflows_a_queued_caller_at_once()
		{
			_deskConfig.VoiceHoldSeconds = 0;
			_item.State = (int)DispatcherQueueItemStates.Queued;

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "0")).Hold(ItemId));

			content.Should().Contain("<Leave");
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_keeps_an_offered_caller_until_the_hard_cap()
		{
			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-120);

			Content(await Controller(("CallSid", CallerSid), ("QueueTime", "120")).Hold(ItemId)).Should().NotContain("<Leave");

			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-(DispatcherVoiceController.MaxHoldSeconds + 5));
			Content(await Controller(("CallSid", CallerSid), ("QueueTime", "605")).Hold(ItemId)).Should().Contain("<Leave");
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_rechecks_a_caller_a_dispatcher_took_while_the_hold_time_loaded()
		{
			_item.State = (int)DispatcherQueueItemStates.Queued;
			_item.AssignedUserId = null;
			_item.CreatedOn = DateTime.UtcNow.AddSeconds(-45);
			var taken = new DispatcherQueueItem
			{
				DispatcherQueueItemId = ItemId, DepartmentId = DepartmentId, Kind = _item.Kind, State = (int)DispatcherQueueItemStates.Accepted,
				AssignedUserId = DispatcherId, ExternalReference = CallerSid, CreatedOn = _item.CreatedOn, Version = _item.Version + 1
			};
			// The webhook reads the Queued item; by the time the queued limit is decided, a dispatcher has accepted it.
			_items.SetupSequence(x => x.GetItemAsync(ItemId)).ReturnsAsync(_item).ReturnsAsync(taken);

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueTime", "45")).Hold(ItemId));

			content.Should().NotContain("<Leave");
			_routing.Verify(x => x.TryMarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Hold_ignores_a_request_from_another_call()
		{
			var content = Content(await Controller(("CallSid", "CA-someone-else")).Hold(ItemId));

			content.Should().Contain("<Leave");
			_config.Verify(x => x.GetConfigAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion Hold

		#region EnqueueAction / Overflow / Voicemail

		[Test]
		public async System.Threading.Tasks.Task EnqueueAction_hangup_marks_the_caller_abandoned_and_keeps_the_item_open()
		{
			await Controller(("CallSid", CallerSid), ("QueueResult", "hangup")).EnqueueAction(ItemId);

			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, null, "Caller hung up while holding", Times.Once());
			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.CompleteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DispatcherQueueItemOutcomes>(),
				It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task EnqueueAction_bridged_records_the_end_and_hangs_up()
		{
			var content = Content(await Controller(("CallSid", CallerSid), ("QueueResult", "bridged")).EnqueueAction(ItemId));

			content.Should().Contain("<Hangup");
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, null, "Call ended", Times.Once());
		}

		[Test]
		public async System.Threading.Tasks.Task EnqueueAction_leave_marks_overflow_and_takes_a_voicemail()
		{
			var content = Content(await Controller(("CallSid", CallerSid), ("QueueResult", "leave")).EnqueueAction(ItemId));

			_routing.Verify(x => x.MarkOverflowedAsync(_item, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.VoicemailIntro));
			content.Should().Contain("<Record");
			content.Should().Contain($"api/Twilio/DeskVoicemail?itemId={ItemId}");
			content.Should().Contain($"api/Twilio/DeskVoicemailTranscription?itemId={ItemId}");
			content.Should().Contain($"maxLength=\"{DispatcherVoiceController.VoicemailMaxSeconds}\"");
		}

		[Test]
		public async System.Threading.Tasks.Task EnqueueAction_leave_does_not_mark_an_item_already_overflowed()
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;

			var content = Content(await Controller(("CallSid", CallerSid), ("QueueResult", "leave")).EnqueueAction(ItemId));

			content.Should().Contain("<Record");
			_routing.Verify(x => x.MarkOverflowedAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Overflow_marks_the_item_and_returns_the_voicemail()
		{
			var content = Content(await Controller(("CallSid", CallerSid)).Overflow(ItemId));

			content.Should().Contain("<Record");
			_routing.Verify(x => x.MarkOverflowedAsync(_item, "No dispatcher free: voicemail", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Overflow_for_an_unknown_item_still_tells_the_caller_and_hangs_up()
		{
			var content = Content(await Controller(("CallSid", CallerSid)).Overflow("missing"));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.NoDispatcherAvailable));
			content.Should().Contain("<Hangup");
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_makes_a_pending_call_of_the_voice_item()
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;
			_pendingCalls.Setup(x => x.FromVoicemailAsync(_item, "+15551234567", "https://api.twilio.com/rec/RE1", 17, "RE1", It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.Saved, new Call { CallId = 900, DepartmentId = DepartmentId }));

			var content = Content(await Controller(("CallSid", CallerSid), ("RecordingUrl", "https://api.twilio.com/rec/RE1"),
				("RecordingDuration", "17"), ("RecordingSid", "RE1")).Voicemail(ItemId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.VoicemailThanks));
			content.Should().Contain("<Hangup");
			_pendingCalls.Verify(x => x.FromVoicemailAsync(_item, "+15551234567", "https://api.twilio.com/rec/RE1", 17, "RE1", It.IsAny<CancellationToken>()),
				Times.Once);
			// The reopened item (it waits as the voicemail call) must not read as a caller still on hold.
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, null, "Caller left a voicemail", Times.Once());
			// The item is never written behind the routing service's back.
			_items.Verify(x => x.TryUpdateAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_repeated_does_not_make_a_second_call()
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;
			_item.CallId = 900;

			await Controller(("CallSid", CallerSid), ("RecordingUrl", "https://api.twilio.com/rec/RE1"), ("RecordingSid", "RE1")).Voicemail(ItemId);

			_pendingCalls.Verify(x => x.FromVoicemailAsync(It.IsAny<DispatcherQueueItem>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Voicemail_another_request_already_took_is_acknowledged()
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;
			_pendingCalls.Setup(x => x.FromVoicemailAsync(_item, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.AlreadyTaken));

			var content = Content(await Controller(("CallSid", CallerSid), ("RecordingUrl", "https://api.twilio.com/rec/RE1"), ("RecordingSid", "RE1")).Voicemail(ItemId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.VoicemailThanks));
		}

		[TestCase(true)]
		[TestCase(false)]
		public async System.Threading.Tasks.Task Voicemail_that_could_not_be_saved_is_not_acknowledged_and_stays_on_the_item(bool throws)
		{
			_item.State = (int)DispatcherQueueItemStates.Overflowed;
			var setup = _pendingCalls.Setup(x => x.FromVoicemailAsync(_item, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()));
			if (throws)
				setup.ThrowsAsync(new InvalidOperationException("db down"));
			else
				setup.ReturnsAsync(DispatcherVoicemailResult.Of(DispatcherVoicemailOutcomes.NotSaved));

			var content = Content(await Controller(("CallSid", CallerSid), ("RecordingUrl", "https://api.twilio.com/rec/RE1"), ("RecordingSid", "RE1")).Voicemail(ItemId));

			// The caller is not told the message reached a dispatcher; the dispatchers still find the recording in the item's history.
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.VoicemailNotSaved));
			content.Should().NotContain(Prompt(DispatcherDeskVoicePrompts.VoicemailThanks));
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, null,
				"The voicemail could not be saved as a call. Recording: https://api.twilio.com/rec/RE1", Times.Once());
		}

		[Test]
		public async System.Threading.Tasks.Task VoicemailTranscription_waits_for_the_voicemail_call_to_be_linked()
		{
			// The transcription arrives while the record action is still saving the call: the item reads unlinked, then linked.
			var reads = 0;
			_items.Setup(x => x.GetItemAsync(ItemId)).ReturnsAsync(() =>
			{
				reads++;
				_item.CallId = reads >= 3 ? 900 : (int?)null;
				return _item;
			});

			var controller = Controller(("CallSid", CallerSid), ("TranscriptionText", "Smoke on Main"), ("TranscriptionStatus", "completed"));
			controller.TranscriptionLinkCheckInterval = TimeSpan.Zero;
			await controller.VoicemailTranscription(ItemId);

			_calls.Verify(x => x.SaveCallNoteAsync(It.Is<CallNote>(n => n.CallId == 900 && n.Note == "Voicemail transcription (automatic): Smoke on Main"),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task VoicemailTranscription_with_no_call_yet_is_kept_on_the_item()
		{
			_item.CallId = null;

			var controller = Controller(("CallSid", CallerSid), ("TranscriptionText", "Smoke on Main"), ("TranscriptionStatus", "completed"));
			controller.TranscriptionLinkCheckInterval = TimeSpan.Zero;
			await controller.VoicemailTranscription(ItemId);

			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, null, "Voicemail transcription (automatic): Smoke on Main", Times.Once());
			_items.Verify(x => x.GetItemAsync(ItemId), Times.Exactly(1 + DispatcherVoiceController.TranscriptionLinkChecks));
		}

		[Test]
		public async System.Threading.Tasks.Task VoicemailTranscription_adds_a_note_to_the_pending_call()
		{
			_item.CallId = 900;

			await Controller(("CallSid", CallerSid), ("TranscriptionText", " There is smoke at 12 Main St "), ("TranscriptionStatus", "completed"))
				.VoicemailTranscription(ItemId);

			_calls.Verify(x => x.SaveCallNoteAsync(It.Is<CallNote>(n => n.CallId == 900 && n.UserId == "admin-1" &&
				n.Note == "Voicemail transcription (automatic): There is smoke at 12 Main St"), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task VoicemailTranscription_that_failed_adds_nothing()
		{
			_item.CallId = 900;

			await Controller(("CallSid", CallerSid), ("TranscriptionText", "x"), ("TranscriptionStatus", "failed")).VoicemailTranscription(ItemId);

			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Bridge_records_the_connection_for_the_dispatcher()
		{
			var content = Content(await Controller(("CallSid", CallerSid)).Bridge(ItemId, DispatcherId));

			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceConnected, DispatcherId, null, Times.Once());
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.ConnectingCaller));
		}

		#endregion EnqueueAction / Overflow / Voicemail

		#region Dispatcher's leg

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnect_asks_the_dispatcher_to_press_1()
		{
			var content = Content(await Controller(("CallSid", "CA-dispatcher")).DispatcherConnect(ItemId, DispatcherId));

			content.Should().Contain("<Gather");
			content.Should().Contain("numDigits=\"1\"");
			content.Should().Contain($"api/Twilio/DeskConnectConfirm?itemId={ItemId}&amp;userId={DispatcherId}");
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.DispatcherConnect("5 5 5, 1 2 3, 4 5 6 7")));
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnect_tells_the_dispatcher_when_the_caller_hung_up()
		{
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceEnded, UserId = null, OccurredOn = DateTime.UtcNow });

			var content = Content(await Controller(("CallSid", "CA-dispatcher")).DispatcherConnect(ItemId, DispatcherId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.CallerGone));
			content.Should().NotContain("<Gather");
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnect_refuses_an_item_held_by_someone_else()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;
			_item.AssignedUserId = "dispatcher-2";

			var content = Content(await Controller(("CallSid", "CA-dispatcher")).DispatcherConnect(ItemId, DispatcherId));

			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.NotWaitingForYou));
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnectAction_1_dials_the_callers_queue()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "1")).DispatcherConnectAction(ItemId, DispatcherId));

			content.Should().Contain("<Dial");
			content.Should().Contain($">{QueueName}</Queue>");
			content.Should().Contain($"api/Twilio/DeskBridge?itemId={ItemId}&amp;userId={DispatcherId}");
			_routing.Verify(x => x.AcceptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnectAction_1_on_an_offer_takes_the_item_first()
		{
			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "1")).DispatcherConnectAction(ItemId, DispatcherId));

			_routing.Verify(x => x.AcceptAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()), Times.Once);
			content.Should().Contain($">{QueueName}</Queue>");
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnectAction_1_when_the_offer_was_lost_does_not_dial()
		{
			_routing.Setup(x => x.AcceptAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(DispatcherDeskResult<DispatcherQueueItem>.Fail(DispatcherDeskErrors.ItemNotAvailable));

			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "1")).DispatcherConnectAction(ItemId, DispatcherId));

			content.Should().NotContain("<Dial");
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.NotWaitingForYou));
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnectAction_other_key_returns_the_item_to_the_queue()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "")).DispatcherConnectAction(ItemId, DispatcherId));

			_routing.Verify(x => x.ReleaseAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()), Times.Once);
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Dispatcher did not press 1", Times.Once());
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.ReturnedToQueue));
			content.Should().NotContain("<Dial");
		}

		[Test]
		public async System.Threading.Tasks.Task DispatcherConnectAction_other_key_on_an_offer_declines_it()
		{
			await Controller(("CallSid", "CA-dispatcher"), ("Digits", "9")).DispatcherConnectAction(ItemId, DispatcherId);

			_routing.Verify(x => x.DeclineAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task CallBack_asks_the_dispatcher_to_press_1()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var content = Content(await Controller(("CallSid", "CA-dispatcher")).CallBack(ItemId, DispatcherId));

			content.Should().Contain("<Gather");
			content.Should().Contain($"api/Twilio/DeskCallBackConfirm?itemId={ItemId}&amp;userId={DispatcherId}");
			content.Should().Contain(Prompt(DispatcherDeskVoicePrompts.CallBack("5 5 5, 1 2 3, 4 5 6 7")));
		}

		[Test]
		public async System.Threading.Tasks.Task CallBackAction_1_dials_the_caller_from_the_number_they_called()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "1")).CallBackAction(ItemId, DispatcherId));

			content.Should().Contain("callerId=\"+15559990000\"");
			content.Should().Contain("<Number>+15551234567</Number>");
			content.Should().Contain($"api/Twilio/DeskCallBackEnded?itemId={ItemId}&amp;userId={DispatcherId}");
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceConnecting, DispatcherId, null, Times.Once());
		}

		[Test]
		public async System.Threading.Tasks.Task CallBackAction_other_key_cancels_and_keeps_the_item()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var content = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "2")).CallBackAction(ItemId, DispatcherId));

			content.Should().NotContain("<Dial");
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Call back cancelled", Times.Once());
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task CallBackEnded_records_whether_the_caller_answered()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			await Controller(("DialCallStatus", "completed"), ("DialCallDuration", "64")).CallBackEnded(ItemId, DispatcherId);
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceConnected, DispatcherId, "Call back answered (64 seconds)", Times.Once());

			await Controller(("DialCallStatus", "no-answer")).CallBackEnded(ItemId, DispatcherId);
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Call back not answered (no-answer)", Times.Once());
		}

		[Test]
		public async System.Threading.Tasks.Task Status_dispatcher_phone_not_answered_returns_the_item_to_the_queue()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			await Controller(("CallStatus", "no-answer")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);

			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Dispatcher's phone: no-answer", Times.Once());
			_routing.Verify(x => x.ReleaseAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Status_of_an_earlier_ring_does_not_return_the_item_while_a_newer_ring_is_out()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;
			var first = DateTime.UtcNow.AddSeconds(-40);
			var second = DateTime.UtcNow.AddSeconds(-10);
			// Each placed leg records VoiceConnecting just after it starts.
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = DispatcherId, OccurredOn = first.AddSeconds(1) });
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = DispatcherId, OccurredOn = second.AddSeconds(1) });

			await Controller(("CallStatus", "no-answer")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher, first.Ticks);

			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Dispatcher's phone: no-answer (an earlier ring)", Times.Once());
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// The newer ring's own terminal status still returns the item.
			await Controller(("CallStatus", "no-answer")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher, second.Ticks);

			_routing.Verify(x => x.ReleaseAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void Attempt_ticks_out_of_range_are_ignored()
		{
			DispatcherVoiceController.AttemptStartedOn(null).Should().BeNull();
			DispatcherVoiceController.AttemptStartedOn(0).Should().BeNull();
			DispatcherVoiceController.AttemptStartedOn(long.MaxValue).Should().BeNull();
			DispatcherVoiceController.AttemptStartedOn(new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc).Ticks)
				.Should().Be(new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc));
		}

		[Test]
		public async System.Threading.Tasks.Task Status_dispatcher_hung_up_before_connecting_returns_the_item()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = DispatcherId, OccurredOn = DateTime.UtcNow });

			await Controller(("CallStatus", "completed")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);

			_routing.Verify(x => x.ReleaseAsync(DepartmentId, ItemId, DispatcherId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async System.Threading.Tasks.Task Status_completed_after_the_bridge_leaves_the_item_alone()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;
			var now = DateTime.UtcNow;
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnecting, UserId = DispatcherId, OccurredOn = now.AddSeconds(-30) });
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceConnected, UserId = DispatcherId, OccurredOn = now.AddSeconds(-20) });

			await Controller(("CallStatus", "completed")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);

			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Dispatcher's phone call ended", Times.Once());
		}

		[Test]
		public async System.Threading.Tasks.Task Status_call_back_leg_not_answered_keeps_the_item_with_the_dispatcher()
		{
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			await Controller(("CallStatus", "busy")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.CallBack);

			VerifyVoiceEvent(DispatcherQueueEventTypes.VoiceEnded, DispatcherId, "Dispatcher's phone: busy", Times.Once());
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task Status_call_back_leg_sent_as_dispatcher_keeps_the_item_when_the_caller_had_hung_up()
		{
			// DispatcherVoiceService sends leg=dispatcher for both legs: a caller who already hung up means this was a call back.
			_item.State = (int)DispatcherQueueItemStates.Accepted;
			_events.Add(new DispatcherQueueEvent { EventType = (int)DispatcherQueueEventTypes.VoiceEnded, UserId = null, OccurredOn = DateTime.UtcNow.AddMinutes(-5) });

			await Controller(("CallStatus", "no-answer")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);
			await Controller(("CallStatus", "completed")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);

			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_routing.Verify(x => x.DeclineAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async System.Threading.Tasks.Task CallBack_works_for_a_text_conversation()
		{
			_item.Kind = (int)DispatcherQueueItemKinds.Text;
			_item.ExternalReference = null;
			_item.State = (int)DispatcherQueueItemStates.Accepted;

			var gather = Content(await Controller(("CallSid", "CA-dispatcher")).CallBack(ItemId, DispatcherId));
			gather.Should().Contain("<Gather");

			var dial = Content(await Controller(("CallSid", "CA-dispatcher"), ("Digits", "1")).CallBackAction(ItemId, DispatcherId));
			dial.Should().Contain("<Number>+15551234567</Number>");

			await Controller(("CallStatus", "busy")).Status(ItemId, DispatcherId, DispatcherVoiceRoutes.Legs.Dispatcher);
			_routing.Verify(x => x.ReleaseAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Dispatcher's leg

		[Test]
		public void Serves_the_paths_the_voice_and_routing_services_build()
		{
			// DispatcherVoiceService (DeskConnect, DeskCallBack, DeskLegStatus) and DispatcherRoutingService (DeskOverflow) build these
			// URLs themselves; renaming one here breaks every desk phone call.
			DispatcherVoiceRoutes.RoutePrefix.Should().Be("api/Twilio");
			DispatcherVoiceRoutes.DispatcherConnect.Should().Be("DeskConnect");
			DispatcherVoiceRoutes.DispatcherConnectAction.Should().Be("DeskConnectConfirm");
			DispatcherVoiceRoutes.CallBack.Should().Be("DeskCallBack");
			DispatcherVoiceRoutes.CallBackAction.Should().Be("DeskCallBackConfirm");
			DispatcherVoiceRoutes.Status.Should().Be("DeskLegStatus");
			DispatcherVoiceRoutes.Overflow.Should().Be("DeskOverflow");
			DispatcherVoiceRoutes.Legs.Dispatcher.Should().Be("dispatcher");

			var api = global::Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			api.Should().NotBeNull();
			DispatcherVoiceRoutes.OverflowUrl("i1").Should().Be(api.TrimEnd('/') + "/api/Twilio/DeskOverflow?itemId=i1");
			DispatcherVoiceRoutes.StatusUrl("i1", "u1", DispatcherVoiceRoutes.Legs.Dispatcher)
				.Should().EndWith("/api/Twilio/DeskLegStatus?itemId=i1&userId=u1&leg=dispatcher");
		}

		[Test]
		public void Every_webhook_serves_the_path_the_routes_contract_names()
		{
			var paths = typeof(DispatcherVoiceController).GetMethods()
				.SelectMany(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>())
				.Select(a => a.Template).ToList();

			paths.Should().Contain(new[]
			{
				DispatcherVoiceRoutes.Hold, DispatcherVoiceRoutes.EnqueueAction, DispatcherVoiceRoutes.Overflow, DispatcherVoiceRoutes.Voicemail,
				DispatcherVoiceRoutes.VoicemailTranscription, DispatcherVoiceRoutes.DispatcherConnect, DispatcherVoiceRoutes.DispatcherConnectAction,
				DispatcherVoiceRoutes.CallBack, DispatcherVoiceRoutes.CallBackAction, DispatcherVoiceRoutes.Bridge, DispatcherVoiceRoutes.CallBackEnded,
				DispatcherVoiceRoutes.Status
			});

			var route = (RouteAttribute)typeof(DispatcherVoiceController).GetCustomAttributes(typeof(RouteAttribute), false).Single();
			route.Template.Should().Be(DispatcherVoiceRoutes.RoutePrefix);

			// Every webhook checks the Twilio signature.
			typeof(DispatcherVoiceController).GetMethods().Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any())
				.Should().OnlyContain(m => m.GetCustomAttributes(typeof(global::Twilio.AspNet.Core.ValidateRequestAttribute), false).Any());
		}

		[TestCase("+15551234567", "5 5 5, 1 2 3, 4 5 6 7")]
		[TestCase("5551234567", "5 5 5, 1 2 3, 4 5 6 7")]
		[TestCase("+3225551234", "3 2 2, 5 5 5, 1 2 3 4")]
		[TestCase("1234567", "1 2 3, 4 5 6 7")]
		[TestCase("+447700900123", "4 4 7, 7 0 0, 9 0 0, 1 2 3")]
		[TestCase("", "an unknown number")]
		[TestCase(null, "an unknown number")]
		public void SpokenNumber_reads_digits_in_groups(string number, string spoken)
		{
			DispatcherDeskVoicePrompts.SpokenNumber(number).Should().Be(spoken);
		}

		[Test]
		public void CallerFor_prefers_the_callers_name()
		{
			DispatcherDeskVoicePrompts.CallerFor("Pat Smith", "+15551234567").Should().Be("Pat Smith");
			DispatcherDeskVoicePrompts.CallerFor(" ", "+15551234567").Should().Be("5 5 5, 1 2 3, 4 5 6 7");
		}
	}
}
