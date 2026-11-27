using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// What goes onto a call (P0 items 5, 5a, 5b, 6): the server decides which protocols are attached and records why (trigger
	/// or by hand), the answers in words and the score it computed itself, the protocol's own code, and the hash of the text as
	/// dispatched; a protocol is on a call once and is taken off with a reason. The matcher works on call type ids and UTC windows
	/// at the call's logged time.
	/// </summary>
	[TestFixture]
	public class ProtocolCallRecordTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 99;

		private Mock<IDispatchProtocolRepository> _protocols;
		private Mock<IDispatchProtocolTriggersRepository> _triggers;
		private Mock<IDispatchProtocolQuestionsRepository> _questions;
		private Mock<IDispatchProtocolAttachmentRepository> _attachments;
		private Mock<ICallTypesRepository> _callTypes;
		private Mock<ICallProtocolsRepository> _callProtocols;
		private Mock<IDispatchProtocolSnapshotsRepository> _snapshots;
		private Mock<ICacheProvider> _cache;
		private ProtocolsService _service;

		private static readonly DateTime LoggedOn = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

		[SetUp]
		public void SetUp()
		{
			_protocols = new Mock<IDispatchProtocolRepository>();
			_triggers = new Mock<IDispatchProtocolTriggersRepository>();
			_questions = new Mock<IDispatchProtocolQuestionsRepository>();
			_attachments = new Mock<IDispatchProtocolAttachmentRepository>();
			_callTypes = new Mock<ICallTypesRepository>();
			_callProtocols = new Mock<ICallProtocolsRepository>();
			_snapshots = new Mock<IDispatchProtocolSnapshotsRepository>();
			_cache = new Mock<ICacheProvider>();

			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId, Name = "Structure fire", Code = "SF", ProtocolText = "<p>Size up</p>" },
				new DispatchProtocol { DispatchProtocolId = 2, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 2000, DepartmentId = DepartmentId, Name = "Cardiac", Code = "09-E-01", ProtocolText = "<p>Start CPR</p>", MinimumWeight = 10 },
				new DispatchProtocol { DispatchProtocolId = 3, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 3000, DepartmentId = DepartmentId, Name = "Retired", Code = "OLD", IsDisabled = true },
				new DispatchProtocol { DispatchProtocolId = 4, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 4000, DepartmentId = DepartmentId, Name = "Gone", Code = "GONE", IsDeleted = true },
				new DispatchProtocol { DispatchProtocolId = 5, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 5000, DepartmentId = DepartmentId, Name = "Hazmat", Code = "HZ", ProtocolText = "<p>Stay upwind</p>" },
				// The generic department query has returned other departments' rows before.
				new DispatchProtocol { DispatchProtocolId = 7, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 7000, DepartmentId = OtherDepartmentId, Name = "Theirs", Code = "THR" }
			});
			_triggers.Setup(x => x.GetTriggersByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolTrigger>
			{
				new DispatchProtocolTrigger { DispatchProtocolTriggerId = 100, DispatchProtocolId = 1, Type = (int)ProtocolTriggerTypes.CallType, CallTypeId = 70, CallType = "Old name" },
				new DispatchProtocolTrigger { DispatchProtocolTriggerId = 101, DispatchProtocolId = 2, Type = (int)ProtocolTriggerTypes.CallPriorty, Priority = 3 },
				new DispatchProtocolTrigger { DispatchProtocolTriggerId = 102, DispatchProtocolId = 3, Type = (int)ProtocolTriggerTypes.CallPriorty, Priority = 3 }
			});
			_questions.Setup(x => x.GetQuestionsWithAnswersByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolQuestion>
			{
				new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = 200, DispatchProtocolId = 2, Question = "Is the patient breathing?",
					Answers = new List<DispatchProtocolQuestionAnswer>
					{
						new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 300, DispatchProtocolQuestionId = 200, Answer = "No", Weight = 8 },
						new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 301, DispatchProtocolQuestionId = 200, Answer = "Yes", Weight = 0 }
					}
				},
				new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = 201, DispatchProtocolId = 2, Question = "Is the patient conscious?",
					Answers = new List<DispatchProtocolQuestionAnswer>
					{
						new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 302, DispatchProtocolQuestionId = 201, Answer = "No", Weight = 4 },
						new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 303, DispatchProtocolQuestionId = 201, Answer = "Yes", Weight = 1 }
					}
				}
			});
			_attachments.Setup(x => x.GetAttachmentInfoByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocolAttachment>());
			_callTypes.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<CallType>
			{
				new CallType { CallTypeId = 70, DepartmentId = DepartmentId, Type = "Structure Fire" }
			});

			_service = new ProtocolsService(_protocols.Object, _attachments.Object, _questions.Object, _triggers.Object,
				Mock.Of<IDispatchProtocolQuestionAnswersRepository>(), new Lazy<IDispatchTriggerFactsService>(() => Mock.Of<IDispatchTriggerFactsService>()),
				new Lazy<IProtocolAiSuggestionService>(() => Mock.Of<IProtocolAiSuggestionService>()), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), null, _cache.Object, _callTypes.Object, _callProtocols.Object, _snapshots.Object);
		}

		private static Call CallFor(int priority, string type) => new Call { CallId = 900, DepartmentId = DepartmentId, Priority = priority, Type = type, LoggedOn = LoggedOn };

		private static ProtocolAttachRequest Ask(int id, bool answersRequired = true, params int[] answers) =>
			new ProtocolAttachRequest { ProtocolId = id, AnswerIds = answers.ToList(), AnswersRequired = answersRequired };

		#region Attach: the server decides

		[Test]
		public async Task Disabled_deleted_and_foreign_protocols_are_refused()
		{
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { Ask(3), Ask(4), Ask(7), Ask(404) }, "u1");

			result.Attached.Should().BeEmpty();
			result.Refused.Select(x => (x.ProtocolId, x.Reason)).Should().BeEquivalentTo(new[]
			{
				(3, ProtocolAttachRefusals.Disabled), (4, ProtocolAttachRefusals.Deleted), (7, ProtocolAttachRefusals.NotFound), (404, ProtocolAttachRefusals.NotFound)
			});
		}

		[Test]
		public async Task The_server_recomputes_the_score_records_the_answers_in_words_and_uses_the_protocols_own_code()
		{
			// Priority 3 matches protocol 2's trigger; "No" (8) + "No" (4) = 12, at least the minimum of 10.
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { Ask(2, true, 300, 302) }, "dispatcher-1");

			var row = result.Attached.Should().ContainSingle().Subject;
			row.DispatchProtocolId.Should().Be(2);
			row.Score.Should().Be(12);
			row.Data.Should().Be("09-E-01", "responders see the protocol's own code, never a posted one");
			row.Source.Should().Be((int)CallProtocolSources.Trigger);
			row.Trigger.Should().Be((int)ProtocolTriggerTypes.CallPriorty);
			row.AttachedByUserId.Should().Be("dispatcher-1");
			row.AttachedOn.Should().NotBeNull();

			var answers = JsonConvert.DeserializeObject<CallProtocolAnswers>(row.AnswersJson);
			answers.MinimumWeight.Should().Be(10);
			answers.Score.Should().Be(12);
			answers.Passed.Should().BeTrue();
			answers.MatchedTriggerId.Should().Be(101);
			answers.Answers.Select(a => (a.Question, a.Answer, a.Weight)).Should().Equal(
				("Is the patient breathing?", "No", 8), ("Is the patient conscious?", "No", 4));
		}

		[Test]
		public async Task A_suggested_protocol_with_questions_is_not_attached_unanswered_whatever_a_pending_flag_says()
		{
			// The old form posted pendingProtocol_{id}=1 to claim the questions passed; there is no such thing now: no answers, no attach.
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { Ask(2) }, "u1");

			result.Attached.Should().BeEmpty();
			result.Refused.Should().ContainSingle(x => x.ProtocolId == 2 && x.Reason == ProtocolAttachRefusals.QuestionsUnanswered);
		}

		[Test]
		public async Task A_suggested_protocol_below_its_minimum_weight_is_not_attached()
		{
			// "Yes" (0) + "Yes" (1) = 1 < 10.
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { Ask(2, true, 301, 303) }, "u1");

			result.Refused.Should().ContainSingle(x => x.Reason == ProtocolAttachRefusals.BelowMinimumWeight);
		}

		[Test]
		public async Task Answer_ids_of_other_questions_or_protocols_do_not_count()
		{
			// 999 is nobody's answer; 300 answers question 200 only once even when sent twice.
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { Ask(2, true, 300, 300, 999) }, "u1");

			result.Refused.Should().ContainSingle(x => x.Reason == ProtocolAttachRefusals.QuestionsUnanswered);
		}

		[Test]
		public async Task A_protocol_picked_by_hand_is_attached_and_recorded_as_manual()
		{
			// Priority 1 matches nothing for protocol 2: the dispatcher's pick, with the answers as given.
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(1, null), new[] { Ask(2, true, 301), Ask(5) }, "u1");

			result.Attached.Select(x => (x.DispatchProtocolId, x.Source)).Should().BeEquivalentTo(new[] { (2, (int?)CallProtocolSources.Manual), (5, (int?)CallProtocolSources.Manual) });
			JsonConvert.DeserializeObject<CallProtocolAnswers>(result.Attached.First(x => x.DispatchProtocolId == 2).AnswersJson).Complete.Should().BeFalse();
			result.Attached.First(x => x.DispatchProtocolId == 5).AnswersJson.Should().BeNull();
		}

		[Test]
		public async Task Ids_alone_from_an_older_app_still_attach()
		{
			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(3, null), new[] { new ProtocolAttachRequest { ProtocolId = 2 } }, "u1");

			result.Attached.Should().ContainSingle(x => x.DispatchProtocolId == 2 && x.AnswersJson == null);
		}

		[Test]
		public async Task A_protocol_is_on_a_call_once()
		{
			var call = CallFor(3, null);
			call.Protocols = new List<CallProtocol> { new CallProtocol { CallProtocolId = 1, CallId = 900, DispatchProtocolId = 5, RemovedOn = DateTime.UtcNow } };

			var result = await _service.PrepareCallProtocolsAsync(DepartmentId, call, new[] { Ask(1), Ask(1), Ask(5) }, "u1");

			result.Attached.Select(x => x.DispatchProtocolId).Should().Equal(1);
			result.Refused.Select(x => (x.ProtocolId, x.Reason)).Should().BeEquivalentTo(new[] { (1, ProtocolAttachRefusals.Duplicate), (5, ProtocolAttachRefusals.Duplicate) });
		}

		#endregion

		#region Snapshots

		[Test]
		public async Task The_text_as_dispatched_is_stored_once_per_content_hash()
		{
			var stored = new List<DispatchProtocolSnapshot>();
			_snapshots.Setup(x => x.InsertIfMissingAsync(It.IsAny<DispatchProtocolSnapshot>(), It.IsAny<CancellationToken>()))
				.Callback<DispatchProtocolSnapshot, CancellationToken>((s, _) => stored.Add(s))
				.Returns(Task.CompletedTask);

			var first = await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(1, null), new[] { Ask(5) }, "u1");
			var second = await _service.PrepareCallProtocolsAsync(DepartmentId, new Call { CallId = 901, Priority = 1, LoggedOn = LoggedOn }, new[] { Ask(5) }, "u1");

			var hash = ProtocolContentHash.Compute("<p>Stay upwind</p>");
			first.Attached.Single().ContentHash.Should().Be(hash);
			second.Attached.Single().ContentHash.Should().Be(hash);
			stored.Should().OnlyContain(s => s.DispatchProtocolId == 5 && s.ContentHash == hash && s.ProtocolText == "<p>Stay upwind</p>" && s.DepartmentId == DepartmentId);
			stored.Select(s => (s.DispatchProtocolId, s.ContentHash)).Distinct().Should().ContainSingle("the snapshot is keyed by protocol and hash, so the same text is one row");
		}

		[Test]
		public void The_content_hash_is_sha256_of_the_stored_text_in_lower_case_hex()
		{
			// SHA-256("") — the documented algorithm; P1 builds versions from these hashes, so this must never change.
			ProtocolContentHash.Compute(null).Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
			ProtocolContentHash.Compute("<p>a</p>").Should().NotBe(ProtocolContentHash.Compute("<p>b</p>"));
			ProtocolContentHash.Compute("<p>a</p>").Should().Be(ProtocolContentHash.Compute("<p>a</p>"));
		}

		[Test]
		public async Task A_dry_run_for_the_field_policy_stores_no_snapshot()
		{
			await _service.PrepareCallProtocolsAsync(DepartmentId, CallFor(1, null), new[] { Ask(5) }, "u1", null, false);

			_snapshots.Verify(x => x.InsertIfMissingAsync(It.IsAny<DispatchProtocolSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion

		#region Removal

		[Test]
		public async Task Removing_a_protocol_keeps_the_row_with_who_when_and_why()
		{
			var row = new CallProtocol { CallProtocolId = 77, CallId = 900, DispatchProtocolId = 1 };
			_callProtocols.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(row);
			_callProtocols.Setup(x => x.SaveOrUpdateAsync(row, It.IsAny<CancellationToken>(), true)).ReturnsAsync(row);
			_protocols.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId });

			var removed = await _service.RemoveProtocolFromCallAsync(DepartmentId, 900, 77, "  Wrong call type  ", "u1");

			removed.Should().BeSameAs(row);
			row.RemovedOn.Should().NotBeNull();
			row.RemovedByUserId.Should().Be("u1");
			row.RemovedReason.Should().Be("Wrong call type");
			_callProtocols.Verify(x => x.DeleteAsync(It.IsAny<CallProtocol>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Removal_needs_a_reason_the_right_call_and_the_departments_protocol()
		{
			_callProtocols.Setup(x => x.GetByIdAsync(77)).ReturnsAsync(new CallProtocol { CallProtocolId = 77, CallId = 900, DispatchProtocolId = 7 });
			_protocols.Setup(x => x.GetByIdAsync(7)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 7, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 7000, DepartmentId = OtherDepartmentId });

			(await _service.RemoveProtocolFromCallAsync(DepartmentId, 900, 77, " ", "u1")).Should().BeNull();
			(await _service.RemoveProtocolFromCallAsync(DepartmentId, 901, 77, "reason", "u1")).Should().BeNull();
			(await _service.RemoveProtocolFromCallAsync(DepartmentId, 900, 77, "reason", "u1")).Should().BeNull();
			_callProtocols.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CallProtocol>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		#endregion

		#region Matching

		[Test]
		public async Task A_type_trigger_matches_by_call_type_id_whatever_name_it_stored()
		{
			// The trigger stored "Old name"; the type is "Structure Fire" now. The id still matches.
			var context = await _service.BuildCallContextAsync(DepartmentId, 1, null, "structure fire", LoggedOn);
			var matches = await _service.GetProtocolsForCallAsync(DepartmentId, context);

			context.CallTypeId.Should().Be(70);
			var match = matches.Single(x => x.Protocol.DispatchProtocolId == 1);
			match.State.Should().Be(ProtocolStates.Active);
			match.MatchedTrigger.DispatchProtocolTriggerId.Should().Be(100);
		}

		[Test]
		public async Task A_translated_no_type_label_is_no_type()
		{
			var context = await _service.BuildCallContextAsync(DepartmentId, 1, null, "Sin tipo", LoggedOn);

			context.CallTypeId.Should().BeNull();
		}

		[Test]
		public async Task Disabled_protocols_are_never_suggested()
		{
			var matches = await _service.GetProtocolsForCallAsync(DepartmentId, await _service.BuildCallContextAsync(DepartmentId, 3, null, null, LoggedOn));

			matches.Select(x => x.Protocol.DispatchProtocolId).Should().NotContain(new[] { 3, 4, 7 });
			matches.Single(x => x.Protocol.DispatchProtocolId == 2).State.Should().Be(ProtocolStates.Pending);
		}

		[Test]
		public void The_no_type_trigger_matches_a_call_without_a_type_only()
		{
			var protocol = new DispatchProtocol { Triggers = new List<DispatchProtocolTrigger> { new DispatchProtocolTrigger { Type = (int)ProtocolTriggerTypes.CallType } } };

			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { AtUtc = LoggedOn, CallTypeId = null }).Should().ContainSingle();
			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { AtUtc = LoggedOn, CallTypeId = 70 }).Should().BeEmpty();

			// The English label the old form stored means the same.
			protocol.Triggers.Single().CallType = ProtocolTriggerMatcher.LegacyNoTypeLabel;
			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { AtUtc = LoggedOn, CallTypeId = null }).Should().ContainSingle();
		}

		[Test]
		public void Trigger_windows_are_compared_in_utc_with_the_calls_logged_time()
		{
			var protocol = new DispatchProtocol
			{
				Triggers = new List<DispatchProtocolTrigger>
				{
					new DispatchProtocolTrigger
					{
						Type = (int)ProtocolTriggerTypes.CallPriorty, Priority = 3,
						StartsOn = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc), EndsOn = new DateTime(2026, 11, 20, 16, 0, 0, DateTimeKind.Utc)
					}
				}
			};

			// A call logged inside the window matches, whenever it is looked at (an archived call entered days later).
			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { Priority = 3, AtUtc = LoggedOn }).Should().ContainSingle();
			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { Priority = 3, AtUtc = LoggedOn.AddHours(2) }).Should().BeEmpty();
			ProtocolTriggerMatcher.Match(protocol, new ProtocolCallContext { Priority = 3, AtUtc = LoggedOn.AddHours(-2) }).Should().BeEmpty();
		}

		[Test]
		public async Task A_rename_moves_the_triggers_name_and_keeps_the_id()
		{
			var saved = new List<DispatchProtocolTrigger>();
			_triggers.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocolTrigger>(), It.IsAny<CancellationToken>(), true))
				.Callback<DispatchProtocolTrigger, CancellationToken, bool>((t, _, __) => saved.Add(t))
				.ReturnsAsync((DispatchProtocolTrigger t, CancellationToken _, bool __) => t);

			var changed = await _service.RenameCallTypeInTriggersAsync(DepartmentId, 70, "Structure Fire", "Working Fire");

			changed.Should().Be(1);
			saved.Single().CallTypeId.Should().Be(70);
			saved.Single().CallType.Should().Be("Working Fire");
		}

		[Test]
		public async Task Deleting_a_type_or_removing_a_priority_reports_the_triggers_it_affects()
		{
			(await _service.GetTriggersForCallTypeAsync(DepartmentId, 70, "Structure Fire")).Select(x => x.TriggerId).Should().Equal(100);
			(await _service.GetTriggersForPriorityAsync(DepartmentId, 3)).Select(x => x.ProtocolCode).Should().BeEquivalentTo("09-E-01", "OLD");
		}

		#endregion

		#region Lists, caches and bytes

		[Test]
		public async Task Summaries_batch_load_children_and_never_read_attachment_bytes()
		{
			var all = await _service.GetProtocolSummariesForDepartmentAsync(DepartmentId);
			var enabled = await _service.GetProtocolSummariesForDepartmentAsync(DepartmentId, true);

			all.Select(x => x.DispatchProtocolId).Should().BeEquivalentTo(new[] { 1, 2, 3, 5 }, "deleted and foreign rows are left out");
			enabled.Select(x => x.DispatchProtocolId).Should().BeEquivalentTo(new[] { 1, 2, 5 });
			all.Single(x => x.DispatchProtocolId == 2).Questions.Should().HaveCount(2);

			// One query per child kind for the whole department, and never the byte-loading attachment query.
			_triggers.Verify(x => x.GetTriggersByDepartmentIdAsync(DepartmentId), Times.Exactly(2));
			_triggers.Verify(x => x.GetDispatchProtocolTriggersByProtocolIdAsync(It.IsAny<int>()), Times.Never);
			_questions.Verify(x => x.GetDispatchProtocolQuestionsByProtocolIdAsync(It.IsAny<int>()), Times.Never);
			_attachments.Verify(x => x.GetDispatchProtocolAttachmentByProtocolIdAsync(It.IsAny<int>()), Times.Never);
			_attachments.Verify(x => x.GetByIdAsync(It.IsAny<object>()), Times.Never);
		}

		[Test]
		public async Task Saving_disabling_or_deleting_drops_the_matching_set_and_the_sync_reference()
		{
			_protocols.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId });
			_protocols.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((DispatchProtocol p, CancellationToken _, bool __) => p);

			await _service.SetProtocolDisabledAsync(1, true, "u1");

			_cache.Verify(x => x.RemoveAsync("ProtocolMatchingSet_" + DepartmentId), Times.Once);
			_cache.Verify(x => x.RemoveAsync(SyncService.ReferenceCacheKey(DepartmentId)), Times.Once);
		}

		#endregion
	}
}
