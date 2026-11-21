using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Intake spam handling (call-intake-and-ai-destinations-plan.md §9, M0326): "dismiss as spam" cancels only a pending intake call, never
	/// texts the sender, and blocks the sender once (renewing a standing block); automatic screenings never overwrite a dispatcher's.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeScreeningServiceTests
	{
		private const int DepartmentId = 55;
		private const int CallId = 900;
		private const string Sender = "+15305550199";

		private sealed class FixedClock : TimeProvider
		{
			public static readonly DateTime Now = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now);
		}

		private static readonly DateTime Claimed = new DateTime(2026, 11, 20, 17, 59, 59, DateTimeKind.Utc);

		private Mock<IIntakeMessagesRepository> _messages;
		private Mock<IIntakeBlockedSendersRepository> _blocked;
		private Mock<ICallsService> _calls;
		private Mock<IIntakeConversationService> _conversations;
		private Mock<IEventAggregator> _events;
		private Call _call;
		private IntakeMessage _message;
		private readonly List<IntakeBlockedSender> _inserted = new List<IntakeBlockedSender>();
		private readonly List<IntakeBlockedSender> _renewed = new List<IntakeBlockedSender>();
		private IntakeScreeningService _service;
		private int _savedDays;

		[SetUp]
		public void SetUp()
		{
			_savedDays = IntakeConfig.SpamBlockDays;
			IntakeConfig.SpamBlockDays = 30;
			_inserted.Clear();
			_renewed.Clear();

			_call = new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Pending, CallSource = (int)CallSources.Intake, Name = "Hospital line: +1 530" };
			_message = new IntakeMessage
			{
				IntakeMessageId = "m-1", DepartmentId = DepartmentId, CallId = CallId, ChannelType = (int)IntakeChannelTypes.Sms, Sender = Sender,
				State = (int)IntakeMessageStates.CallCreated
			};

			_messages = new Mock<IIntakeMessagesRepository>();
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(() => new List<IntakeMessage> { _message });
			_messages.Setup(m => m.SetScreeningAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int[]>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_blocked = new Mock<IIntakeBlockedSendersRepository>();
			_blocked.Setup(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeBlockedSender b, CancellationToken _) => _inserted.Add(b)).ReturnsAsync(true);
			_blocked.Setup(b => b.RenewAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeBlockedSender b, CancellationToken _) => _renewed.Add(b)).ReturnsAsync(true);

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, true)).ReturnsAsync(() => _call);
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => c);
			// The dismissal claims the stored call the way a dispatch does; like the real claim, it marks the row dispatched.
			_calls.Setup(c => c.TryClaimCallForDispatchAsync(CallId, DepartmentId, It.IsAny<CancellationToken>()))
				.Callback(() => _call.HasBeenDispatched = true).ReturnsAsync(Claimed);

			_conversations = new Mock<IIntakeConversationService>();
			_events = new Mock<IEventAggregator>();

			_service = new IntakeScreeningService(_messages.Object, _blocked.Object, _calls.Object, new Lazy<IIntakeConversationService>(() => _conversations.Object),
				_events.Object, new FixedClock());
		}

		[TearDown]
		public void TearDown() => IntakeConfig.SpamBlockDays = _savedDays;

		[Test]
		public async Task dismissing_a_pending_intake_call_cancels_it_blocks_the_sender_and_tells_nobody()
		{
			_conversations.Setup(c => c.GetForCallAsync(DepartmentId, CallId))
				.ReturnsAsync(new IntakeConversation { IntakeConversationId = "conv-1", DepartmentId = DepartmentId, CallId = CallId, State = (int)IntakeConversationStates.Open });

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.Dismissed);
			_call.State.Should().Be((int)CallStates.Cancelled);
			_call.ClosedByUserId.Should().Be("dispatcher-1");
			_call.ClosedOn.Should().Be(FixedClock.Now);
			_inserted.Should().ContainSingle();
			var block = _inserted[0];
			block.Sender.Should().Be(Sender);
			block.DepartmentId.Should().Be(DepartmentId);
			block.CallId.Should().Be(CallId);
			block.Reason.Should().Be(IntakeBlockReasons.Spam);
			block.BlockedByUserId.Should().Be("dispatcher-1");
			block.ExpiresOn.Should().Be(FixedClock.Now.AddDays(30));
			result.Block.Should().BeSameAs(block);
			_messages.Verify(m => m.SetScreeningAsync(DepartmentId, CallId, (int)IntakeScreenings.DismissedAsSpam,
				It.Is<int[]>(r => r.SequenceEqual(new[] { 0, 1, 2 })), "dispatcher-1", FixedClock.Now, It.IsAny<CancellationToken>()), Times.Once);
			_conversations.Verify(c => c.CloseAsync(DepartmentId, "conv-1", "dispatcher-1", "DismissedAsSpam", false, It.IsAny<CancellationToken>()), Times.Once);
			_events.Verify(e => e.SendMessage(It.Is<CallClosedEvent>(x => x.DepartmentId == DepartmentId && x.Call.CallId == CallId)), Times.Once);
			// Claimed like a dispatch, saved as never sent, and the claim ended.
			_call.HasBeenDispatched.Should().BeNull("a dismissed call was never sent; the claim's mark is not kept");
			_calls.Verify(c => c.CompleteCallDispatchClaimAsync(CallId, DepartmentId, Claimed, It.IsAny<CancellationToken>()), Times.Once);
			_calls.Verify(c => c.ReleaseCallDispatchClaimAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime?>(),
				It.IsAny<bool?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_call_sent_while_it_is_being_dismissed_is_not_pending_and_nobody_is_blocked()
		{
			// A dispatcher's "Dispatch now" (or the scheduled-calls worker) claimed the stored row after this request read it as pending.
			_calls.Setup(c => c.TryClaimCallForDispatchAsync(CallId, DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync((DateTime?)null);

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.NotPending);
			result.Block.Should().BeNull();
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never, "a stale save would overwrite the dispatch");
			_blocked.Verify(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()), Times.Never,
				"a call a dispatcher decided to send is not spam");
			_messages.Verify(m => m.SetScreeningAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int[]>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_block_that_is_not_in_force_fails_the_dismissal_and_gives_the_call_back()
		{
			// Every read found the row lifted again by the time of the write (a racing unblock), so no block stands.
			_blocked.Setup(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync((IntakeBlockedSender)null);
			_blocked.Setup(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.Failed);
			result.Block.Should().BeNull();
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_calls.Verify(c => c.ReleaseCallDispatchClaimAsync(CallId, DepartmentId, Claimed, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
			_messages.Verify(m => m.SetScreeningAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int[]>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_expired_standing_block_is_not_reported_as_a_block_in_force()
		{
			// Every read is a fresh row (as the database returns), still expired because every renewal lost.
			_blocked.Setup(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync(() =>
				new IntakeBlockedSender { IntakeBlockedSenderId = "b-1", DepartmentId = DepartmentId, Sender = Sender, ExpiresOn = FixedClock.Now.AddDays(-1) });
			_blocked.Setup(b => b.RenewAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.Failed);
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_label_that_cannot_be_written_leaves_the_call_pending()
		{
			_messages.Setup(m => m.SetScreeningAsync(DepartmentId, CallId, (int)IntakeScreenings.DismissedAsSpam, It.IsAny<int[]>(), It.IsAny<string>(), It.IsAny<DateTime>(),
				It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: false);

			result.Result.Should().Be(IntakeDismissResults.Failed, "a dismissal is never reported without its label");
			_call.State.Should().Be((int)CallStates.Pending);
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_calls.Verify(c => c.ReleaseCallDispatchClaimAsync(CallId, DepartmentId, Claimed, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_cancel_that_cannot_be_saved_takes_the_label_back_off()
		{
			_message.Screening = (int)IntakeScreenings.LikelyNotIncident;
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: false);

			result.Result.Should().Be(IntakeDismissResults.Failed);
			_calls.Verify(c => c.ReleaseCallDispatchClaimAsync(CallId, DepartmentId, Claimed, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once);
			_messages.Verify(m => m.SetScreeningAsync(DepartmentId, CallId, (int)IntakeScreenings.LikelyNotIncident,
				It.Is<int[]>(r => r.SequenceEqual(new[] { (int)IntakeScreenings.DismissedAsSpam })), null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once,
				"the call is still pending, so it goes back to what it said before");
			_events.Verify(e => e.SendMessage(It.IsAny<CallClosedEvent>()), Times.Never);
		}

		[Test]
		public async Task dismissing_without_blocking_leaves_the_sender_alone()
		{
			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: false);

			result.Result.Should().Be(IntakeDismissResults.Dismissed);
			result.Block.Should().BeNull();
			_blocked.Verify(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase((int)CallStates.Active)]
		[TestCase((int)CallStates.Closed)]
		[TestCase((int)CallStates.Cancelled)]
		public async Task a_call_that_is_no_longer_pending_is_not_dismissed(int state)
		{
			_call.State = state;

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.NotPending);
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_blocked.Verify(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_call_of_another_department_or_not_from_intake_is_not_found()
		{
			_call.CallSource = (int)CallSources.User;
			(await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true)).Result.Should().Be(IntakeDismissResults.NotFound);

			_call.CallSource = (int)CallSources.Intake;
			_call.DepartmentId = 77;
			(await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true)).Result.Should().Be(IntakeDismissResults.NotFound);

			_call.DepartmentId = DepartmentId;
			_message = null;
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<IntakeMessage>());
			(await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true)).Result.Should().Be(IntakeDismissResults.NotFound, "its intake record was purged");

			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_withheld_caller_is_dismissed_but_never_blocked()
		{
			_message.Sender = "+266696687";
			_message.ChannelType = (int)IntakeChannelTypes.Voice;

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", blockSender: true);

			result.Result.Should().Be(IntakeDismissResults.Dismissed);
			result.SenderNotBlockable.Should().BeTrue();
			result.Block.Should().BeNull();
			_blocked.Verify(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_second_dismissal_renews_the_standing_block_and_never_shortens_it()
		{
			var standing = new IntakeBlockedSender
			{
				IntakeBlockedSenderId = "b-1", DepartmentId = DepartmentId, Sender = Sender, BlockedOn = FixedClock.Now.AddDays(-5),
				ExpiresOn = FixedClock.Now.AddDays(90)
			};
			_blocked.Setup(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync(standing);

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-2", blockSender: true);

			_inserted.Should().BeEmpty();
			_renewed.Should().ContainSingle().Which.ExpiresOn.Should().Be(FixedClock.Now.AddDays(90), "the later end wins");
			_renewed[0].BlockedByUserId.Should().Be("dispatcher-2");
			_renewed[0].CallId.Should().Be(CallId);
			result.Block.IntakeBlockedSenderId.Should().Be("b-1");
		}

		[Test]
		public async Task renewing_an_expired_block_starts_a_new_period_and_a_permanent_one_stays_permanent()
		{
			var expired = new IntakeBlockedSender { IntakeBlockedSenderId = "b-1", DepartmentId = DepartmentId, Sender = Sender, ExpiresOn = FixedClock.Now.AddDays(-1) };
			_blocked.Setup(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync(expired);
			await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true);
			_renewed.Last().ExpiresOn.Should().Be(FixedClock.Now.AddDays(30));

			_call.State = (int)CallStates.Pending;
			var permanent = new IntakeBlockedSender { IntakeBlockedSenderId = "b-2", DepartmentId = DepartmentId, Sender = Sender, ExpiresOn = null };
			_blocked.Setup(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync(permanent);
			await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true);
			_renewed.Last().ExpiresOn.Should().BeNull();
		}

		[Test]
		public async Task with_spam_block_days_zero_a_block_lasts_until_lifted()
		{
			IntakeConfig.SpamBlockDays = 0;

			await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true);

			_inserted.Should().ContainSingle().Which.ExpiresOn.Should().BeNull();
		}

		[Test]
		public async Task losing_the_insert_race_renews_the_winners_block()
		{
			var winner = new IntakeBlockedSender { IntakeBlockedSenderId = "b-9", DepartmentId = DepartmentId, Sender = Sender, ExpiresOn = FixedClock.Now.AddDays(30) };
			// The last read is the renewal's read-back of the stored row.
			_blocked.SetupSequence(b => b.GetCurrentAsync(DepartmentId, Sender)).ReturnsAsync((IntakeBlockedSender)null).ReturnsAsync(winner).ReturnsAsync(winner);
			_blocked.Setup(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true);

			result.Block.IntakeBlockedSenderId.Should().Be("b-9");
			_renewed.Should().ContainSingle();
		}

		[Test]
		public async Task a_block_that_fails_leaves_the_call_untouched()
		{
			_blocked.Setup(b => b.TryInsertAsync(It.IsAny<IntakeBlockedSender>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

			var result = await _service.DismissAsSpamAsync(DepartmentId, CallId, "dispatcher-1", true);

			result.Result.Should().Be(IntakeDismissResults.Failed);
			_call.State.Should().Be((int)CallStates.Pending);
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
			_calls.Verify(c => c.ReleaseCallDispatchClaimAsync(CallId, DepartmentId, Claimed, (int)CallStates.Pending, null, null, It.IsAny<CancellationToken>()), Times.Once,
				"the call is given back to waiting for the dispatcher to try again");
		}

		[Test]
		public async Task the_ai_label_replaces_only_none_and_clearing_removes_only_its_own_label()
		{
			await _service.SetAiScreeningAsync(DepartmentId, CallId, likelyNotIncident: true);
			await _service.SetAiScreeningAsync(DepartmentId, CallId, likelyNotIncident: false);

			_messages.Verify(m => m.SetScreeningAsync(DepartmentId, CallId, (int)IntakeScreenings.LikelyNotIncident, It.Is<int[]>(r => r.SequenceEqual(new[] { 0, 1 })),
				null, FixedClock.Now, It.IsAny<CancellationToken>()), Times.Once);
			_messages.Verify(m => m.SetScreeningAsync(DepartmentId, CallId, (int)IntakeScreenings.None, It.Is<int[]>(r => r.SequenceEqual(new[] { 1 })),
				null, FixedClock.Now, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task screenings_leave_out_unscreened_calls_and_other_departments()
		{
			_messages.Setup(m => m.GetByCallIdsAsync(DepartmentId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<IntakeMessage>
			{
				new IntakeMessage { DepartmentId = DepartmentId, CallId = 1, Screening = (int)IntakeScreenings.RepeatSender },
				new IntakeMessage { DepartmentId = DepartmentId, CallId = 2, Screening = 0 },
				new IntakeMessage { DepartmentId = DepartmentId, CallId = 3 },
				new IntakeMessage { DepartmentId = 77, CallId = 4, Screening = (int)IntakeScreenings.LikelyNotIncident }
			});

			var screenings = await _service.GetScreeningsAsync(DepartmentId, new[] { 1, 2, 3, 4 });

			screenings.Should().BeEquivalentTo(new Dictionary<int, IntakeScreenings> { [1] = IntakeScreenings.RepeatSender });
		}

		[Test]
		public async Task unblocking_lifts_the_block_in_the_department_only()
		{
			_blocked.Setup(b => b.RemoveAsync(DepartmentId, "b-1", "admin-1", FixedClock.Now, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await _service.UnblockAsync(DepartmentId, " b-1 ", "admin-1")).Should().BeTrue();
			(await _service.UnblockAsync(DepartmentId, "", "admin-1")).Should().BeFalse();
		}

		[TestCase("+1 (530) 555-0199", "+15305550199")]
		[TestCase("15305550199", "+15305550199")]
		[TestCase(" Dispatch@Mercy.ORG ", "dispatch@mercy.org")]
		[TestCase("U012ABC", "U012ABC")]
		[TestCase("+266696687", null)]
		[TestCase("+7378742833", null)]
		[TestCase("   ", null)]
		public void senders_normalize_the_same_way_for_the_block_and_the_check(string sender, string expected)
		{
			IntakeBlockedSenders.Normalize(sender).Should().Be(expected);
		}
	}
}
