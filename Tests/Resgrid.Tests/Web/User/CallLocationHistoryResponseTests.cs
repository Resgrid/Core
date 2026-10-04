using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Calls;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	[TestFixture]
	[NonParallelizable]
	public class CallLocationHistoryResponseTests
	{
		private const int DepartmentId = 12;

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		[TestCase(true)]
		[TestCase(false)]
		public async Task History_redacts_enveloped_types_and_loads_all_priorities_once(bool mvc)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetCallPrioritiesForDepartmentAsync(DepartmentId, false)).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 7, Name = "High", Color = "#ff0000" },
				new DepartmentCallPriority { DepartmentCallPriorityId = 1, Name = "Should not replace built-in priorities" }
			});
			calls.Setup(x => x.GetDefaultCallPriorities()).Returns(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 2, Name = "Low" }
			});
			var department = new Department { DepartmentId = DepartmentId, TimeZone = "UTC" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(department);
			var history = new CallLocationHistoryResult
			{
				Entries = new List<CallLocationHistoryEntry>
				{
					new CallLocationHistoryEntry { Call = new Call { CallId = 1, Priority = 7, Type = "rgdp:unreadable", LoggedOn = DateTime.UtcNow } },
					new CallLocationHistoryEntry { Call = new Call { CallId = 2, Priority = 2, Type = "Medical", LoggedOn = DateTime.UtcNow } }
				}
			};
			if (mvc)
			{
				var localizer = new Mock<IStringLocalizer>();
				localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
				var result = await CallLocationHistoryJson.FromAsync(history, department, calls.Object, departments.Object, localizer.Object);
				result.Entries.Select(e => e.Type).Should().Equal(ProtectedDataEnvelope.RedactionValue, "Medical");
				result.Entries.Select(e => e.PriorityName).Should().Equal("High", "Low");
			}
			else
			{
				var reads = new Mock<IProtectedReadService>();
				reads.Setup(x => x.ResolveForReadAsync(DepartmentId, It.IsAny<IReadOnlyList<Call>>(), null, "user", It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, IReadOnlyList<Call> entries, string __, string ___, CancellationToken ____) => (IReadOnlyList<ProtectedReadResult>)entries.Select(c => new ProtectedReadResult { Call = c }).ToList());
				var result = await LocationHistoryResultBuilder.BuildAsync(history, DepartmentId, null, "user", reads.Object, calls.Object, departments.Object);
				result.Calls.Select(e => e.Type).Should().Equal(ProtectedDataEnvelope.RedactionValue, "Medical");
				result.Calls.Select(e => e.PriorityText).Should().Equal("High", "Low");
			}
			calls.Verify(x => x.GetCallPrioritiesForDepartmentAsync(DepartmentId, false), Times.Once);
			calls.Verify(x => x.GetDefaultCallPriorities(), Times.Once);
			calls.Verify(x => x.GetCallPrioritiesByIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Occupancy_history_maps_service_access_denial_to_forbidden()
		{
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, "user"), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			}, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(x => x.GetModuleStateAsync(DepartmentId, false)).ReturnsAsync(new RecordsModuleState { FlagEnabled = true });
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(x => x.IsEnabledAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);
			var history = new Mock<ICallLocationHistoryService>();
			history.Setup(x => x.GetHistoryForOccupancyAsync(DepartmentId, "user", "occ", It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
			var controller = new RecordOccupanciesController(Mock.Of<IRecordsOccupancyService>(), Mock.Of<IRecordsInspectionsService>(), Mock.Of<IRecordsPermitsService>(),
				Mock.Of<IRecordsHydrantsService>(), Mock.Of<IRecordsPreventionAttachmentsService>(), Mock.Of<IContactsService>(), cutover.Object, flags.Object,
				Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records>>(), history.Object, Mock.Of<ICallsService>(), Mock.Of<IDepartmentsService>(),
				Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.LocationHistory>>())
			{ ControllerContext = new ControllerContext { HttpContext = http } };

			(await controller.CallHistory("occ")).Should().BeOfType<ForbidResult>();
		}
	}
}
