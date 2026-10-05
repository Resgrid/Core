using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using File = System.IO.File;
using ContactsLocalization = Resgrid.Localization.Areas.User.Contacts.Contacts;
using LocationHistoryLocalization = Resgrid.Localization.Areas.User.Dispatch.LocationHistory;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Contacts/Delete and Contacts/DeleteCategory were plain GETs with no antiforgery check, so any link or
	/// &lt;img&gt; on another site deleted a contact (or an empty category) for a signed-in user holding
	/// Contacts_Delete. Both are now POST + antiforgery, the list pages post a token form instead of linking,
	/// and the existing department / permission / open-billing guards are unchanged. AddNote (a form POST that
	/// already carried the tag-helper token) now validates it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ContactDeleteCsrfTests
	{
		private const int OurDepartment = 9;
		private const string Officer = "officer";

		// SecureBaseController.Unauthorized() redirects here rather than returning a 401.
		private const string UnauthorizedPage = "/Public/Unauthorized";

		private Mock<IContactsService> _contacts;
		private Mock<IAuthorizationService> _authorization;
		private ContactsController _controller;

		[SetUp]
		public void Setup()
		{
			_contacts = new Mock<IContactsService>();
			_contacts.Setup(c => c.GetContactByIdAsync("ours")).ReturnsAsync(new Contact { ContactId = "ours", DepartmentId = OurDepartment });
			_contacts.Setup(c => c.GetContactByIdAsync("theirs")).ReturnsAsync(new Contact { ContactId = "theirs", DepartmentId = 10 });
			_contacts.Setup(c => c.GetContactCategoryByIdAsync("our-category")).ReturnsAsync(new ContactCategory { ContactCategoryId = "our-category", DepartmentId = OurDepartment });
			_contacts.Setup(c => c.GetContactCategoryByIdAsync("their-category")).ReturnsAsync(new ContactCategory { ContactCategoryId = "their-category", DepartmentId = 10 });

			_authorization = new Mock<IAuthorizationService>();
			_authorization.Setup(a => a.CanUserDeleteContactAsync(Officer, OurDepartment)).ReturnsAsync(true);

			var localizer = new Mock<IStringLocalizer<ContactsLocalization>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, name));

			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Officer),
					new Claim(ClaimTypes.PrimaryGroupSid, OurDepartment.ToString())
				}, "test"))
			};
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			_controller = new ContactsController(_contacts.Object, Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IAddressService>(), Mock.Of<IEventAggregator>(), Mock.Of<ICallsService>(), _authorization.Object,
				Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IRouteService>(), Mock.Of<IPhoneNumberProcesserProvider>(), Mock.Of<IProtectedReadService>(),
				Mock.Of<IContactPreplanOwnershipGate>(), localizer.Object, Mock.Of<IInvoicingService>(), Mock.Of<IFeatureToggleService>(),
				Mock.Of<ICallLocationHistoryService>(), Mock.Of<IOccupancyLocationLookup>(), Mock.Of<IStringLocalizer<LocationHistoryLocalization>>())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
			};
		}

		[TearDown]
		public void Cleanup() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;

		[TestCase(nameof(ContactsController.Delete))]
		[TestCase(nameof(ContactsController.DeleteCategory))]
		[TestCase(nameof(ContactsController.AddNote))]
		public void State_changing_contact_actions_accept_only_an_antiforgery_validated_post(string action)
		{
			var methods = typeof(ContactsController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Where(m => m.Name == action).ToList();

			methods.Should().ContainSingle($"{action} has no GET overload to fall back to");
			var method = methods.Single();
			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull($"{action} must be a POST");
			method.GetCustomAttribute<HttpGetAttribute>().Should().BeNull($"{action} must not answer a GET");
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull($"{action} must validate the antiforgery token");
		}

		[Test]
		public async Task Delete_of_another_departments_contact_is_refused_and_nothing_is_deleted()
		{
			(await _controller.Delete("theirs", default)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be(UnauthorizedPage);

			VerifyNoContactDeleted();
		}

		[Test]
		public async Task Delete_without_the_delete_permission_is_refused_and_nothing_is_deleted()
		{
			_authorization.Setup(a => a.CanUserDeleteContactAsync(Officer, OurDepartment)).ReturnsAsync(false);

			(await _controller.Delete("ours", default)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be(UnauthorizedPage);

			VerifyNoContactDeleted();
		}

		[TestCase(null, typeof(BadRequestResult))]
		[TestCase("missing", typeof(NotFoundResult))]
		public async Task Delete_of_a_blank_or_unknown_contact_deletes_nothing(string contactId, Type expected)
		{
			(await _controller.Delete(contactId, default)).Should().BeOfType(expected);

			VerifyNoContactDeleted();
		}

		[Test]
		public async Task Delete_of_our_contact_deletes_it_as_the_caller_in_the_callers_department()
		{
			(await _controller.Delete("ours", default)).Should().BeOfType<RedirectToActionResult>()
				.Which.ActionName.Should().Be("Index");

			_contacts.Verify(c => c.DeleteContactAsync("ours", Officer, OurDepartment, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Delete_of_a_contact_with_open_billing_returns_to_the_contact_with_a_notice()
		{
			_contacts.Setup(c => c.DeleteContactAsync("ours", Officer, OurDepartment, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException(ContactsService.HasOpenBillingReason));

			var result = (await _controller.Delete("ours", default)).Should().BeOfType<RedirectToActionResult>().Which;

			result.ActionName.Should().Be("View");
			result.RouteValues["contactId"].Should().Be("ours");
			_controller.TempData["ContactsMessage"].Should().Be("ContactHasOpenBillingNotice");
		}

		[Test]
		public async Task DeleteCategory_of_another_departments_category_is_refused_and_nothing_is_deleted()
		{
			(await _controller.DeleteCategory("their-category")).Should().BeOfType<RedirectResult>().Which.Url.Should().Be(UnauthorizedPage);

			_contacts.Verify(c => c.DeleteContactCategoryAsync(It.IsAny<ContactCategory>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DeleteCategory_of_our_empty_category_deletes_it()
		{
			(await _controller.DeleteCategory("our-category")).Should().BeOfType<RedirectToActionResult>()
				.Which.ActionName.Should().Be("Categories");

			_contacts.Verify(c => c.DeleteContactCategoryAsync(It.Is<ContactCategory>(x => x.ContactCategoryId == "our-category"), It.IsAny<CancellationToken>()), Times.Once);
		}

		/// <summary>
		/// A GET link left in a view would now 405 instead of deleting, so the pages must post a token form.
		/// The buttons render disabled and are enabled by the page script that binds them (no inline handler).
		/// </summary>
		[TestCase("Index.cshtml", "Delete", "deleteContactForm", "contact-delete", "resgrid.contacts.index.js")]
		[TestCase("Categories.cshtml", "DeleteCategory", "deleteCategoryForm", "contact-category-delete", "resgrid.contacts.categories.js")]
		public void The_list_pages_post_deletes_through_an_antiforgery_form(string view, string action, string formId, string buttonClass, string script)
		{
			var root = RepositoryRoot();
			var viewsDirectory = Path.Combine(root, "Web", "Resgrid.Web", "Areas", "User", "Views", "Contacts");
			var source = File.ReadAllText(Path.Combine(viewsDirectory, view));

			foreach (var file in Directory.GetFiles(viewsDirectory, "*.cshtml"))
			{
				var text = File.ReadAllText(file);
				text.Should().NotMatchRegex($@"Url\.Action\(\s*""{action}""\s*,\s*""Contacts""", $"{Path.GetFileName(file)} must not link to the {action} POST");
				text.Should().NotMatchRegex($@"<a\b[^>]*asp-action=""{action}""", $"{Path.GetFileName(file)} must not link to the {action} POST");
			}

			var form = Regex.Match(source, $@"<form id=""{formId}""[^>]*>");
			form.Success.Should().BeTrue($"{view} should render the #{formId} token form");
			form.Value.Should().Contain("method=\"post\"").And.Contain($"asp-action=\"{action}\"").And.Contain("asp-antiforgery=\"true\"");

			source.Should().Contain(buttonClass);
			Regex.IsMatch(source, $@"<button[^>]*{buttonClass}[^>]*\bdisabled\b").Should().BeTrue("the delete button renders disabled until the page script binds it");
			Regex.IsMatch(source, $@"<button[^>]*{buttonClass}[^>]*\bonclick=").Should().BeFalse("no inline handler");
			source.Should().Contain(script);

			var js = File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web", "wwwroot", "js", "app", "internal", "contacts", script));
			js.Should().Contain($"'.{buttonClass}'").And.Contain($"getElementById('{formId}')").And.Contain(".prop('disabled', false)");
		}

		private void VerifyNoContactDeleted() =>
			_contacts.Verify(c => c.DeleteContactAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");
			return directory!.FullName;
		}
	}
}
