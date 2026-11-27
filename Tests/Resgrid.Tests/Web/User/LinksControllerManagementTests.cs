using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Links;
using File = System.IO.File;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Department Links management: creating, accepting (Enable / EnableLink), disabling and deleting a link are
	/// department-admin actions (Department_Update, the same claim the Index page checks before showing the buttons);
	/// EnableLink, DisableLink and DeleteLink are antiforgery POSTs instead of GET links; accepting and disabling stay
	/// with the linked department, while either party can delete (DeleteLink used to do nothing, so the creating
	/// department could never revoke a link); and an unknown link id is refused instead of throwing.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LinksControllerManagementTests
	{
		private const int Creator = 100;
		private const int Linked = 200;
		private const int Outsider = 300;
		private const int LinkId = 7;
		private const int UnknownLinkId = 404;

		// SecureBaseController.Unauthorized() redirects here rather than returning a 401.
		private const string UnauthorizedPage = "/Public/Unauthorized";

		private static readonly string[] AdminActions =
		{
			nameof(LinksController.New), nameof(LinksController.Enable), nameof(LinksController.EnableLink),
			nameof(LinksController.DisableLink), nameof(LinksController.DeleteLink)
		};

		private Mock<IDepartmentLinksService> _links;
		private Mock<ISignatureService> _signatures;
		private DepartmentLink _link;

		[SetUp]
		public void Setup()
		{
			_link = new DepartmentLink { DepartmentLinkId = LinkId, DepartmentId = Creator, LinkedDepartmentId = Linked, LinkEnabled = true, LinkAccepted = DateTime.UtcNow };
			_links = new Mock<IDepartmentLinksService>();
			_links.Setup(l => l.GetLinkByIdAsync(LinkId)).ReturnsAsync(() => _link);
			_links.Setup(l => l.GetLinkByIdAsync(UnknownLinkId)).ReturnsAsync((DepartmentLink)null);
			_links.Setup(l => l.SaveAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>())).ReturnsAsync((DepartmentLink l, CancellationToken _) => l);
			_links.Setup(l => l.DeleteLinkAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			// Accepting a link is an L2 signature (round 10 A3); the step-up filter ran before the action.
			_signatures = new Mock<ISignatureService>();
			_signatures.Setup(s => s.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SignatureResult { Outcome = SignatureOutcomes.Signed, Record = new SignatureRecord { SignatureRecordId = 1 } });
		}

		[TearDown]
		public void Cleanup() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private LinksController As(int departmentId)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, "admin"),
					new Claim(ClaimTypes.PrimaryGroupSid, departmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

			return new LinksController(_links.Object, Mock.Of<IDepartmentsService>(), Mock.Of<IEmailService>(), Mock.Of<ICallsService>(),
				Mock.Of<IUnitsService>(), Mock.Of<IActionLogsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<ILimitsService>(), Mock.Of<IMappingService>(), Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IStringLocalizer<Resgrid.Localization.Common>>(), Mock.Of<ILinkedProtocolService>(), Mock.Of<IMutualAidCountersignService>(),
				Mock.Of<IRecordDeploymentPartnerService>(), _signatures.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		private void VerifyNothingWritten()
		{
			_links.Verify(l => l.SaveAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>()), Times.Never);
			_links.Verify(l => l.DeleteLinkAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		private static void ShouldBeRefused(IActionResult result)
			=> result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(UnauthorizedPage);

		private static void ShouldReturnToIndex(IActionResult result)
			=> result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");

		// ---- Attributes: admin-only, and the state-changing actions are antiforgery POSTs ----

		[Test]
		public void Every_link_management_action_requires_the_department_admin_policy()
		{
			foreach (var action in AdminActions)
			{
				var methods = typeof(LinksController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.Name == action).ToList();

				methods.Should().NotBeEmpty(action);
				methods.Should().OnlyContain(m => m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == ResgridResources.Department_Update),
					$"{action} creates, accepts, disables or deletes a link, which the Index page only offers department admins");
			}
		}

		[TestCase(nameof(LinksController.EnableLink))]
		[TestCase(nameof(LinksController.DisableLink))]
		[TestCase(nameof(LinksController.DeleteLink))]
		public void State_changing_link_actions_accept_only_an_antiforgery_validated_post(string action)
		{
			var methods = typeof(LinksController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.Name == action).ToList();

			methods.Should().ContainSingle($"{action} has no GET overload to fall back to");
			var method = methods.Single();
			method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull($"{action} must be a POST");
			method.GetCustomAttribute<HttpGetAttribute>().Should().BeNull($"{action} must not answer a GET");
			method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull($"{action} must validate the antiforgery token");
		}

		[TestCase(nameof(LinksController.New))]
		[TestCase(nameof(LinksController.Enable))]
		public void Link_form_posts_validate_the_antiforgery_token(string action)
		{
			typeof(LinksController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Where(m => m.Name == action && m.GetCustomAttribute<HttpPostAttribute>() != null).Should().ContainSingle()
				.Which.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
		}

		// ---- DeleteLink: either party, never an outsider ----

		[TestCase(Creator)]
		[TestCase(Linked)]
		public async Task Either_party_can_delete_the_link(int departmentId)
		{
			ShouldReturnToIndex(await As(departmentId).DeleteLink(LinkId, CancellationToken.None));

			_links.Verify(l => l.DeleteLinkAsync(It.Is<DepartmentLink>(x => x.DepartmentLinkId == LinkId), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_outside_department_cannot_delete_the_link()
		{
			ShouldBeRefused(await As(Outsider).DeleteLink(LinkId, CancellationToken.None));

			VerifyNothingWritten();
		}

		[Test]
		public async Task Deleting_an_unknown_link_is_refused()
		{
			ShouldBeRefused(await As(Creator).DeleteLink(UnknownLinkId, CancellationToken.None));

			VerifyNothingWritten();
		}

		// ---- Accept / disable: the linked department only ----

		[Test]
		public async Task The_linked_department_can_disable_and_re_enable_the_link()
		{
			var controller = As(Linked);

			ShouldReturnToIndex(await controller.DisableLink(LinkId, CancellationToken.None));
			_link.LinkEnabled.Should().BeFalse();
			_link.LinkAccepted.Should().BeNull();

			ShouldReturnToIndex(await controller.EnableLink(LinkId, CancellationToken.None));
			_link.LinkEnabled.Should().BeTrue();
			_link.LinkAccepted.Should().NotBeNull();

			_links.Verify(l => l.SaveAsync(_link, It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task The_linked_department_can_accept_the_link_with_a_color()
		{
			_link.LinkEnabled = false;
			_link.LinkAccepted = null;
			var controller = As(Linked);

			(await controller.Enable(LinkId)).Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<NewLinksView>().Which.Link.Should().BeSameAs(_link);

			var posted = new NewLinksView { Link = new DepartmentLink { DepartmentLinkId = LinkId, DepartmentColor = "#ff0000" } };
			ShouldReturnToIndex(await controller.Enable(posted, CancellationToken.None));

			_link.LinkEnabled.Should().BeTrue();
			_link.LinkAccepted.Should().NotBeNull();
			_link.DepartmentColor.Should().Be("#ff0000");
			_links.Verify(l => l.SaveAsync(_link, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Accepting_a_link_is_an_L2_signature_bound_to_its_parties_and_sharing()
		{
			_link.LinkEnabled = false;
			_link.LinkAccepted = null;
			_link.LinkedDepartmentShareProtocols = true;
			var controller = As(Linked);

			ShouldReturnToIndex(await controller.EnableLink(LinkId, CancellationToken.None));

			_link.LinkEnabled.Should().BeTrue();
			_signatures.Verify(s => s.SignAsync(It.Is<SignatureRequest>(r => r.Use == SignatureUse.DepartmentLinkAcceptance && r.DepartmentId == Linked &&
				r.SubjectId == LinkId.ToString() && r.ArtifactSha256 == SignaturePolicy.HashArtifact(Resgrid.Model.Helpers.DepartmentLinkSharing.AcceptanceCanonical(_link))),
				It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()), Times.Once);
			SignatureUse.DepartmentLinkAcceptance.Floor.Should().Be(SignatureLevels.StepUp);
			foreach (var method in typeof(LinksController).GetMethods().Where(m => m.Name == nameof(LinksController.EnableLink) ||
				(m.Name == nameof(LinksController.Enable) && m.GetCustomAttribute<HttpPostAttribute>() != null)))
				method.GetCustomAttribute<Resgrid.Web.Attributes.RequiresRecentTwoFactorAttribute>().Should().NotBeNull()
					.And.Match<Resgrid.Web.Attributes.RequiresRecentTwoFactorAttribute>(a => a.RequireForOperation);
		}

		[Test]
		public async Task A_refused_acceptance_signature_leaves_the_link_unaccepted()
		{
			_link.LinkEnabled = false;
			_link.LinkAccepted = null;
			_signatures.Setup(s => s.SignAsync(It.IsAny<SignatureRequest>(), It.IsAny<SignatureProof>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(SignatureResult.Of(SignatureOutcomes.StepUpRequired));
			var controller = As(Linked);

			ShouldReturnToIndex(await controller.EnableLink(LinkId, CancellationToken.None));
			ShouldReturnToIndex(await controller.Enable(new NewLinksView { Link = new DepartmentLink { DepartmentLinkId = LinkId } }, CancellationToken.None));

			_link.LinkEnabled.Should().BeFalse();
			_link.LinkAccepted.Should().BeNull();
			VerifyNothingWritten();
		}

		[TestCase(Creator)]
		[TestCase(Outsider)]
		public async Task Only_the_linked_department_can_accept_or_disable_the_link(int departmentId)
		{
			var controller = As(departmentId);
			var posted = new NewLinksView { Link = new DepartmentLink { DepartmentLinkId = LinkId, DepartmentColor = "#ff0000" } };

			ShouldBeRefused(await controller.EnableLink(LinkId, CancellationToken.None));
			ShouldBeRefused(await controller.DisableLink(LinkId, CancellationToken.None));
			ShouldBeRefused(await controller.Enable(LinkId));
			ShouldBeRefused(await controller.Enable(posted, CancellationToken.None));

			VerifyNothingWritten();
		}

		[Test]
		public async Task An_unknown_link_id_is_refused_instead_of_throwing()
		{
			var controller = As(Linked);

			ShouldBeRefused(await controller.EnableLink(UnknownLinkId, CancellationToken.None));
			ShouldBeRefused(await controller.DisableLink(UnknownLinkId, CancellationToken.None));
			ShouldBeRefused(await controller.Enable(UnknownLinkId));
			ShouldBeRefused(await controller.Enable(new NewLinksView { Link = new DepartmentLink { DepartmentLinkId = UnknownLinkId } }, CancellationToken.None));
			ShouldBeRefused(await controller.Enable(new NewLinksView(), CancellationToken.None));
			ShouldBeRefused(await controller.Enable((NewLinksView)null, CancellationToken.None));

			VerifyNothingWritten();
		}

		// ---- Through the real MVC pipeline: members are refused, GETs no longer write, the page posts token forms ----

		private sealed class PageBodyFilter : IResultFilter
		{
			public void OnResultExecuting(ResultExecutingContext context)
			{
				if (context.Result is ViewResult view)
					context.Result = new PartialViewResult { ViewName = "/Areas/User/Views/" + context.RouteData.Values["controller"] + "/" + (view.ViewName ?? context.RouteData.Values["action"]) + ".cshtml", ViewData = view.ViewData, TempData = view.TempData };
			}
			public void OnResultExecuted(ResultExecutedContext context) { }
		}

		private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
		{
			public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
			protected override Task<AuthenticateResult> HandleAuthenticateAsync()
			{
				// NameIdentifier pins the antiforgery claim uid, so a token issued to the admin also validates for the member
				// request: the member checks then reach the admin policy instead of failing antiforgery.
				var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "manager"), new Claim(ClaimTypes.PrimarySid, "manager"), new Claim(ClaimTypes.PrimaryGroupSid, Linked.ToString()) };
				if (!Request.Headers.ContainsKey("X-Member"))
					claims.Add(new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
				return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "links-test")), "links-test")));
			}
		}

		private static string Token(string html)
			=> WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

		[Test]
		public async Task Members_are_refused_link_management_writes_need_an_antiforgery_post_and_admins_can_disable_and_delete()
		{
			// The signed-in department (Linked) accepted link 7 from Creator and created link 8 toward Outsider.
			var created = new DepartmentLink { DepartmentLinkId = 8, DepartmentId = Linked, LinkedDepartmentId = Outsider, LinkEnabled = false, LinkCreated = DateTime.UtcNow };
			_link.Department = new Department { DepartmentId = Creator, Name = "Creator Fire" };
			_link.LinkedDepartment = new Department { DepartmentId = Linked, Name = "Linked EMS" };
			created.Department = _link.LinkedDepartment;
			created.LinkedDepartment = new Department { DepartmentId = Outsider, Name = "Outsider SAR" };
			_links.Setup(l => l.GetAllLinksForDepartmentAsync(Linked)).ReturnsAsync(() => new List<DepartmentLink> { _link, created });
			_links.Setup(l => l.GetLinkByIdAsync(8)).ReturnsAsync(created);

			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(root!.FullName, "Web", "Resgrid.Web"), EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddLocalization(); builder.Services.AddAdminAssistFieldHelpStubs(); builder.Services.AddWebOptimizer();
			builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
			builder.Services.AddAuthentication("links-test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("links-test", _ => { });
			// The production policy (Startup): Department_Update is the department-admin claim.
			builder.Services.AddAuthorization(o => o.AddPolicy(ResgridResources.Department_Update, p => p.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)));
			builder.Services.AddControllersWithViews(o => o.Filters.Add(new PageBodyFilter())).AddApplicationPart(typeof(LinksController).Assembly);

			var limits = new Mock<ILimitsService>();
			limits.Setup(l => l.CanDepartmentUseLinksAsync(It.IsAny<int>())).ReturnsAsync(true);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Linked, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Linked, Name = "Linked EMS", LinkCode = "LINKME" });
			builder.Services.AddSingleton(_links.Object);
			builder.Services.AddSingleton(limits.Object);
			builder.Services.AddSingleton(departments.Object);
			foreach (var service in new[] { typeof(IEmailService), typeof(ICallsService), typeof(IUnitsService), typeof(IActionLogsService), typeof(IDepartmentGroupsService),
				typeof(IUserStateService), typeof(IPersonnelRolesService), typeof(IMappingService), typeof(IDepartmentSettingsService), typeof(ILinkedProtocolService),
				typeof(IMutualAidCountersignService), typeof(IRecordDeploymentPartnerService), typeof(ISignatureService) })
				builder.Services.AddSingleton(service, ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(service))!).Object);

			List<string> Writes() => _links.Invocations
				.Where(i => i.Method.Name == nameof(IDepartmentLinksService.SaveAsync) || i.Method.Name == nameof(IDepartmentLinksService.DeleteLinkAsync))
				.Select(i => i.Method.Name + "(" + ((DepartmentLink)i.Arguments[0]).DepartmentLinkId + ")").ToList();

			await using var app = builder.Build();
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			ClaimsAuthorizationHelper._httpContextAccessor = app.Services.GetRequiredService<IHttpContextAccessor>();
			app.Use(async (context, next) => { try { await next(); } catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); } });
			app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures("en"));
			app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
			app.MapControllerRoute("areas", "{area:exists}/{controller}/{action=Index}/{id?}");
			try
			{
				await app.StartAsync();
				using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
				using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
				async Task<HttpResponseMessage> Post(string url, params (string Key, string Value)[] fields)
					=> await client.PostAsync(url, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

				// ---- Admin Index: disable and delete are small POST forms with a token, never GET links ----
				var response = await client.GetAsync("/User/Links");
				var html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				html.Should().Contain("Creator Fire").And.Contain("Linked EMS").And.Contain("Outsider SAR");

				var disableForm = Regex.Match(html, "<form class=\"department-link-disable-form\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
				disableForm.Should().Contain("action=\"/User/Links/DisableLink\"").And.Contain("method=\"post\"")
					.And.Contain("name=\"linkId\" value=\"7\"").And.Contain("__RequestVerificationToken");

				var deleteForms = Regex.Matches(html, "<form class=\"department-link-delete-form\"[^>]*>.*?</form>", RegexOptions.Singleline).Select(m => m.Value).ToList();
				deleteForms.Should().HaveCount(2, "the admin can delete the link it accepted and the link it created");
				deleteForms.Should().OnlyContain(f => f.Contains("action=\"/User/Links/DeleteLink\"") && f.Contains("method=\"post\"")
					&& f.Contains("onsubmit=\"return confirm(") && f.Contains("__RequestVerificationToken"));
				deleteForms.Should().ContainSingle(f => f.Contains("name=\"linkId\" value=\"7\""));
				deleteForms.Should().ContainSingle(f => f.Contains("name=\"linkId\" value=\"8\""));

				html.Should().NotContain("/User/Links/DisableLink?").And.NotContain("/User/Links/DeleteLink?").And.NotContain("/User/Links/EnableLink");
				var token = Token(disableForm);
				token.Should().NotBeNullOrEmpty();

				// ---- GETs no longer write, and a POST without the antiforgery token is refused ----
				(await client.GetAsync($"/User/Links/DisableLink?linkId={LinkId}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
				(await client.GetAsync($"/User/Links/EnableLink?linkId={LinkId}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
				(await client.GetAsync($"/User/Links/DeleteLink?linkId={LinkId}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
				(await Post("/User/Links/DisableLink", ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				(await Post("/User/Links/EnableLink", ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				(await Post("/User/Links/DeleteLink", ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				(await Post("/User/Links/Enable", ("Link.DepartmentLinkId", "7"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				(await Post("/User/Links/New", ("LinkCode", "OTHER"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no antiforgery token");
				Writes().Should().BeEmpty();

				// ---- Members: the list still renders without management controls; every management page and write is refused ----
				client.DefaultRequestHeaders.Add("X-Member", "1");
				response = await client.GetAsync("/User/Links");
				html = await response.Content.ReadAsStringAsync();
				response.StatusCode.Should().Be(HttpStatusCode.OK, html);
				html.Should().NotContain("department-link-disable-form").And.NotContain("department-link-delete-form");

				foreach (var page in new[] { "/User/Links/New", $"/User/Links/Enable?linkId={LinkId}" })
					(await client.GetAsync(page)).StatusCode.Should().Be(HttpStatusCode.Forbidden, page);

				(await Post("/User/Links/New", ("__RequestVerificationToken", token), ("LinkCode", "OTHER"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "New");
				(await Post("/User/Links/Enable", ("__RequestVerificationToken", token), ("Link.DepartmentLinkId", "7"), ("Link.DepartmentColor", "#000000"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "Enable");
				(await Post("/User/Links/EnableLink", ("__RequestVerificationToken", token), ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "EnableLink");
				(await Post("/User/Links/DisableLink", ("__RequestVerificationToken", token), ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "DisableLink");
				(await Post("/User/Links/DeleteLink", ("__RequestVerificationToken", token), ("linkId", "7"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "DeleteLink");
				Writes().Should().BeEmpty();
				client.DefaultRequestHeaders.Remove("X-Member");

				// ---- Admin POSTs with the token: disable the accepted link, then delete both sides' links ----
				response = await Post("/User/Links/DisableLink", ("__RequestVerificationToken", token), ("linkId", "7"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/Links");
				_link.LinkEnabled.Should().BeFalse();

				response = await Post("/User/Links/DeleteLink", ("__RequestVerificationToken", token), ("linkId", "8"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
				response.Headers.Location!.OriginalString.Should().EndWith("/User/Links");
				response = await Post("/User/Links/DeleteLink", ("__RequestVerificationToken", token), ("linkId", "7"));
				response.StatusCode.Should().Be(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());

				Writes().Should().Equal("SaveAsync(7)", "DeleteLinkAsync(8)", "DeleteLinkAsync(7)");
			}
			finally
			{
				await app.StopAsync();
				ClaimsAuthorizationHelper._httpContextAccessor = previous;
			}
		}
	}
}
