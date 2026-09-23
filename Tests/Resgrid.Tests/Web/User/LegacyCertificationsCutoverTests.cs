using System;
using System.Collections.Generic;
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
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Certifications;
using Resgrid.Web.Areas.User.Models.Profile;
using Resgrid.Web.Areas.User.Models.Types;
using Resgrid.Web.Helpers;
using Resgrid.Workers.Framework.Logic;
using Resgrid.Workers.Framework.Workers.ReportDelivery;

namespace Resgrid.Tests.Web.User
{
    [TestFixture, NonParallelizable]
    public class LegacyCertificationsCutoverTests
    {
        private const int DepartmentId = 77;
        private const string UserId = "member";
        private IHttpContextAccessor _previousAccessor;
        private DefaultHttpContext _http;
        private Mock<IBusinessOperationsAccessService> _access;
        private Mock<IAuthorizationService> _authorization;

        [SetUp]
        public void SetUp()
        {
            _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
            _http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
            ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
            _access = new Mock<IBusinessOperationsAccessService>(MockBehavior.Strict);
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(true);
            _authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
            _authorization.Setup(x => x.CanUserEditProfileAsync(UserId, DepartmentId, UserId)).ReturnsAsync(true);
        }
        [TearDown]
        public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

        private T Controller<T>(params object[] dependencies) where T : Controller
        {
            var supplied = dependencies.Concat(new object[] { _access.Object, _authorization.Object }).ToArray();
            var constructor = typeof(T).GetConstructors().Single();
            var controller = (T)constructor.Invoke(constructor.GetParameters().Select(p => supplied.FirstOrDefault(p.ParameterType.IsInstanceOfType)).ToArray());
            controller.ControllerContext = new ControllerContext { HttpContext = _http };
            controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
            return controller;
        }

