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
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;
using Resgrid.Workers.Framework.Logic;
using File = System.IO.File;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Guards for round 3 A: Unified Search carries a protocol's file text only for the version in force, with the audience and status
	/// the query filters on (R-P4: a draft or retired protocol leaves the index even when its file has text); the must-read card is
	/// rendered on home and nowhere near the dispatch console, a call or a status change (R-P7); worker 86 runs only due-work steps and
	/// worker 83 feeds protocol acknowledgment from the shared audience queue (R-P3); a moved document's link redirects (R-P6).
	/// </summary>
	[TestFixture]
	public class ProtocolRound3GuardTests
	{
		#region Unified Search

		private static (SearchProjectionService Service, Mock<IProtocolFileTextsRepository> Texts) Projection(params ProtocolAudience[] audience)
		{
			var texts = new Mock<IProtocolFileTextsRepository>();
			texts.Setup(x => x.GetSearchTextAsync(1, 5, 50, It.IsAny<int>())).ReturnsAsync("Mayday crews call LUNAR from the extracted PDF page");
			var audiences = new Mock<IProtocolAudiencesRepository>();
			audiences.Setup(x => x.GetForProtocolAsync(1, 5)).ReturnsAsync(audience.ToList());
			return (new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<Resgrid.Model.Repositories.IProtocolVersionTranslationsRepository>(), protocolAudiences: audiences.Object,
				protocolFileTexts: texts.Object), texts);
		}

		private static DispatchProtocol Protocol(ProtocolStatuses status, int? currentVersion = 50) => new DispatchProtocol
		{
			DispatchProtocolId = 5, DepartmentId = 1, Name = "Mayday", Code = "SOG-204", Kind = (int)ProtocolKinds.Sog, Status = (int)status,
			CurrentVersionId = currentVersion, ProtocolText = ProtocolDocumentRules.StubHtml("Mayday", "SOG-204 Mayday.pdf")
		};

		[Test]
		public async Task A_published_document_protocol_is_found_by_its_file_text_with_its_audience_in_the_row()
		{
			var (service, _) = Projection(new ProtocolAudience { DepartmentId = 1, DispatchProtocolId = 5, Kind = (int)ContentAudienceKinds.Group, RefId = "6" });

			var projection = await service.BuildProtocolAsync(Protocol(ProtocolStatuses.Published));

			projection.SearchText.Should().Contain("LUNAR", "the extracted file text is searchable");
			var metadata = JsonConvert.DeserializeObject<Dictionary<string, string>>(projection.MetadataJson);
			metadata["Audience"].Should().Be("1:6", "the query filters file-text hits by the same audience as the body");
			metadata["Status"].Should().Be("Published");
			metadata["HasFileText"].Should().Be("true");
		}

		[TestCase(ProtocolStatuses.Draft)]
		[TestCase(ProtocolStatuses.Retired)]
		public async Task A_protocol_not_in_force_never_reaches_the_index_whatever_its_file_text(ProtocolStatuses status)
		{
			var (service, texts) = Projection();

			(await service.BuildProtocolAsync(Protocol(status))).Should().BeNull();
			texts.Verify(x => x.GetSearchTextAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>()), Times.Never,
				"a draft's file text is never read for the index");
		}

		[Test]
		public async Task The_projection_asks_only_for_the_text_of_the_version_in_force()
		{
			var (service, texts) = Projection();

			await service.BuildProtocolAsync(Protocol(ProtocolStatuses.Published, 50));

			texts.Verify(x => x.GetSearchTextAsync(1, 5, 50, It.IsAny<int>()), Times.Once);
		}

		[Test]
		public async Task A_document_moved_to_protocols_leaves_the_document_search()
		{
			var service = new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<Resgrid.Model.Repositories.IProtocolVersionTranslationsRepository>());
			var document = new Document { DocumentId = 7, DepartmentId = 1, Name = "Uniforms", MovedToProtocolId = 5, Data = Array.Empty<byte>() };

			(await service.BuildDocumentAsync(document)).Should().BeNull();
		}

		#endregion Unified Search

		#region Must-read placement (R-P7)

		private static DirectoryInfo Root()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln")))
				root = root.Parent;
			return root;
		}

		[Test]
		public void The_must_read_card_is_rendered_on_home_and_never_on_dispatch_calls_or_the_layout()
		{
			var web = Path.Combine(Root().FullName, "Web", "Resgrid.Web");
			var users = Directory.GetFiles(web, "*.cshtml", SearchOption.AllDirectories)
				.Where(f => File.ReadAllText(f).Contains("\"ProtocolMustRead\"", StringComparison.Ordinal))
				.Select(f => Path.GetRelativePath(web, f).Replace('\\', '/'))
				.OrderBy(f => f)
				.ToList();

			users.Should().BeEquivalentTo("Areas/User/Views/Home/Dashboard.cshtml", "Areas/User/Views/Home/DashboardModern.cshtml");
			users.Should().NotContain(f => f.Contains("/Dispatch/") || f.Contains("/Shared/_") || f.Contains("Layout") || f.Contains("/Personnel/") || f.Contains("/Units/"),
				"a bulletin never sits in front of the dispatch console, a call or a status change");
		}

		#endregion Must-read placement (R-P7)

		#region Workers

		[Test]
		public async Task Worker_86_runs_only_its_due_work_steps_and_one_failing_step_does_not_stop_the_rest()
		{
			var documents = new Mock<IProtocolDocumentService>(MockBehavior.Strict);
			documents.Setup(x => x.QueueAttachmentExtractionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
			documents.Setup(x => x.ProcessPendingExtractionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("disk"));
			var acknowledgments = new Mock<IProtocolAcknowledgmentService>(MockBehavior.Strict);
			acknowledgments.Setup(x => x.CompletePassedQuizzesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
			acknowledgments.Setup(x => x.SendRemindersAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);

			var result = await ProtocolAcknowledgmentLogic.Process(documents.Object, acknowledgments.Object, Mock.Of<IProtocolLearningExtensions>(), DateTime.UtcNow);

			result.AttachmentsQueued.Should().Be(2);
			result.TextsExtracted.Should().Be(0);
			result.QuizzesCompleted.Should().Be(1);
			result.RemindersSent.Should().Be(3);
			result.Failures.Should().Be(1);
			// Strict mocks: nothing else (no per-department sweep, no gate) was called.
			documents.VerifyAll();
			acknowledgments.VerifyAll();
		}

		[Test]
		public void Worker_86_logic_and_its_due_work_queries_never_touch_a_billing_or_entitlement_gate()
		{
			var root = Root().FullName;
			foreach (var file in new[]
			{
				"Workers/Resgrid.Workers.Framework/Logic/ProtocolAcknowledgmentLogic.cs",
				"Repositories/Resgrid.Repositories.DataRepository/ProtocolDocumentRepositories.cs"
			})
			{
				var text = File.ReadAllText(Path.Combine(root, file));
				text.Should().NotContain("ILmsAccessService").And.NotContain("ISubscriptionsService").And.NotContain("GetCurrentPlanForDepartment", file);
			}

			// The acknowledgment service's sweeps start from the due-work queries; its only gate call is in the LMS standards paths.
			var service = File.ReadAllText(Path.Combine(root, "Core/Resgrid.Services/ProtocolAcknowledgmentService.cs"));
			var reminders = Section(service, "public async Task<int> SendRemindersAsync", "private async Task<List<string>> SupervisorsAsync");
			reminders.Should().Contain("GetRemindersDueAsync").And.NotContain("_lms").And.NotContain("GetProtocolSummariesForDepartmentAsync");
			var quizzes = Section(service, "public async Task<int> CompletePassedQuizzesAsync", "#endregion Worker 86");
			quizzes.Should().Contain("GetAwaitingQuizAsync").And.NotContain("_lms");
		}

		private static string Section(string text, string from, string to)
		{
			var start = text.IndexOf(from, StringComparison.Ordinal);
			var end = text.IndexOf(to, start, StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, from);
			end.Should().BeGreaterThan(start, to);
			return text.Substring(start, end - start);
		}

		[Test]
		public async Task Worker_83_feeds_protocol_acknowledgment_from_the_same_audience_queue()
		{
			var changes = new Mock<IContentAudienceChangesRepository>();
			changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange>
			{
				new ContentAudienceChange { ContentAudienceChangeId = 1, DepartmentId = 1, UserId = "alice" },
				new ContentAudienceChange { ContentAudienceChangeId = 2, DepartmentId = 1, UserId = "ALICE" },
				new ContentAudienceChange { ContentAudienceChangeId = 3, DepartmentId = 2, UserId = null }
			});
			var assignments = new Mock<ITrainingAssignmentService>();
			assignments.Setup(x => x.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(0, 0));
			assignments.Setup(x => x.ReevaluateDepartmentAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(0, 0));
			var trainings = new Mock<ITrainingRepository>();
			trainings.Setup(x => x.GetTrainingsDueForRepeatAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<Training>());
			var acknowledgments = new Mock<IProtocolAcknowledgmentService>();
			acknowledgments.Setup(x => x.ReevaluateMemberAsync(1, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(2, 1));
			acknowledgments.Setup(x => x.ReevaluateDepartmentAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(3, 0));

			var result = await TrainingAssignmentLogic.Process(changes.Object, assignments.Object, trainings.Object, acknowledgments.Object, DateTime.UtcNow);

			acknowledgments.Verify(x => x.ReevaluateMemberAsync(1, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once,
				"one member, however their id is cased, is re-evaluated once");
			acknowledgments.Verify(x => x.ReevaluateDepartmentAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			result.AcknowledgmentsAdded.Should().Be(5);
			result.AcknowledgmentsWithdrawn.Should().Be(1);
			result.ChangesProcessed.Should().Be(3);
		}

		[Test]
		public async Task Worker_83_keeps_draining_the_queue_when_acknowledgment_fails()
		{
			var changes = new Mock<IContentAudienceChangesRepository>();
			changes.Setup(x => x.GetPendingAsync(It.IsAny<int>())).ReturnsAsync(new List<ContentAudienceChange> { new ContentAudienceChange { ContentAudienceChangeId = 1, DepartmentId = 1, UserId = "bob" } });
			var assignments = new Mock<ITrainingAssignmentService>();
			assignments.Setup(x => x.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(Tuple.Create(1, 0));
			var trainings = new Mock<ITrainingRepository>();
			trainings.Setup(x => x.GetTrainingsDueForRepeatAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<Training>());
			var acknowledgments = new Mock<IProtocolAcknowledgmentService>();
			acknowledgments.Setup(x => x.ReevaluateMemberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("db"));

			var result = await TrainingAssignmentLogic.Process(changes.Object, assignments.Object, trainings.Object, acknowledgments.Object, DateTime.UtcNow);

			result.Added.Should().Be(1, "the training side of the change still ran");
			result.Failures.Should().Be(1);
			changes.Verify(x => x.MarkProcessedAsync(It.Is<IEnumerable<int>>(ids => ids.Single() == 1), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion Workers

		#region Documents redirect (R-P6)

		private static Resgrid.Web.Areas.User.Controllers.DocumentsController DocumentsController(Document document)
		{
			var documents = new Mock<IDocumentsService>();
			documents.Setup(x => x.GetDocumentByIdAsync(document.DocumentId)).ReturnsAsync(document);
			var controller = new Resgrid.Web.Areas.User.Controllers.DocumentsController(Mock.Of<IDepartmentsService>(), documents.Object, Mock.Of<IEventAggregator>(),
				Mock.Of<IProtectedReadService>(), Mock.Of<IProtocolDocumentService>(), Mock.Of<IProtocolAccessService>());
			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, "user-1"),
				new Claim(ClaimTypes.PrimaryGroupSid, "1")
			}, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void KeepTheAccessor() => _previousAccessor = Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;

		[TearDown]
		public void RestoreTheAccessor() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		[Test]
		public async Task The_old_link_of_a_moved_document_redirects_to_its_protocol()
		{
			var moved = new Document { DocumentId = 7, DepartmentId = 1, Name = "Uniforms", MovedToProtocolId = 42, Data = Array.Empty<byte>(), UserId = "u" };
			var controller = DocumentsController(moved);

			foreach (var result in new[] { await controller.GetDocument(7), await controller.ViewDocument(7) })
			{
				var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
				redirect.ControllerName.Should().Be("Protocols");
				redirect.ActionName.Should().Be("View");
				redirect.RouteValues["id"].Should().Be(42);
			}
		}

		#endregion Documents redirect (R-P6)
	}
}
