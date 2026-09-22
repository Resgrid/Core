using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
    [TestFixture, NonParallelizable]
    public class RecordAuthoringTests
    {
        private const int DepartmentId = 77;
        private IHttpContextAccessor _previousAccessor;
        private Mock<IDepartmentsService> _departments;
        private RecordsController _controller;

        [SetUp]
        public void SetUp()
        {
            _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor;
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.PrimarySid, "author"),
                new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
            ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
            _departments = new Mock<IDepartmentsService>(MockBehavior.Strict);
            _departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false))
                .ReturnsAsync(new Department { DepartmentId = DepartmentId, TimeZone = "UTC" });
            var cutover = new Mock<IRecordsCutoverService>();
            cutover.Setup(x => x.GetModuleStateAsync(DepartmentId, false)).ReturnsAsync(new RecordsModuleState
            {
                FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active
            });
            var definitions = new Mock<IRecordDefinitionsService>();
            definitions.Setup(x => x.ListAsync(DepartmentId, false)).ReturnsAsync(new List<RecordDefinitionSummary>());
            _controller = new RecordsController(Mock.Of<IRecordsService>(), cutover.Object, Mock.Of<IRecordsAuthorizationService>(),
                _departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUnitsService>(), Mock.Of<ICallsService>(),
                Mock.Of<IDepartmentSettingsService>(), null, Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records>>(),
                null, null, null, null, null, null, null, null, Mock.Of<IRecordsUdfService>(),
                Mock.Of<IRecordsProtectionService>(), null, null, definitions.Object, null, null, null, null, null)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
            };
        }

        [TearDown]
        public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

        [Test]
        public async Task New_training_only_offers_current_visible_active_department_members()
        {
            var members = new List<DepartmentMember>
            {
                new DepartmentMember { DepartmentId = DepartmentId, UserId = "visible", IsHidden = false, IsDisabled = false },
                new DepartmentMember { DepartmentId = DepartmentId, UserId = "nullable-flags" },
                new DepartmentMember { DepartmentId = DepartmentId, UserId = "hidden", IsHidden = true },
                new DepartmentMember { DepartmentId = DepartmentId, UserId = "deleted", IsDeleted = true },
                new DepartmentMember { DepartmentId = DepartmentId, UserId = "disabled", IsDisabled = true },
                new DepartmentMember { DepartmentId = DepartmentId + 1, UserId = "other-department" }
            };
            _departments.Setup(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, true)).ReturnsAsync(members);
            _departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(
                members.Select(m => new PersonName { UserId = m.UserId, FirstName = m.UserId })
                    .Append(new PersonName { UserId = "stale-profile", FirstName = "Former member" }).ToList());

            var result = (ViewResult)await _controller.New(RmsDefinitionKeys.Training, null);
            var model = (RecordEditView)result.Model;

            result.ViewName.Should().Be("Edit");
            model.Personnel.Select(p => p.Value).Should().Equal("NULLABLE-FLAGS", "VISIBLE");
            model.ParticipantRows.Should().ContainSingle(p => p.Selected && string.IsNullOrEmpty(p.UserId));
            _departments.Verify(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, true), Times.Once);

            // A cached name must not keep a newly hidden or deleted member selectable.
            members[0].IsDeleted = true;
            members[1].IsHidden = true;
            var refreshed = (RecordEditView)((ViewResult)await _controller.New(RmsDefinitionKeys.Training, null)).Model;
            refreshed.Personnel.Should().BeEmpty();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task New_training_does_not_offer_cached_names_without_current_members(bool missingMemberships)
        {
            _departments.Setup(x => x.GetAllMembersForDepartmentUnlimitedAsync(DepartmentId, true))
                .ReturnsAsync(missingMemberships ? null : new List<DepartmentMember>());
            _departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId))
                .ReturnsAsync(new List<PersonName> { new PersonName { UserId = "stale-profile", FirstName = "Former member" } });

            var model = (RecordEditView)((ViewResult)await _controller.New(RmsDefinitionKeys.Training, null)).Model;

            model.Personnel.Should().BeEmpty();
        }
    }
}
