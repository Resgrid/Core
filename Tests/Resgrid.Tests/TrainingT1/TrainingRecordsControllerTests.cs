using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Controllers;
using TrainingsResources = Resgrid.Localization.Areas.User.Trainings.Trainings;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>
	/// Trainings → Transcript and Records (Training T1): the module switch closes every page; department-wide record pages need
	/// the records right; the LMS pages (built dark) are NotFound unless the operator flag is on and the department may author LMS
	/// content; a certificate is printed only for a verified record the member owns or reviews; the public verify page 404s on an
	/// unknown code or a flag that is off (the service answers null).
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingRecordsControllerTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member";
		private const string OtherMember = "other";

		private Mock<ILearningRecordService> _records;
		private Mock<ITrainingHistoryImportService> _import;
		private Mock<ILmsAccessService> _lmsAccess;
		private Mock<ILmsTrainingService> _lms;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IPdfProvider> _pdf;
		private Mock<IDocumentSealingService> _sealing;
		private TrainingRecordsController _controller;

		[SetUp]
		public void SetUp()
		{
			_records = new Mock<ILearningRecordService>();
			_import = new Mock<ITrainingHistoryImportService>();
			_lmsAccess = new Mock<ILmsAccessService>();
			_lms = new Mock<ILmsTrainingService>();
			_authorization = new Mock<IAuthorizationService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_pdf = new Mock<IPdfProvider>();
			_sealing = new Mock<IDocumentSealingService>();
			// e-Signature S1: with sealing not configured the document comes back unsealed ("sealing pending").
			_sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<System.Threading.CancellationToken>()))
				.ReturnsAsync((SealHtmlRequest request, System.Threading.CancellationToken _) => new SealOutcome { Bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 } });

			ScopeIs(TrainingManagementScope.None(UserId, DepartmentId));

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var localizer = new Mock<IStringLocalizer<TrainingsResources>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_controller = new TrainingRecordsController(_records.Object, _import.Object, _lmsAccess.Object, _lms.Object, _authorization.Object,
				Mock.Of<IDepartmentsService>(), _settings.Object, Mock.Of<ICertificationService>(), _pdf.Object, _sealing.Object, localizer.Object,
				Mock.Of<ITrainingCompletionRecordingService>(), Mock.Of<ITrainingSubmissionService>())
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

		private void ScopeIs(TrainingManagementScope scope) =>
			_authorization.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(scope);

		/// <summary>The assign-and-report right held department-wide: CanManageRecords.</summary>
		private static TrainingManagementScope RecordsManager() => new TrainingManagementScope
		{
			UserId = UserId,
			DepartmentId = DepartmentId,
			Author = new TrainingRightScope { ManagesAll = true }
		};

		/// <summary>A group admin who reviews the records of one member only.</summary>
		private static TrainingManagementScope GroupAdminOf(params string[] members) => new TrainingManagementScope
		{
			UserId = UserId,
			DepartmentId = DepartmentId,
			Author = new TrainingRightScope { AdministeredMemberUserIds = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase) }
		};

		private static void ShouldBeRefused(IActionResult result) =>
			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");

		private static LearningRecord Record(int id, string userId, LearningRecordStatuses status) => new LearningRecord
		{
			LearningRecordId = id,
			DepartmentId = DepartmentId,
			UserId = userId,
			Title = "Pump operations",
			CompletedOn = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
			Hours = 3m,
			Status = (int)status
		};

		#region Module switch

		[Test]
		public async Task Every_records_page_is_closed_when_the_department_switched_the_Trainings_module_off()
		{
			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { TrainingDisabled = true });
			var context = new ActionExecutingContext(new ActionContext(_controller.HttpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
				new Dictionary<string, object>(), _controller);
			var ran = false;

			await _controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

			ran.Should().BeFalse();
			context.Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Records_pages_run_while_the_module_is_on()
		{
			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			var context = new ActionExecutingContext(new ActionContext(_controller.HttpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
				new Dictionary<string, object>(), _controller);
			var ran = false;

			await _controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

			ran.Should().BeTrue();
			_controller.ViewData["Scope"].Should().BeOfType<TrainingManagementScope>();
		}

		#endregion Module switch

		#region Department-wide pages need the records right

		[Test]
		public async Task A_member_without_records_rights_is_refused_every_department_wide_page()
		{
			ShouldBeRefused(await _controller.Queue());
			ShouldBeRefused(await _controller.Categories(null));
			ShouldBeRefused(await _controller.Categorize(null, null));
			ShouldBeRefused(await _controller.Credits(null));
			ShouldBeRefused(await _controller.Import());
			ShouldBeRefused(await _controller.ImportTemplate());
			ShouldBeRefused(await _controller.Backfill());
			ShouldBeRefused(await _controller.Settings());
			ShouldBeRefused(await _controller.Hours(null, null));
			ShouldBeRefused(await _controller.HoursCsv(null, null));
			ShouldBeRefused(await _controller.HoursPdf(null, null));

			_records.Verify(x => x.GetPendingAsync(It.IsAny<int>()), Times.Never);
			_records.Verify(x => x.GetHoursReportAsync(It.IsAny<TrainingHoursReportQuery>()), Times.Never);
			_records.Verify(x => x.GetLatestBackfillAsync(It.IsAny<int>()), Times.Never);
			_records.Verify(x => x.GetSettingsAsync(It.IsAny<int>()), Times.Never);
			_import.Verify(x => x.Template(), Times.Never);
		}

		[Test]
		public async Task A_member_without_records_rights_cannot_change_department_wide_records()
		{
			ShouldBeRefused(await _controller.Settings(15, 0, true, 7, 0, null, null, false, 0, CancellationToken.None));
			ShouldBeRefused(await _controller.RequestBackfill(false, CancellationToken.None));
			ShouldBeRefused(await _controller.SaveCategory(new TrainingCategoryInput { Name = "Hazmat" }, CancellationToken.None));
			ShouldBeRefused(await _controller.DeleteCategory(4, CancellationToken.None));
			ShouldBeRefused(await _controller.ApplyTemplate(TrainingCategoryTemplateCatalog.IsoFsrs, CancellationToken.None));
			ShouldBeRefused(await _controller.Categorize(4, "Pump operations", null, null, null, CancellationToken.None));

			_records.Verify(x => x.SaveSettingsAsync(It.IsAny<int>(), It.IsAny<TrainingRecordSettings>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_records.Verify(x => x.RequestBackfillAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_records.Verify(x => x.SaveCategoryAsync(It.IsAny<TrainingCategory>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_records.Verify(x => x.ApplyCategoryTemplateAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_group_admin_reviews_their_members_queue_but_not_the_department_wide_pages()
		{
			ScopeIs(GroupAdminOf(OtherMember));
			_records.Setup(x => x.GetPendingAsync(DepartmentId)).ReturnsAsync(new List<LearningRecord>
			{
				Record(1, OtherMember, LearningRecordStatuses.Pending),
				Record(2, "stranger", LearningRecordStatuses.Pending)
			});

			var model = (await _controller.Queue()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingRecordQueueView>().Subject;
			model.Pending.Select(r => r.LearningRecordId).Should().Equal(1);

			ShouldBeRefused(await _controller.Settings());
			ShouldBeRefused(await _controller.Hours(null, null));
			ShouldBeRefused(await _controller.Import());
		}

		[Test]
		public async Task Another_members_transcript_needs_the_right_to_review_them()
		{
			ShouldBeRefused(await _controller.Transcript(OtherMember, null, null, null, null));
			ShouldBeRefused(await _controller.TranscriptCsv(OtherMember, null, null, null, null));
			_records.Verify(x => x.GetTranscriptAsync(It.IsAny<int>(), OtherMember, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task An_officer_cannot_verify_a_record_of_a_member_they_do_not_review()
		{
			ScopeIs(GroupAdminOf(OtherMember));
			_records.Setup(x => x.GetRecordAsync(DepartmentId, 9)).ReturnsAsync(Record(9, "stranger", LearningRecordStatuses.Pending));

			ShouldBeRefused(await _controller.Verify(9, null, null, CancellationToken.None));
			_records.Verify(x => x.VerifyAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_service_refusal_code_comes_back_as_a_localized_error_on_the_queue()
		{
			ScopeIs(RecordsManager());
			_records.Setup(x => x.GetRecordAsync(DepartmentId, 9)).ReturnsAsync(Record(9, OtherMember, LearningRecordStatuses.Pending));
			_records.Setup(x => x.VerifyAsync(DepartmentId, 9, UserId, "ok", 2.5m, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("learning_record_self_review"));

			var result = await _controller.Verify(9, "ok", 2.5m, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Queue");
			_controller.TempData["TrainingRecordsError"].Should().Be("learning_record_self_review");
		}

		[Test]
		public async Task Applying_history_with_nothing_ticked_writes_nothing()
		{
			ScopeIs(RecordsManager());
			var claims = (ClaimsIdentity)_controller.HttpContext.User.Identity;
			claims.AddClaim(new Claim(Resgrid.Providers.Claims.ResgridClaimTypes.Resources.Department, Resgrid.Providers.Claims.ResgridClaimTypes.Actions.Update));
			var certifications = new Mock<ICertificationService>();
			certifications.Setup(x => x.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = 3, DepartmentId = DepartmentId, Type = "EMT" }
			});
			var localizer = new Mock<IStringLocalizer<TrainingsResources>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var controller = new TrainingRecordsController(_records.Object, _import.Object, _lmsAccess.Object, _lms.Object, _authorization.Object,
				Mock.Of<IDepartmentsService>(), _settings.Object, certifications.Object, _pdf.Object, _sealing.Object, localizer.Object,
				Mock.Of<ITrainingCompletionRecordingService>(), Mock.Of<ITrainingSubmissionService>())
			{
				ControllerContext = new ControllerContext { HttpContext = _controller.HttpContext }
			};
			controller.TempData = new TempDataDictionary(_controller.HttpContext, Mock.Of<ITempDataProvider>());

			var result = await controller.ApplyHistory(3, Array.Empty<int>(), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ApplyHistory");
			_records.Verify(x => x.ApplyHistoryCreditsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Department-wide pages need the records right

		#region LMS pages are built dark

		[Test]
		public async Task Lms_pages_are_not_found_while_the_operator_flag_is_off()
		{
			ScopeIs(RecordsManager());
			_lmsAccess.Setup(x => x.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);
			_lmsAccess.Setup(x => x.CanAuthorAndManageAsync(DepartmentId)).ReturnsAsync(true);

			await LmsPagesShouldBeNotFound();
		}

		[Test]
		public async Task Lms_pages_are_not_found_when_the_department_may_not_author_lms_content()
		{
			ScopeIs(RecordsManager());
			_lmsAccess.Setup(x => x.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_lmsAccess.Setup(x => x.CanAuthorAndManageAsync(DepartmentId)).ReturnsAsync(false);

			await LmsPagesShouldBeNotFound();
		}

		[Test]
		public async Task Lms_pages_are_not_found_for_a_member_without_records_rights_even_with_the_add_on()
		{
			_lmsAccess.Setup(x => x.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(true);
			_lmsAccess.Setup(x => x.CanAuthorAndManageAsync(DepartmentId)).ReturnsAsync(true);

			await LmsPagesShouldBeNotFound();
		}

		[Test]
		public async Task The_officer_strip_offers_lms_pages_only_when_the_lms_check_passes()
		{
			ScopeIs(RecordsManager());
			_lmsAccess.Setup(x => x.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);

			var model = (await _controller.Backfill()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingBackfillView>().Subject;

			model.OfficerTabs.Tabs.Select(t => t.Action).Should().Contain(new[] { "Queue", "Categories", "Settings" })
				.And.NotContain(new[] { "IsoReport", "NccpReport", "CertificateTemplates" });
		}

		private async Task LmsPagesShouldBeNotFound()
		{
			(await _controller.IsoReport(null, null)).Should().BeOfType<NotFoundResult>();
			(await _controller.IsoReport(null, null, false, "csv")).Should().BeOfType<NotFoundResult>();
			(await _controller.NccpReport()).Should().BeOfType<NotFoundResult>();
			(await _controller.NccpReport(false, "pdf")).Should().BeOfType<NotFoundResult>();
			(await _controller.CertificateTemplates(null)).Should().BeOfType<NotFoundResult>();
			(await _controller.SaveCertificateTemplate(new CertificateTemplateInput { Name = "Ours", Body = "<p>{{member}}</p>" }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
			(await _controller.DeleteCertificateTemplate(3, CancellationToken.None)).Should().BeOfType<NotFoundResult>();

			_lms.Verify(x => x.GetIsoFsrsReportAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>()), Times.Never);
			_lms.Verify(x => x.GetNccpProgressAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<DateTime>()), Times.Never);
			_lms.Verify(x => x.GetCertificateTemplatesAsync(It.IsAny<int>()), Times.Never);
			_lms.Verify(x => x.SaveCertificateTemplateAsync(It.IsAny<LmsCertificateTemplate>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_lms.Verify(x => x.DeleteCertificateTemplateAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion LMS pages are built dark

		#region Certificates

		[Test]
		public async Task A_certificate_is_not_found_for_an_unknown_record()
		{
			(await _controller.Certificate(5)).Should().BeOfType<NotFoundResult>();
		}

		[TestCase(LearningRecordStatuses.Pending)]
		[TestCase(LearningRecordStatuses.Rejected)]
		[TestCase(LearningRecordStatuses.Voided)]
		public async Task A_certificate_is_not_found_for_a_record_that_is_not_verified(LearningRecordStatuses status)
		{
			_records.Setup(x => x.GetRecordAsync(DepartmentId, 5)).ReturnsAsync(Record(5, UserId, status));

			(await _controller.Certificate(5)).Should().BeOfType<NotFoundResult>();
			_lmsAccess.Verify(x => x.CanAuthorAndManageAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_certificate_is_not_found_for_another_members_record_the_member_does_not_review()
		{
			_records.Setup(x => x.GetRecordAsync(DepartmentId, 5)).ReturnsAsync(Record(5, OtherMember, LearningRecordStatuses.Verified));

			(await _controller.Certificate(5)).Should().BeOfType<NotFoundResult>();
			_lms.Verify(x => x.EnsureVerificationCodeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_certificate_is_not_found_for_a_record_of_another_department()
		{
			var foreign = Record(5, UserId, LearningRecordStatuses.Verified);
			foreign.DepartmentId = 99;
			_records.Setup(x => x.GetRecordAsync(DepartmentId, 5)).ReturnsAsync(foreign);

			(await _controller.Certificate(5)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Evidence_is_refused_to_a_member_who_neither_owns_nor_reviews_it()
		{
			_records.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 8)).ReturnsAsync(new LearningRecordFile
			{
				LearningRecordFileId = 8, DepartmentId = DepartmentId, UserId = OtherMember, FileName = "card.pdf", Data = new byte[] { 1 }
			});

			ShouldBeRefused(await _controller.Evidence(8));
		}

		[Test]
		public async Task Evidence_downloads_for_its_owner_as_an_attachment_with_a_safe_type()
		{
			_records.Setup(x => x.GetEvidenceFileAsync(DepartmentId, 8)).ReturnsAsync(new LearningRecordFile
			{
				LearningRecordFileId = 8, DepartmentId = DepartmentId, UserId = UserId, FileName = "card.pdf", ContentType = "text/html", Data = new byte[] { 1, 2 }
			});

			var file = (await _controller.Evidence(8)).Should().BeOfType<FileContentResult>().Subject;
			file.ContentType.Should().Be("application/pdf");
			file.FileDownloadName.Should().Be("card.pdf");
		}

		#endregion Certificates

		#region Public verify page

		[Test]
		public async Task The_public_verify_page_is_not_found_when_the_service_has_no_answer()
		{
			var lms = new Mock<ILmsTrainingService>();
			lms.Setup(x => x.VerifyCertificateAsync(It.IsAny<string>())).ReturnsAsync((CertificateVerification)null);
			var controller = new CertificateVerifyController(lms.Object);

			(await controller.Index("ABCD-1234")).Should().BeOfType<NotFoundResult>();
			lms.Verify(x => x.VerifyCertificateAsync("ABCD-1234"), Times.Once);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("<script>")]
		[TestCase("a/b")]
		public async Task The_public_verify_page_is_not_found_for_a_malformed_code(string code)
		{
			var lms = new Mock<ILmsTrainingService>();
			var controller = new CertificateVerifyController(lms.Object);

			(await controller.Index(code)).Should().BeOfType<NotFoundResult>();
			lms.Verify(x => x.VerifyCertificateAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task The_public_verify_page_shows_the_services_answer()
		{
			var answer = new CertificateVerification { MemberName = "Pat Doe", Course = "Pump operations", DepartmentName = "Station 1", Status = LearningRecordStatuses.Verified };
			var lms = new Mock<ILmsTrainingService>();
			lms.Setup(x => x.VerifyCertificateAsync("ABCD-1234")).ReturnsAsync(answer);
			var controller = new CertificateVerifyController(lms.Object);

			(await controller.Index("ABCD-1234")).Should().BeOfType<ViewResult>().Which.Model.Should().BeSameAs(answer);
		}

		[Test]
		public async Task Security_review_L4_the_public_verify_page_is_rate_limited_per_address()
		{
			var lms = new Mock<ILmsTrainingService>();
			lms.Setup(x => x.VerifyCertificateAsync(It.IsAny<string>())).ReturnsAsync((CertificateVerification)null);
			CertificateVerifyController From(string ip)
			{
				var http = new DefaultHttpContext();
				http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
				return new CertificateVerifyController(lms.Object) { ControllerContext = new ControllerContext { HttpContext = http } };
			}
			// A unique address per run: the limiter is process-wide.
			var address = "198.51.100." + (1 + Random.Shared.Next(250));
			var other = "203.0.113." + (1 + Random.Shared.Next(250));

			for (var i = 0; i < CertificateVerifyController.RequestsPerMinute; i++)
				(await From(address).Index("ABCD-" + i.ToString("D4"))).Should().BeOfType<NotFoundResult>();

			(await From(address).Index("ABCD-9999")).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(429);
			(await From(other).Index("ABCD-9999")).Should().BeOfType<NotFoundResult>("another address has its own allowance");
			lms.Verify(x => x.VerifyCertificateAsync("ABCD-9999"), Times.Once, "the limited request never reaches the lookup");
		}

		#endregion Public verify page
	}
}
