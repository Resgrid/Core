using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.PersonnelLocation;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class PersonnelLocationControllerTests
	{
		private const int DepartmentId = 10;
		private const string CallerUserId = "4F1C2B7E-0000-4000-8000-00000000ABCD";
		private const string StoredUserId = "4f1c2b7e-0000-4000-8000-00000000abcd";

		private Mock<IUsersService> _usersService;
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IPersonnelLocationEventProvider> _personnelLocationEventProvider;
		private PersonnelLocationController _controller;

		[SetUp]
		public void SetUp()
		{
			_usersService = new Mock<IUsersService>();
			_departmentsService = new Mock<IDepartmentsService>();
			_personnelLocationEventProvider = new Mock<IPersonnelLocationEventProvider>();

			// The caller spells its own id in upper case; the store holds it in lower case.
			_usersService
				.Setup(service => service.GetUserById(CallerUserId, It.IsAny<bool>()))
				.Returns(new IdentityUser { UserId = StoredUserId });
			_departmentsService
				.Setup(service => service.GetAllDepartmentsForUserAsync(StoredUserId))
				.ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = DepartmentId, UserId = StoredUserId } });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, CallerUserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new PersonnelLocationController(_usersService.Object, _departmentsService.Object, _personnelLocationEventProvider.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task SetPersonLocation_ReturnsServiceUnavailable_WhenPublishFails()
		{
			_personnelLocationEventProvider
				.Setup(provider => provider.EnqueuePersonnelLocationEventAsync(It.IsAny<PersonnelLocationEvent>()))
				.ReturnsAsync(false);

			var response = await _controller.SetPersonLocation(new PersonnelLocationInput
			{
				UserId = CallerUserId,
				Latitude = "47.6062",
				Longitude = "-122.3321"
			});

			response.Result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
		}

		[Test]
		public async Task SetPersonLocation_QueuesTheStoredUserId_SoRealtimeUpdatesMatchTheMapMarker()
		{
			PersonnelLocationEvent queued = null;
			_personnelLocationEventProvider
				.Setup(provider => provider.EnqueuePersonnelLocationEventAsync(It.IsAny<PersonnelLocationEvent>()))
				.Callback<PersonnelLocationEvent>(location => queued = location)
				.ReturnsAsync(true);

			var response = await _controller.SetPersonLocation(new PersonnelLocationInput
			{
				UserId = CallerUserId,
				Latitude = "47.6062",
				Longitude = "-122.3321"
			});

			response.Result.Should().BeOfType<CreatedAtActionResult>();
			queued.Should().NotBeNull();
			queued.UserId.Should().Be(StoredUserId);
			queued.DepartmentId.Should().Be(DepartmentId);
			queued.Latitude.Should().Be(47.6062m);
			queued.Longitude.Should().Be(-122.3321m);
		}
	}
}
