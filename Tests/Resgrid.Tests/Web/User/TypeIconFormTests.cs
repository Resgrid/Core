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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Areas.User.Models.Types;
using Resgrid.Web.Helpers;
using Resgrid.Localization.Areas.User.Icons;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Custom icons on the unit type, call type, POI type and personnel role pages: a usable upload is stored after the
	/// type or role is saved, a rejected one comes back with its localized reason and saves nothing, Remove clears the
	/// uploaded icon, and the Roles page saves the role order that picks a person's map icon.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TypeIconFormTests
	{
		private const int DepartmentId = 12;
		private const int UnitTypeId = 10220;
		private const int CallTypeId = 3301;
		private const int PoiTypeId = 77;
		private const string UserId = "admin-1";
		private const string Key = "0123456789abcdef0123456789abcdef";
		private const int RoleId = 55;
		private const string Rejection = "text:IconRejectUnsupportedFormat";

		private Dictionary<Type, Mock> _mocks;

		[SetUp]
		public void SetUp()
		{
			_mocks = new Dictionary<Type, Mock>();

			M<IAuthorizationService>().Setup(x => x.CanUserAddUnitTypeAsync(UserId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserEditUnitTypeAsync(UserId, UnitTypeId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserAddCallTypeAsync(UserId)).ReturnsAsync(true);
			M<IAuthorizationService>().Setup(x => x.CanUserModifyCallTypeAsync(UserId, CallTypeId)).ReturnsAsync(true);
			M<ICustomStateService>().Setup(x => x.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			M<IUnitsService>().Setup(x => x.AddUnitTypeAsync(DepartmentId, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string name, int set, int? icon, CancellationToken _) => new UnitType { UnitTypeId = UnitTypeId, DepartmentId = d, Type = name });
			M<IUnitsService>().Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "MUG", IconKey = Key } });
			M<IUnitsService>().Setup(x => x.GetUnitTypeByIdAsync(UnitTypeId))
				.ReturnsAsync(() => new UnitType { UnitTypeId = UnitTypeId, DepartmentId = DepartmentId, Type = "MUG", IconKey = Key });
			M<IUnitsService>().Setup(x => x.SaveUnitTypeAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>())).ReturnsAsync((UnitType t, CancellationToken _) => t);
			M<ICallsService>().Setup(x => x.GetCallTypeByIdAsync(CallTypeId))
				.ReturnsAsync(() => new CallType { CallTypeId = CallTypeId, DepartmentId = DepartmentId, Type = "Cardiac Arrest", IconKey = Key });
			M<ICallsService>().Setup(x => x.SaveCallTypeAsync(It.IsAny<CallType>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallType t, CancellationToken _) => { if (t.CallTypeId == 0) t.CallTypeId = CallTypeId; return t; });
			M<ITypeIconsService>().Setup(x => x.ValidateIcon(It.Is<byte[]>(b => b.Length > 0 && b[0] == (byte)'G'))).Returns(TypeIconRejections.None);
			M<ITypeIconsService>().Setup(x => x.ValidateIcon(It.Is<byte[]>(b => b.Length > 0 && b[0] == (byte)'B'))).Returns(TypeIconRejections.UnsupportedFormat);
			M<IStringLocalizer<IconUpload>>().Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, "text:" + key));
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		#region Unit types

		[Test]
		public async Task NewUnitType_uploads_a_usable_icon_for_the_new_type()
		{
			var result = await Build<TypesController>().NewUnitType(new EditUnitTypeView { UnitType = new UnitType { Type = "MUG", BaseType = -1 }, IconFile = File("Good") }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.UnitType, UnitTypeId, UserId, It.Is<byte[]>(b => b[0] == (byte)'G'), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewUnitType_with_a_rejected_icon_returns_the_form_and_creates_nothing()
		{
			var controller = Build<TypesController>();

			var result = await controller.NewUnitType(new EditUnitTypeView { UnitType = new UnitType { Type = "MUG", BaseType = -1 }, IconFile = File("Bad") }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["IconFile"].Errors.Single().ErrorMessage.Should().Be(Rejection);
			M<IUnitsService>().Verify(x => x.AddUnitTypeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditUnitType_uploads_after_saving_the_fields()
		{
			var order = new List<string>();
			M<IUnitsService>().Setup(x => x.SaveUnitTypeAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("save")).ReturnsAsync((UnitType t, CancellationToken _) => t);
			M<ITypeIconsService>().Setup(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.UnitType, UnitTypeId, UserId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("upload")).ReturnsAsync(Key);

			var result = await Build<TypesController>().EditUnitType(EditUnit(File("Good")), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			order.Should().Equal(new[] { "save", "upload" }, "the field save writes the IconKey it loaded, so the upload must set the new key after it");
		}

		[Test]
		public async Task EditUnitType_with_a_rejected_icon_saves_nothing_and_keeps_the_current_icon_on_the_form()
		{
			var controller = Build<TypesController>();

			var result = await controller.EditUnitType(EditUnit(File("Bad")), CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<EditUnitTypeView>().Subject;
			model.UnitType.IconKey.Should().Be(Key);
			controller.ModelState["IconFile"].Errors.Single().ErrorMessage.Should().Be(Rejection);
			M<IUnitsService>().Verify(x => x.SaveUnitTypeAsync(It.IsAny<UnitType>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditUnitType_remove_clears_the_uploaded_icon()
		{
			var view = EditUnit(null);
			view.RemoveCustomIcon = true;

			await Build<TypesController>().EditUnitType(view, CancellationToken.None);

			M<ITypeIconsService>().Verify(x => x.RemoveIconAsync(DepartmentId, TypeIconKinds.UnitType, UnitTypeId, It.IsAny<CancellationToken>()), Times.Once);
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Unit types

		#region Call types

		[Test]
		public async Task NewCallType_uploads_a_usable_icon_for_the_new_type()
		{
			var result = await Build<TypesController>().NewCallType(new EditCallTypeView { CallType = new CallType { Type = "Cardiac Arrest", BaseType = -1 }, CallTypeIcon = -1, IconFile = File("Good") }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.CallType, CallTypeId, UserId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task NewCallType_with_a_rejected_icon_returns_the_form_and_creates_nothing()
		{
			var result = await Build<TypesController>().NewCallType(new EditCallTypeView { CallType = new CallType { Type = "Cardiac Arrest", BaseType = -1 }, CallTypeIcon = -1, IconFile = File("Bad") }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			M<ICallsService>().Verify(x => x.SaveCallTypeAsync(It.IsAny<CallType>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task EditCallType_uploads_after_saving_and_remove_clears()
		{
			var order = new List<string>();
			M<ICallsService>().Setup(x => x.SaveCallTypeAsync(It.IsAny<CallType>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("save")).ReturnsAsync((CallType t, CancellationToken _) => t);
			M<ITypeIconsService>().Setup(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.CallType, CallTypeId, UserId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("upload")).ReturnsAsync(Key);

			(await Build<TypesController>().EditCallType(EditCall(File("Good")), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			order.Should().Equal("save", "upload");

			var remove = EditCall(null);
			remove.RemoveCustomIcon = true;
			await Build<TypesController>().EditCallType(remove, CancellationToken.None);
			M<ITypeIconsService>().Verify(x => x.RemoveIconAsync(DepartmentId, TypeIconKinds.CallType, CallTypeId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task EditCallType_with_a_rejected_icon_saves_nothing_and_keeps_the_current_icon_on_the_form()
		{
			var result = await Build<TypesController>().EditCallType(EditCall(File("Bad")), CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<EditCallTypeView>().Which.CallType.IconKey.Should().Be(Key);
			M<ICallsService>().Verify(x => x.SaveCallTypeAsync(It.IsAny<CallType>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Call types

		#region POI types

		[Test]
		public async Task SetPOITypeIcon_uploads_for_the_departments_type()
		{
			M<IMappingService>().Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync(new PoiType { PoiTypeId = PoiTypeId, DepartmentId = DepartmentId, Name = "Hospitals" });

			var result = await Build<MappingController>(admin: true).SetPOITypeIcon(PoiTypeId, File("Good"), false, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.PoiType, PoiTypeId, UserId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SetPOITypeIcon_refuses_another_departments_type()
		{
			M<IMappingService>().Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync(new PoiType { PoiTypeId = PoiTypeId, DepartmentId = DepartmentId + 1, Name = "Hospitals" });

			var result = await Build<MappingController>(admin: true).SetPOITypeIcon(PoiTypeId, File("Good"), false, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task SetPOITypeIcon_with_a_rejected_icon_stores_nothing_and_reports_why()
		{
			M<IMappingService>().Setup(x => x.GetTypeByIdAsync(PoiTypeId)).ReturnsAsync(new PoiType { PoiTypeId = PoiTypeId, DepartmentId = DepartmentId, Name = "Hospitals" });
			var controller = Build<MappingController>(admin: true);

			await controller.SetPOITypeIcon(PoiTypeId, File("Bad"), false, CancellationToken.None);

			controller.TempData["PoiTypeIconError"].Should().Be(Rejection);
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(It.IsAny<int>(), It.IsAny<TypeIconKinds>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion POI types

		#region Upload limits

		[Test]
		public async Task An_icon_over_the_byte_limit_is_refused_before_it_is_read()
		{
			var oversized = new FormFile(new MemoryStream(new byte[] { (byte)'G' }), 0, TypeIcons.MaxUploadBytes + 1, "IconFile", "huge.png");
			var controller = Build<TypesController>();

			var result = await controller.NewUnitType(new EditUnitTypeView { UnitType = new UnitType { Type = "MUG", BaseType = -1 }, IconFile = oversized }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["IconFile"].Errors.Single().ErrorMessage.Should().Be("text:IconRejectTooManyBytes");
			M<ITypeIconsService>().Verify(x => x.ValidateIcon(It.IsAny<byte[]>()), Times.Never, "the size check runs before the file is buffered");
		}

		[TestCase(TypeIconRejections.Required, "IconRejectRequired")]
		[TestCase(TypeIconRejections.TooManyBytes, "IconRejectTooManyBytes")]
		[TestCase(TypeIconRejections.Unreadable, "IconRejectUnreadable")]
		[TestCase(TypeIconRejections.UnsupportedFormat, "IconRejectUnsupportedFormat")]
		[TestCase(TypeIconRejections.TooLarge, "IconRejectTooLarge")]
		[TestCase(TypeIconRejections.TooSmall, "IconRejectTooSmall")]
		public void Every_rejection_has_its_own_localized_message(TypeIconRejections rejection, string key)
		{
			TypeIconUploads.RejectionMessage(M<IStringLocalizer<IconUpload>>().Object, rejection).Should().Be("text:" + key);
		}

		#endregion Upload limits

		#region Personnel roles

		[Test]
		public async Task AddRole_uploads_the_icon_after_saving_and_ignores_a_posted_icon_key_and_order()
		{
			PersonnelRole saved = null;
			M<IPersonnelRolesService>().Setup(x => x.SaveRoleAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Callback((PersonnelRole r, CancellationToken _, string __) => saved = r)
				.ReturnsAsync((PersonnelRole r, CancellationToken _, string __) => { r.PersonnelRoleId = RoleId; return r; });

			var model = new Resgrid.Web.Areas.User.Models.Personnel.AddRoleModel
			{
				Role = new PersonnelRole { Name = "Paramedic", IconKey = "fedcba9876543210fedcba9876543210", SortOrder = 99 },
				IconFile = File("Good")
			};

			var result = await BuildPersonnel().AddRole(model, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			saved.IconKey.Should().BeNull("a role's icon is only ever set by the upload");
			saved.SortOrder.Should().Be(0, "the order is only ever set by the Roles page");
			M<ITypeIconsService>().Verify(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.PersonnelRole, RoleId, UserId, It.Is<byte[]>(b => b[0] == (byte)'G'), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task AddRole_with_a_rejected_icon_returns_the_form_and_saves_nothing()
		{
			var controller = BuildPersonnel();

			var result = await controller.AddRole(new Resgrid.Web.Areas.User.Models.Personnel.AddRoleModel { Role = new PersonnelRole { Name = "Paramedic" }, IconFile = File("Bad") }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
			controller.ModelState["IconFile"].Errors.Single().ErrorMessage.Should().Be(Rejection);
			M<IPersonnelRolesService>().Verify(x => x.SaveRoleAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task EditRole_uploads_after_the_role_save_and_remove_clears()
		{
			var order = new List<string>();
			SetUpEditableRole();
			M<IPersonnelRolesService>().Setup(x => x.ReplaceRoleMembersAsync(It.IsAny<PersonnelRole>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Callback(() => order.Add("save")).ReturnsAsync((PersonnelRole r, IEnumerable<string> _, CancellationToken __, string ___) => r);
			M<ITypeIconsService>().Setup(x => x.UploadIconAsync(DepartmentId, TypeIconKinds.PersonnelRole, RoleId, UserId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Callback(() => order.Add("upload")).ReturnsAsync(Key);

			var result = await BuildPersonnel().EditRole(EditRole(File("Good")), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			order.Should().Equal(new[] { "save", "upload" }, "the role save writes back the IconKey it loaded, so the upload must set the new key after it");

			var remove = EditRole(null);
			remove.RemoveCustomIcon = true;
			await BuildPersonnel().EditRole(remove, new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);
			M<ITypeIconsService>().Verify(x => x.RemoveIconAsync(DepartmentId, TypeIconKinds.PersonnelRole, RoleId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task EditRole_with_a_rejected_icon_saves_nothing_and_keeps_the_current_icon_on_the_form()
		{
			SetUpEditableRole();

			var result = await BuildPersonnel().EditRole(EditRole(File("Bad")), new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<Resgrid.Web.Areas.User.Models.Personnel.EditRoleModel>().Subject;
			model.Role.IconKey.Should().Be(Key);
			M<IPersonnelRolesService>().Verify(x => x.ReplaceRoleMembersAsync(It.IsAny<PersonnelRole>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task SetRoleOrder_saves_the_posted_order_for_a_department_admin()
		{
			M<IPersonnelRolesService>().Setup(x => x.SetRoleOrderAsync(DepartmentId, It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var controller = BuildPersonnel(admin: true);

			var result = await controller.SetRoleOrder(new List<int> { 3, 1, 2 }, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Roles");
			controller.TempData["RoleOrderSaved"].Should().Be("true");
			M<IPersonnelRolesService>().Verify(x => x.SetRoleOrderAsync(DepartmentId, It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 3, 1, 2 })), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task SetRoleOrder_is_refused_for_a_member_who_is_not_a_department_admin()
		{
			var result = await BuildPersonnel(admin: false).SetRoleOrder(new List<int> { 3, 1, 2 }, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			M<IPersonnelRolesService>().Verify(x => x.SetRoleOrderAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Roles_lists_the_roles_in_role_order()
		{
			M<IPersonnelRolesService>().Setup(x => x.GetRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole>
			{
				new PersonnelRole { PersonnelRoleId = 1, Name = "Firefighter", SortOrder = 2, Users = new List<PersonnelRoleUser>() },
				new PersonnelRole { PersonnelRoleId = 2, Name = "Officer", SortOrder = 1, Users = new List<PersonnelRoleUser>() }
			});

			var result = await BuildPersonnel(admin: true).Roles();

			var model = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<Resgrid.Web.Areas.User.Models.Personnel.PersonnelRolesModel>().Subject;
			model.Roles.Select(r => r.Name).Should().Equal("Officer", "Firefighter");
			model.CanOrderRoles.Should().BeTrue();
		}

		private void SetUpEditableRole()
		{
			M<IAuthorizationService>().Setup(x => x.CanUserEditRoleAsync(UserId, RoleId)).ReturnsAsync(true);
			M<IPersonnelRolesService>().Setup(x => x.GetRoleByIdAsync(RoleId))
				.ReturnsAsync(() => new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = DepartmentId, Name = "Paramedic", IconKey = Key, Users = new List<PersonnelRoleUser>() });
			M<IDepartmentsService>().Setup(x => x.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser>());
		}

		private static Resgrid.Web.Areas.User.Models.Personnel.EditRoleModel EditRole(IFormFile file) => new Resgrid.Web.Areas.User.Models.Personnel.EditRoleModel
		{
			Role = new PersonnelRole { PersonnelRoleId = RoleId, DepartmentId = DepartmentId, Name = "Paramedic" },
			IconFile = file
		};

		/// <summary>The Personnel controller takes a UserManager, which a loose mock cannot build, so it is constructed with only what the role pages use.</summary>
		private PersonnelController BuildPersonnel(bool admin = true)
		{
			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
			};
			// A department admin is the Department Update claim (ClaimsAuthorizationHelper.IsUserDepartmentAdmin).
			if (admin)
				claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));

			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			return new PersonnelController(M<IDepartmentsService>().Object, M<IUsersService>().Object, null, null, M<IUserProfileService>().Object, null,
				M<IAuthorizationService>().Object, null, M<IPersonnelRolesService>().Object, null, null, null, null, null, null, null, null, null, null, null, null,
				null, null, null, null, null, null, null, Mock.Of<IDispatchScopeService>(), typeIconsService: M<ITypeIconsService>().Object,
				iconLocalizer: M<IStringLocalizer<IconUpload>>().Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		#endregion Personnel roles

		[Test]
		public void Icon_html_prefers_the_upload_then_the_built_in_pin_and_encodes_the_url()
		{
			var types = new List<UnitType>
			{
				new UnitType { Type = "MUG", IconKey = Key, MapIconType = (int)MapIconTypes.Ambulance },
				new UnitType { Type = "Ambulance", MapIconType = (int)MapIconTypes.Ambulance },
				new UnitType { Type = "Bike Team" }
			};

			TypeIconHtml.UrlForTypeName(types, "mug").Should().Be("/TypeIcons/" + Key);
			TypeIconHtml.UrlForTypeName(types, "Ambulance").Should().Be("/images/Mapping/ambulance.png");
			TypeIconHtml.UrlForTypeName(types, "Bike Team").Should().BeNull();
			TypeIconHtml.Url(new CallType { MapIconType = (int)MapIconTypes.Fire }).Should().Be("/images/Mapping/fire.png");
			TypeIconHtml.Url(new PoiType { Image = "map-icon-hospital" }).Should().BeNull("POI built-ins are icon-font classes, not images");
			TypeIconHtml.Image(types, "Bike Team").ToString().Should().BeEmpty("a type without an icon renders the row as before");
			TypeIconHtml.Image("/x\"onerror=\"alert(1)").ToString().Should().Contain("&quot;onerror=&quot;");
		}

		private static EditUnitTypeView EditUnit(IFormFile file) => new EditUnitTypeView
		{
			UnitType = new UnitType { UnitTypeId = UnitTypeId, Type = "MUG", BaseType = -1 },
			UnitTypeIcon = -1,
			IconFile = file
		};

		private static EditCallTypeView EditCall(IFormFile file) => new EditCallTypeView
		{
			CallType = new CallType { CallTypeId = CallTypeId, Type = "Cardiac Arrest", BaseType = -1 },
			CallTypeIcon = -1,
			IconFile = file
		};

		private static IFormFile File(string content)
		{
			var bytes = System.Text.Encoding.ASCII.GetBytes(content);
			return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "IconFile", "icon.png");
		}

		private Mock<T> M<T>() where T : class
		{
			if (!_mocks.TryGetValue(typeof(T), out var mock))
			{
				mock = new Mock<T>();
				_mocks[typeof(T)] = mock;
			}

			return (Mock<T>)mock;
		}

		/// <summary>A controller from loose mocks (the configured ones where a test set them up) and a signed-in department admin.</summary>
		private TController Build<TController>(bool admin = false) where TController : Controller
		{
			var constructor = typeof(TController).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
			{
				if (!_mocks.TryGetValue(p.ParameterType, out var mock))
				{
					mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType));
					_mocks[p.ParameterType] = mock;
				}

				return mock.Object;
			}).ToArray();

			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.PrimarySid, UserId),
				new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
				new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
			};
			if (admin)
				claims.Add(new Claim(ClaimTypes.Role, "Admins"));

			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			var controller = (TController)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
			return controller;
		}
	}
}
