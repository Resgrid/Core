using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Services.Models.v4.Trainings;
using Resgrid.Web.ServicesCore.Helpers;
using ApiTrainingReviewsController = Resgrid.Web.Services.Controllers.v4.TrainingReviewsController;

namespace Resgrid.Tests.TrainingSubmissions
{
	/// <summary>
	/// Round 9B: the Training reviews pages and the v4 endpoints Omni uses. Evidence is served inline (images sandboxed, PDFs framed only by the
	/// site, no sniffing) only to those the service allows; a member who reviews nobody lands on their own submissions; decisions and refusals
	/// come back as the service says; the v4 inbox carries the counts and evidence counts.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingReviewsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "reviewer";

		private Mock<ITrainingSubmissionService> _submissions;
		private Mock<ILearningRecordService> _records;
		private Mock<IDepartmentSettingsService> _settings;
		private TrainingReviewsController _controller;
		private ApiTrainingReviewsController _api;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_submissions = new Mock<ITrainingSubmissionService>();
			_records = new Mock<ILearningRecordService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(TrainingManagementScope.None(UserId, DepartmentId));
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "member", FirstName = "Morgan" } });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("TrainingReviewsControllerTests").Start();

			var text = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TrainingReviews.TrainingReviews>>();
			text.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key.StartsWith("learning_record_", StringComparison.Ordinal) ? "text:" + key : key));
			var trainings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings>>();
			trainings.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key, true));

			_controller = new TrainingReviewsController(_submissions.Object, _records.Object, authorization.Object, departments.Object, _settings.Object, text.Object, trainings.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
			_controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
			_api = new ApiTrainingReviewsController(_submissions.Object, _records.Object, _settings.Object, departments.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = httpContext.User } }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static LearningRecordFile Evidence(string name) => new LearningRecordFile
		{
			LearningRecordFileId = 5, DepartmentId = DepartmentId, UserId = "member", FileName = name, Data = Encoding.ASCII.GetBytes("%PDF-1.7")
		};

		[Test]
		public async Task A_photo_is_shown_inline_sandboxed_a_pdf_framed_by_the_site_and_other_types_download()
		{
			_submissions.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 5, UserId)).ReturnsAsync(Evidence("IMG_1.jpg"));
			var photo = await _controller.Evidence(5);
			photo.Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("image/jpeg");
			_controller.Response.Headers["Content-Disposition"].ToString().Should().StartWith("inline");
			_controller.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("sandbox").And.Contain("frame-ancestors 'self'");
			_controller.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");

			_controller.ControllerContext.HttpContext = new DefaultHttpContext { User = _controller.User };
			_submissions.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 5, UserId)).ReturnsAsync(Evidence("certificate.pdf"));
			(await _controller.Evidence(5)).Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("application/pdf");
			_controller.Response.Headers["Content-Security-Policy"].ToString().Should().Be("frame-ancestors 'self'", "a sandbox would stop the browser's PDF viewer");

			_controller.ControllerContext.HttpContext = new DefaultHttpContext { User = _controller.User };
			_submissions.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 5, UserId)).ReturnsAsync(Evidence("notes.docx"));
			(await _controller.Evidence(5)).Should().BeOfType<FileContentResult>().Which.FileDownloadName.Should().Be("notes.docx");

			_submissions.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 5, UserId)).ReturnsAsync((LearningRecordFile)null);
			(await _controller.Evidence(5)).Should().BeOfType<NotFoundResult>("only the member and their reviewers read evidence");
		}

		[Test]
		public async Task A_member_who_reviews_nobody_lands_on_their_own_submissions()
		{
			_submissions.Setup(x => x.GetInboxAsync(It.IsAny<TrainingReviewInboxQuery>())).ReturnsAsync(new TrainingReviewInbox { IsReviewer = false });

			(await _controller.Index()).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Mine");
		}

		[Test]
		public async Task Decisions_redirect_with_the_services_answer_and_refusals_are_shown_as_text()
		{
			_submissions.Setup(x => x.DenyAsync(DepartmentId, 9, UserId, "", It.IsAny<CancellationToken>())).ReturnsAsync(TrainingSubmissionOutcome.Fail("learning_record_reason_required"));
			var refused = await _controller.Deny(9, "", CancellationToken.None);
			refused.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Review");
			_controller.TempData["TrainingReviewsError"].Should().Be("text:learning_record_reason_required");

			_submissions.Setup(x => x.ApproveAsync(DepartmentId, 9, UserId, "ok", 2.5m, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingSubmissionOutcome { Record = new LearningRecord { LearningRecordId = 9 }, ReviewRequired = true });
			(await _controller.Approve(9, "ok", 2.5m, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			(await _controller.Approve(9, "ok", 5000m, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Review");
			_submissions.Verify(x => x.ApproveAsync(DepartmentId, 9, UserId, "ok", 5000m, It.IsAny<CancellationToken>()), Times.Never);

			_submissions.Setup(x => x.RequestInfoAsync(DepartmentId, 404, UserId, "x", It.IsAny<CancellationToken>())).ReturnsAsync(TrainingSubmissionOutcome.Fail("learning_record_not_found"));
			(await _controller.RequestInfo(404, "x", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task The_v4_inbox_carries_the_counts_and_the_evidence_counts_and_refusals_carry_their_code()
		{
			var record = new LearningRecord
			{
				LearningRecordId = 21, DepartmentId = DepartmentId, UserId = "member", Title = "Hazmat refresher", Hours = 8m, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission,
				Status = (int)LearningRecordStatuses.Pending, ReviewState = (int)TrainingSubmissionReviewStates.AwaitingReview, SubmittedOn = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)
			};
			_submissions.Setup(x => x.GetInboxAsync(It.Is<TrainingReviewInboxQuery>(q => q.DepartmentId == DepartmentId && q.ReviewerUserId == UserId))).ReturnsAsync(new TrainingReviewInbox
			{
				IsReviewer = true, AwaitingCount = 4, InfoRequestedCount = 1, Items = new List<LearningRecord> { record }, FileCounts = new Dictionary<int, int> { [21] = 3 }
			});

			var inbox = (await _api.GetReviewInbox()).Value;
			inbox.AwaitingCount.Should().Be(4);
			inbox.InfoRequestedCount.Should().Be(1);
			inbox.Data.Should().ContainSingle().Which.Should().Match<TrainingReviewItemData>(i => i.EvidenceFiles == 3 && i.MemberName == "Morgan " && i.Record.ReviewState == "AwaitingReview");

			_submissions.Setup(x => x.DenyAsync(DepartmentId, 21, UserId, null, It.IsAny<CancellationToken>())).ReturnsAsync(TrainingSubmissionOutcome.Fail("learning_record_reason_required"));
			var refused = await _api.DenySubmission(new ReviewTrainingSubmissionInput { Id = 21 }, CancellationToken.None);
			refused.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<TrainingSubmissionResult>().Which.Error.Should().Be("learning_record_reason_required");

			_submissions.Setup(x => x.ResubmitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TrainingSubmissionCorrection>(),
				It.IsAny<IReadOnlyList<TrainingEvidenceUpload>>(), It.IsAny<CancellationToken>())).ReturnsAsync(TrainingSubmissionOutcome.Fail("learning_record_resubmit_empty"));
			var badFile = await _api.RespondToSubmission(new RespondToTrainingSubmissionInput
			{
				Id = 21, Files = new List<TrainingEvidenceFileInput> { new TrainingEvidenceFileInput { FileName = "x.exe", Data = Convert.ToBase64String(new byte[] { 1 }) } }
			}, CancellationToken.None);
			badFile.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<TrainingSubmissionResult>().Which.Error.Should().Be("file_type_not_allowed");
			_submissions.Verify(x => x.ResubmitAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TrainingSubmissionCorrection>(),
				It.IsAny<IReadOnlyList<TrainingEvidenceUpload>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task The_dashboard_item_shows_the_count_waiting_and_the_members_own_requests_and_nothing_otherwise()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId });
			departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>());
			var component = new Resgrid.Web.ViewComponents.PendingTrainingReviewsViewComponent(_submissions.Object, departments.Object, _settings.Object)
			{
				ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
				{
					ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = _controller.HttpContext }
				},
				ViewEngine = Mock.Of<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>()
			};

			_submissions.Setup(x => x.GetInboxAsync(It.IsAny<TrainingReviewInboxQuery>())).ReturnsAsync(new TrainingReviewInbox { IsReviewer = false });
			_submissions.Setup(x => x.GetMySubmissionsAsync(DepartmentId, UserId)).ReturnsAsync(new List<LearningRecord>());
			(await component.InvokeAsync()).Should().BeOfType<Microsoft.AspNetCore.Mvc.ViewComponents.ContentViewComponentResult>()
				.Which.Content.Should().BeEmpty("nothing to review and nothing asked of the member");

			var waiting = new LearningRecord { LearningRecordId = 1, UserId = "member", SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, Status = 0, ReviewState = 0 };
			var asked = new LearningRecord { LearningRecordId = 2, UserId = UserId, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, Status = 0, ReviewState = 1, ReviewNote = "Page 2?" };
			_submissions.Setup(x => x.GetInboxAsync(It.Is<TrainingReviewInboxQuery>(q => q.ReviewerUserId == UserId && q.Take == 3)))
				.ReturnsAsync(new TrainingReviewInbox { IsReviewer = true, AwaitingCount = 7, Items = new List<LearningRecord> { waiting } });
			_submissions.Setup(x => x.GetMySubmissionsAsync(DepartmentId, UserId)).ReturnsAsync(new List<LearningRecord> { asked });

			var shown = await component.InvokeAsync();
			var model = shown.Should().BeOfType<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>().Which.ViewData.Model.Should().BeOfType<PendingTrainingReviewsView>().Subject;
			model.AwaitingCount.Should().Be(7, "the count badge is every submission waiting for this reviewer");
			model.Waiting.Should().ContainSingle();
			model.InfoNeeded.Should().ContainSingle().Which.ReviewNote.Should().Be("Page 2?");
		}

		[Test]
		public async Task The_module_switch_closes_the_v4_endpoints()
		{
			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { TrainingDisabled = true });

			(await _api.GetReviewInbox()).Result.Should().BeOfType<NotFoundResult>();
			(await _api.GetMySubmissions()).Result.Should().BeOfType<NotFoundResult>();
			(await _api.GetEvidenceFile(5)).Should().BeOfType<NotFoundResult>();
			_submissions.VerifyNoOtherCalls();
		}
	}
}
