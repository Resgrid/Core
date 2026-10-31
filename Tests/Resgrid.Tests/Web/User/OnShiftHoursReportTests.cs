using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Reports.Shifts;
using MvcClaims = Resgrid.Web.Helpers.ClaimsAuthorizationHelper;
using HoursStrings = Resgrid.Localization.Areas.User.Reports.OnShiftHours;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The On-Shift Hours report: scope validation (group / shift / static shift of this department only), who the caller may
	/// see, and that approved and automatic hours reach the page.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class OnShiftHoursReportTests
	{
		private const int DepartmentId = 7;
		private const string Caller = "CALLER-OSH";
		private const string Visible = "visible";
		private const string Hidden = "hidden";

		private Mock<IShiftStaffingService> _staffing;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IShiftsService> _shifts;
		private Mock<IWorkShiftsService> _workshifts;
		private Mock<IAuthorizationService> _authorization;

		[TearDown]
		public void TearDown()
		{
			MvcClaims._httpContextAccessor = null;
		}

		private ReportsController Controller(OnShiftHoursReport report)
		{
			var department = new Department { DepartmentId = DepartmentId, Name = "Test", TimeZone = "UTC" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);
			departments.Setup(x => x.GetAllPersonnelNamesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Visible, FirstName = "Vis", LastName = "Ible" }, new PersonName { UserId = Hidden, FirstName = "Hid", LastName = "Den" }
			});

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(x => x.GetGroupByIdAsync(3, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = DepartmentId, Name = "Station 3" });
			_groups.Setup(x => x.GetGroupByIdAsync(4, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = DepartmentId + 1, Name = "Elsewhere" });

			_shifts = new Mock<IShiftsService>();
			_shifts.Setup(x => x.GetShiftByIdAsync(5)).ReturnsAsync(new Shift { ShiftId = 5, DepartmentId = DepartmentId, Name = "A Shift" });
			_shifts.Setup(x => x.GetShiftByIdAsync(6)).ReturnsAsync(new Shift { ShiftId = 6, DepartmentId = DepartmentId + 1, Name = "Theirs" });

			_workshifts = new Mock<IWorkShiftsService>();
			_workshifts.Setup(x => x.GetWorkshiftByIdAsync("ws")).ReturnsAsync(new Workshift { WorkshiftId = "ws", DepartmentId = DepartmentId, Name = "Engine Week" });

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(Visible, Caller, DepartmentId)).ReturnsAsync(true);
			_authorization.Setup(x => x.CanUserViewPersonViaMatrixAsync(Hidden, Caller, DepartmentId)).ReturnsAsync(false);

			_staffing = new Mock<IShiftStaffingService>();
			_staffing.Setup(x => x.GetOnShiftHoursAsync(DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>()))
				.ReturnsAsync(report);

			var localizer = new Mock<IStringLocalizer<HoursStrings>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));

			var known = new Dictionary<Type, object>
			{
				[typeof(IDepartmentsService)] = departments.Object,
				[typeof(IDepartmentGroupsService)] = _groups.Object,
				[typeof(IShiftsService)] = _shifts.Object,
				[typeof(IWorkShiftsService)] = _workshifts.Object,
				[typeof(IAuthorizationService)] = _authorization.Object,
				[typeof(IShiftStaffingService)] = _staffing.Object,
				[typeof(IStringLocalizer<HoursStrings>)] = localizer.Object
			};

			var constructor = typeof(ReportsController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (ReportsController)constructor.Invoke(arguments);

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Caller),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			MvcClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			controller.ControllerContext = new ControllerContext { HttpContext = http };

			return controller;
		}

		private static OnShiftHoursReport Report(string approver = Visible)
		{
			var day = new DateTime(2026, 10, 1);
			var manual = new ShiftDayStaffing
			{
				ShiftType = StaffedShiftTypes.Signup, Source = ShiftStaffingSources.Manual, ShiftId = 5, Name = "A Shift", Day = day,
				Start = day.AddHours(7), End = day.AddHours(19), SetByUserId = approver, SetOn = day,
				Personnel = new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Visible }, new ShiftDayStaffingPerson { UserId = Hidden } }
			};
			var automatic = new ShiftDayStaffing
			{
				ShiftType = StaffedShiftTypes.Workshift, Source = ShiftStaffingSources.Automatic, WorkshiftId = "ws", Name = "Engine Week", Day = day.AddDays(1),
				Start = day.AddDays(1), End = day.AddDays(2),
				Personnel = new List<ShiftDayStaffingPerson> { new ShiftDayStaffingPerson { UserId = Visible, UnitId = 9 } }
			};
			var occurrences = new List<ShiftDayStaffing> { manual, automatic };

			return new OnShiftHoursReport
			{
				Start = day,
				End = day.AddDays(7),
				Occurrences = occurrences,
				People = OnShiftHoursCalculator.Calculate(occurrences, day, day.AddDays(7))
			};
		}

		[Test]
		public async Task Hours_are_split_by_staffing_source_and_only_people_the_caller_may_see_are_listed()
		{
			var result = await Controller(Report()).OnShiftHoursReport(new DateTime(2026, 10, 1), new DateTime(2026, 10, 7, 23, 59, 59));

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<OnShiftHoursView>().Subject;
			model.Scope.Should().Be(OnShiftHoursScopes.Department);
			// An end of 23:59:59 counts the whole last day.
			model.End.Should().Be(new DateTime(2026, 10, 8));

			var person = model.People.Should().ContainSingle().Subject;
			person.UserId.Should().Be(Visible);
			person.TotalHours.Should().Be(36);
			person.ManualHours.Should().Be(12);
			person.AutomaticHours.Should().Be(24);
			person.SignupHours.Should().Be(12);
			person.StaticHours.Should().Be(24);
			person.Mixed.Should().BeTrue();
			person.Entries.Select(x => x.Source).Should().Equal(ShiftStaffingSources.Manual, ShiftStaffingSources.Automatic);

			model.Occurrences.Should().HaveCount(2);
			model.Occurrences.First().People.Should().Be(2);
			model.Occurrences.First().SetBy.Should().Be("Vis Ible");
		}

		[Test]
		public async Task An_approver_the_caller_may_not_see_is_not_named()
		{
			var result = await Controller(Report(approver: Hidden)).OnShiftHoursReport(new DateTime(2026, 10, 1), new DateTime(2026, 10, 7, 23, 59, 59));

			var model = (OnShiftHoursView)((ViewResult)result).Model;
			var approved = model.Occurrences.Single(x => x.Source == ShiftStaffingSources.Manual);
			approved.SetBy.Should().BeNull();
			approved.SetOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_group_or_shift_from_another_department_is_not_found()
		{
			var controller = Controller(Report());
			var start = new DateTime(2026, 10, 1);
			var end = new DateTime(2026, 11, 1);

			(await controller.OnShiftHoursReport(start, end, groupId: 4)).Should().BeOfType<NotFoundResult>();
			(await controller.OnShiftHoursReport(start, end, shift: "s:6")).Should().BeOfType<NotFoundResult>();
			(await controller.OnShiftHoursReport(start, end, shift: "w:missing")).Should().BeOfType<NotFoundResult>();
			(await controller.OnShiftHoursReport(start, end, shift: "bogus")).Should().BeOfType<NotFoundResult>();

			_staffing.Verify(x => x.GetOnShiftHoursAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task The_scope_reaches_the_service_for_a_group_a_shift_and_a_static_shift()
		{
			var controller = Controller(Report());
			var start = new DateTime(2026, 10, 1);
			var end = new DateTime(2026, 11, 1);

			var group = (OnShiftHoursView)((ViewResult)await controller.OnShiftHoursReport(start, end, groupId: 3)).Model;
			var shift = (OnShiftHoursView)((ViewResult)await controller.OnShiftHoursReport(start, end, shift: "s:5")).Model;
			var workshift = (OnShiftHoursView)((ViewResult)await controller.OnShiftHoursReport(start, end, shift: "w:ws")).Model;

			group.ScopeName.Should().Be("Station 3");
			shift.Scope.Should().Be(OnShiftHoursScopes.Shift);
			workshift.ScopeName.Should().Be("Engine Week");
			_staffing.Verify(x => x.GetOnShiftHoursAsync(DepartmentId, start, end, 3, null, null), Times.Once);
			_staffing.Verify(x => x.GetOnShiftHoursAsync(DepartmentId, start, end, null, 5, null), Times.Once);
			_staffing.Verify(x => x.GetOnShiftHoursAsync(DepartmentId, start, end, null, null, "ws"), Times.Once);
		}
	}
}
