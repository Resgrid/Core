using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Protocols;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using V4 = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Round 10 (Omni): Protocols/GetCategories names the categories behind ProtocolResultData.CategoryId: the caller's department only, in
	/// the department's sort order, with the parent and the kind; Protocol_View like the library.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ProtocolCategoriesApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "api-user";

		private Mock<IProtocolLibraryService> _library;

		[SetUp]
		public void SetUp()
		{
			_library = new Mock<IProtocolLibraryService>();
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "test"))
			};
			ApiClaims._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
		}

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = null;

		private V4.ProtocolsController Controller() =>
			new V4.ProtocolsController(Mock.Of<IProtocolsService>(), Mock.Of<IStepUpCapabilityService>(), library: _library.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = ApiClaims._httpContextAccessor.HttpContext }
			};

		[Test]
		public void GetCategories_needs_protocol_view()
		{
			typeof(V4.ProtocolsController).GetMethod(nameof(V4.ProtocolsController.GetCategories))!.GetCustomAttributes<AuthorizeAttribute>()
				.Select(a => a.Policy).Should().ContainSingle().Which.Should().Be(ResgridResources.Protocol_View);
		}

		[Test]
		public async Task GetCategories_returns_the_departments_categories_in_their_order_with_parent_and_kind()
		{
			_library.Setup(x => x.GetCategoriesAsync(DepartmentId)).ReturnsAsync(new List<ProtocolCategory>
			{
				new ProtocolCategory { ProtocolCategoryId = 3, DepartmentId = DepartmentId, Name = "Hazmat", SortOrder = 2, ParentCategoryId = 1, Kind = (int)ProtocolKinds.Procedure },
				new ProtocolCategory { ProtocolCategoryId = 1, DepartmentId = DepartmentId, Name = "Operations", SortOrder = 1 },
				new ProtocolCategory { ProtocolCategoryId = 2, DepartmentId = DepartmentId, Name = "Admin", SortOrder = 2 },
				new ProtocolCategory { ProtocolCategoryId = 9, DepartmentId = 99, Name = "Another department's", SortOrder = 0 },
				null
			});

			var result = (GetProtocolCategoriesResult)((OkObjectResult)(await Controller().GetCategories()).Result).Value;

			result.Data.Select(c => c.CategoryId).Should().Equal(1, 2, 3);
			var hazmat = result.Data.Single(c => c.CategoryId == 3);
			hazmat.Name.Should().Be("Hazmat");
			hazmat.ParentId.Should().Be(1);
			hazmat.SortOrder.Should().Be(2);
			hazmat.Kind.Should().Be((int)ProtocolKinds.Procedure);
			result.Data.Single(c => c.CategoryId == 1).ParentId.Should().BeNull();
			result.PageSize.Should().Be(3);
			result.Status.Should().Be("success");
		}

		[Test]
		public async Task GetCategories_with_none_is_an_empty_success()
		{
			_library.Setup(x => x.GetCategoriesAsync(DepartmentId)).ReturnsAsync((List<ProtocolCategory>)null);

			var result = (GetProtocolCategoriesResult)((OkObjectResult)(await Controller().GetCategories()).Result).Value;

			result.Data.Should().BeEmpty();
			result.PageSize.Should().Be(0);
		}
	}
}
