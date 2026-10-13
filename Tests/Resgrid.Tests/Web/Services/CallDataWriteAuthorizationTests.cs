using System;
using System.Diagnostics;
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

		private CallNotesController NotesController() => new CallNotesController(_callsService.Object, Mock.Of<IDepartmentsService>(),
			Mock.Of<IProtectedReadService>(), _protectedWriteService.Object, _authorizationService.Object)
		{
			ControllerContext = new ControllerContext { HttpContext = Context() }
		};

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
	}
}
