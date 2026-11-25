using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using File = System.IO.File;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The protocol editor (New and Edit share it): triggers in UTC and by call type id, children updated in place by id, posted
	/// child ids of another protocol refused, a failed save keeping what the author built, the code and description rules, the
	/// upload limits, and the attachment download.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ProtocolsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "protocol-admin";

		private Mock<IProtocolsService> _protocolsService;
		private Mock<ICallsService> _callsService;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentsService> _departments;
		private Mock<IEventAggregator> _events;
		private Mock<IProtocolLibraryService> _library;
		private ProtocolsController _controller;

		[SetUp]
		public void SetUp()
		{
			_protocolsService = new Mock<IProtocolsService>();
			_callsService = new Mock<ICallsService>();
			_authorization = new Mock<IAuthorizationService>();
			_departments = new Mock<IDepartmentsService>();
			_events = new Mock<IEventAggregator>();
			// P1: New creates a draft protocol through the library (its first draft version holds the text).
			_library = new Mock<IProtocolLibraryService>();
			_library.Setup(x => x.GetSettingsAsync(DepartmentId)).ReturnsAsync(new ProtocolLibrarySettings());
			_library.Setup(x => x.UpdateLibraryFieldsAsync(It.IsAny<ProtocolReader>(), It.IsAny<int>(), It.IsAny<ProtocolLibraryFields>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolLifecycleResult());
			_library.Setup(x => x.SetAudienceAsync(It.IsAny<ProtocolReader>(), It.IsAny<int>(), It.IsAny<IEnumerable<IContentAudienceRule>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtocolLifecycleResult());
			_library.Setup(x => x.SaveDraftAsync(It.IsAny<ProtocolReader>(), It.IsAny<int>(), It.IsAny<ProtocolDraftInput>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtocolReader _, int id, ProtocolDraftInput input, CancellationToken __) => new ProtocolLifecycleResult
				{
					Version = new ProtocolVersion { ProtocolVersionId = 2, DispatchProtocolId = id, ContentHtml = input.ContentHtml, RowVersion = 1 }
				});
			CaptureCreated(_ => { });

			_callsService.Setup(x => x.GetCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 3, DepartmentId = DepartmentId, Name = "High" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 4, DepartmentId = DepartmentId, Name = "Gone", IsDeleted = true }
			});
			_callsService.Setup(x => x.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>
			{
				new CallType { CallTypeId = 7, DepartmentId = DepartmentId, Type = "Fire" }
			});
			_departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "Eastern Standard Time" });
			_authorization.Setup(x => x.CanUserModifyProtocolAsync(UserId, It.IsAny<int>())).ReturnsAsync(true);

			_protocolsService
				.Setup(service => service.SaveProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DispatchProtocol protocol, CancellationToken _) => protocol);

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			_controller = new ProtocolsController(_protocolsService.Object, _callsService.Object, _authorization.Object, _departments.Object, Mock.Of<IMappingService>(), _events.Object,
				library: _library.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
			_controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private void CaptureCreated(Action<DispatchProtocol> saved) =>
			_library.Setup(x => x.CreateAsync(It.IsAny<ProtocolCreateRequest>(), It.IsAny<CancellationToken>()))
				.Callback((ProtocolCreateRequest r, CancellationToken _) => saved(r.Protocol))
				.ReturnsAsync((ProtocolCreateRequest r, CancellationToken _) => new ProtocolLifecycleResult
				{
					Protocol = r.Protocol, Version = new ProtocolVersion { ProtocolVersionId = 1, ContentHtml = r.ContentHtml, RowVersion = 1 }
				});

		private static DispatchProtocol Existing() => new DispatchProtocol
		{
			DispatchProtocolId = 50,
			DepartmentId = DepartmentId,
			Name = "Cardiac",
			Code = "CARD",
			Triggers = new List<DispatchProtocolTrigger> { new DispatchProtocolTrigger { DispatchProtocolTriggerId = 11, DispatchProtocolId = 50, Type = 0, Priority = 3 } },
			Questions = new List<DispatchProtocolQuestion>
			{
				new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = 21, DispatchProtocolId = 50, Question = "Breathing?",
					Answers = new List<DispatchProtocolQuestionAnswer> { new DispatchProtocolQuestionAnswer { DispatchProtocolQuestionAnswerId = 31, DispatchProtocolQuestionId = 21, Answer = "No", Weight = 5 } }
				}
			},
			Attachments = new List<DispatchProtocolAttachment> { new DispatchProtocolAttachment { DispatchProtocolAttachmentId = 41, DispatchProtocolId = 50, FileName = "card.pdf" } }
		};

		[Test]
		public async Task New_saves_a_trigger_by_call_type_id_and_priority_with_its_window_in_utc()
		{
			DispatchProtocol saved = null;
			CaptureCreated(p => saved = p);

			var model = new ProtocolEditorModel
			{
				Name = "Structure fire",
				Code = "sog-204",
				Triggers = new List<ProtocolTriggerInput>
				{
					// 08:00 in New York on 1 July is 12:00 UTC (daylight time).
					new ProtocolTriggerInput { Key = "t1", Type = 2, Priority = 3, CallTypeId = 7, StartsOn = "2026-07-01T08:00" }
				}
			};

			var result = await _controller.New(model, null);

			// A new protocol is a draft: the author lands in its editor, not on a published page.
			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Edit");
			saved.Code.Should().Be("SOG-204");
			var trigger = saved.Triggers.Should().ContainSingle().Subject;
			trigger.Type.Should().Be(2);
			trigger.Priority.Should().Be(3);
			trigger.CallTypeId.Should().Be(7);
			trigger.CallType.Should().Be("Fire");
			trigger.StartsOn.Should().Be(new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc));
		}

		[Test]
		public async Task New_stores_no_type_as_a_null_id_not_a_label()
		{
			DispatchProtocol saved = null;
			CaptureCreated(p => saved = p);

			await _controller.New(new ProtocolEditorModel { Name = "Untyped", Code = "UT", Triggers = new List<ProtocolTriggerInput> { new ProtocolTriggerInput { Key = "t1", Type = 1 } } }, null);

			var trigger = saved.Triggers.Should().ContainSingle().Subject;
			trigger.CallTypeId.Should().BeNull();
			trigger.CallType.Should().BeNull();
			ProtocolTriggerMatcher.IsNoTypeTrigger(trigger).Should().BeTrue();
		}

		[Test]
		public async Task A_failed_save_keeps_the_triggers_and_questions_the_author_built()
		{
			var model = new ProtocolEditorModel
			{
				Name = "",
				Code = "X1",
				Triggers = new List<ProtocolTriggerInput> { new ProtocolTriggerInput { Key = "tnew1", Type = 0, Priority = 3 } },
				Questions = new List<ProtocolQuestionInput>
				{
					new ProtocolQuestionInput { Key = "qnew1", Text = "Conscious?", Answers = new List<ProtocolAnswerInput> { new ProtocolAnswerInput { Key = "anew1", Text = "Yes", Weight = 1 } } }
				}
			};

			var result = await _controller.New(model, null);

			var view = result.Should().BeOfType<ViewResult>().Subject;
			view.ViewName.Should().Be("New");
			var shown = view.Model.Should().BeOfType<ProtocolEditorModel>().Subject;
			shown.Triggers.Should().ContainSingle(t => t.Key == "tnew1" && t.Priority == 3);
			shown.Questions.Should().ContainSingle(q => q.Key == "qnew1" && q.Text == "Conscious?" && q.Answers.Single().Text == "Yes");
			_library.Verify(x => x.CreateAsync(It.IsAny<ProtocolCreateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase("09-E-01-ABCD/123", true)]
		[TestCase("09-E-01-ABCD/1234", false)]
		[TestCase("26A01", true)]
		[TestCase("<b>", false)]
		public async Task The_code_can_be_up_to_16_characters(string code, bool valid)
		{
			var result = await _controller.New(new ProtocolEditorModel { Name = "Determinant", Code = code }, null);

			if (valid)
				result.Should().BeOfType<RedirectToActionResult>();
			else
				result.Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task The_description_is_counted_on_its_text_not_its_markup()
		{
			var markup = "<p><strong><em><u>" + new string('a', 480) + "</u></em></strong></p>";
			(await _controller.New(new ProtocolEditorModel { Name = "Markup", Code = "MK", Description = markup }, null))
				.Should().BeOfType<RedirectToActionResult>("480 characters of text are within the limit, however much markup wraps them");

			(await _controller.New(new ProtocolEditorModel { Name = "Long", Code = "LG", Description = "<p>" + new string('a', 501) + "</p>" }, null))
				.Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task Edit_updates_children_in_place_by_id()
		{
			_protocolsService.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(Existing());
			DispatchProtocol edited = null;
			List<int> removed = null;
			_protocolsService.Setup(x => x.UpdateProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<IEnumerable<int>>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<DispatchProtocol, IEnumerable<int>, string, CancellationToken>((p, r, _, __) => { edited = p; removed = r.ToList(); })
				.ReturnsAsync((DispatchProtocol p, IEnumerable<int> _, string __, CancellationToken ___) => p);

			var model = new ProtocolEditorModel
			{
				Name = "Cardiac arrest",
				Code = "CARD",
				Triggers = new List<ProtocolTriggerInput> { new ProtocolTriggerInput { Key = "t11", Id = 11, Type = 0, Priority = 3 } },
				Questions = new List<ProtocolQuestionInput>
				{
					new ProtocolQuestionInput
					{
						Key = "q21", Id = 21, Text = "Breathing normally?",
						Answers = new List<ProtocolAnswerInput>
						{
							new ProtocolAnswerInput { Key = "a31", Id = 31, Text = "No", Weight = 10 },
							new ProtocolAnswerInput { Key = "anew", Text = "Unsure", Weight = 5 }
						}
					}
				},
				RemoveAttachmentIds = new List<int> { 41 }
			};

			var result = await _controller.Edit(50, model, null);

			result.Should().BeOfType<RedirectToActionResult>();
			edited.DispatchProtocolId.Should().Be(50);
			edited.Triggers.Single().DispatchProtocolTriggerId.Should().Be(11);
			edited.Questions.Single().DispatchProtocolQuestionId.Should().Be(21);
			edited.Questions.Single().Answers.Select(a => a.DispatchProtocolQuestionAnswerId).Should().Equal(31, 0);
			removed.Should().Equal(41);
			_events.Verify(x => x.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(e => e.Type == AuditLogTypes.ProtocolUpdated && e.DepartmentId == DepartmentId)), Times.Once);
		}

		[Test]
		public async Task Edit_refuses_a_child_id_that_is_not_the_protocols_own()
		{
			_protocolsService.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(Existing());

			var model = new ProtocolEditorModel
			{
				Name = "Cardiac",
				Code = "CARD",
				// Trigger 999 belongs to another protocol: posting it must not update that row by key.
				Triggers = new List<ProtocolTriggerInput> { new ProtocolTriggerInput { Key = "t999", Id = 999, Type = 0, Priority = 3 } }
			};

			var result = await _controller.Edit(50, model, null);

			result.Should().BeOfType<BadRequestResult>();
			_protocolsService.Verify(x => x.UpdateProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Edit_refuses_removing_an_attachment_of_another_protocol()
		{
			_protocolsService.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(Existing());

			var result = await _controller.Edit(50, new ProtocolEditorModel { Name = "Cardiac", Code = "CARD", RemoveAttachmentIds = new List<int> { 777 } }, null);

			result.Should().BeOfType<BadRequestResult>();
		}

		[Test]
		public async Task Edit_of_another_departments_protocol_is_unauthorized()
		{
			_authorization.Setup(x => x.CanUserModifyProtocolAsync(UserId, 50)).ReturnsAsync(false);

			// SecureBaseController's Unauthorized sends the member to the not-authorized page.
			(await _controller.Edit(50, new ProtocolEditorModel { Name = "x", Code = "X" }, null)).Should().BeOfType<RedirectResult>();
			_protocolsService.Verify(x => x.UpdateProtocolAsync(It.IsAny<DispatchProtocol>(), It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Uploads_are_read_whole_and_limited_in_count()
		{
			DispatchProtocol saved = null;
			CaptureCreated(p => saved = p);

			IFormFile File(string name, int size)
			{
				var bytes = Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray();
				return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "attachments", name) { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
			}

			await _controller.New(new ProtocolEditorModel { Name = "Files", Code = "F1" }, new List<IFormFile> { File("a.pdf", 70000), File("b.pdf", 12) });

			saved.Attachments.Select(a => a.Data.Length).Should().Equal(70000, 12);

			var tooMany = Enumerable.Range(0, ProtocolRules.MaxFilesPerSave + 1).Select(i => File(i + ".pdf", 10)).ToList();
			(await _controller.New(new ProtocolEditorModel { Name = "Files", Code = "F2" }, tooMany)).Should().BeOfType<ViewResult>();

			(await _controller.New(new ProtocolEditorModel { Name = "Files", Code = "F3" }, new List<IFormFile> { File("run.exe", 10) })).Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task An_unknown_attachment_is_a_404()
		{
			_protocolsService.Setup(x => x.GetAttachmentForDepartmentAsync(DepartmentId, 404)).ReturnsAsync((DispatchProtocolAttachment)null);

			(await _controller.GetProtocolAttachment(404)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Set_disabled_toggles_and_audits()
		{
			_protocolsService.Setup(x => x.GetProtocolHeaderByIdAsync(50)).ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 50, DepartmentId = DepartmentId });
			_protocolsService.Setup(x => x.SetProtocolDisabledAsync(50, true, UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DispatchProtocol { DispatchProtocolId = 50, DepartmentId = DepartmentId, IsDisabled = true });

			var result = await _controller.SetDisabled(50, true);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			_events.Verify(x => x.SendMessage(It.Is<Resgrid.Model.Events.AuditEvent>(e => e.Type == AuditLogTypes.ProtocolDisabled)), Times.Once);
		}

		[TestCase(typeof(ProtocolsController), "New")]
		[TestCase(typeof(ProtocolsController), "Edit")]
		[TestCase(typeof(ProtocolsController), "SetDisabled")]
		[TestCase(typeof(ProtocolsController), "Delete")]
		[TestCase(typeof(DispatchController), "RemoveCallProtocol")]
		public void Protocol_writes_are_posts_with_antiforgery(Type controller, string action)
		{
			var posts = controller.GetMethods().Where(m => m.Name == action && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any()).ToList();

			posts.Should().NotBeEmpty();
			posts.Should().OnlyContain(m => m.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).Any());
			controller.GetMethods().Where(m => m.Name == action && action != "New" && action != "Edit")
				.Should().OnlyContain(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any(), "a state change is never a GET");
		}

		[Test]
		public void Edit_and_disable_need_the_update_policy()
		{
			string Policy(string action, bool post) => typeof(ProtocolsController).GetMethods()
				.Where(m => m.Name == action && m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any() == post)
				.SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>())
				.Select(a => a.Policy).Single();

			Policy("Edit", false).Should().Be(ResgridResources.Protocol_Update);
			Policy("Edit", true).Should().Be(ResgridResources.Protocol_Update);
			Policy("SetDisabled", true).Should().Be(ResgridResources.Protocol_Update);
			Policy("GetProtocolAttachment", false).Should().Be(ResgridResources.Protocol_View);
		}

		[TestCase("Protocols", "Index.cshtml", "\"SetDisabled\", \"Protocols\"")]
		[TestCase("Protocols", "View.cshtml", "\"SetDisabled\", \"Protocols\"")]
		[TestCase("Protocols", "View.cshtml", "\"Delete\", \"Protocols\"")]
		[TestCase("Dispatch", "_CallProtocols.cshtml", "asp-action=\"RemoveCallProtocol\"")]
		public void Views_reach_protocol_writes_through_a_post_form(string folder, string view, string target)
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var source = File.ReadAllText(Path.Combine(root!.FullName, "Web", "Resgrid.Web", "Areas", "User", "Views", folder, view));

			var at = source.IndexOf(target, StringComparison.Ordinal);
			at.Should().BeGreaterThan(0, target);
			while (at >= 0)
			{
				var tagStart = source.LastIndexOf('<', at);
				source.Substring(tagStart, 5).Should().Be("<form", $"{target} in {folder}/{view} must be a POST form");
				at = source.IndexOf(target, at + target.Length, StringComparison.Ordinal);
			}
		}

		[Test]
		public void The_call_forms_never_preselect_an_answer_and_post_answer_ids()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var dispatch = Path.Combine(root!.FullName, "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "dispatch");
			var panel = File.ReadAllText(Path.Combine(dispatch, "resgrid.dispatch.protocolpanel.js"));

			panel.Should().Contain("'protocolAnswer_' + protocol.Id + '_' + q.Id", "answers post as answer ids per protocol and question");
			panel.Should().NotContain("selected=\"selected\"");
			panel.Should().Contain("(chosen[q.Id] === a.Id ? ' checked' : '')", "an answer is checked only once the dispatcher chose it");
			panel.Should().Contain("encodeURIComponent(options.getType", "the call type is URL-encoded in the lookup (D17)");

			foreach (var form in new[] { "resgrid.dispatch.newcall.js", "resgrid.dispatch.editcall.js", "resgrid.dispatch.addArchivedCall.js" })
			{
				var source = File.ReadAllText(Path.Combine(dispatch, form));
				source.Should().Contain("resgrid.dispatch.protocolpanel.init(", form);
				source.Should().NotContain("protocolCode_", form);
				source.Should().NotContain("pendingProtocol_", form);
			}
		}

		[Test]
		public async Task The_get_protocol_json_never_carries_attachment_bytes()
		{
			_authorization.Setup(x => x.CanUserViewProtocolAsync(UserId, 50)).ReturnsAsync(true);
			var protocol = Existing();
			protocol.Attachments.First().Data = new byte[] { 1, 2, 3, 4 };
			_protocolsService.Setup(x => x.GetProtocolByIdAsync(50)).ReturnsAsync(protocol);

			var json = (await _controller.GetProtocol(50)).Should().BeOfType<JsonResult>().Subject;

			json.Value.Should().BeOfType<ProtocolDetailJson>();
			Newtonsoft.Json.JsonConvert.SerializeObject(json.Value).Should().NotContain("AQIDBA==");
		}
	}
}
