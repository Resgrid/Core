using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using ClaimsHelper = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Services
{
	[TestFixture, NonParallelizable]
	public class ChecklistCalendarApiTests
	{
		[Test]
		public async Task V4_calendar_inclusion_is_opt_in_preserves_ordinary_ids_and_uses_authenticated_protected_context()
		{
			var previous = ClaimsHelper._httpContextAccessor; var context = new DefaultHttpContext();
			context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, "author"), new Claim(ClaimTypes.PrimaryGroupSid, "77") }, "test"));
			context.Request.Headers[DataProtectionController.GrantHeader] = "synthetic-grant";
			ClaimsHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = context };
			using var activity = new Activity("checklist-calendar-test").Start();
			try
			{
				var calendar = new Mock<ICalendarService>(); var departments = new Mock<IDepartmentsService>(); var checklists = new Mock<IChecklistsService>(); var now = new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc);
				departments.Setup(d => d.GetDepartmentByIdAsync(77, false)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "author", TimeZone = "UTC" });
				departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(77)).ReturnsAsync(new List<PersonName>());
				calendar.Setup(c => c.GetAllCalendarItemsForDepartmentInRangeAsync(77, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<CalendarItem> { new CalendarItem { DepartmentId = 77, CalendarItemId = 42, Start = now, End = now.AddHours(1), Title = "Ordinary event", CreatorUserId = "author" } });
				calendar.Setup(c => c.GetAllCalendarItemTypesForDepartmentAsync(77)).ReturnsAsync(new List<CalendarItemType>());
				var id = Guid.NewGuid().ToString(); checklists.Setup(c => c.CalendarAsync(It.Is<ChecklistActor>(a => a.DepartmentId == 77 && a.UserId == "author" && a.GrantToken == "synthetic-grant"), now.Date, now.Date.AddDays(1))).ReturnsAsync(new List<ChecklistCalendarEntry> { new ChecklistCalendarEntry { Id = "checklist:" + id, OccurrenceId = id, Title = "REDACTED", IsRedacted = true, State = 4, StartUtc = now, EndUtc = now.AddHours(2) } });
				var controller = new CalendarController(calendar.Object, departments.Object, Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), Mock.Of<IEventAggregator>(), Mock.Of<IUserProfileService>(), Mock.Of<IProtectedReadService>(), checklists.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
				var ordinary = (await controller.GetDepartmentCalendarItemsInRange(now.Date, now.Date)).Value; ordinary.Data.Should().ContainSingle(i => i.CalendarItemId == "42" && !i.IsVirtual); checklists.Invocations.Should().BeEmpty();
				var combined = (await controller.GetDepartmentCalendarItemsInRange(now.Date, now.Date, true)).Value; combined.PageSize.Should().Be(2);
				var readiness = combined.Data.Single(i => i.IsVirtual); readiness.CalendarItemId.Should().Be("checklist:" + id); readiness.SourceId.Should().Be(id); readiness.LockEditing.Should().BeTrue(); readiness.IsRedacted.Should().BeTrue(); readiness.ChecklistState.Should().Be(4); readiness.TypeColor.Should().Be("#c0392b"); readiness.DeepLinkUrl.Should().EndWith(id); readiness.StartUtc.Should().Be(now);
				context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
				checklists.Setup(c => c.CalendarAsync(It.IsAny<ChecklistActor>(), It.IsAny<DateTime>(), It.IsAny<DateTime>())).ThrowsAsync(new ChecklistException(403, "Unavailable"));
				var partial = (await controller.GetDepartmentCalendarItemsInRange(now.Date, now.Date, true)).Value;
				partial.Data.Should().ContainSingle(i => i.CalendarItemId == "42" && !i.IsVirtual); partial.PageSize.Should().Be(1);
				(await controller.GetDepartmentCalendarItemsInRange(now, now.AddDays(94), true)).Result.Should().BeOfType<BadRequestResult>();
			}
			finally { ClaimsHelper._httpContextAccessor = previous; }
		}
	}
}
