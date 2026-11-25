using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Controllers;
using TrainingsResources = Resgrid.Localization.Areas.User.Trainings.Trainings;

namespace Resgrid.Tests.LmsBilling
{
	/// <summary>
	/// General availability is the operator switching Training.Lms on, not code (§25.4a item 7). With the real gate behind them, the
	/// wave-2 dark features (ISO FSRS and NREMT NCCP reports, custom certificate templates) appear and work when the flag and the
	/// add-on are on and 404 when either is off; the public verify page keeps answering for certificates already issued after the
	/// add-on lapses (lapse rule 9), and only the operator flag hides it. Members without a seat show as not licensed in the reports.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LmsDarkFeaturesGoLiveTests
	{
		private const int DepartmentId = 91;
		private const string OfficerId = "officer";

		private bool _flagOn;
		private List<PaymentAddon> _payments;
		private LmsAddonState _state;
		private LmsAccessService _gate;
		private Mock<ILmsTrainingService> _lms;
		private string _billingUrl, _billingKey;

		[SetUp]
		public void SetUp()
		{
			_billingUrl = SystemBehaviorConfig.BillingApiBaseUrl;
			_billingKey = ApiConfig.BackendInternalApikey;
			SystemBehaviorConfig.BillingApiBaseUrl = "https://billing.example.invalid";
			ApiConfig.BackendInternalApikey = "unit-test-only";

			_flagOn = true;
			_payments = new List<PaymentAddon>();
			_state = null;
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.EvaluateFreshAsync(FeatureFlagKeys.TrainingLms, DepartmentId)).ReturnsAsync(() => new FeatureFlagEvaluation { IsEnabled = _flagOn });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			var billing = new Mock<ISubscriptionsService>();
			billing.Setup(s => s.GetAllAddonPlansByTypeAsync(PlanAddonTypes.Lms, false))
				.ReturnsAsync(new List<PlanAddon> { new PlanAddon { PlanAddonId = LmsAddonConfig.PlanAddonId, AddonType = (int)PlanAddonTypes.Lms } });
			billing.Setup(s => s.GetCurrentPaymentAddonsForDepartmentAsync(DepartmentId, It.IsAny<List<string>>())).ReturnsAsync(() => _payments);
			var states = new Mock<ILmsAddonStatesRepository>();
			states.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(() => _state);
			states.Setup(s => s.SaveForDepartmentAsync(It.IsAny<LmsAddonState>(), It.IsAny<CancellationToken>()))
				.Callback<LmsAddonState, CancellationToken>((s, _) => _state = s).Returns(Task.CompletedTask);
			var lapses = new Mock<ILmsAddonLapsesRepository>();
			lapses.Setup(l => l.OpenAsync(It.IsAny<LmsAddonLapse>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var seats = new Mock<ILmsMemberSeatsRepository>();
			seats.Setup(s => s.GetForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<LmsMemberSeat>());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
			_gate = new LmsAccessService(flags.Object, settings.Object, billing.Object, states.Object, lapses.Object, seats.Object, departments.Object);

			_lms = new Mock<ILmsTrainingService>();
			_lms.Setup(x => x.GetNccpProgressAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<DateTime>())).ReturnsAsync(new NccpProgressReport());
			_lms.Setup(x => x.GetCertificateTemplatesAsync(DepartmentId)).ReturnsAsync(new List<LmsCertificateTemplate>());
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.BillingApiBaseUrl = _billingUrl;
			ApiConfig.BackendInternalApikey = _billingKey;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private void AddonRunning() => _payments.Add(new PaymentAddon
		{
			DepartmentId = DepartmentId, PlanAddonId = LmsAddonConfig.PlanAddonId, TransactionId = "in_paid", Quantity = 2,
			PurchaseOn = DateTime.UtcNow.AddDays(-5), EffectiveOn = DateTime.UtcNow.AddDays(-5), EndingOn = DateTime.UtcNow.AddDays(25)
		});

		private void AddonLapsed() => _state = new LmsAddonState
		{
			DepartmentId = DepartmentId, Packs = 2, PaidThroughUtc = DateTime.UtcNow.AddDays(-40), RenewalCancelled = true, UpdatedOn = DateTime.UtcNow.AddDays(-41)
		};

		private TrainingRecordsController Controller(ILearningRecordService records = null, IPdfProvider pdf = null)
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, OfficerId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(x => x.GetTrainingManagementScopeAsync(OfficerId, DepartmentId)).ReturnsAsync(new TrainingManagementScope
			{
				UserId = OfficerId, DepartmentId = DepartmentId, Author = new TrainingRightScope { ManagesAll = true }
			});
			var localizer = new Mock<IStringLocalizer<TrainingsResources>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 9", TimeZone = "Eastern Standard Time" });

			// e-Signature S1: unconfigured sealing hands back the rendered PDF unsealed; route it through the PDF provider so the HTML can be read.
			var renderer = pdf ?? Mock.Of<IPdfProvider>();
			var sealing = new Mock<IDocumentSealingService>();
			sealing.Setup(x => x.SealHtmlAsync(It.IsAny<SealHtmlRequest>(), It.IsAny<System.Threading.CancellationToken>()))
				.ReturnsAsync((SealHtmlRequest request, System.Threading.CancellationToken _) => new SealOutcome { Bytes = renderer.ConvertHtmlToPdf(request.Html) ?? new byte[] { 1 } });
			var controller = new TrainingRecordsController(records ?? Mock.Of<ILearningRecordService>(), Mock.Of<ITrainingHistoryImportService>(), _gate, _lms.Object,
				authorization.Object, departments.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<ICertificationService>(), renderer, sealing.Object, localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
			controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
			return controller;
		}

		[Test]
		public async Task With_the_flag_and_the_add_on_on_the_dark_features_appear_and_work()
		{
			AddonRunning();

			(await Controller().NccpReport()).Should().BeOfType<ViewResult>();
			(await Controller().CertificateTemplates(null)).Should().BeOfType<ViewResult>();
			_lms.Verify(x => x.GetNccpProgressAsync(DepartmentId, false, It.IsAny<DateTime>()), Times.Once);
		}

		[Test]
		public async Task With_the_flag_off_the_dark_routes_are_not_found()
		{
			AddonRunning();
			_flagOn = false;

			(await Controller().NccpReport()).Should().BeOfType<NotFoundResult>();
			(await Controller().IsoReport(null, null)).Should().BeOfType<NotFoundResult>();
			(await Controller().CertificateTemplates(null)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task With_the_flag_on_but_no_add_on_the_dark_routes_are_not_found()
		{
			(await Controller().NccpReport()).Should().BeOfType<NotFoundResult>();
			(await Controller().IsoReport(null, null)).Should().BeOfType<NotFoundResult>();
			(await Controller().CertificateTemplates(null)).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task After_a_lapse_the_dark_routes_close_again()
		{
			AddonLapsed();

			(await Controller().NccpReport()).Should().BeOfType<NotFoundResult>();
			_lms.Verify(x => x.GetNccpProgressAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<DateTime>()), Times.Never);
		}

		private LmsTrainingService RealLmsTraining(LearningRecord issued, ILmsSeatService seats = null)
		{
			var records = new Mock<ILearningRecordsRepository>();
			records.Setup(r => r.GetByVerificationCodeAsync(issued.VerificationCode)).ReturnsAsync(issued);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 9" });
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "member", FirstName = "Pat", LastName = "Doe" } });
			return new LmsTrainingService(records.Object, Mock.Of<ITrainingCategoriesRepository>(), Mock.Of<ILmsCertificateTemplatesRepository>(), Mock.Of<ICertificationService>(),
				Mock.Of<IPersonnelCertificationRepository>(), departments.Object, _gate, seats ?? Mock.Of<ILmsSeatService>());
		}

		private static LearningRecord Issued() => new LearningRecord
		{
			LearningRecordId = 5, DepartmentId = DepartmentId, UserId = "member", Title = "Pump operations", Status = (int)LearningRecordStatuses.Verified,
			CompletedOn = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), VerificationCode = "ABCDEFGH23"
		};

