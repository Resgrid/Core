using System;
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
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.WebCore.Areas.User.Models.Files;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// MVC call notes and call files: Add Call Data (Security &gt; Permissions) gates every write on top of the existing
	/// view/edit checks, and an uploaded call file records its uploader.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class CallDataWriteAuthorizationTests
	{
		private const int DepartmentId = 12;
		private const int CallId = 42;
		private const string UserId = "responder-1";

		private Mock<ICallsService> _calls;
		private Mock<IAuthorizationService> _authorization;

		[SetUp]
		public void SetUp()
		{
			_calls = new Mock<ICallsService>();
			_calls.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = DepartmentId });
			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewCallAsync(UserId, CallId)).ReturnsAsync(true);
			_authorization.Setup(x => x.CanUserEditCallAsync(UserId, CallId)).ReturnsAsync(true);
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		// SecureBaseController.Unauthorized() redirects to the access-denied page rather than returning a bare 401.

		private static ControllerContext Context()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new ControllerContext { HttpContext = http };
		}

		/// <summary>Builds a controller whose every constructor dependency is a loose mock, except the two under test.</summary>
		private T Build<T>() where T : Controller
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(ICallsService) ? _calls.Object :
				p.ParameterType == typeof(IAuthorizationService) ? (object)_authorization.Object :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = Context();
			return controller;
		}

		[Test]
		public async Task AddCallNote_is_refused_when_Add_Call_Data_denies_a_member_who_can_view_the_call()
		{
			_authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);

			var result = await Build<DispatchController>().AddCallNote(new AddCallNoteInput { CallId = CallId, Note = "Second alarm requested" }, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_calls.Verify(x => x.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task AddCallNote_saves_when_Add_Call_Data_allows()
		{
			_authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);

			var result = await Build<DispatchController>().AddCallNote(new AddCallNoteInput { CallId = CallId, Note = "Second alarm requested" }, CancellationToken.None);

			result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status200OK);
			_calls.Verify(x => x.SaveCallNoteAsync(It.Is<CallNote>(n => n.UserId == UserId && n.CallId == CallId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task AttachCallFile_is_refused_when_Add_Call_Data_denies_even_the_calls_editor()
		{
			_authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);

			var result = await Build<DispatchController>().AttachCallFile(new FileAttachInput { CallId = CallId }, null, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_calls.Verify(x => x.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("ViewCall.cshtml", "id=\"note-box-submit\"", "CanAddCallData")]
		[TestCase("ViewCall.cshtml", "id=\"note-box-submit1\"", "CanAddCallData")]
		[TestCase("ViewCall.cshtml", "id=\"note-container\"", "CanAddCallData")]
		[TestCase("ViewCall.cshtml", "@localizer[\"AddFile\"]</a>", "CanAttachCallFiles")]
		[TestCase("ViewCall.cshtml", "@localizer[\"AddImage\"]</a>", "CanAttachCallFiles")]
		[TestCase("CallData.cshtml", "id=\"note-box-submit\"", "CanAddCallData")]
		[TestCase("CallData.cshtml", "\"AttachCallFile\"", "CanAttachCallFiles")]
		public void Call_pages_render_note_and_attach_controls_only_inside_their_permission_guard(string view, string control, string guard)
		{
			// Denied members would only reach /Public/Unauthorized from these controls, so the views must not render them.
			var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(root!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", "Dispatch", view));

			var at = source.IndexOf(control, StringComparison.Ordinal);
			at.Should().BeGreaterThan(0, control);
			source.IndexOf(control, at + control.Length, StringComparison.Ordinal).Should().Be(-1, "each control renders once");

			var opening = source.LastIndexOf("@if (Model." + guard + ")", at, StringComparison.Ordinal);
			opening.Should().BeGreaterThan(0, $"{control} must sit inside @if (Model.{guard})");
			var between = source.Substring(opening, at - opening);
			(between.Count(c => c == '{') - between.Count(c => c == '}')).Should().BeGreaterThan(0, $"the {guard} block must still be open at {control}");
		}

		[TestCase(FileUploadTypes.CallFile)]
		[TestCase(FileUploadTypes.CallImage)]
		public async Task Files_upload_to_a_call_needs_Add_Call_Data_and_records_the_uploader(FileUploadTypes type)
		{
			IFormFile File()
			{
				var bytes = new byte[] { 1, 2, 3 };
				return new FormFile(new System.IO.MemoryStream(bytes), 0, bytes.Length, "fileToUpload", "scene.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
			}
			var model = new UploadFileView { Type = (int)type, ResourceId = CallId.ToString(), Name = "Scene" };

			_authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(false);
			(await Build<FilesController>().Upload(model, File(), CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_calls.Verify(x => x.SaveCallAttachmentAsync(It.IsAny<CallAttachment>(), It.IsAny<CancellationToken>()), Times.Never);

			_authorization.Setup(x => x.CanUserAddCallDataAsync(UserId, CallId, DepartmentId)).ReturnsAsync(true);
			(await Build<FilesController>().Upload(model, File(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			_calls.Verify(x => x.SaveCallAttachmentAsync(It.Is<CallAttachment>(a => a.UserId == UserId && a.CallId == CallId), It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
