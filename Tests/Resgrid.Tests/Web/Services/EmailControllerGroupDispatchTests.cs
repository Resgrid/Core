using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using PostmarkDotNet;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services.CallEmailTemplates;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// A group's dispatch address reads the email with the department's email type, the same as the department address.
	/// Types that read the plain-text body (Active911 here) used to get none on this path and dropped every email; and a
	/// follow-up page for an open call must update it without alerting the group again.
	/// </summary>
	[TestFixture]
	public class EmailControllerGroupDispatchTests
	{
		private const int DepartmentId = 10;
		private const string GroupCode = "station2";

		private Department _department;
		private DepartmentGroup _group;
		private List<Call> _activeCalls;
		private List<Call> _saved;
		private Mock<IQueueService> _queue;
		private Mock<IGeoLocationProvider> _geo;
		private EmailController _controller;

		private const string Active911Body = @"CALL: Medical Unknown Problems
ADDR1: 555 MAIN ST
ID: 2026-000153-MED
NARR: 55 YOM";

		[SetUp]
		public void SetUp()
		{
			_department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" };
			_group = new DepartmentGroup { DepartmentGroupId = 5, DepartmentId = DepartmentId, Department = _department, DispatchEmail = GroupCode };
			_activeCalls = new List<Call>();
			_saved = new List<Call>();

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupByDispatchEmailCodeAsync(GroupCode)).ReturnsAsync(_group);
			groups.Setup(g => g.GetAllMembersForGroupAndChildGroups(_group)).Returns(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "member" } });

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "member" }, new IdentityUser { Id = "elsewhere" } });
			departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId))
				.ReturnsAsync(new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = (int)CallEmailTypes.Active911 });
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(_department);

			var factory = new CallEmailFactory();
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(() => _activeCalls);
			calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
					It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()))
				.Returns((int type, CallEmail email, string manager, List<IdentityUser> users, Department department, List<Call> active, List<Unit> units, int priority,
					List<DepartmentCallPriority> priorities, List<CallType> types) =>
					factory.GenerateCallFromEmailText((CallEmailTypes)type, email, manager, users, department, active, units, priority, priorities, types, Mock.Of<IGeoLocationProvider>()));
			calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken ct) => { if (call.CallId <= 0) call.CallId = 900 + _saved.Count; _saved.Add(call); return call; });
			calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetAllUnitsForGroupAsync(_group.DepartmentGroupId)).ReturnsAsync(new List<Unit>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());
			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_geo = new Mock<IGeoLocationProvider>();
			_geo.Setup(g => g.GetLatLonFromAddress("555 MAIN ST")).ReturnsAsync("41.513334,-76.025467");

			_controller = new EmailController(Mock.Of<IDepartmentSettingsService>(), Mock.Of<INumbersService>(), Mock.Of<ILimitsService>(), calls.Object, _queue.Object,
				departments.Object, profiles.Object, Mock.Of<ITextCommandService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<ICommunicationService>(), Mock.Of<IDistributionListsService>(), Mock.Of<IUsersService>(), Mock.Of<IEmailService>(), groups.Object,
				Mock.Of<IMessageService>(), Mock.Of<IFileService>(), units.Object, _geo.Object, Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchAdminService>());
		}

		private static PostmarkInboundMessage GroupEmail(string subject, string textBody, string htmlBody = null)
		{
			return new PostmarkInboundMessage
			{
				MessageID = Guid.NewGuid().ToString(),
				Subject = subject,
				TextBody = textBody,
				HtmlBody = htmlBody,
				FromFull = new FromFull { Email = "cad@county.example", Name = "County CAD" },
				ToFull = new List<ToFull> { new ToFull { Email = GroupCode + "@" + Resgrid.Config.InboundEmailConfig.GroupsDomain, Name = "Station 2" } },
				Attachments = new List<Attachment>()
			};
		}

		[Test]
		public async Task A_plain_text_email_type_creates_a_call_from_a_group_address()
		{
			// CAD mail is commonly multipart: the HTML part becomes Body, and Active911 reads only the text part.
			await _controller.Receive(GroupEmail("ACTIVE 9-1-1", Active911Body, "<p>" + Active911Body + "</p>"), CancellationToken.None);

			_saved.Should().ContainSingle();
			var call = _saved[0];
			call.IncidentNumber.Should().Be("2026-000153-MED");
			call.Address.Should().Be("555 MAIN ST");
			call.GeoLocationData.Should().Be("41.513334,-76.025467", "an address with no coordinates is placed on the map, as on the department address");
			call.Dispatches.Should().ContainSingle(d => d.UserId == "member", "only the group's members are dispatched");
			_queue.Verify(q => q.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_follow_up_page_updates_the_open_call_without_alerting_again()
		{
			_activeCalls.Add(new Call { CallId = 42, DepartmentId = DepartmentId, IncidentNumber = "2026-000153-MED", Name = "Medical Unknown Problems", DispatchCount = 1 });

			await _controller.Receive(GroupEmail("ACTIVE 9-1-1", Active911Body), CancellationToken.None);

			_saved.Should().ContainSingle().Which.CallId.Should().Be(42);
			_queue.Verify(q => q.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
