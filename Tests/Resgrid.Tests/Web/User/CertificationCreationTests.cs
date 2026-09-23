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
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Certifications;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
    [TestFixture, NonParallelizable]
    public class CertificationCreationTests
    {
        private const int DepartmentId = 77;
        private const int TypeId = 12;
        private const string Subject = "HOLDER";
        private IHttpContextAccessor _previousAccessor;
        private DefaultHttpContext _http;
        private Mock<ICertificationService> _certifications;
        private Mock<IDepartmentsService> _departments;
        private DepartmentCertificationType _type;
        private CertificationsController _controller;

        [SetUp]
        public void SetUp()
        {
            _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
            _http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.PrimarySid, "manager"),
                new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
            ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
            _certifications = new Mock<ICertificationService>(MockBehavior.Strict);
            _departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
            _departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
                .ReturnsAsync(new List<PersonName> { new PersonName { UserId = Subject, FirstName = "Alex", LastName = "Member" } });
            _departments.Setup(x => x.GetSelectablePersonnelNamesAsync(DepartmentId))
                .ReturnsAsync(new List<PersonName> { new PersonName { UserId = Subject, FirstName = "Alex", LastName = "Member" } });
            _departments.Setup(x => x.GetDepartmentMemberAsync(Subject, DepartmentId, true))
                .ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = Subject });
            _type = new DepartmentCertificationType { DepartmentCertificationTypeId = TypeId, DepartmentId = DepartmentId, Type = "EMT", IsActive = true };
            _certifications.Setup(x => x.GetAllCertificationTypesByDepartmentAsync(DepartmentId))
                .ReturnsAsync(new List<DepartmentCertificationType> { _type });
            _certifications.Setup(x => x.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PersonnelCertification record, CancellationToken _) => record);
            var strings = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Certifications.Certifications>>();
            strings.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
            _controller = new CertificationsController(_certifications.Object, null, null, _departments.Object, null, null, null, strings.Object, null)
            {
                ControllerContext = new ControllerContext { HttpContext = _http },
                TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>())
            };
        }

        [TearDown]
        public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

        private void Grant(string resource, string action) => _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(resource, action) }));
        private void Manage() => Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update);
        private static CertificationRecordInput Input() => new CertificationRecordInput
        {
            UserId = Subject, DepartmentCertificationTypeId = TypeId, Name = "Emergency Medical Technician",
            Number = "EMT-100", Area = "Nevada", IssuedBy = "State", RecievedOn = new DateTime(2026, 9, 21), ExpiresOn = new DateTime(2028, 9, 21)
        };
        private void VerifyNotSaved() => _certifications.Verify(x => x.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()), Times.Never);

        [TestCase(false)]
        [TestCase(true)]
        public async Task Members_and_viewers_cannot_open_or_submit_creation(bool viewer)
        {
            if (viewer) Grant(ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.View);
            (await _controller.Add()).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
            (await _controller.Add(Input(), null, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
            _departments.VerifyNoOtherCalls();
            _certifications.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Managers_and_department_admins_can_select_members_and_active_person_types(bool admin)
        {
            Grant(admin ? ResgridClaimTypes.Resources.Department : ResgridClaimTypes.Resources.Certifications, ResgridClaimTypes.Actions.Update);
            _certifications.Setup(x => x.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentCertificationType>
            {
                _type,
                new DepartmentCertificationType { DepartmentId = DepartmentId, IsActive = false },
                new DepartmentCertificationType { DepartmentId = DepartmentId, IsDeleted = true },
                new DepartmentCertificationType { DepartmentId = DepartmentId, AppliesTo = (int)CertificationAppliesTo.Unit },
                new DepartmentCertificationType { DepartmentId = DepartmentId + 1 }
            });
            var view = (await _controller.Add()).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<CertificationAddView>().Subject;
            view.CanManage.Should().BeTrue();
            view.Personnel.Should().ContainKey(Subject).WhoseValue.Should().Be("Alex Member");
            view.Types.Should().ContainSingle().Which.Should().BeSameAs(_type);
            view.Input.UserId.Should().BeNull("the user must explicitly choose the certification holder");
        }

        [TestCase("missing")]
        [TestCase("other-department")]
        [TestCase("deleted")]
        public async Task Posted_subject_requires_current_department_membership_even_if_names_are_cached(string scenario)
        {
            Manage();
            _departments.Setup(x => x.GetDepartmentMemberAsync(Subject, DepartmentId, true)).ReturnsAsync(scenario == "missing" ? null :
                new DepartmentMember { UserId = Subject, DepartmentId = scenario == "other-department" ? DepartmentId + 1 : DepartmentId, IsDeleted = scenario == "deleted" });
            (await _controller.Add(Input(), null, CancellationToken.None)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
            VerifyNotSaved();
        }

        [TestCase("inactive")]
        [TestCase("unit")]
        [TestCase("deleted")]
        [TestCase("other-department")]
        [TestCase("missing")]
        public async Task Invalid_catalog_type_cannot_be_posted(string scenario)
        {
            Manage();
            _type.IsActive = scenario != "inactive";
            _type.IsDeleted = scenario == "deleted";
            _type.AppliesTo = scenario == "unit" ? (int)CertificationAppliesTo.Unit : (int)CertificationAppliesTo.Person;
            if (scenario == "other-department") _type.DepartmentId++;
            var input = Input();
            if (scenario == "missing") input.DepartmentCertificationTypeId = null;
            (await _controller.Add(input, null, CancellationToken.None)).Should().BeOfType<ViewResult>();
            _controller.ModelState["Input.DepartmentCertificationTypeId"].Errors.Should().NotBeEmpty();
            VerifyNotSaved();
        }

        [Test]
        public async Task Missing_fields_keep_the_form_and_choices_without_saving()
        {
            Manage();
            var input = Input(); input.UserId = " "; input.Name = " ";
            var view = (await _controller.Add(input, null, CancellationToken.None)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<CertificationAddView>().Subject;
            view.Input.Should().BeSameAs(input);
            view.Personnel.Should().NotBeEmpty(); view.Types.Should().NotBeEmpty();
            _controller.ModelState["Input.UserId"].Errors.Should().NotBeEmpty();
            _controller.ModelState["Input.Name"].Errors.Should().NotBeEmpty();
            VerifyNotSaved();
        }

        [Test]
        public async Task Binding_errors_are_not_saved_as_missing_optional_values()
        {
            Manage();
            _controller.ModelState.AddModelError("Input.ExpiresOn", "Invalid date");
            (await _controller.Add(Input(), null, CancellationToken.None)).Should().BeOfType<ViewResult>();
            VerifyNotSaved();
        }

        [TestCase("file.exe", 4)]
        [TestCase("file.pdf", 10485761)]
        public async Task Invalid_attachment_keeps_input_without_saving(string filename, long size)
        {
            Manage();
            var file = new Mock<IFormFile>();
            file.SetupGet(x => x.FileName).Returns(filename); file.SetupGet(x => x.Length).Returns(size);
            (await _controller.Add(Input(), file.Object, CancellationToken.None)).Should().BeOfType<ViewResult>();
            _controller.ModelState["fileToUpload"].Errors.Should().NotBeEmpty();
            file.Verify(x => x.OpenReadStream(), Times.Never);
            VerifyNotSaved();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Valid_submission_saves_for_selected_member_and_returns_to_dashboard(bool neverExpires)
        {
            Manage(); _type.NeverExpires = neverExpires;
            var input = Input();
            var bytes = new byte[] { 1, 2, 3, 4 };
            using var stream = new MemoryStream(bytes);
            var file = new FormFile(stream, 0, bytes.Length, "fileToUpload", "certificate.PDF") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            using var cancellation = new CancellationTokenSource();
            var result = (await _controller.Add(input, file, cancellation.Token)).Should().BeOfType<RedirectToActionResult>().Subject;
            result.ActionName.Should().Be("Index");
            _controller.TempData["CertificationsSaved"].Should().Be(true);
            _certifications.Verify(x => x.SaveCertificationAsync(It.Is<PersonnelCertification>(r =>
                r.PersonnelCertificationId == 0 && r.DepartmentId == DepartmentId && r.UserId == Subject &&
                r.DepartmentCertificationTypeId == TypeId && r.Type == _type.Type && r.Name == input.Name && r.Number == input.Number &&
                r.Area == input.Area && r.IssuedBy == input.IssuedBy && r.RecievedOn == input.RecievedOn &&
                r.ExpiresOn == (neverExpires ? null : input.ExpiresOn) && r.Status == (int)PersonnelCertificationStatuses.Active &&
                r.Data.SequenceEqual(bytes) && r.Filename == "certificate.PDF" && r.Filetype == "application/pdf"), cancellation.Token), Times.Once);
        }

        [Test]
        public async Task Service_validation_error_returns_the_submitted_form()
        {
            Manage();
            _certifications.Setup(x => x.SaveCertificationAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("certifications_type_not_found"));
            var input = Input();
            var view = (await _controller.Add(input, null, CancellationToken.None)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<CertificationAddView>().Subject;
            view.Input.Should().BeSameAs(input);
            _controller.ModelState[string.Empty].Errors.Should().NotBeEmpty();
        }

        [Test]
        public void Submission_requires_antiforgery_validation()
        {
            var action = typeof(CertificationsController).GetMethods().Single(m => m.Name == "Add" && m.GetParameters().Length == 3);
            action.IsDefined(typeof(HttpPostAttribute), true).Should().BeTrue();
            action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true).Should().BeTrue();
        }
    }
}
