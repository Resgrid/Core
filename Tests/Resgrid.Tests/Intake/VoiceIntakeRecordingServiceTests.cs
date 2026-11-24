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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Voice intake call recording (M0332): started once after the caller accepts, attached to the pending call as an MP3 file when the
	/// status callback arrives, retried / discarded / cleaned up by worker 80, the carrier copy deleted once Resgrid holds the file.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class VoiceIntakeRecordingServiceTests
	{
		private const int DepartmentId = 55;
		private const string CallSid = "CA00000000000000000000000000000001";
		private const string RecordingSid = "RE00000000000000000000000000000001";
		private static readonly DateTime Now = new DateTime(2026, 11, 23, 14, 0, 0, DateTimeKind.Utc);

		private FakeSessions _sessions;
		private Mock<IVoiceRecordingProvider> _provider;
		private Mock<ICallsService> _calls;
		private Mock<ICallAttachmentRepository> _attachments;
		private FixedClock _clock;
		private DepartmentPhoneNumber _number;
		private Call _call;
		private List<CallAttachment> _saved;
		private int _saves;
		private VoiceIntakeRecordingService _service;

		private bool _savedHostSwitch;
		private bool _savedDeleteCopy;
		private int _savedMaxAttempts;
		private int _savedMaxBytes;

		[SetUp]
		public void SetUp()
		{
			_savedHostSwitch = IntakeConfig.VoiceRecordingEnabled;
			_savedDeleteCopy = IntakeConfig.VoiceRecordingDeleteCarrierCopy;
			_savedMaxAttempts = IntakeConfig.VoiceRecordingMaxAttempts;
			_savedMaxBytes = IntakeConfig.VoiceRecordingMaxBytes;
			IntakeConfig.VoiceRecordingEnabled = true;
			IntakeConfig.VoiceRecordingDeleteCarrierCopy = true;
			IntakeConfig.VoiceRecordingMaxAttempts = 3;
			IntakeConfig.VoiceRecordingMaxBytes = 1024;

			_sessions = new FakeSessions();
			_provider = new Mock<IVoiceRecordingProvider>();
			_provider.Setup(p => p.StartCallRecordingAsync(CallSid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(RecordingSid);
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Downloaded, Data = new byte[] { 1, 2, 3, 4 }, Extension = ".mp3" });
			_provider.Setup(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			_call = new Call { CallId = 900, DepartmentId = DepartmentId, ReportingUserId = "managing-user" };
			_saved = new List<CallAttachment>();
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(900, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_saves = 0;
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					// SaveOrUpdate: an id updates that row, none inserts one.
					_saves++;
					if (a.CallAttachmentId > 0)
					{
						_saved[_saved.FindIndex(s => s.CallAttachmentId == a.CallAttachmentId)] = a;
						return a;
					}

					a.CallAttachmentId = 7000 + _saved.Count;
					_saved.Add(a);
					return a;
				});
			_attachments = new Mock<ICallAttachmentRepository>();
			_attachments.Setup(a => a.GetCallDispatchesByCallIdAsync(900)).ReturnsAsync(() => _saved.ToList());
			_attachments.Setup(a => a.DeleteAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) => _saved.RemoveAll(s => s.CallAttachmentId == a.CallAttachmentId) > 0);

			_clock = new FixedClock(Now);
			_number = new DepartmentPhoneNumber { DepartmentId = DepartmentId, PhoneNumber = "+17755550100", Status = (int)DepartmentPhoneNumberStatuses.Active, VoiceIntakeEnabled = true, VoiceCapable = true, VoiceRecordingEnabled = true };
			_service = new VoiceIntakeRecordingService(_sessions, _provider.Object, _calls.Object, _attachments.Object, _clock);
		}

		[TearDown]
		public void TearDown()
		{
			IntakeConfig.VoiceRecordingEnabled = _savedHostSwitch;
			IntakeConfig.VoiceRecordingDeleteCarrierCopy = _savedDeleteCopy;
			IntakeConfig.VoiceRecordingMaxAttempts = _savedMaxAttempts;
			IntakeConfig.VoiceRecordingMaxBytes = _savedMaxBytes;
		}

		private VoiceIntakeSession Session(VoiceIntakeSteps step = VoiceIntakeSteps.Nature, int? callId = null, DateTime? startedOn = null)
		{
			var session = new VoiceIntakeSession
			{
				VoiceIntakeSessionId = Guid.NewGuid().ToString("D"),
				DepartmentId = DepartmentId,
				ProviderCallId = CallSid,
				Step = (int)step,
				CallId = callId,
				StartedOn = startedOn ?? Now.AddMinutes(-3)
			};
			_sessions.Rows.Add(session);
			return session;
		}

		private static VoiceRecordingStatus Completed(int? seconds = 161) =>
			new VoiceRecordingStatus { CallSid = CallSid, RecordingSid = RecordingSid, Status = "completed", DurationSeconds = seconds };

		// ---- start ----

		[Test]
		public async Task start_records_the_call_once_with_the_status_callback_and_keeps_the_sid()
		{
			var session = Session();

			(await _service.StartAsync(_number, CallSid)).Should().BeTrue();
			(await _service.StartAsync(_number, CallSid)).Should().BeFalse("a repeated gate webhook never starts a second recording");

			// The callback URL carries Twilio's 5xx retry override: a callback this side could not take in is sent again.
			_provider.Verify(p => p.StartCallRecordingAsync(CallSid, It.Is<string>(u => u.EndsWith("/api/Twilio/VoiceIntakeRecording#rp=5xx,ct,rt")), It.IsAny<CancellationToken>()), Times.Once);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Requested);
			session.RecordingSid.Should().Be(RecordingSid);
		}

		[TestCase(false, true)]
		[TestCase(true, false)]
		public async Task start_does_nothing_when_the_number_or_the_host_does_not_record(bool numberSwitch, bool hostSwitch)
		{
			var session = Session();
			_number.VoiceRecordingEnabled = numberSwitch;
			IntakeConfig.VoiceRecordingEnabled = hostSwitch;

			(await _service.StartAsync(_number, CallSid)).Should().BeFalse();

			_provider.Verify(p => p.StartCallRecordingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.None);
		}

		[Test]
		public async Task start_needs_a_session_of_the_numbers_department()
		{
			(await _service.StartAsync(_number, CallSid)).Should().BeFalse("no session");

			var other = Session();
			other.DepartmentId = 99;
			(await _service.StartAsync(_number, CallSid)).Should().BeFalse("another department's session");
			_provider.Verify(p => p.StartCallRecordingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_carrier_that_refuses_the_start_leaves_the_session_discarded()
		{
			var session = Session();
			_provider.Setup(p => p.StartCallRecordingAsync(CallSid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string)null);

			(await _service.StartAsync(_number, CallSid)).Should().BeFalse();

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
		}

		[Test]
		public async Task a_start_that_throws_fails_open()
		{
			Session();
			_provider.Setup(p => p.StartCallRecordingAsync(CallSid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("twilio"));

			(await _service.StartAsync(_number, CallSid)).Should().BeFalse();
		}

		[Test]
		public async Task a_slow_start_stays_requested_for_the_status_callback_to_bind()
		{
			var session = Session();
			_provider.Setup(p => p.StartCallRecordingAsync(CallSid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

			(await _service.StartAsync(_number, CallSid)).Should().BeTrue();

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Requested);
			session.RecordingSid.Should().BeNull();
		}

		// ---- status callback ----

		[Test]
		public async Task a_completed_recording_becomes_an_mp3_file_on_the_pending_call_and_twilios_copy_is_deleted()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			session.StartedOn = new DateTime(2026, 11, 23, 13, 57, 5, DateTimeKind.Utc);

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Attached);

			var file = _saved.Should().ContainSingle().Subject;
			file.CallId.Should().Be(900);
			file.CallAttachmentType.Should().Be((int)CallAttachmentTypes.File, "a file on the call, never DispatchAudio (that one is played to responders)");
			file.FileName.Should().Be("caller-recording-20261123-135705Z.mp3");
			file.Name.Should().Be("Caller recording (2:41)");
			file.Data.Should().Equal(1, 2, 3, 4);
			file.Size.Should().Be(4);
			file.UserId.Should().Be("managing-user");
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			session.RecordingCallAttachmentId.Should().Be(7000);
			session.RecordingDurationSeconds.Should().Be(161);
			session.RecordingClaimedOn.Should().BeNull();
			session.RecordingCarrierDeletedOn.Should().Be(Now);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_repeated_callback_attaches_nothing_twice()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			await _service.HandleStatusAsync(Completed());
			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Duplicate);

			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task with_the_carrier_copy_kept_twilio_is_not_asked_to_delete()
		{
			IntakeConfig.VoiceRecordingDeleteCarrierCopy = false;
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Attached);

			_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			session.RecordingCarrierDeletedOn.Should().BeNull();
		}

		[TestCase("absent")]
		[TestCase("failed")]
		public async Task an_empty_or_failed_recording_keeps_nothing(string status)
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.HandleStatusAsync(new VoiceRecordingStatus { CallSid = CallSid, RecordingSid = RecordingSid, Status = status })).Should().Be(VoiceRecordingOutcomes.Discarded);

			_saved.Should().BeEmpty();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_caller_who_hung_up_before_the_pending_call_existed_keeps_no_recording()
		{
			var session = Session(VoiceIntakeSteps.Abandoned, callId: null);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Discarded);

			_saved.Should().BeEmpty();
			_provider.Verify(p => p.DownloadRecordingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_recording_that_arrives_while_the_pending_call_is_being_created_waits_for_it()
		{
			// The caller hung up during the last answer: the session is still on the question while intake creates the call.
			var session = Session(VoiceIntakeSteps.Callback, callId: null);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed);

			// Intake finished; worker 80's next pass attaches it.
			session.Step = (int)VoiceIntakeSteps.Completed;
			session.CallId = 900;
			_clock.Now = Now.AddMinutes(15);
			(await _service.ProcessPendingAsync(_clock.Now)).Should().Be(1);

			_saved.Should().ContainSingle();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
		}

		[Test]
		public async Task a_callback_for_a_call_with_no_session_deletes_the_orphan_recording()
		{
			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Unknown);

			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task a_callback_for_a_session_that_never_asked_for_a_recording_attaches_nothing()
		{
			Session(VoiceIntakeSteps.Completed, callId: 900);

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Unknown);

			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task another_accounts_recording_is_never_touched()
		{
			var saved = NumberProviderConfig.TwilioAccountSid;
			try
			{
				NumberProviderConfig.TwilioAccountSid = "AC00000000000000000000000000000001";
				var session = Session(VoiceIntakeSteps.Completed, callId: 900);
				session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

				var status = Completed();
				status.AccountSid = "AC00000000000000000000000000000002";
				(await _service.HandleStatusAsync(status)).Should().Be(VoiceRecordingOutcomes.Unknown);

				_saved.Should().BeEmpty();
				_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
				session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Requested);
			}
			finally
			{
				NumberProviderConfig.TwilioAccountSid = saved;
			}
		}

		[Test]
		public async Task a_deleted_call_keeps_no_recording()
		{
			_call.IsDeleted = true;
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Discarded);

			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task a_recording_too_large_for_the_call_is_failed_and_kept_at_twilio_until_the_purge()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, 1024, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.TooLarge });

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Failed);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Failed);
			_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// Worker 80, once the session is about to be purged: the carrier copy goes with it.
			_clock.Now = Now.AddHours(IntakeConfig.VoiceSessionRetentionHours + 1);
			await _service.ProcessPendingAsync(_clock.Now);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
			session.RecordingCarrierDeletedOn.Should().NotBeNull();
		}

		// ---- worker 80 ----

		[Test]
		public async Task an_unavailable_download_is_retried_by_worker_80_then_failed_after_the_attempt_limit()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable });

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed);
			session.RecordingClaimedOn.Should().BeNull("the lease is released for the next attempt");

			await _service.ProcessPendingAsync(Now.AddMinutes(15));
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed);
			await _service.ProcessPendingAsync(Now.AddMinutes(30));

			session.RecordingAttempts.Should().Be(3);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Failed);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task a_retry_after_a_save_that_was_not_recorded_finds_the_file_instead_of_adding_a_second()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_sessions.FailNextAttachedWrite = true;

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);
			_saved.Should().ContainSingle("the file was saved, then recording it failed");

			(await _service.ProcessPendingAsync(Now.AddMinutes(15))).Should().Be(1);

			_saved.Should().ContainSingle();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			session.RecordingCallAttachmentId.Should().Be(7000);
		}

		[Test]
		public async Task a_held_lease_keeps_a_second_attempt_out()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			session.RecordingClaimedOn = Now.AddMinutes(-1);

			(await _service.ProcessPendingAsync(Now)).Should().Be(0);
			_saved.Should().BeEmpty();

			// A lease older than LeaseMinutes belonged to an attempt that died: taken over.
			(await _service.ProcessPendingAsync(Now.AddMinutes(VoiceIntakeRecordingService.LeaseMinutes + 1))).Should().Be(1);
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task a_file_saved_before_its_protected_write_failed_is_saved_again_by_its_id_before_the_recording_counts_as_attached()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			// CallsService.SaveCallAttachmentAsync inserts the file, then the ADP step throws: the row is there, still unprotected.
			var first = true;
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					_saves++;
					if (a.CallAttachmentId > 0)
					{
						_saved[_saved.FindIndex(s => s.CallAttachmentId == a.CallAttachmentId)] = a;
						return a;
					}

					a.CallAttachmentId = 7000 + _saved.Count;
					_saved.Add(a);
					if (first)
					{
						first = false;
						throw new InvalidOperationException("Protected write blocked (broker unavailable)");
					}
					return a;
				});

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed);
			_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "Twilio's copy stays until the file is safe");

			(await _service.ProcessPendingAsync(Now.AddMinutes(15))).Should().Be(1);

			_saves.Should().Be(2, "the recovered file goes through the save (and its protected-write step) again");
			_saved.Should().ContainSingle().Which.CallAttachmentId.Should().Be(7000);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			session.RecordingCallAttachmentId.Should().Be(7000);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task the_file_is_saved_with_the_second_the_call_began_which_identifies_it_where_adp_seals_its_name()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			session.StartedOn = new DateTime(2026, 11, 23, 13, 57, 5, 640, DateTimeKind.Utc);
			// Another file on the call, the same size and added after the call began, its name sealed: not this recording.
			_saved.Add(new CallAttachment { CallAttachmentId = 6000, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = "rgdp1:sealed-photo",
				Size = 4, Timestamp = session.StartedOn.AddMinutes(2) });

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Attached);

			_saved.Should().HaveCount(2, "a same-size file added later is not taken for the recording");
			var file = _saved.Single(a => a.CallAttachmentId != 6000);
			file.Timestamp.Should().Be(new DateTime(2026, 11, 23, 13, 57, 5, DateTimeKind.Utc));
			session.RecordingCallAttachmentId.Should().Be(file.CallAttachmentId);
		}

		[Test]
		public async Task a_retry_finds_its_sealed_file_by_size_and_stamp()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			session.RecordingAttempts = 1;
			_saved.Add(new CallAttachment { CallAttachmentId = 6000, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = "rgdp1:sealed-name",
				Size = 4, Timestamp = VoiceIntakeRecordingService.RecordingStamp(session) });

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			_saved.Should().ContainSingle().Which.CallAttachmentId.Should().Be(6000);
			session.RecordingCallAttachmentId.Should().Be(6000);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
		}

		[Test]
		public async Task an_attempt_that_lost_its_lease_while_downloading_saves_nothing_and_changes_nothing()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			var takenOver = Now.AddMinutes(VoiceIntakeRecordingService.LeaseMinutes + 1);
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() =>
				{
					// The download stalled past the lease; worker 80 took the recording over meanwhile.
					session.RecordingClaimedOn = takenOver;
					return new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Downloaded, Data = new byte[] { 1, 2, 3, 4 }, Extension = ".mp3" };
				});

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);

			_saved.Should().BeEmpty();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed);
			session.RecordingClaimedOn.Should().Be(takenOver, "the other attempt's lease is left alone");
			_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task an_attempt_that_lost_its_lease_during_the_save_neither_marks_it_attached_nor_deletes_twilios_copy()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			var takenOver = Now.AddMinutes(VoiceIntakeRecordingService.LeaseMinutes + 1);
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					a.CallAttachmentId = 7000 + _saved.Count;
					_saved.Add(a);
					session.RecordingClaimedOn = takenOver;
					return a;
				});

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed, "the attempt holding the lease records the outcome");
			session.RecordingClaimedOn.Should().Be(takenOver);
			_provider.Verify(p => p.DeleteRecordingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// The takeover finds the file instead of adding a second one.
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					if (a.CallAttachmentId > 0)
						_saved[_saved.FindIndex(s => s.CallAttachmentId == a.CallAttachmentId)] = a;
					else
					{
						a.CallAttachmentId = 7000 + _saved.Count;
						_saved.Add(a);
					}
					return a;
				});
			_clock.Now = takenOver.AddMinutes(VoiceIntakeRecordingService.LeaseMinutes + 1);
			(await _service.ProcessPendingAsync(_clock.Now)).Should().Be(1);

			_saved.Should().ContainSingle();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
		}

		[Test]
		public async Task a_timeout_discard_never_overwrites_a_recording_a_callback_completed_meanwhile()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_sessions.OnWorkRead = () =>
			{
				// The status callback lands after worker 80 read the row as Requested with no sid.
				_sessions.OnWorkRead = null;
				session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
				session.RecordingSid = RecordingSid;
				session.RecordingDurationSeconds = 20;
			};

			(await _service.ProcessPendingAsync(Now)).Should().Be(0);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed, "the callback's recording stays to be attached");

			(await _service.ProcessPendingAsync(Now.AddMinutes(1))).Should().Be(1);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task a_first_sid_that_arrives_after_the_session_gave_the_recording_up_is_stored_and_twilios_copy_deleted()
		{
			var session = Session(VoiceIntakeSteps.Location, callId: null, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			(await _service.ProcessPendingAsync(Now)).Should().Be(1);
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
			_provider.SetupSequence(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>())).ReturnsAsync(false).ReturnsAsync(true);

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Discarded);

			session.RecordingSid.Should().Be(RecordingSid, "the sid is kept so the copy can still be deleted");
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
			_saved.Should().BeEmpty();
			session.RecordingCarrierDeletedOn.Should().BeNull("the first delete failed");

			// Worker 80 finishes the delete.
			(await _service.ProcessPendingAsync(Now.AddMinutes(15))).Should().Be(1);
			session.RecordingCarrierDeletedOn.Should().NotBeNull();
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task a_lost_status_callback_is_recovered_by_fetching_the_recording_by_sid()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			session.RecordingSid = RecordingSid;

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			_saved.Should().ContainSingle();
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
		}

		[Test]
		public async Task a_requested_recording_is_left_alone_while_the_call_may_still_be_going()
		{
			var session = Session(VoiceIntakeSteps.Location, callId: null, startedOn: Now.AddMinutes(-5));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			session.RecordingSid = RecordingSid;

			(await _service.ProcessPendingAsync(Now)).Should().Be(0);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Requested);
			_provider.Verify(p => p.DownloadRecordingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task a_start_never_confirmed_and_never_called_back_is_discarded_after_the_wait()
		{
			var session = Session(VoiceIntakeSteps.Location, callId: null, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
		}

		[Test]
		public async Task a_recording_with_no_call_after_the_wait_is_discarded()
		{
			var session = Session(VoiceIntakeSteps.Location, callId: null, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task a_carrier_copy_whose_delete_failed_is_deleted_by_worker_80()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_provider.SetupSequence(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>())).ReturnsAsync(false).ReturnsAsync(true);

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Attached);
			session.RecordingCarrierDeletedOn.Should().BeNull();

			(await _service.ProcessPendingAsync(Now.AddMinutes(15))).Should().Be(1);
			session.RecordingCarrierDeletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task a_recording_twilio_no_longer_has_is_discarded()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.NotFound });

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Discarded);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Discarded);
			session.RecordingCarrierDeletedOn.Should().NotBeNull("nothing is left at the carrier");
		}

		[Test]
		public async Task a_recording_whose_call_intake_saved_after_worker_80_read_it_is_attached_not_discarded()
		{
			var session = Session(VoiceIntakeSteps.Location, callId: null, startedOn: Now.AddMinutes(-31));
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			// Intake saves the pending call after worker 80 read the session without one.
			_sessions.OnWorkRead = () => { _sessions.OnWorkRead = null; session.CallId = 900; };

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task a_callback_with_no_session_whose_carrier_delete_fails_asks_twilio_to_send_it_again()
		{
			// Nothing else remembers this sid: only Twilio's retry brings the delete back.
			_provider.Setup(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.RetryLater);

			IntakeConfig.VoiceRecordingDeleteCarrierCopy = false;
			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Unknown, "a carrier copy kept by the host's setting needs no retry");
		}

		[Test]
		public async Task a_file_saved_after_the_lease_was_lost_is_removed_once_the_takeover_attached_its_own()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					a.CallAttachmentId = 7000;
					_saved.Add(a);
					// The save outlasted the lease: the takeover attempt found nothing, saved its own copy and attached it meanwhile.
					_saved.Add(new CallAttachment { CallAttachmentId = 7100, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = a.FileName, Size = a.Size, Timestamp = a.Timestamp });
					session.RecordingClaimedOn = null;
					session.RecordingState = (int)VoiceIntakeRecordingStates.Attached;
					session.RecordingCallAttachmentId = 7100;
					return a;
				});

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Waiting);

			_saved.Select(a => a.CallAttachmentId).Should().Equal(new[] { 7100 }, "the stale attempt's copy goes, the linked one stays");
			session.RecordingCallAttachmentId.Should().Be(7100);
		}

		[Test]
		public async Task a_stale_copy_that_landed_during_the_save_is_swept_once_the_recording_is_attached()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) =>
				{
					a.CallAttachmentId = 7000;
					_saved.Add(a);
					// An attempt that lost its lease earlier lands its save now.
					_saved.Add(new CallAttachment { CallAttachmentId = 7500, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = a.FileName, Size = a.Size, Timestamp = a.Timestamp });
					return a;
				});

			(await _service.HandleStatusAsync(Completed())).Should().Be(VoiceRecordingOutcomes.Attached);

			_saved.Select(a => a.CallAttachmentId).Should().Equal(new[] { 7000 });
			session.RecordingCallAttachmentId.Should().Be(7000);
		}

		[Test]
		public async Task on_the_last_attempt_a_file_an_earlier_attempt_saved_is_completed_and_becomes_the_recording()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			session.RecordingAttempts = 2;
			// An earlier attempt's insert went through but its protected write did not; the carrier now keeps failing the download.
			_saved.Add(new CallAttachment { CallAttachmentId = 7000, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = VoiceIntakeRecording.FileName(session),
				Size = 4, Timestamp = VoiceIntakeRecordingService.RecordingStamp(session), Data = new byte[] { 1, 2, 3, 4 } });
			_provider.Setup(p => p.DownloadRecordingAsync(RecordingSid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new VoiceRecordingDownload { Status = VoiceRecordingDownloadStatuses.Unavailable });

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Attached);
			session.RecordingCallAttachmentId.Should().Be(7000);
			_saves.Should().Be(1, "the file is saved once more so its protected write completes");
			_provider.Verify(p => p.DeleteRecordingAsync(RecordingSid, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task on_the_last_attempt_a_file_whose_protected_write_still_fails_is_removed()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			session.RecordingAttempts = 2;
			_saved.Add(new CallAttachment { CallAttachmentId = 7000, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = VoiceIntakeRecording.FileName(session),
				Size = 4, Timestamp = VoiceIntakeRecordingService.RecordingStamp(session), Data = new byte[] { 1, 2, 3, 4 } });
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Protected write blocked; call attachment 7000 has transient plaintext pending re-encryption."));

			(await _service.ProcessPendingAsync(Now)).Should().Be(1);

			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Failed);
			_saved.Should().BeEmpty("no transient plaintext stays on the call");
		}

		[Test]
		public async Task on_the_last_attempt_a_file_is_not_removed_once_another_attempt_took_the_lease_during_its_save()
		{
			var session = Session(VoiceIntakeSteps.Completed, callId: 900);
			session.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
			session.RecordingSid = RecordingSid;
			session.RecordingAttempts = 2;
			_saved.Add(new CallAttachment { CallAttachmentId = 7000, CallId = 900, CallAttachmentType = (int)CallAttachmentTypes.File, FileName = VoiceIntakeRecording.FileName(session),
				Size = 4, Timestamp = VoiceIntakeRecordingService.RecordingStamp(session), Data = new byte[] { 1, 2, 3, 4 } });
			var saves = 0;
			_calls.Setup(c => c.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.Returns((CallAttachment a, CancellationToken ct) =>
				{
					// The orphan's own save outlasts the lease: another attempt claims the recording (and may link this very file).
					if (++saves == 2)
						session.RecordingClaimedOn = Now.AddMinutes(10);
					throw new InvalidOperationException("Protected write blocked; call attachment 7000 has transient plaintext pending re-encryption.");
				});

			await _service.ProcessPendingAsync(Now);

			_saved.Should().ContainSingle(a => a.CallAttachmentId == 7000, "the attempt that lost the lease leaves the file to the one holding it");
			session.RecordingState.Should().Be((int)VoiceIntakeRecordingStates.Completed, "an attempt without the lease writes nothing");
		}

		[TestCase(null, "Caller recording")]
		[TestCase(0, "Caller recording")]
		[TestCase(9, "Caller recording (0:09)")]
		[TestCase(3725, "Caller recording (62:05)")]
		public void the_file_title_shows_the_length(int? seconds, string expected)
		{
			VoiceIntakeRecordingService.DisplayName(seconds).Should().Be(expected);
		}

		private sealed class FixedClock : TimeProvider
		{
			public FixedClock(DateTime now) { Now = now; }

			public DateTime Now { get; set; }

			public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, TimeSpan.Zero);
		}

		/// <summary>The recording half of the sessions repository, in memory, with the SQL's conditional semantics.</summary>
		private sealed class FakeSessions : IVoiceIntakeSessionsRepository
		{
			public List<VoiceIntakeSession> Rows { get; } = new List<VoiceIntakeSession>();

			/// <summary>The next write of Attached throws (the attempt died after the file was saved).</summary>
			public bool FailNextAttachedWrite { get; set; }

			private VoiceIntakeSession ByCall(string callSid) => Rows.FirstOrDefault(r => string.Equals(r.ProviderCallId, callSid?.Trim(), StringComparison.OrdinalIgnoreCase));
			private VoiceIntakeSession ById(string id) => Rows.FirstOrDefault(r => r.VoiceIntakeSessionId == id);

			// The service reads a copy, as it would from the database.
			private static VoiceIntakeSession Copy(VoiceIntakeSession s) => s == null ? null : (VoiceIntakeSession)typeof(VoiceIntakeSession)
				.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(s, null);

			public Task<VoiceIntakeSession> GetByProviderCallIdAsync(string providerCallId) => Task.FromResult(Copy(ByCall(providerCallId)));

			public Task<bool> TryBeginRecordingAsync(string providerCallId, CancellationToken cancellationToken = default)
			{
				var row = ByCall(providerCallId);
				if (row == null || row.RecordingState != (int)VoiceIntakeRecordingStates.None)
					return Task.FromResult(false);
				row.RecordingState = (int)VoiceIntakeRecordingStates.Requested;
				return Task.FromResult(true);
			}

			public Task<bool> SetRecordingSidAsync(string providerCallId, string recordingSid, CancellationToken cancellationToken = default)
			{
				var row = ByCall(providerCallId);
				if (row == null || row.RecordingSid != null)
					return Task.FromResult(false);
				row.RecordingSid = recordingSid;
				return Task.FromResult(true);
			}

			public Task<bool> CompleteRecordingAsync(string providerCallId, string recordingSid, int? durationSeconds, int state, CancellationToken cancellationToken = default)
			{
				var row = ByCall(providerCallId);
				if (row == null || row.RecordingState != (int)VoiceIntakeRecordingStates.Requested)
					return Task.FromResult(false);
				row.RecordingState = state;
				row.RecordingDurationSeconds = durationSeconds;
				row.RecordingSid ??= recordingSid;
				return Task.FromResult(true);
			}

			public Task<bool> TryClaimRecordingAsync(string voiceIntakeSessionId, int expectedState, DateTime claimedOn, DateTime staleBefore, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingState != expectedState || (row.RecordingClaimedOn.HasValue && row.RecordingClaimedOn.Value >= staleBefore))
					return Task.FromResult(false);
				row.RecordingClaimedOn = claimedOn;
				row.RecordingAttempts++;
				return Task.FromResult(true);
			}

			public Task<bool> SetRecordingStateAsync(string voiceIntakeSessionId, int state, int? callAttachmentId, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null)
					return Task.FromResult(false);
				row.RecordingState = state;
				row.RecordingClaimedOn = null;
				row.RecordingCallAttachmentId = callAttachmentId ?? row.RecordingCallAttachmentId;
				return Task.FromResult(true);
			}

			/// <summary>Runs between the lease renewal and the save (another attempt taking the lease over meanwhile).</summary>
			public Action<VoiceIntakeSession> OnRenew { get; set; }

			public Task<bool> SetClaimedRecordingStateAsync(string voiceIntakeSessionId, DateTime claimedOn, int state, int? callAttachmentId, CancellationToken cancellationToken = default)
			{
				if (state == (int)VoiceIntakeRecordingStates.Attached && FailNextAttachedWrite)
				{
					FailNextAttachedWrite = false;
					throw new TimeoutException("database");
				}

				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingClaimedOn != claimedOn)
					return Task.FromResult(false);
				row.RecordingState = state;
				row.RecordingClaimedOn = null;
				row.RecordingCallAttachmentId = callAttachmentId ?? row.RecordingCallAttachmentId;
				return Task.FromResult(true);
			}

			public Task<bool> RenewRecordingClaimAsync(string voiceIntakeSessionId, DateTime claimedOn, DateTime renewedOn, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingClaimedOn != claimedOn || row.RecordingState != (int)VoiceIntakeRecordingStates.Completed)
					return Task.FromResult(false);
				row.RecordingClaimedOn = renewedOn;
				OnRenew?.Invoke(row);
				return Task.FromResult(true);
			}

			public Task<bool> TryDiscardUnconfirmedRecordingAsync(string voiceIntakeSessionId, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingState != (int)VoiceIntakeRecordingStates.Requested || row.RecordingSid != null)
					return Task.FromResult(false);
				row.RecordingState = (int)VoiceIntakeRecordingStates.Discarded;
				row.RecordingClaimedOn = null;
				return Task.FromResult(true);
			}

			public Task<bool> TryDiscardWithoutCallAsync(string voiceIntakeSessionId, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingState != (int)VoiceIntakeRecordingStates.Completed || row.CallId.HasValue)
					return Task.FromResult(false);
				row.RecordingState = (int)VoiceIntakeRecordingStates.Discarded;
				row.RecordingClaimedOn = null;
				return Task.FromResult(true);
			}

			public Task<bool> MarkRecordingCarrierDeletedAsync(string voiceIntakeSessionId, DateTime deletedOn, CancellationToken cancellationToken = default)
			{
				var row = ById(voiceIntakeSessionId);
				if (row == null || row.RecordingCarrierDeletedOn.HasValue)
					return Task.FromResult(false);
				row.RecordingCarrierDeletedOn = deletedOn;
				return Task.FromResult(true);
			}

			public Task<List<VoiceIntakeSession>> GetRecordingWorkAsync(DateTime purgeBefore, bool includeCarrierCopies, int max, CancellationToken cancellationToken = default)
			{
				var work = Rows.Where(r =>
						r.RecordingState == (int)VoiceIntakeRecordingStates.Requested || r.RecordingState == (int)VoiceIntakeRecordingStates.Completed ||
						(includeCarrierCopies && r.RecordingSid != null && r.RecordingCarrierDeletedOn == null &&
						 (r.RecordingState == (int)VoiceIntakeRecordingStates.Attached || r.RecordingState == (int)VoiceIntakeRecordingStates.Discarded ||
						  (r.RecordingState == (int)VoiceIntakeRecordingStates.Failed && r.StartedOn < purgeBefore))))
					.OrderBy(r => r.StartedOn).Take(max).Select(Copy).ToList();
				OnWorkRead?.Invoke();
				return Task.FromResult(work);
			}

			/// <summary>Runs right after worker 80 read its work (a callback landing between that read and the worker's writes).</summary>
			public Action OnWorkRead { get; set; }

			public Task<bool> TryInsertAsync(VoiceIntakeSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<bool> UpdateProgressAsync(VoiceIntakeSession session, int expectedStep, int expectedRetries, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<int> CountStartedAsync(int departmentId, DateTime from, DateTime before) => throw new NotSupportedException();
			public Task<int> PurgeStartedBeforeAsync(DateTime before, bool keepCarrierCleanup, CancellationToken cancellationToken = default) => throw new NotSupportedException();
			public Task<IEnumerable<VoiceIntakeSession>> GetAllAsync() => throw new NotSupportedException();
			public Task<VoiceIntakeSession> GetByIdAsync(object id) => throw new NotSupportedException();
			public Task<IEnumerable<VoiceIntakeSession>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException();
			public Task<VoiceIntakeSession> InsertAsync(VoiceIntakeSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
			public Task<VoiceIntakeSession> UpdateAsync(VoiceIntakeSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
			public Task<bool> DeleteAsync(VoiceIntakeSession entity, CancellationToken cancellationToken) => throw new NotSupportedException();
			public Task<VoiceIntakeSession> SaveOrUpdateAsync(VoiceIntakeSession entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
			public Task<IEnumerable<VoiceIntakeSession>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();
			public Task<bool> DeleteMultipleAsync(VoiceIntakeSession entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
		}
	}
}
