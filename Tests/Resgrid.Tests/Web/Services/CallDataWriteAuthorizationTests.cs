using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Tests.Mocks;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.CallFiles;
using Resgrid.Web.Services.Models.v4.CallNotes;
using Resgrid.Web.Services.Models.v4.Calls;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 call notes and call files: the Add Call Data permission (with group-scoped dispatch) gates every write, not just
	/// Call_View, and the author is always the authenticated member, never the UserId a client posts. The SMTP relay's
	/// system key keeps attaching inbound email files for the department under the author it names.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallDataWriteAuthorizationTests
	{
		private const int DepartmentId = 10;
		private const int CallId = 42;
		private const string UserId = "responder-1";
		private const string SpoofedUserId = "chief-7";

		private Mock<ICallsService> _callsService;
		private Mock<IAuthorizationService> _authorizationService;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private CallNote _savedNote;
		private CallAttachment _savedAttachment;
		private int _savedAttachments;
		private MockInboundEmailReceiptsRepository _receipts;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_callsService = new Mock<ICallsService>();
			_callsService.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId, State = (int)CallStates.Active });
			_callsService.Setup(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallNote n, CancellationToken _) => { n.CallNoteId = 5; _savedNote = n; return n; });
			_callsService.Setup(x => x.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallAttachment a, CancellationToken _) => { a.CallAttachmentId = 6 + _savedAttachments++; _savedAttachment = a; return a; });

			_authorizationService = new Mock<IAuthorizationService>();

			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService.Setup(x => x.PreflightWriteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			_protectedWriteService.Setup(x => x.PrepareCallNoteWriteAsync(It.IsAny<int>(), It.IsAny<CallNote>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });
			_protectedWriteService.Setup(x => x.PrepareCallAttachmentWriteAsync(It.IsAny<int>(), It.IsAny<CallAttachment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			_savedNote = null;
			_savedAttachment = null;
			_savedAttachments = 0;
			_receipts = new MockInboundEmailReceiptsRepository();
			_activity = new Activity(nameof(CallDataWriteAuthorizationTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static DefaultHttpContext Context(bool systemKey = false)
		{
			var identity = systemKey
				? new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ResgridClaimTypes.Data.ServiceAccount, "true") }, "SystemApiKey")
				: new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test");
			var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			return httpContext;
		}

		private CallNotesController NotesController(string idempotencyKey = null)
		{
			var context = Context();
			if (idempotencyKey != null)
				context.Request.Headers[CallsController.IdempotencyKeyHeader] = idempotencyKey;

			return new CallNotesController(_callsService.Object, Mock.Of<IDepartmentsService>(),
				Mock.Of<IProtectedReadService>(), _protectedWriteService.Object, _authorizationService.Object,
				new InboundEmailReceiptsService(_receipts, TimeProvider.System))
			{
				ControllerContext = new ControllerContext { HttpContext = context }
			};
		}

		private CallFilesController FilesController(bool systemKey = false, string idempotencyKey = null)
		{
			var context = Context(systemKey);
			if (idempotencyKey != null)
				context.Request.Headers[CallsController.IdempotencyKeyHeader] = idempotencyKey;

			return new CallFilesController(_callsService.Object, Mock.Of<IDepartmentsService>(),
				Mock.Of<IProtectedReadService>(), _protectedWriteService.Object, _authorizationService.Object,
				new InboundEmailReceiptsService(_receipts, TimeProvider.System))
			{
				ControllerContext = new ControllerContext { HttpContext = context }
			};
		}

		private static SaveCallNoteInput NoteInput() => new SaveCallNoteInput { CallId = CallId.ToString(), UserId = SpoofedUserId, Note = "Hydrant out of service" };

		private static SaveCallFileInput FileInput() => new SaveCallFileInput
		{
			CallId = CallId.ToString(), UserId = SpoofedUserId, Type = (int)CallAttachmentTypes.Image, Name = "scene.png", Data = Convert.ToBase64String(new byte[] { 1, 2, 3 })
		};

		[Test]
		public async Task A_note_is_refused_when_Add_Call_Data_denies_the_member()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);

			var result = await NotesController().SaveCallNote(NoteInput(), CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedResult>();
			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_note_is_authored_by_the_caller_whatever_UserId_the_client_posts()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			var result = await NotesController().SaveCallNote(NoteInput(), CancellationToken.None);

			result.Result.Should().BeOfType<CreatedAtActionResult>();
			_savedNote.UserId.Should().Be(UserId);
		}

		[Test]
		public async Task A_file_is_refused_when_Add_Call_Data_denies_the_member()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);

			var result = await FilesController().SaveCallFile(FileInput(), CancellationToken.None);

			result.Result.Should().BeOfType<UnauthorizedResult>();
			_callsService.Verify(x => x.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_file_is_authored_by_the_caller_whatever_UserId_the_client_posts()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			await FilesController().SaveCallFile(FileInput(), CancellationToken.None);

			_savedAttachment.Should().NotBeNull();
			_savedAttachment.UserId.Should().Be(UserId);
		}

		[Test]
		public async Task The_system_key_attaches_without_a_member_check_under_the_author_it_names()
		{
			await FilesController(systemKey: true).SaveCallFile(FileInput(), CancellationToken.None);

			_authorizationService.Verify(x => x.CanUserAddCallDataAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			_savedAttachment.Should().NotBeNull();
			_savedAttachment.UserId.Should().Be(SpoofedUserId);
		}

		[Test]
		public async Task A_keyed_file_sent_again_by_the_relay_returns_the_first_attachment_instead_of_a_second()
		{
			var first = await FilesController(systemKey: true, idempotencyKey: "msg-1@cad.example#attachment-0").SaveCallFile(FileInput(), CancellationToken.None);
			var again = await FilesController(systemKey: true, idempotencyKey: "msg-1@cad.example#attachment-0").SaveCallFile(FileInput(), CancellationToken.None);
			var other = await FilesController(systemKey: true, idempotencyKey: "msg-1@cad.example#attachment-1").SaveCallFile(FileInput(), CancellationToken.None);

			Id(first).Should().Be("6");
			Id(again).Should().Be("6");
			Id(other).Should().Be("7");
			_savedAttachments.Should().Be(2);
			_receipts.Receipts.Should().OnlyContain(r => r.Kind == (int)InboundEmailKinds.ApiCallFile && r.State == (int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task Unkeyed_files_are_saved_every_time()
		{
			await FilesController(systemKey: true).SaveCallFile(FileInput(), CancellationToken.None);
			await FilesController(systemKey: true).SaveCallFile(FileInput(), CancellationToken.None);

			_savedAttachments.Should().Be(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		private static string Id(ActionResult<SaveCallFileResult> result) => ((result.Result as CreatedAtActionResult)?.Value as SaveCallFileResult)?.Id;

		[Test]
		public async Task A_file_resent_from_an_app_outbox_with_its_body_key_comes_back_with_the_first_id()
		{
			// Omni's offline outbox sends its event id as the body's IdempotencyKey, not as a header.
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			var input = FileInput();
			input.IdempotencyKey = "6f1c2a7e-outbox-file";

			var first = await FilesController().SaveCallFile(input, CancellationToken.None);
			var again = await FilesController().SaveCallFile(input, CancellationToken.None);

			Id(first).Should().Be("6");
			Id(again).Should().Be("6");
			_savedAttachments.Should().Be(1);
			_receipts.Receipts.Should().ContainSingle().Which.Kind.Should().Be((int)InboundEmailKinds.ApiCallFile);
		}

		[Test]
		public async Task A_file_header_key_wins_over_its_body_key()
		{
			var keyed = FileInput();
			keyed.IdempotencyKey = "body-key";

			await FilesController(systemKey: true, idempotencyKey: "msg-1@cad.example#attachment-0").SaveCallFile(keyed, CancellationToken.None);
			var bodyOnly = await FilesController(systemKey: true).SaveCallFile(keyed, CancellationToken.None);

			Id(bodyOnly).Should().Be("7", "the first upload was keyed by its header, not by its body");
			_savedAttachments.Should().Be(2);
		}

		private static string NoteId(ActionResult<SaveCallNoteResult> result) => ((result.Result as CreatedAtActionResult)?.Value as SaveCallNoteResult)?.Id;

		private static SaveCallNoteInput OutboxNoteInput(string idempotencyKey)
		{
			var input = NoteInput();
			input.IdempotencyKey = idempotencyKey;
			return input;
		}

		[Test]
		public async Task A_note_resent_from_an_app_outbox_with_its_body_key_returns_the_first_note_without_saving_again()
		{
			// Omni's offline outbox sends its event id as the body's IdempotencyKey.
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			var first = await NotesController().SaveCallNote(OutboxNoteInput("6f1c2a7e-outbox-note"), CancellationToken.None);
			var again = await NotesController().SaveCallNote(OutboxNoteInput("6f1c2a7e-outbox-note"), CancellationToken.None);

			NoteId(first).Should().Be("5");
			NoteId(again).Should().Be("5");
			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Once);
			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.Kind.Should().Be((int)InboundEmailKinds.ApiCallNote);
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task A_note_keyed_by_the_header_is_saved_once()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			await NotesController(idempotencyKey: "note-1").SaveCallNote(OutboxNoteInput("ignored-body-key"), CancellationToken.None);
			var again = await NotesController(idempotencyKey: "note-1").SaveCallNote(NoteInput(), CancellationToken.None);

			NoteId(again).Should().Be("5");
			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		/// <summary>The stored call with the notes and attachments the controller saved, as PopulateCallData reads them back.</summary>
		private void StoredCallData()
		{
			_callsService.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.Returns((Call call, bool a1, bool a2, bool a3, bool a4, bool a5, bool a6, bool a7, bool a8, bool a9, bool a10) =>
				{
					call.CallNotes = _savedNote != null ? new System.Collections.Generic.List<CallNote> { _savedNote } : new System.Collections.Generic.List<CallNote>();
					return Task.FromResult(call);
				});
			_callsService.Setup(x => x.GetCallAttachmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => _savedAttachment != null && _savedAttachment.CallAttachmentId == id ? _savedAttachment : null);
		}

		[Test]
		public async Task A_resend_after_the_note_was_saved_but_not_yet_protected_protects_that_note_instead_of_adding_one()
		{
			// The first answer is 503; the outbox retries it with the same key, which must protect the saved note, not add a second.
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			StoredCallData();
			_protectedWriteService.SetupSequence(x => x.PrepareCallNoteWriteAsync(It.IsAny<int>(), It.IsAny<CallNote>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = false, Reason = "broker_unavailable" })
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			var first = await NotesController().SaveCallNote(OutboxNoteInput("outbox-note-2"), CancellationToken.None);

			(first.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
			var receipt = _receipts.Receipts.Should().ContainSingle().Subject;
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Saved, "the request is not finished until the note is protected");
			receipt.EntityId.Should().Be(5);

			var again = await NotesController().SaveCallNote(OutboxNoteInput("outbox-note-2"), CancellationToken.None);

			NoteId(again).Should().Be("5");
			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Once);
			_protectedWriteService.Verify(x => x.PrepareCallNoteWriteAsync(DepartmentId, It.Is<CallNote>(n => n.CallNoteId == 5), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task A_resend_after_the_note_service_failed_past_its_insert_resumes_that_note()
		{
			// CallsService.SaveCallNoteAsync inserts the row, then its own protection step throws.
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			StoredCallData();
			_callsService.Setup(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()))
				.Returns((CallNote n, CancellationToken _) =>
				{
					n.CallNoteId = 5;
					_savedNote = n;
					throw new InvalidOperationException("Protected write blocked (broker_unavailable); call note 5 has transient plaintext pending re-encryption.");
				});

			Func<Task> first = () => NotesController().SaveCallNote(OutboxNoteInput("outbox-note-3"), CancellationToken.None);
			await first.Should().ThrowAsync<InvalidOperationException>();

			var again = await NotesController().SaveCallNote(OutboxNoteInput("outbox-note-3"), CancellationToken.None);

			NoteId(again).Should().Be("5");
			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Once);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task A_file_resend_after_it_was_saved_but_not_yet_protected_protects_that_file_instead_of_adding_one()
		{
			StoredCallData();
			_protectedWriteService.SetupSequence(x => x.PrepareCallAttachmentWriteAsync(It.IsAny<int>(), It.IsAny<CallAttachment>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = false, Reason = "broker_unavailable" })
				.ReturnsAsync(new ProtectedWriteResult { Success = true });

			var first = await FilesController(systemKey: true, idempotencyKey: "msg-2@cad.example#attachment-0").SaveCallFile(FileInput(), CancellationToken.None);

			(first.Result as ObjectResult)?.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Saved);

			var again = await FilesController(systemKey: true, idempotencyKey: "msg-2@cad.example#attachment-0").SaveCallFile(FileInput(), CancellationToken.None);

			Id(again).Should().Be("6");
			_savedAttachments.Should().Be(1);
			_protectedWriteService.Verify(x => x.PrepareCallAttachmentWriteAsync(DepartmentId, It.Is<CallAttachment>(a => a.CallAttachmentId == 6), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_receipts.Receipts.Single().State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task Unkeyed_notes_are_saved_every_time()
		{
			_authorizationService.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			await NotesController().SaveCallNote(NoteInput(), CancellationToken.None);
			await NotesController().SaveCallNote(NoteInput(), CancellationToken.None);

			_callsService.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
			_receipts.Receipts.Should().BeEmpty();
		}
	}
}
