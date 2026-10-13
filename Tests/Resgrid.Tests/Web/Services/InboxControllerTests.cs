using System.Security.Claims;
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
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Inbox deletes used to go to Novu's environment-wide DELETE /v1/messages/{id} with whatever id the client
	/// sent, so anyone holding Messages_Delete could delete any user's or department's inbox message by guessing
	/// its ObjectId. The delete is now always made as one of the caller's own subscribers, and a unit inbox only
	/// for a unit in the caller's department. The unit inbox's HMAC is handed out on the same terms, since it is
	/// what opens that inbox once Novu enforces HMAC.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class InboxControllerTests
	{
		private const int DepartmentId = 10;
		private const string DepartmentCode = "ABCD";
		private const string UserId = "responder-1";
		private const string MessageId = "66f1c2a9e4b0a1b2c3d4e5f6";

		private Mock<INovuProvider> _novu;
		private Mock<IUnitsService> _units;
		private InboxController _controller;

		[SetUp]
		public void SetUp()
		{
			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = DepartmentCode });

			_units = new Mock<IUnitsService>();
			_units.Setup(x => x.GetUnitByIdAsync(15)).ReturnsAsync(new Unit { UnitId = 15, DepartmentId = DepartmentId });
			_units.Setup(x => x.GetUnitByIdAsync(99)).ReturnsAsync(new Unit { UnitId = 99, DepartmentId = 77 });

			_novu = new Mock<INovuProvider>();

			_controller = new InboxController(_novu.Object, departments.Object, _units.Object)
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
		public async Task DeleteMessage_should_delete_from_the_callers_own_inbox()
		{
			_novu.Setup(x => x.DeleteUserInboxMessage(UserId, DepartmentCode, MessageId)).ReturnsAsync(NovuInboxDeleteResult.Deleted);

			var result = await _controller.DeleteMessage(MessageId);

			result.Should().BeOfType<OkResult>();
			_novu.Verify(x => x.DeleteUserInboxMessage(UserId, DepartmentCode, MessageId), Times.Once);
			_novu.Verify(x => x.DeleteUnitInboxMessage(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task DeleteMessage_should_report_a_message_outside_the_callers_inbox_as_not_found()
		{
			_novu.Setup(x => x.DeleteUserInboxMessage(UserId, DepartmentCode, MessageId)).ReturnsAsync(NovuInboxDeleteResult.NotFound);

			var result = await _controller.DeleteMessage(MessageId);

			result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task DeleteMessage_should_report_a_novu_failure_as_a_server_error()
		{
			_novu.Setup(x => x.DeleteUserInboxMessage(UserId, DepartmentCode, MessageId)).ReturnsAsync(NovuInboxDeleteResult.Failed);

			var result = await _controller.DeleteMessage(MessageId);

			result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
		}

		[Test]
		public async Task DeleteMessage_should_delete_from_a_unit_inbox_in_the_callers_department()
		{
			_novu.Setup(x => x.DeleteUnitInboxMessage(15, DepartmentCode, MessageId)).ReturnsAsync(NovuInboxDeleteResult.Deleted);

			var result = await _controller.DeleteMessage(MessageId, 15);

			result.Should().BeOfType<OkResult>();
			_novu.Verify(x => x.DeleteUserInboxMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task DeleteMessage_should_not_touch_another_departments_unit_inbox()
		{
			var result = await _controller.DeleteMessage(MessageId, 99);

			result.Should().BeOfType<NotFoundResult>();
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task DeleteMessage_should_treat_an_unknown_unit_as_not_found()
		{
			var result = await _controller.DeleteMessage(MessageId, 12345);

			result.Should().BeOfType<NotFoundResult>();
			_novu.VerifyNoOtherCalls();
		}

		[TestCase("")]
		[TestCase("not-an-object-id")]
		[TestCase("66f1c2a9e4b0a1b2c3d4e5f6/../../preferences")]
		[TestCase("66f1c2a9e4b0a1b2c3d4e5f")]
		public async Task DeleteMessage_should_reject_anything_but_a_novu_message_id(string messageId)
		{
			var result = await _controller.DeleteMessage(messageId);

			result.Should().BeOfType<BadRequestObjectResult>();
			_novu.VerifyNoOtherCalls();
		}

		[Test]
		public async Task GetUnitSubscriberHash_should_return_the_hash_for_a_visible_unit_in_the_department()
		{
			_novu.Setup(x => x.GetUnitSubscriberHash(15, DepartmentCode)).Returns("unit-15-hash");

			var result = await _controller.GetUnitSubscriberHash(15);

			result.Value.Data.UnitId.Should().Be("15");
			result.Value.Data.SubscriberHash.Should().Be("unit-15-hash");
		}

		// The hash opens the unit's inbox once Novu enforces HMAC, so it is the unit inbox's credential.
		[TestCase(99)]
		[TestCase(12345)]
		public async Task GetUnitSubscriberHash_should_not_hand_out_a_hash_for_a_unit_the_caller_cannot_use(int unitId)
		{
			var result = await _controller.GetUnitSubscriberHash(unitId);

			result.Result.Should().BeOfType<NotFoundResult>();
			_novu.VerifyNoOtherCalls();
		}
	}
}