		[Test]
		public async Task The_public_verify_page_keeps_working_for_issued_certificates_after_a_lapse()
		{
			AddonLapsed();
			(await _gate.CanAuthorAndManageAsync(DepartmentId)).Should().BeFalse("the add-on has lapsed");

			var controller = new CertificateVerifyController(RealLmsTraining(Issued()));
			var view = (await controller.Index("ABCDEFGH23")).Should().BeOfType<ViewResult>().Subject;

			view.Model.Should().BeOfType<CertificateVerification>().Which.Course.Should().Be("Pump operations", "lapse rule 9: evidence stays public");
		}

		[Test]
		public async Task A_certificate_issued_with_a_code_keeps_printing_it_after_a_lapse_and_no_new_code_is_issued()
		{
			AddonLapsed();
			var record = Issued();
			record.UserId = OfficerId;
			var records = new Mock<ILearningRecordService>();
			records.Setup(r => r.GetRecordAsync(DepartmentId, 5)).ReturnsAsync(record);
			string html = null;
			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((h, _) => html = h).Returns(new byte[] { 1 });
			pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Callback<string>(h => html = h).Returns(new byte[] { 1 });

			(await Controller(records.Object, pdf.Object).Certificate(5)).Should().BeOfType<FileContentResult>();

			html.Should().Contain("ABCDEFGH23", "lapse rule 9: the certificate the member already holds keeps its verification code and QR");
			_lms.Verify(x => x.EnsureVerificationCodeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_lms.Verify(x => x.GetCertificateTemplatesAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Only_the_operator_flag_hides_the_public_verify_page()
		{
			AddonRunning();
			_flagOn = false;

			var controller = new CertificateVerifyController(RealLmsTraining(Issued()));

			(await controller.Index("ABCDEFGH23")).Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Reports_show_members_without_a_seat_as_not_licensed()
		{
			AddonRunning();
			var records = new Mock<ILearningRecordsRepository>();
			records.Setup(r => r.QueryAsync(It.IsAny<LearningRecordQuery>())).ReturnsAsync(new List<LearningRecord>
			{
				new LearningRecord { DepartmentId = DepartmentId, UserId = "seated", CategoryId = 1, Hours = 3m, Status = (int)LearningRecordStatuses.Verified },
				new LearningRecord { DepartmentId = DepartmentId, UserId = "free", CategoryId = 1, Hours = 2m, Status = (int)LearningRecordStatuses.Verified }
			});
			var categories = new Mock<ITrainingCategoriesRepository>();
			categories.Setup(c => c.GetForDepartmentAsync(DepartmentId, false)).ReturnsAsync(new List<TrainingCategory>
				{ new TrainingCategory { TrainingCategoryId = 1, DepartmentId = DepartmentId, Code = "ISO-FAC", Name = "Facility", Kind = (int)TrainingCategoryKinds.IsoFsrs } });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(new[] { "seated", "free" }, StringComparer.OrdinalIgnoreCase));
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>());
			var seats = new Mock<ILmsSeatService>();
			seats.Setup(s => s.GetLicensedUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string>(new[] { "SEATED" }, StringComparer.OrdinalIgnoreCase));
			var service = new LmsTrainingService(records.Object, categories.Object, Mock.Of<ILmsCertificateTemplatesRepository>(), Mock.Of<ICertificationService>(),
				Mock.Of<IPersonnelCertificationRepository>(), departments.Object, _gate, seats.Object);

			var report = await service.GetIsoFsrsReportAsync(DepartmentId, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, false);

			report.Rows.Should().ContainSingle(r => r.UserId == "seated" && !r.NotLicensed);
			report.Rows.Should().ContainSingle(r => r.UserId == "free" && r.NotLicensed, "their hours are their own evidence; the row is marked, not dropped");
		}
	}
}
