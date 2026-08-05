using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Messages;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	[NonParallelizable]
	public class MessagesControllerTests
	{
		private const int DepartmentId = 10;
		private const int RoleId = 6727;
		private const string SenderUserId = "sender-user";
		private const string RecipientUserId = "recipient-user";

		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IMessageService> _messageService;
		private MessagesController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_messageService = new Mock<IMessageService>();

			_departmentsService
				.Setup(service => service.GetAllMembersForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { DepartmentId = DepartmentId, UserId = RecipientUserId }
				});
			_departmentGroupsService
				.Setup(service => service.GetAllGroupsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<DepartmentGroup>());
			_personnelRolesService
				.Setup(service => service.GetAllRolesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<PersonnelRole>
				{
					new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = DepartmentId, Name = "Test role" }
				});
			_personnelRolesService
				.Setup(service => service.GetAllMembersOfRoleAsync(RoleId))
				.ReturnsAsync(new List<PersonnelRoleUser>
				{
					new PersonnelRoleUser { PersonnelRoleId = RoleId, DepartmentId = DepartmentId, UserId = RecipientUserId }
				});

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, SenderUserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("MessagesControllerTests").Start();

			_controller = new MessagesController(
				Mock.Of<ICallsService>(),
				_departmentsService.Object,
				Mock.Of<IUserProfileService>(),
				Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IAuthorizationService>(),
				_messageService.Object,
				Mock.Of<IUsersService>(),
				_departmentGroupsService.Object,
				_personnelRolesService.Object,
				Mock.Of<IUnitsService>(),
				Mock.Of<ICalendarService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		[Test]
		public async Task SendMessage_UsesParsedRoleId_ForPrefixedRoleRecipient()
		{
			Message messageToSave = null;
			_messageService
				.Setup(service => service.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.Callback<Message, CancellationToken>((message, _) => messageToSave = message)
				.ReturnsAsync(new Message { MessageId = 123 });

			var response = await _controller.SendMessage(new NewMessageInput
			{
				Title = "Test message",
				Body = "Test body",
				Recipients = new List<MessageRecipientInput>
				{
					new MessageRecipientInput { Id = $"R:{RoleId}", Type = 3, Name = "Test role" }
				}
			}, CancellationToken.None);

			response.Value.Should().NotBeNull();
			messageToSave.Should().NotBeNull();
			messageToSave.GetRecipients().Should().ContainSingle().Which.Should().Be(RecipientUserId);
			_personnelRolesService.Verify(service => service.GetAllMembersOfRoleAsync(RoleId), Times.Once);
		}
	}
}
