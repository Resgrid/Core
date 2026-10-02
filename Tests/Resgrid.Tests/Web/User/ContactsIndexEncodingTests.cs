using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Contacts;
using Resgrid.WebCore.Areas.User.Models;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The contacts page renders the category tree with bstreeview, which appends each node's text as HTML.
	/// User-entered category names therefore have to reach the page encoded.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class ContactsIndexEncodingTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "contacts-user";

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task Index_EncodesCategoryNamesInTheTreeData()
		{
			const string hostileName = "<img src=x onerror=alert(1)>";
			var contacts = new Mock<IContactsService>();
			contacts.Setup(s => s.GetContactCategoriesForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<ContactCategory> { new ContactCategory { ContactCategoryId = "5", DepartmentId = DepartmentId, Name = hostileName } });
			contacts.Setup(s => s.GetAllContactsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Contact>());
			contacts.Setup(s => s.GetPreplansDueForReviewAsync(DepartmentId)).ReturnsAsync(new List<ContactPreplan>());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(s => s.GetDepartmentByIdAsync(DepartmentId, false)).ReturnsAsync(new Department { DepartmentId = DepartmentId });

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

			var controller = new ContactsController(contacts.Object, departments.Object, Mock.Of<IUserProfileService>(),
				Mock.Of<IAddressService>(), Mock.Of<IEventAggregator>(), Mock.Of<ICallsService>(), Mock.Of<IAuthorizationService>(),
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IRouteService>(), Mock.Of<IPhoneNumberProcesserProvider>(), Mock.Of<IProtectedReadService>(),
				Mock.Of<IContactPreplanOwnershipGate>(), Mock.Of<IStringLocalizer<Resgrid.Localization.Areas.User.Contacts.Contacts>>(),
				Mock.Of<IInvoicingService>(), Mock.Of<IFeatureToggleService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};

			var result = await controller.Index();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<ContactsIndexView>().Subject;
			var nodes = JsonConvert.DeserializeObject<List<BSTreeModel>>(model.TreeData);
			nodes.Single(n => n.id == "TreeGroup_5").text.Should().Be("&lt;img src=x onerror=alert(1)&gt;");
			model.TreeData.Should().NotContain("<img");
		}
	}
}
