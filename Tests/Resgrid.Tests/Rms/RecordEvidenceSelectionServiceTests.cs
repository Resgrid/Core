using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordEvidenceSelectionServiceTests
	{
		private Mock<IRecordsAuthorizationService> _auth;
		private Mock<IAuthorizationService> _sourceAuth;
		private Mock<IChatPermissionService> _chat;
		private Mock<IChatChannelRepository> _channels;
		private Mock<IChatMessageRepository> _messages;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentsService> _departments;
		private RmsOperationalRecord _record;
		private ChatChannel _channel;
		private RecordEvidenceSelectionService _service;

		[SetUp]
		public void Setup()
		{
			_record = new RmsOperationalRecord { DepartmentId = 9, RmsOperationalRecordId = "record", AuthorUserId = "officer", State = (int)RmsRecordState.Draft, RowVersion = 7, CallId = 501 };
			var records = new Mock<IRmsOperationalRecordsRepository>(); records.Setup(r => r.GetByIdForDepartmentAsync(9, "record")).ReturnsAsync(() => _record);
			var incidents = new Mock<IRmsIncidentReportsRepository>(); incidents.Setup(r => r.GetByIdForDepartmentAsync(9, "incident")).ReturnsAsync(new RmsIncidentReport {
				DepartmentId = 9, RmsIncidentReportId = "incident", AuthorUserId = "officer", State = (int)RmsRecordState.Draft, RowVersion = 8, CallId = 501 });
			_auth = new(); _auth.Setup(a => a.CanUserViewRecordAsync("officer", It.IsAny<string>(), 9)).ReturnsAsync(true);
			_auth.Setup(a => a.HasPermissionAsync("officer", 9, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_auth.Setup(a => a.CanReadSourceCallAsync("officer", 9, It.IsAny<Call>())).ReturnsAsync(true);
			_sourceAuth = new(); _chat = new(); _channels = new(); _messages = new(); _units = new(); _departments = new();
			var cutover = new Mock<IRecordsCutoverService>(); cutover.Setup(c => c.GetModuleStateAsync(9, false)).ReturnsAsync(new RecordsModuleState {
				FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active });
			var evidence = new Mock<IRecordsEvidenceService>(); evidence.Setup(e => e.GetSourceStatesAsync(9)).ReturnsAsync(Enum.GetValues<RmsEvidenceKind>()
				.Select(k => new RecordEvidenceSourceState { Kind = k, Available = k != RmsEvidenceKind.ReadinessPacket, Reason = "Unavailable dependency" }).ToList());
			_channel = new ChatChannel { DepartmentId = 9, CallId = 501, ChatChannelId = "channel", Name = "Incident" };
			_channels.Setup(c => c.GetByCallIdAsync(501)).ReturnsAsync(new[] { _channel });
			_chat.Setup(c => c.CanAccessChannelAsync(_channel, "officer", null)).ReturnsAsync(true);
			_service = new RecordEvidenceSelectionService(records.Object, incidents.Object, _auth.Object, cutover.Object, evidence.Object,
				Mock.Of<ICallsService>(), _units.Object, _departments.Object, new Lazy<IAuthorizationService>(() => _sourceAuth.Object),
				_channels.Object, _messages.Object, new Lazy<IChatPermissionService>(() => _chat.Object));
		}
		private Task<RecordEvidenceSelection> Select(RmsEvidenceKind source, string channel = null) =>
			_service.GetAsync(9, "officer", "record", RmsRecordKind.Operational, source, channel);

		[TestCase("record", RmsRecordKind.Operational)]
		[TestCase("incident", RmsRecordKind.IncidentReport)]
		public async Task Both_officer_record_kinds_offer_only_source_authorized_personnel(string id, RmsRecordKind kind)
		{
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(9)).ReturnsAsync(new List<PersonName> {
				new() { UserId = "visible", FirstName = "Visible", LastName = "Member" }, new() { UserId = "hidden", FirstName = "Sensitive", LastName = "Person" } });
			_sourceAuth.Setup(a => a.CanUserViewPersonAsync("officer", "VISIBLE", 9)).ReturnsAsync(true);
			var selection = await _service.GetAsync(9, "officer", id, kind, RmsEvidenceKind.CertificationSnapshot);
			selection.Choices.Should().ContainSingle().Which.Label.Should().Be("Visible Member");
			selection.Context.RecordKind.Should().Be(kind); selection.Context.CanCapture.Should().BeTrue();
		}

		[Test]
		public async Task Unit_choices_hide_foreign_and_denied_units_and_recheck_earlier_permissions()
		{
			_units.Setup(u => u.GetUnitsForDepartmentAsync(9)).ReturnsAsync(new List<Unit> {
				new() { DepartmentId = 9, UnitId = 1, Name = "Engine 1" }, new() { DepartmentId = 9, UnitId = 2, Name = "Hidden" }, new() { DepartmentId = 10, UnitId = 3, Name = "Foreign" } });
			_sourceAuth.Setup(a => a.CanUserViewUnitLocationAsync("officer", 1, 9)).ReturnsAsync(true);
			(await Select(RmsEvidenceKind.TrackingFix)).Choices.Should().ContainSingle().Which.Id.Should().Be("1");
			_sourceAuth.SetupSequence(a => a.CanUserViewUnitLocationAsync("officer", 1, 9)).ReturnsAsync(true).ReturnsAsync(false);
			Func<Task> revoked = () => Select(RmsEvidenceKind.TrackingFix); await revoked.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Chat_pages_include_replies_and_advance_past_deleted_moderated_and_foreign_rows_without_exposing_them()
		{
			var rows = Enumerable.Range(1, 101).Select(i => new ChatMessage { ChatMessageId = i.ToString(), DepartmentId = 9, ChatChannelId = "channel",
				MessageSeq = i, Body = "Message " + i, ThreadRootMessageId = "parent" }).ToList();
			rows[0].DeletedOn = DateTime.UtcNow; rows[1].IsModerated = true; rows[2].DepartmentId = 10; rows[3].ChatChannelId = "other";
			_messages.Setup(m => m.GetAfterSeqAsync("channel", 0, 101)).ReturnsAsync(rows);
			var selection = await Select(RmsEvidenceKind.ChatPromotion, "channel");
			selection.NextSequence.Should().Be(100); selection.Choices.Should().HaveCount(96);
			selection.Choices.Select(c => c.Id).Should().NotContain(new[] { "1", "2", "3", "4", "101" });
			selection.Choices.Last().Body.Should().Be("Message 100");
		}

		[TestCase("record")]
		[TestCase("call")]
		[TestCase("restricted")]
		[TestCase("owner")]
		[TestCase("finalized")]
		public async Task Selection_is_denied_before_reading_message_bodies_when_a_required_grant_or_edit_state_is_missing(string boundary)
		{
			if (boundary == "record") _auth.Setup(a => a.CanUserViewRecordAsync("officer", "record", 9)).ReturnsAsync(false);
			if (boundary == "call") _auth.Setup(a => a.CanReadSourceCallAsync("officer", 9, It.IsAny<Call>())).ReturnsAsync(false);
			if (boundary == "restricted") _auth.Setup(a => a.HasPermissionAsync("officer", 9, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			if (boundary == "owner") _record.AuthorUserId = "another-officer";
			if (boundary == "finalized") _record.State = (int)RmsRecordState.Finalized;
			Func<Task> denied = () => Select(RmsEvidenceKind.ChatPromotion, "channel"); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			_messages.VerifyNoOtherCalls();
		}

		[TestCase("channel")]
		[TestCase("rebind")]
		[TestCase("restricted")]
		[TestCase("record")]
		public async Task Access_loss_during_the_source_read_cannot_return_earlier_message_bodies(string boundary)
		{
			_messages.Setup(m => m.GetAfterSeqAsync("channel", 0, 101)).Callback(() => {
				if (boundary == "channel") _chat.Setup(c => c.CanAccessChannelAsync(_channel, "officer", null)).ReturnsAsync(false);
				if (boundary == "rebind") _channel.CallId = 999;
				if (boundary == "restricted") _auth.Setup(a => a.HasPermissionAsync("officer", 9, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
				if (boundary == "record") _auth.Setup(a => a.CanUserViewRecordAsync("officer", "record", 9)).ReturnsAsync(false);
			}).ReturnsAsync(new[] { new ChatMessage { DepartmentId = 9, ChatChannelId = "channel", Body = "Sensitive" } });
			Func<Task> denied = () => Select(RmsEvidenceKind.ChatPromotion, "channel"); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task A_forged_channel_and_a_concurrently_changed_parent_never_return_a_capture_form()
		{
			Func<Task> forged = () => Select(RmsEvidenceKind.ChatPromotion, "another-channel"); await forged.Should().ThrowAsync<UnauthorizedAccessException>();
			_messages.VerifyNoOtherCalls();
			_messages.Setup(m => m.GetAfterSeqAsync("channel", 0, 101)).Callback(() => _record.RowVersion++).ReturnsAsync(Array.Empty<ChatMessage>());
			Func<Task> stale = () => Select(RmsEvidenceKind.ChatPromotion, "channel"); await stale.Should().ThrowAsync<RecordConcurrencyException>();
		}
	}
}
