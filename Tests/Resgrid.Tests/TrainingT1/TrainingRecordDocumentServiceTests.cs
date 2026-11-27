using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Localization.Areas.User.Trainings;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.ServicesCore.Helpers;
using ApiTrainingsController = Resgrid.Web.Services.Controllers.v4.TrainingsController;
using WebTrainingRecordsController = Resgrid.Web.Areas.User.Controllers.TrainingRecordsController;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>
	/// Round 10 (Omni): the member's own transcript and certificate PDFs outside the web page. Only the caller's own (verified) records print;
	/// the documents carry the web's labels in the asked language, the LMS verification code with its QR (lapse rule 9 for an old code), the
	/// department template, and are sealed under the same subject revision as the web download, so either host's sealed copy is the one the
	/// other serves again. The v4 routes answer 404 while the Trainings module is off.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingRecordDocumentServiceTests
	{
		private const int DepartmentId = 52;
		private const string Member = "doc-member";
		private const string Other = "doc-other";

		private Mock<ILearningRecordService> _records;
		private Mock<IDepartmentsService> _departments;
		private Mock<ITrainingCompletionRecordingService> _recordings;
		private Mock<ILmsAccessService> _lms;
		private Mock<ILmsTrainingService> _lmsTraining;
		private Mock<IDocumentSealingService> _sealing;
		private readonly List<SealHtmlRequest> _sealed = new List<SealHtmlRequest>();
		private string _previousBaseUrl;

		[SetUp]
		public void SetUp()
		{
			_sealed.Clear();
			_previousBaseUrl = SystemBehaviorConfig.ResgridBaseUrl;
			SystemBehaviorConfig.ResgridBaseUrl = "https://app.example.test";
			_records = new Mock<ILearningRecordService>();
			_records.Setup(r => r.GetCategoriesAsync(DepartmentId, true)).ReturnsAsync(new List<TrainingCategory>
			{
				new TrainingCategory { TrainingCategoryId = 4, DepartmentId = DepartmentId, Name = "Hazmat" }
			});
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Mesa Flats ERT", TimeZone = "UTC" });
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Member, FirstName = "Casey", LastName = "Member" },
				new PersonName { UserId = "officer", FirstName = "Ola", LastName = "Officer" }
			});
			_recordings = new Mock<ITrainingCompletionRecordingService>();
			_lms = new Mock<ILmsAccessService>();
			_lmsTraining = new Mock<ILmsTrainingService>();
			_sealing = new Mock<IDocumentSealingService>();
			_sealing.Setup(s => s.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SealHtmlRequest request, CancellationToken ct) =>
				{
					_sealed.Add(request);
					return new SealOutcome { Bytes = Encoding.UTF8.GetBytes(request.Html) };
				});
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.ResgridBaseUrl = _previousBaseUrl;
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private TrainingRecordDocumentService Service() =>
			new TrainingRecordDocumentService(_records.Object, _departments.Object, _recordings.Object, _lms.Object, _lmsTraining.Object, _sealing.Object);

		private static TrainingRecordDocumentRequest Request(string culture = "en", Func<string, string> qr = null) => new TrainingRecordDocumentRequest
		{
			DepartmentId = DepartmentId, UserId = Member, UserName = "casey", Culture = culture, QrDataUrl = qr
		};

		private static LearningRecord Record(int id, string userId = Member, int status = (int)LearningRecordStatuses.Verified, string code = null) => new LearningRecord
		{
			LearningRecordId = id, DepartmentId = DepartmentId, UserId = userId, Status = status, Title = "Confined space entry", CourseCode = "CSE-1",
			CompletedOn = new DateTime(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc), Hours = 4m, CeHours = 2m, CategoryId = 4, VerifiedByUserId = "officer",
			SourceKind = (int)LearningRecordSourceKinds.ManualCredit, VerificationCode = code
		};

		#region Certificate

		[Test]
		public async Task A_members_verified_record_prints_and_is_sealed_under_the_web_downloads_revision()
		{
			var record = Record(81);
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 81)).ReturnsAsync(record);

			var file = await Service().GetMyCertificatePdfAsync(Request(), 81);

			file.Should().NotBeNull();
			file.FileName.Should().Be("certificate-81.pdf");
			file.ContentType.Should().Be("application/pdf");
			var html = Encoding.UTF8.GetString(file.Bytes);
			html.Should().Contain("Casey Member").And.Contain("Confined space entry").And.Contain("Mesa Flats ERT").And.Contain("Ola Officer").And.Contain("Hazmat");
			var seal = _sealed.Should().ContainSingle().Subject;
			seal.SubjectKind.Should().Be(SignatureSubjectKinds.TrainingCertificate);
			seal.SubjectId.Should().Be("81");
			seal.RevisionSha256.Should().Be(TrainingDocuments.CertificateRevisionSha256(record, null))
				.And.Be(WebTrainingRecordsController.CertificateRevisionSha256(record, null), "the web download seals the same revision");
			seal.PageSize.Should().Be("Letter");
			seal.RequestedByUserId.Should().Be(Member);
			seal.LongTermSubject.Should().BeTrue();
		}

		[Test]
		public async Task Another_members_an_unverified_or_another_departments_record_does_not_print()
		{
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 82)).ReturnsAsync(Record(82, Other));
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 83)).ReturnsAsync(Record(83, status: (int)LearningRecordStatuses.Pending));
			var foreign = Record(84);
			foreign.DepartmentId = DepartmentId + 1;
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 84)).ReturnsAsync(foreign);

			(await Service().GetMyCertificatePdfAsync(Request(), 82)).Should().BeNull("reviewers print others' certificates on the web");
			(await Service().GetMyCertificatePdfAsync(Request(), 83)).Should().BeNull();
			(await Service().GetMyCertificatePdfAsync(Request(), 84)).Should().BeNull();
			(await Service().GetMyCertificatePdfAsync(Request(), 85)).Should().BeNull();
			(await Service().GetMyCertificatePdfAsync(new TrainingRecordDocumentRequest { DepartmentId = DepartmentId }, 81)).Should().BeNull();
			_sealed.Should().BeEmpty();
		}

		[Test]
		public async Task With_the_lms_add_on_the_certificate_carries_its_code_qr_and_the_default_template()
		{
			var record = Record(86);
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 86)).ReturnsAsync(record);
			_lms.Setup(l => l.CanAuthorAndManageAsync(DepartmentId)).ReturnsAsync(true);
			_lmsTraining.Setup(l => l.EnsureVerificationCodeAsync(DepartmentId, 86, It.IsAny<CancellationToken>())).ReturnsAsync("VC-86");
			_lmsTraining.Setup(l => l.GetCertificateTemplatesAsync(DepartmentId)).ReturnsAsync(new List<LmsCertificateTemplate>
			{
				new LmsCertificateTemplate { DepartmentId = DepartmentId, IsDefault = true, Body = "<h1>{{course}}</h1><p>{{verificationcode}}</p>{{qr}}" }
			});
			string asked = null;

			var file = await Service().GetMyCertificatePdfAsync(Request(qr: url => { asked = url; return "data:image/png;base64,QUJD"; }), 86);

			asked.Should().Be("https://app.example.test/verify/certificate/VC-86");
			var html = Encoding.UTF8.GetString(file.Bytes);
			html.Should().Contain("<h1>Confined space entry</h1>").And.Contain("VC-86").And.Contain("data:image/png;base64,QUJD");
			_sealed.Single().RevisionSha256.Should().Be(TrainingDocuments.CertificateRevisionSha256(record, "VC-86"));
		}

		[Test]
		public async Task After_a_lapse_an_earlier_code_keeps_printing_while_the_flag_is_on()
		{
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 87)).ReturnsAsync(Record(87, code: "OLD-87"));
			_lms.Setup(l => l.CanAuthorAndManageAsync(DepartmentId)).ReturnsAsync(false);
			_lms.Setup(l => l.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(true);

			Encoding.UTF8.GetString((await Service().GetMyCertificatePdfAsync(Request(), 87)).Bytes).Should().Contain("OLD-87");
			_lmsTraining.Verify(l => l.EnsureVerificationCodeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never, "no new code after a lapse");

			_lms.Setup(l => l.IsFlagEnabledAsync(DepartmentId)).ReturnsAsync(false);
			Encoding.UTF8.GetString((await Service().GetMyCertificatePdfAsync(Request(), 87)).Bytes).Should().NotContain("OLD-87");
		}

		[Test]
		public async Task The_certificate_prints_in_the_asked_language_on_a4()
		{
			_records.Setup(r => r.GetRecordAsync(DepartmentId, 88)).ReturnsAsync(Record(88));

			var file = await Service().GetMyCertificatePdfAsync(Request("de-AT"), 88);

			var german = TrainingTexts.Get("Doc.CertificateTitle", "de");
			german.Should().NotBe(TrainingTexts.Get("Doc.CertificateTitle", "en"));
			Encoding.UTF8.GetString(file.Bytes).Should().Contain(german);
			_sealed.Single().PageSize.Should().Be("A4");
			_sealed.Single().Culture.Should().Be("de");
		}

		[Test]
		public void Unknown_cultures_and_keys_fall_back_to_english_and_the_key()
		{
			TrainingTexts.Get("Doc.CertificateTitle", "xx").Should().Be(TrainingTexts.Get("Doc.CertificateTitle", "en"));
			TrainingTexts.Get("No.Such.Key", "de").Should().Be("No.Such.Key");
			TrainingTexts.Culture("pt-BR").Name.Should().Be("en");
			TrainingTexts.Culture("uk-UA").Name.Should().Be("uk");
		}

		#endregion Certificate

		#region Transcript

		[Test]
		public async Task The_transcript_prints_only_the_callers_records_and_seals_the_web_revision()
		{
			var mine = Record(90);
			var pending = Record(91, status: (int)LearningRecordStatuses.Pending);
			var theirs = Record(92, Other);
			_records.Setup(r => r.GetTranscriptAsync(DepartmentId, Member, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), false))
				.ReturnsAsync(new List<LearningRecord> { pending, theirs, mine });

			var file = await Service().GetMyTranscriptPdfAsync(Request(), new DateTime(2026, 10, 31), new DateTime(2026, 10, 1), 4, (int)LearningRecordSourceKinds.ManualCredit);

			file.FileName.Should().MatchRegex(@"^training-transcript-\d{4}-\d{2}-\d{2}\.pdf$");
			_records.Verify(r => r.GetTranscriptAsync(DepartmentId, Member, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
				It.Is<IEnumerable<int>>(c => c.SequenceEqual(new[] { 4 })), It.Is<IEnumerable<int>>(s => s.SequenceEqual(new[] { (int)LearningRecordSourceKinds.ManualCredit })), false), Times.Once,
				"the swapped window is put back in order and read in department time");
			var seal = _sealed.Single();
			seal.SubjectKind.Should().Be(SignatureSubjectKinds.TrainingTranscript);
			seal.SubjectId.Should().Be(Member);
			seal.Title.Should().Be("Casey Member");

			// The web's transcript document for the same records hashes the same.
			var html = Encoding.UTF8.GetString(file.Bytes);
			html.Should().Contain(TrainingTexts.Get("LearningStatus.Pending", "en")).And.Contain(TrainingTexts.Get("LearningSource.ManualCredit", "en"));
			html.Should().NotContain(Other);
		}

		[Test]
		public async Task An_unknown_source_filter_reads_every_source_and_a_blank_caller_prints_nothing()
		{
			_records.Setup(r => r.GetTranscriptAsync(DepartmentId, Member, null, null, null, null, false)).ReturnsAsync(new List<LearningRecord>());

			(await Service().GetMyTranscriptPdfAsync(Request(), null, null, null, 999)).Should().NotBeNull();
			_records.Verify(r => r.GetTranscriptAsync(DepartmentId, Member, null, null, null, null, false), Times.Once);

			(await Service().GetMyTranscriptPdfAsync(new TrainingRecordDocumentRequest { DepartmentId = DepartmentId, UserId = " " }, null, null, null, null)).Should().BeNull();
		}

		[Test]
		public async Task A_seal_without_bytes_is_no_document()
		{
			_records.Setup(r => r.GetTranscriptAsync(DepartmentId, Member, null, null, null, null, false)).ReturnsAsync(new List<LearningRecord>());
			_sealing.Setup(s => s.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SealOutcome());

			(await Service().GetMyTranscriptPdfAsync(Request(), null, null, null, null)).Should().BeNull();
		}

		#endregion Transcript

		#region v4 routes

		private ApiTrainingsController Api(Mock<ITrainingRecordDocumentService> documents, bool trainingDisabled = false, string acceptLanguage = null)
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { TrainingDisabled = trainingDisabled });
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			if (acceptLanguage != null)
				http.Request.Headers["Accept-Language"] = acceptLanguage;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new ApiTrainingsController(Mock.Of<ITrainingService>(), Mock.Of<ITrainingAssignmentService>(), Mock.Of<IMyTrainingService>(), Mock.Of<IAuthorizationService>(),
				settings.Object, Mock.Of<ILearningRecordService>(), Mock.Of<ITrainingCompletionRecordingService>(), Mock.Of<ITrainingSubmissionService>(), documents.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[Test]
		public async Task GetCertificate_serves_the_callers_pdf_in_the_app_language_and_says_whether_it_is_sealed()
		{
			var documents = new Mock<ITrainingRecordDocumentService>();
			TrainingRecordDocumentRequest asked = null;
			documents.Setup(d => d.GetMyCertificatePdfAsync(It.IsAny<TrainingRecordDocumentRequest>(), 81, It.IsAny<CancellationToken>()))
				.Callback((TrainingRecordDocumentRequest r, int id, CancellationToken ct) => asked = r)
				.ReturnsAsync(new TrainingRecordDocumentFile { Bytes = new byte[] { 1, 2 }, FileName = "certificate-81.pdf", Sealed = true });
			var api = Api(documents, acceptLanguage: "fr-CA,fr;q=0.9");

			var file = (await api.GetCertificate(81)).Should().BeOfType<FileContentResult>().Subject;

			file.ContentType.Should().Be("application/pdf");
			file.FileDownloadName.Should().Be("certificate-81.pdf");
			api.Response.Headers["X-Resgrid-Seal"].ToString().Should().Be("sealed");
			asked.UserId.Should().Be(Member, "only the caller's own");
			asked.DepartmentId.Should().Be(DepartmentId);
			asked.Culture.Should().Be("fr-CA");
			asked.QrDataUrl.Should().NotBeNull();
			asked.QrDataUrl("https://app.example.test/verify/certificate/X").Should().StartWith("data:image/png;base64,");

			(await Api(documents).GetCertificate(81, "de")).Should().BeOfType<FileContentResult>();
			asked.Culture.Should().Be("de", "an explicit locale wins");
		}

		[Test]
		public async Task GetTranscriptPdf_passes_the_window_and_filters_and_marks_a_pending_seal()
		{
			var documents = new Mock<ITrainingRecordDocumentService>();
			documents.Setup(d => d.GetMyTranscriptPdfAsync(It.Is<TrainingRecordDocumentRequest>(r => r.UserId == Member), new DateTime(2026, 1, 1), new DateTime(2026, 6, 30), 4, 2,
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingRecordDocumentFile { Bytes = new byte[] { 3 }, FileName = "training-transcript-2026-11-26.pdf", Sealed = false });
			var api = Api(documents);

			(await api.GetTranscriptPdf(new DateTime(2026, 1, 1), new DateTime(2026, 6, 30), 4, 2)).Should().BeOfType<FileContentResult>();
			api.Response.Headers["X-Resgrid-Seal"].ToString().Should().Be("pending");
		}

		[Test]
		public async Task The_downloads_are_404_while_the_module_is_off_or_there_is_nothing_to_print_and_503_when_rendering_fails()
		{
			var documents = new Mock<ITrainingRecordDocumentService>();
			(await Api(documents, trainingDisabled: true).GetCertificate(81)).Should().BeOfType<NotFoundResult>();
			(await Api(documents, trainingDisabled: true).GetTranscriptPdf()).Should().BeOfType<NotFoundResult>();
			documents.VerifyNoOtherCalls();

			(await Api(documents).GetCertificate(81)).Should().BeOfType<NotFoundResult>("not the caller's verified record");

			documents.Setup(d => d.GetMyTranscriptPdfAsync(It.IsAny<TrainingRecordDocumentRequest>(), null, null, null, null, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("pdf renderer down"));
			(await Api(documents).GetTranscriptPdf()).Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		#endregion v4 routes
	}
}