        [TestCase(true, false, true)] [TestCase(false, false, false)] [TestCase(true, true, false)]
        [TestCase(null, false, false)] [TestCase(true, null, false)]
        public async Task Cutover_uses_fresh_flag_and_module_without_paid_entitlement(bool? flag, bool? disabled, bool expected)
        {
            var flags = new Mock<IFeatureToggleService>(MockBehavior.Strict);
            var settings = new Mock<IDepartmentSettingsService>(MockBehavior.Strict);
            flags.Setup(x => x.EvaluateFreshAsync(FeatureFlagKeys.BusinessOperations, DepartmentId)).ReturnsAsync(flag.HasValue ? new FeatureFlagEvaluation { IsEnabled = flag.Value } : null);
            settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, true)).ReturnsAsync(disabled.HasValue ? new DepartmentModuleSettings { BusinessOperationsDisabled = disabled.Value } : null);
            var billing = new Mock<ISubscriptionsService>(MockBehavior.Strict);
            (await new BusinessOperationsAccessService(flags.Object, settings.Object, billing.Object).IsEnabledAsync(DepartmentId)).Should().Be(expected);
            billing.VerifyNoOtherCalls();
            flags.Verify(x => x.EvaluateFreshAsync(FeatureFlagKeys.BusinessOperations, DepartmentId), Times.Once);
        }

        [Test]
        public async Task Member_list_redirects_without_reading_legacy_records()
        {
            var result = (await Controller<ProfileController>().Certifications(null)).Should().BeOfType<RedirectToActionResult>().Subject;
            result.ControllerName.Should().Be("Certifications"); result.ActionName.Should().Be("Person"); result.RouteValues["userId"].Should().Be(UserId);
        }
        [Test]
        public async Task Member_redirect_still_checks_subject_authorization()
        {
            _authorization.Setup(x => x.CanUserEditProfileAsync(UserId, DepartmentId, "other")).ReturnsAsync(false);
            (await Controller<ProfileController>().Certifications("other")).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
            _access.VerifyNoOtherCalls();
        }
        [Test]
        public async Task Legacy_type_get_redirects_but_posts_and_deletes_cannot_mutate()
        {
            var controller = Controller<TypesController>();
            var redirect = (await controller.NewCertificationType()).Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ControllerName.Should().Be("Certifications"); redirect.ActionName.Should().Be("Types");
            (await controller.NewCertificationType(new NewCertificationTypeView(), CancellationToken.None)).Should().BeOfType<NotFoundResult>();
            (await controller.DeleteCertificationType(1, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
            _authorization.VerifyNoOtherCalls();
        }
        [Test]
        public async Task Legacy_type_page_stays_available_when_cutover_is_off()
        {
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(false);
            _authorization.Setup(x => x.CanUserAddCertificationTypeAsync(UserId)).ReturnsAsync(true);
            (await Controller<TypesController>().NewCertificationType()).Should().BeOfType<ViewResult>();
        }
        [Test]
        public async Task Legacy_report_is_unavailable_before_reading_data()
        {
            (await Controller<ReportsController>().CertificationsReport()).Should().BeOfType<NotFoundResult>();
        }
        [Test]
        public async Task Legacy_member_list_and_report_stay_available_when_cutover_is_off()
        {
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(false);
            var department = new Department { DepartmentId = DepartmentId, TimeZone = "Pacific Standard Time" };
            var departments = new Mock<IDepartmentsService>();
            departments.Setup(x => x.GetDepartmentByUserIdAsync(UserId, false)).ReturnsAsync(department);
            departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(department);
            departments.Setup(x => x.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId, false)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser>());
            var certifications = new Mock<ICertificationService>();
            certifications.Setup(x => x.GetCertificationsByUserIdAsync(UserId)).ReturnsAsync(new List<PersonnelCertification>());
            var protectedRead = new Mock<IProtectedReadService>();
            protectedRead.Setup(x => x.ResolveCertificationsForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<PersonnelCertification>>(), "", UserId, false, CancellationToken.None)).ReturnsAsync(new ProtectedReadResult());
            var sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
            sensitive.Setup(x => x.GetResolvedForDepartmentAsync(DepartmentId, null, UserId)).ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>());
            (await Controller<ProfileController>(departments.Object, certifications.Object, protectedRead.Object, Mock.Of<IUsersService>()).Certifications(null)).Should().BeOfType<ViewResult>();
            (await Controller<ReportsController>(departments.Object, sensitive.Object).CertificationsReport()).Should().BeOfType<ViewResult>();
        }
        [Test]
        public async Task Legacy_report_lists_only_active_members_and_only_this_departments_records()
        {
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(false);
            var department = new Department { DepartmentId = DepartmentId, TimeZone = "Pacific Standard Time" };
            var departments = new Mock<IDepartmentsService>();
            departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(department);
            // The helper already drops removed and disabled members; a hidden one still comes back from it.
            departments.Setup(x => x.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(DepartmentId, false)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new() { Id = "active" }, new() { Id = "hidden" } });
            departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId)).ReturnsAsync(new HashSet<string> { "active" });
            // Strict: the hidden member is never even checked against the visibility matrix.
            _authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync("active", UserId, DepartmentId)).ReturnsAsync(true);
            var certifications = new Mock<ICertificationService>();
            certifications.Setup(x => x.GetCertificationsByUserIdAsync("active")).ReturnsAsync(new List<PersonnelCertification>
            {
                new PersonnelCertification { DepartmentId = DepartmentId, UserId = "active", Name = "Here" },
                new PersonnelCertification { DepartmentId = 99, UserId = "active", Name = "Another department" }
            });
            var profiles = new Mock<IUserProfileService>();
            profiles.Setup(x => x.GetProfileByUserIdAsync("active", false)).ReturnsAsync(new UserProfile { UserId = "active", FirstName = "Ada", LastName = "Active" });
            var sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
            sensitive.Setup(x => x.GetResolvedForDepartmentAsync(DepartmentId, null, UserId)).ReturnsAsync(new Dictionary<string, DepartmentMemberSensitiveData>());

            var view = (await Controller<ReportsController>(departments.Object, sensitive.Object, certifications.Object, profiles.Object).CertificationsReport()).Should().BeOfType<ViewResult>().Subject;
            var model = (Resgrid.Web.Areas.User.Models.Reports.Certifications.CertificationsReportView)view.Model;
            model.Rows.Should().ContainSingle();
            model.Rows[0].SubRows.Select(s => s.Name).Should().Equal("Here");
            certifications.Verify(x => x.GetCertificationsByUserIdAsync("hidden"), Times.Never);
        }
        [Test]
        public async Task Internal_report_checks_requested_department_after_authentication()
        {
            var oldToken = Resgrid.Config.SecurityConfig.InternalReportsToken;
            try
            {
                Resgrid.Config.SecurityConfig.InternalReportsToken = "cutover-test-token";
                _http.Request.Headers["X-Internal-Reports-Token"] = "cutover-test-token";
                _access.Setup(x => x.IsEnabledAsync(99)).ReturnsAsync(true);
                (await Controller<ReportsController>().InternalRunReport((int)ReportTypes.Certifications, 99)).Should().BeOfType<NotFoundResult>();
                _access.Verify(x => x.IsEnabledAsync(99), Times.Once); _access.Verify(x => x.IsEnabledAsync(DepartmentId), Times.Never);
                _http.Request.Headers.Remove("X-Internal-Reports-Token");
                (await Controller<ReportsController>().InternalRunReport((int)ReportTypes.Certifications, 99)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
                _access.Verify(x => x.IsEnabledAsync(99), Times.Once);
            }
            finally { Resgrid.Config.SecurityConfig.InternalReportsToken = oldToken; }
        }
        [TestCase(true)] [TestCase(false)]
        public async Task Schedule_choices_switch_and_keep_the_new_compliance_report(bool enabled)
        {
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(enabled);
            var result = (await Controller<ProfileController>().AddNewScheduledReport()).Should().BeOfType<ViewResult>().Subject;
            var model = (NewScheduledReportView)result.Model;
            model.ReportTypes.Any(x => x.Value == ((int)ReportTypes.Certifications).ToString()).Should().Be(!enabled);
            model.ReportTypes.Any(x => x.Value == ((int)ReportTypes.CertificationCompliance).ToString()).Should().BeTrue();
        }
        [Test]
        public async Task Posted_legacy_schedule_cannot_be_created_or_saved()
        {
            var controller = Controller<ProfileController>();
            (await controller.AddNewScheduledReport(new NewScheduledReportView { ReportType = ReportTypes.Certifications }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
            (await controller.EditScheduledReport(new EditScheduledReportView { ReportType = ReportTypes.Certifications }, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        }
        [Test]
        public async Task Existing_legacy_schedule_cannot_be_reactivated()
        {
            var tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
            tasks.Setup(x => x.GetScheduledTaskByIdAsync(1)).ReturnsAsync(new ScheduledTask { DepartmentId = DepartmentId, UserId = UserId, TaskType = (int)TaskTypes.ReportDelivery, Data = "2" });
            (await Controller<ProfileController>(tasks.Object).ActivateScheduledReport(1, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
            tasks.Verify(x => x.GetScheduledTaskByIdAsync(1), Times.Once); tasks.VerifyNoOtherCalls();
        }
        [Test]
        public async Task Existing_legacy_schedule_cannot_be_opened_for_edit()
        {
            var tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
            tasks.Setup(x => x.GetScheduledTaskByIdAsync(1)).ReturnsAsync(new ScheduledTask { DepartmentId = DepartmentId, UserId = UserId, TaskType = (int)TaskTypes.ReportDelivery, Data = "2" });
            (await Controller<ProfileController>(tasks.Object).EditScheduledReport(1)).Should().BeOfType<NotFoundResult>();
            tasks.Verify(x => x.GetScheduledTaskByIdAsync(1), Times.Once); tasks.VerifyNoOtherCalls();
        }
        [Test]
        public async Task Edit_form_selects_the_stored_report_type()
        {
            var tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
            tasks.Setup(x => x.GetScheduledTaskByIdAsync(1)).ReturnsAsync(new ScheduledTask { DepartmentId = DepartmentId, UserId = UserId, TaskType = (int)TaskTypes.ReportDelivery, Data = ((int)ReportTypes.CertificationCompliance).ToString(), ScheduleType = (int)ScheduleTypes.Weekly, Time = "08:00" });
            var result = (await Controller<ProfileController>(tasks.Object).EditScheduledReport(1)).Should().BeOfType<ViewResult>().Subject;
            var model = (EditScheduledReportView)result.Model;
            model.ReportType.Should().Be(ReportTypes.CertificationCompliance);
            model.ReportTypes.Single(x => x.Selected).Value.Should().Be(((int)ReportTypes.CertificationCompliance).ToString());
            model.ReportTypes.Any(x => x.Value == ((int)ReportTypes.Certifications).ToString()).Should().BeFalse();
        }
        [Test]
        public async Task Queued_legacy_delivery_is_logged_as_skipped_without_generating_or_sending()
        {
            var task = new ScheduledTask { DepartmentId = DepartmentId, Data = "2" };
            var tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
            tasks.Setup(x => x.CreateScheduleTaskLogAsync(task, CancellationToken.None)).ReturnsAsync(new ScheduledTaskLog());
            var email = new Mock<IEmailService>(MockBehavior.Strict); var pdf = new Mock<IPdfProvider>(MockBehavior.Strict);
            var worker = new ReportDeliveryLogic(tasks.Object, email.Object, pdf.Object, null, _access.Object);
            var result = await worker.Process(new ReportDeliveryQueueItem { Department = new Department { DepartmentId = DepartmentId }, ScheduledTask = task });
            result.Item1.Should().BeTrue(); tasks.Verify(x => x.CreateScheduleTaskLogAsync(task, CancellationToken.None), Times.Once);
            email.VerifyNoOtherCalls(); pdf.VerifyNoOtherCalls();
        }
        [TestCase(true, null, null)] [TestCase(false, true, null)] [TestCase(false, null, true)]
        public async Task Scheduled_reports_are_not_delivered_to_a_removed_disabled_or_hidden_subscriber(bool deleted, bool? disabled, bool? hidden)
        {
            var task = new ScheduledTask { DepartmentId = DepartmentId, UserId = UserId, Data = ((int)ReportTypes.CertificationCompliance).ToString() };
            var tasks = new Mock<IScheduledTasksService>(MockBehavior.Strict);
            tasks.Setup(x => x.CreateScheduleTaskLogAsync(task, CancellationToken.None)).ReturnsAsync(new ScheduledTaskLog());
            var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
            departments.Setup(x => x.GetDepartmentMemberAsync(UserId, DepartmentId, true)).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = UserId, IsDeleted = deleted, IsDisabled = disabled, IsHidden = hidden });
            var email = new Mock<IEmailService>(MockBehavior.Strict); var pdf = new Mock<IPdfProvider>(MockBehavior.Strict);
            var worker = new ReportDeliveryLogic(tasks.Object, email.Object, pdf.Object, null, _access.Object, departments.Object);
            var result = await worker.Process(new ReportDeliveryQueueItem { Department = new Department { DepartmentId = DepartmentId }, ScheduledTask = task, Email = "former@example.test" });
            result.Item1.Should().BeTrue("the skipped occurrence is logged so it is not retried");
            tasks.Verify(x => x.CreateScheduleTaskLogAsync(task, CancellationToken.None), Times.Once);
            email.VerifyNoOtherCalls(); pdf.VerifyNoOtherCalls();
        }
        [TestCase(true)] [TestCase(false)]
        public async Task Shared_add_form_remains_available_in_both_modes(bool enabled)
        {
            _access.Setup(x => x.IsEnabledAsync(DepartmentId)).ReturnsAsync(enabled);
            var certifications = new Mock<ICertificationService>(MockBehavior.Strict);
            certifications.Setup(x => x.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>());
            (await Controller<ProfileController>(certifications.Object).AddCertification(UserId)).Should().BeOfType<ViewResult>();
            _access.VerifyNoOtherCalls();
        }
        [Test]
        public async Task New_member_page_keeps_untyped_records_and_excludes_deleted_or_foreign_rows()
        {
            var visible = new PersonnelCertification { PersonnelCertificationId = 1, DepartmentId = DepartmentId, UserId = UserId, Data = new byte[] { 1 } };
            var certifications = new Mock<ICertificationService>(MockBehavior.Strict);
            certifications.Setup(x => x.GetCertificationsByUserIdAsync(UserId)).ReturnsAsync(new List<PersonnelCertification> { visible, new PersonnelCertification { DepartmentId = DepartmentId, IsDeleted = true }, new PersonnelCertification { DepartmentId = 99 } });
            var protectedRead = new Mock<IProtectedReadService>(MockBehavior.Strict);
            protectedRead.Setup(x => x.ResolveCertificationsForReadAsync(DepartmentId, It.Is<IReadOnlyList<PersonnelCertification>>(r => r.Count == 1 && r[0] == visible), "", UserId, false, CancellationToken.None)).ReturnsAsync(new ProtectedReadResult());
            var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
            departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = UserId, FirstName = "Member" } });
            var result = (await Controller<CertificationsController>(certifications.Object, protectedRead.Object, departments.Object).Person(null)).Should().BeOfType<ViewResult>().Subject;
            var model = (CertificationPersonView)result.Model;
            model.Records.Should().ContainSingle().Which.Should().BeSameAs(visible); model.UserId.Should().Be(UserId); visible.Data.Should().BeNull(); protectedRead.VerifyAll();
        }
        [Test]
        public async Task New_member_page_denies_unauthorized_subject_before_reading_records()
        {
            _authorization.Setup(x => x.CanUserEditProfileAsync(UserId, DepartmentId, "other")).ReturnsAsync(false);
            (await Controller<CertificationsController>().Person("other")).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
        }
        [Test]
        public async Task New_member_page_admits_certification_viewers_for_department_members_only()
        {
            _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View) }));
            _authorization.Setup(x => x.CanUserEditProfileAsync(UserId, DepartmentId, "other")).ReturnsAsync(false);
            _authorization.Setup(x => x.CanUserEditProfileAsync(UserId, DepartmentId, "stranger")).ReturnsAsync(false);
            var certifications = new Mock<ICertificationService>(MockBehavior.Strict);
            certifications.Setup(x => x.GetCertificationsByUserIdAsync("other")).ReturnsAsync(new List<PersonnelCertification>());
            var protectedRead = new Mock<IProtectedReadService>(MockBehavior.Strict);
            protectedRead.Setup(x => x.ResolveCertificationsForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<PersonnelCertification>>(), "", UserId, false, CancellationToken.None)).ReturnsAsync(new ProtectedReadResult());
            var departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
            departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "other", FirstName = "Other" } });
            var controller = Controller<CertificationsController>(certifications.Object, protectedRead.Object, departments.Object);
            var result = (await controller.Person("other")).Should().BeOfType<ViewResult>().Subject;
            ((CertificationPersonView)result.Model).UserId.Should().Be("other");
            departments.Verify(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId), Times.Once);
            (await controller.Person("stranger")).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
            certifications.Verify(x => x.GetCertificationsByUserIdAsync("stranger"), Times.Never);
        }
    }
}
