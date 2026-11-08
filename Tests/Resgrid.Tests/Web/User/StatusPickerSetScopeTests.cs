using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Support case 2026-11-08 (EU department): the Set Unit Status window's status list came up empty for every unit.
	/// The Units page passes 1 as "no status set" for units whose type has none, and the pickers used to read any set by raw
	/// id; since the "> 25" guard became "> 0" (ff59f552) they read set #1 of the whole database, another department's, which
	/// there had no options left. The pickers now read only the caller's own active set of the right kind and fall back to
	/// the standard statuses for anything else, including a set with no options.
	/// </summary>
	[TestFixture]
	public class StatusPickerSetScopeTests
	{
		private const int Dept = 7;
		private const int OtherDept = 1;
		private const string Me = "dispatcher-1";

		private const int OwnUnitSetId = 4100;
		private const int OwnEmptyUnitSetId = 4101;
		private const int OwnDeletedUnitSetId = 4102;
		private const int OwnPersonnelSetId = 4103;
		private const int OtherDeptSetId = 1;

		private Mock<ICustomStateRepository> _repository;
		private CustomStateService _customStates;
		private UnitsController _unitsController;
		private DefaultHttpContext _http;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<ICustomStateRepository>();
			_repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CustomState>
			{
				Set(OwnUnitSetId, Dept, CustomStateTypes.Unit, false,
					new CustomStateDetail { CustomStateDetailId = 9001, CustomStateId = OwnUnitSetId, ButtonText = "Uitgerukt", Order = 1 },
					new CustomStateDetail { CustomStateDetailId = 9002, CustomStateId = OwnUnitSetId, ButtonText = "In kazerne", Order = 2 },
					new CustomStateDetail { CustomStateDetailId = 9003, CustomStateId = OwnUnitSetId, ButtonText = "Old option", Order = 3, IsDeleted = true }),
				Set(OwnEmptyUnitSetId, Dept, CustomStateTypes.Unit, false,
					new CustomStateDetail { CustomStateDetailId = 9010, CustomStateId = OwnEmptyUnitSetId, ButtonText = "Removed", IsDeleted = true }),
				Set(OwnDeletedUnitSetId, Dept, CustomStateTypes.Unit, true,
					new CustomStateDetail { CustomStateDetailId = 9020, CustomStateId = OwnDeletedUnitSetId, ButtonText = "From a deleted set" }),
				Set(OwnPersonnelSetId, Dept, CustomStateTypes.Personnel, false,
					new CustomStateDetail { CustomStateDetailId = 9030, CustomStateId = OwnPersonnelSetId, ButtonText = "Op wacht" })
			});

			// Set #1 of the database belongs to another department and has no options left: what the empty picker showed.
			var otherDepartmentsSet = Set(OtherDeptSetId, OtherDept, CustomStateTypes.Unit, false,
				new CustomStateDetail { CustomStateDetailId = 1, CustomStateId = OtherDeptSetId, ButtonText = "Another department's option", IsDeleted = true });
			_repository.Setup(x => x.GetCustomStatesByIdAsync(OtherDeptSetId)).ReturnsAsync(otherDepartmentsSet);
			_repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(OtherDept)).ReturnsAsync(new List<CustomState> { otherDepartmentsSet });

			_customStates = new CustomStateService(_repository.Object, Mock.Of<ICustomStateDetailRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<ICacheProvider>());

			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(Dept)).ReturnsAsync(new List<Call>());
			var scope = new Mock<IDispatchScopeService>();
			scope.Setup(x => x.FilterCallsForUserAsync(Dept, Me, It.IsAny<List<Call>>())).ReturnsAsync(new List<Call>());
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetAllStationGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>());
			var mapping = new Mock<IMappingService>();
			mapping.Setup(x => x.GetDestinationPOIsForDepartmentAsync(Dept)).ReturnsAsync(new List<Poi>());

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			var common = new Mock<IStringLocalizer<Resgrid.Localization.Common>>();
			common.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_unitsController = new UnitsController(Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(), Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(),
				Mock.Of<ILimitsService>(), groups.Object, calls.Object, Mock.Of<IEventAggregator>(), _customStates, Mock.Of<IGeoService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<INovuProvider>(), mapping.Object,
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), common.Object,
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IProtectedReadService>(), Mock.Of<IRecordsCutoverService>(), scope.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static CustomState Set(int id, int departmentId, CustomStateTypes type, bool deleted, params CustomStateDetail[] details) => new CustomState
		{
			CustomStateId = id,
			DepartmentId = departmentId,
			Type = (int)type,
			Name = $"Set {id}",
			IsDeleted = deleted,
			Details = new List<CustomStateDetail>(details)
		};

		private async Task<string> UnitStatusOptions(int customStateId) =>
			((ContentResult)await _unitsController.GetUnitStatusHtmlForDropdownByStateId(customStateId)).Content;

		private void ShouldBeStandardStatuses(string html)
		{
			html.Should().Contain(">Available</option>").And.Contain(">Responding</option>").And.Contain(">Out of Service</option>");
		}

		#region Set lookup

		[Test]
		public async Task the_departments_own_active_set_of_that_kind_is_found()
		{
			var set = await _customStates.GetActiveCustomStateForDepartmentAsync(Dept, OwnUnitSetId, CustomStateTypes.Unit);

			set.Should().NotBeNull();
			set.CustomStateId.Should().Be(OwnUnitSetId);
		}

		[TestCase(0, CustomStateTypes.Unit, TestName = "no set id")]
		[TestCase(OtherDeptSetId, CustomStateTypes.Unit, TestName = "another department's set id")]
		[TestCase(OwnDeletedUnitSetId, CustomStateTypes.Unit, TestName = "a deleted set")]
		[TestCase(OwnPersonnelSetId, CustomStateTypes.Unit, TestName = "a personnel set asked for as a unit set")]
		[TestCase(OwnUnitSetId, CustomStateTypes.Staffing, TestName = "a unit set asked for as a staffing set")]
		[TestCase(999999, CustomStateTypes.Unit, TestName = "an unknown set id")]
		public async Task no_set_is_returned_for(int customStateId, CustomStateTypes type)
		{
			var set = await _customStates.GetActiveCustomStateForDepartmentAsync(Dept, customStateId, type);

			set.Should().BeNull();
			_repository.Verify(x => x.GetCustomStatesByIdAsync(It.IsAny<int>()), Times.Never);
		}

		#endregion

		#region Set Unit Status picker

		[Test]
		public async Task the_units_page_no_set_marker_gets_the_standard_statuses_not_set_1_of_another_department()
		{
			var html = await UnitStatusOptions(OtherDeptSetId);

			ShouldBeStandardStatuses(html);
			html.Should().NotContain("Another department's option");
			_repository.Verify(x => x.GetCustomStatesByIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task a_unit_without_a_status_set_gets_the_standard_statuses()
		{
			ShouldBeStandardStatuses(await UnitStatusOptions(0));
		}

		[Test]
		public async Task a_unit_on_the_departments_own_set_gets_its_active_options_only()
		{
			var html = await UnitStatusOptions(OwnUnitSetId);

			html.Should().Be("<option value='9001'>Uitgerukt</option><option value='9002'>In kazerne</option>");
		}

		[Test]
		public async Task a_set_with_every_option_removed_gets_the_standard_statuses_instead_of_an_empty_list()
		{
			var html = await UnitStatusOptions(OwnEmptyUnitSetId);

			ShouldBeStandardStatuses(html);
			html.Should().NotContain("Removed");
		}

		[Test]
		public async Task a_deleted_set_gets_the_standard_statuses()
		{
			var html = await UnitStatusOptions(OwnDeletedUnitSetId);

			ShouldBeStandardStatuses(html);
			html.Should().NotContain("From a deleted set");
		}

		[Test]
		public async Task the_destination_picker_reads_the_same_list()
		{
			// Option 9001 is only offered from the department's own set; under another department's id it is unknown,
			// so the destination list is just "None".
			var own = (ContentResult)await _unitsController.GetUnitStatusDestinationHtmlForDropdown(OwnUnitSetId, 9001);
			var foreign = (ContentResult)await _unitsController.GetUnitStatusDestinationHtmlForDropdown(OtherDeptSetId, 1);

			own.Content.Should().StartWith("<option value=''>None</option>");
			foreign.Content.Should().Be("<option value=''>None</option>");
		}

		[Test]
		public async Task the_multi_unit_menu_gets_the_standard_menu_for_another_departments_set_instead_of_failing()
		{
			var result = (ContentResult)await _unitsController.GetUnitOptionsDropdownForStates(OtherDeptSetId, "3,4");

			result.Content.Should().Contain(">Available</a>").And.NotContain("Another department's option");
			_repository.Verify(x => x.GetCustomStatesByIdAsync(It.IsAny<int>()), Times.Never);
		}

		#endregion
	}
}
