using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Saving a custom field definition from a client that does not send Sensitivity keeps each field's Protected Workflows
	/// tag, so a Part 2 field is never silently re-opened for release.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class UserDefinedFieldsControllerTests
	{
		private const int DepartmentId = 12;
		private const string UserId = "udf-admin";
		private const string DispositionFieldId = "field-disposition";

		private Mock<IUserDefinedFieldsService> _udfService;
		private List<UdfField> _savedFields;
		private UserDefinedFieldsController _controller;

		[SetUp]
		public void SetUp()
		{
			_udfService = new Mock<IUserDefinedFieldsService>();
			_udfService.Setup(x => x.GetActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Call))
				.ReturnsAsync(new UdfDefinition { UdfDefinitionId = "def-1", DepartmentId = DepartmentId, EntityType = (int)UdfEntityType.Call, Version = 1 });
			_udfService.Setup(x => x.GetFieldsForActiveDefinitionAsync(DepartmentId, (int)UdfEntityType.Call))
				.ReturnsAsync(new List<UdfField>
				{
					new UdfField { UdfFieldId = DispositionFieldId, Name = "disposition", Label = "Disposition", Sensitivity = (int)UdfFieldSensitivity.Part2 }
				});
			_udfService.Setup(x => x.SaveDefinitionAsync(DepartmentId, (int)UdfEntityType.Call, It.IsAny<List<UdfField>>(), UserId, It.IsAny<CancellationToken>()))
				.Callback<int, int, List<UdfField>, string, CancellationToken>((d, e, fields, u, c) => _savedFields = fields)
				.ReturnsAsync(new UdfDefinition { UdfDefinitionId = "def-2", DepartmentId = DepartmentId, EntityType = (int)UdfEntityType.Call, Version = 2 });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			_controller = new UserDefinedFieldsController(_udfService.Object, Mock.Of<IUdfRenderingService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<IProtectedReadService>())
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
		public async Task Renaming_a_field_without_sending_sensitivity_keeps_its_tag()
		{
			await SaveAsync(new UdfFieldInput { UdfFieldId = DispositionFieldId, Name = "outcome", Label = "Outcome" });

			_savedFields.Single().Sensitivity.Should().Be((int)UdfFieldSensitivity.Part2);
		}

		[Test]
		public async Task A_field_sent_without_its_id_keeps_its_tag_by_name()
		{
			await SaveAsync(new UdfFieldInput { Name = " Disposition ", Label = "Disposition" });

			_savedFields.Single().Sensitivity.Should().Be((int)UdfFieldSensitivity.Part2);
		}

		[Test]
		public async Task A_sensitivity_the_client_sends_is_kept_as_sent()
		{
			await SaveAsync(new UdfFieldInput { UdfFieldId = DispositionFieldId, Name = "disposition", Label = "Disposition", Sensitivity = (int)UdfFieldSensitivity.None });

			_savedFields.Single().Sensitivity.Should().Be((int)UdfFieldSensitivity.None);
		}

		[Test]
		public async Task A_new_field_starts_untagged()
		{
			await SaveAsync(new UdfFieldInput { Name = "shift_notes", Label = "Shift notes" });

			_savedFields.Single().Sensitivity.Should().Be((int)UdfFieldSensitivity.None);
		}

		private async Task SaveAsync(UdfFieldInput field)
		{
			var response = await _controller.SaveDefinition(new SaveUdfDefinitionInput
			{
				EntityType = (int)UdfEntityType.Call,
				Fields = new List<UdfFieldInput> { field }
			}, CancellationToken.None);

			response.Result.Should().BeOfType<OkObjectResult>();
		}
	}
}
