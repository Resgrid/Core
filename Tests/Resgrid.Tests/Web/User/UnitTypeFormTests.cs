using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Types;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Unit type pages: New Unit Type saves the status set and map icon the page posts (it used to read only the
	/// UnitType.* names, which the page never posts), only this department's status sets can be linked on New or Edit,
	/// and Edit Unit Type shows the saved icon so saving the page keeps it. Also guards the Unit Staffing role check,
	/// which must read the member's roles from selectize's option store.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class UnitTypeFormTests
	{
		private const int DepartmentId = 12;
		private const int OwnSetId = 4548;
		private const int OtherDepartmentSetId = 777;
		private const int UnitTypeId = 10220;
		private const string UserId = "admin-1";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IAuthorizationService>().Setup(x => x.CanUserAddUnitTypeAsync(UserId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserEditUnitTypeAsync(UserId, UnitTypeId)).ReturnsAsync(true);
			M<ICustomStateService>().Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<CustomState> { new CustomState { CustomStateId = OwnSetId, DepartmentId = DepartmentId, Name = "Crisis Team Statuses" } });
			M<IUnitsService>().Setup(x => x.AddUnitTypeAsync(DepartmentId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string name, int set, int? icon, CancellationToken _) => new UnitType { UnitTypeId = UnitTypeId, DepartmentId = d, Type = name });
			M<IUnitsService>().Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType>());
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		#region New Unit Type

		[Test]
		public async Task NewUnitType_saves_the_status_set_and_icon_the_page_posts()
		{
			await Build().NewUnitType(new EditUnitTypeView
			{
				UnitType = new UnitType { Type = "Mobile Crisis Team", BaseType = -1 },
				UnitCustomStatesId = OwnSetId,
				UnitTypeIcon = 37
			}, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(DepartmentId, "Mobile Crisis Team", OwnSetId, 37, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnitType_with_the_default_icon_and_standard_statuses_saves_neither()
		{
			await Build().NewUnitType(new EditUnitTypeView
			{
				UnitType = new UnitType { Type = "Follow-Up Team", BaseType = -1 },
				UnitCustomStatesId = 0,
				UnitTypeIcon = -1
			}, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(DepartmentId, "Follow-Up Team", 0, It.Is<int?>(i => i == null || i < 0), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnitType_still_reads_the_UnitType_names_when_the_page_fields_are_not_posted()
		{
			await Build().NewUnitType(new EditUnitTypeView
			{
				UnitType = new UnitType { Type = "Mobile Crisis Team", BaseType = -1, CustomStatesId = OwnSetId, MapIconType = 24 }
			}, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(DepartmentId, "Mobile Crisis Team", OwnSetId, 24, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnitType_never_links_another_departments_status_set()
		{
			await Build().NewUnitType(new EditUnitTypeView
			{
				UnitType = new UnitType { Type = "Mobile Crisis Team", BaseType = -1 },
				UnitCustomStatesId = OtherDepartmentSetId,
				UnitTypeIcon = 37
			}, CancellationToken.None);

			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(DepartmentId, "Mobile Crisis Team", 0, 37, It.IsAny<CancellationToken>()), Times.Once);
			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(It.IsAny<int>(), It.IsAny<string>(), OtherDepartmentSetId, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion New Unit Type

		#region Edit Unit Type

		[Test]
		public async Task EditUnitType_get_puts_the_saved_icon_on_the_model()
		{
			M<IUnitsService>().Setup(x => x.GetUnitTypeByIdAsync(UnitTypeId))
				.ReturnsAsync(new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "Mobile Crisis Team", CustomStatesId = OwnSetId, MapIconType = 37 });

			var result = await Build().EditUnitType(UnitTypeId);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<EditUnitTypeView>().Subject;
			model.UnitTypeIcon.Should().Be(37);
			model.UnitCustomStatesId.Should().Be(OwnSetId);
		}

		[Test]
		public async Task EditUnitType_get_without_an_icon_selects_the_default_option()
		{
			M<IUnitsService>().Setup(x => x.GetUnitTypeByIdAsync(UnitTypeId))
				.ReturnsAsync(new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "Follow-Up Team" });

			var result = await Build().EditUnitType(UnitTypeId);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<EditUnitTypeView>().Which.UnitTypeIcon.Should().Be(-1);
		}

		[Test]
		public async Task EditUnitType_post_keeps_the_stored_set_when_another_departments_set_is_posted()
		{
			var stored = new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "Mobile Crisis Team", CustomStatesId = OwnSetId, MapIconType = 37 };
			M<IUnitsService>().Setup(x => x.GetUnitTypeByIdAsync(UnitTypeId)).ReturnsAsync(stored);
			UnitType saved = null;
			M<IUnitsService>().Setup(x => x.SaveUnitTypeAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>()))
				.Callback((UnitType t, CancellationToken _) => saved = t).ReturnsAsync((UnitType t, CancellationToken _) => t);

			await Build().EditUnitType(new EditUnitTypeView
			{
				UnitType = new UnitType { UnitTypeId = UnitTypeId, Type = "Mobile Crisis Team", BaseType = 6 },
				UnitCustomStatesId = OtherDepartmentSetId,
				UnitTypeIcon = 37
			}, CancellationToken.None);

			saved.Should().NotBeNull();
			saved.CustomStatesId.Should().Be(OwnSetId);
			saved.MapIconType.Should().Be(37);
		}

		[Test]
		public async Task EditUnitType_post_links_one_of_the_departments_sets_and_clears_on_standard()
		{
			var stored = new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "Mobile Crisis Team" };
			M<IUnitsService>().Setup(x => x.GetUnitTypeByIdAsync(UnitTypeId)).ReturnsAsync(stored);
			var saves = new List<int?>();
			M<IUnitsService>().Setup(x => x.SaveUnitTypeAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>()))
				.Callback((UnitType t, CancellationToken _) => saves.Add(t.CustomStatesId)).ReturnsAsync((UnitType t, CancellationToken _) => t);

			await Build().EditUnitType(new EditUnitTypeView { UnitType = new UnitType { UnitTypeId = UnitTypeId, Type = "Mobile Crisis Team" }, UnitCustomStatesId = OwnSetId, UnitTypeIcon = -1 }, CancellationToken.None);
			await Build().EditUnitType(new EditUnitTypeView { UnitType = new UnitType { UnitTypeId = UnitTypeId, Type = "Mobile Crisis Team" }, UnitCustomStatesId = 0, UnitTypeIcon = -1 }, CancellationToken.None);

			saves.Should().Equal(OwnSetId, null);
		}

		#endregion Edit Unit Type

		#region Views

		[Test]
		public void NewUnitType_view_posts_the_fields_the_controller_reads()
		{
			var view = ReadView("Web/Resgrid.Web/Areas/User/Views/Types/NewUnitType.cshtml");

			view.Should().Contain("m => m.UnitCustomStatesId", "the status set is posted as UnitCustomStatesId");
			view.Should().Contain("name=\"UnitTypeIcon\"", "the icon is posted as UnitTypeIcon");
		}

		[Test]
		public void EditUnitType_view_binds_the_icon_select_so_the_saved_icon_is_selected()
		{
			var view = ReadView("Web/Resgrid.Web/Areas/User/Views/Types/EditUnitType.cshtml");

			view.Should().Contain("<select asp-for=\"UnitTypeIcon\">",
				"with asp-for the option tag helper marks the saved icon selected; a plain select always showed Default and saving cleared the icon");
			view.Should().NotContain("<select name=\"UnitTypeIcon\"");
		}

		[Test]
		public void UnitStaffing_role_check_reads_roles_from_the_selectize_option_store()
		{
			var view = ReadView("Web/Resgrid.Web/Areas/User/Views/Units/UnitStaffing.cshtml");

			view.Should().Contain("el.selectize.options[val]",
				"selectize keeps option data in its own store and strips data-data from the select, so reading only the <option> warned on every seat");
			view.IndexOf("el.selectize.options[val]", StringComparison.Ordinal).Should()
				.BeLessThan(view.IndexOf("attr('data-data')", StringComparison.Ordinal), "the selectize store is read before the plain option fallback");
		}

		#endregion Views

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		/// <summary>TypesController from loose mocks (the configured ones where a test set them up) and a signed-in department admin.</summary>
		private TypesController Build()
		{
			var constructor = typeof(TypesController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var identity = new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
				new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
			}, "test");
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (TypesController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		private static string ReadView(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			return System.IO.File.ReadAllText(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		}
	}
}
