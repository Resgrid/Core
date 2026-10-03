using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Tests.Security.Live;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Areas.User.Models.TwoFactor;
using Resgrid.Web.Attributes;
using Resgrid.Web.Filters;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A submission stopped for step-up is held and finished after verifying, never discarded: the guard holds it, the replay
	/// puts it back before model binding, and only its own user, session, department and address may replay it, once.
	/// </summary>
	[TestFixture]
	public class StepUpFormReplayTests
	{
		private const string UserId = "user-1";
		private const string SessionId = "session-9";
		private const int DepartmentId = 42;
		private const string SettingsPath = "/User/Department/Settings";

		private InMemoryCacheProvider _cache;
		private IDataProtectionProvider _protection;

		[SetUp]
		public void SetUp()
		{
			_cache = new InMemoryCacheProvider();
			_protection = new EphemeralDataProtectionProvider();
		}

		private static ClaimsPrincipal Principal(string userId = UserId, string sessionId = SessionId, int departmentId = DepartmentId) =>
			new(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, userId),
				new Claim(SessionClaimTypes.SessionId, sessionId),
				new Claim(ClaimTypes.PrimaryGroupSid, departmentId.ToString())
			}, "test"));

		private IServiceProvider Services(ICacheProvider cache = null)
		{
			var user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
			users.Setup(m => m.GetUserId(It.IsAny<ClaimsPrincipal>()))
				.Returns((ClaimsPrincipal p) => p.FindFirst(ClaimTypes.NameIdentifier)?.Value);

			return new ServiceCollection()
				.AddSingleton(users.Object)
				.AddSingleton(Mock.Of<IMfaEvidenceService>())
				.AddSingleton(cache ?? _cache)
				.AddSingleton(_protection)
				.BuildServiceProvider();
		}

		private DefaultHttpContext Post(string path, Dictionary<string, StringValues> fields, ClaimsPrincipal principal = null,
			bool script = false, FormFileCollection files = null, ICacheProvider cache = null, string query = null)
		{
			var http = new DefaultHttpContext { RequestServices = Services(cache), User = principal ?? Principal() };
			http.Request.Method = "POST";
			http.Request.Scheme = "https";
			http.Request.Host = new HostString("app.resgrid.test");
			http.Request.Path = path;
			if (query != null)
				http.Request.QueryString = new QueryString(query);
			http.Request.ContentType = files == null ? "application/x-www-form-urlencoded" : "multipart/form-data; boundary=x";
			http.Request.ContentLength = 100;
			http.Request.Headers.Referer = "https://app.resgrid.test" + path;
			if (script)
				http.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
			http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(fields, files)));
			return http;
		}

		private static Dictionary<string, StringValues> SettingsForm() => new()
		{
			["Department.Name"] = "Station 9",
			["Use24HourTime"] = new StringValues(new[] { "true", "false" }),
			["__RequestVerificationToken"] = "token-from-the-original-page"
		};

		private static async Task<(ActionExecutingContext Context, bool Passed)> Guard(HttpContext http)
		{
			var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
				new List<IFilterMetadata>(), new Dictionary<string, object>(), controller: null);
			var passed = false;
			await new RequiresRecentTwoFactorAttribute { RequireForOperation = true }
				.OnActionExecutionAsync(context, () => { passed = true; return Task.FromResult<ActionExecutedContext>(null); });
			return (context, passed);
		}

		private async Task<(ResourceExecutingContext Context, bool Passed)> Replay(HttpContext http)
		{
			var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
				new List<IFilterMetadata>(), new List<IValueProviderFactory>());
			var passed = false;
			await new HeldSubmissionReplayFilter(_cache, _protection, http.RequestServices.GetRequiredService<UserManager<IdentityUser>>())
				.OnResourceExecutionAsync(context, () => { passed = true; return Task.FromResult<ResourceExecutedContext>(null); });
			return (context, passed);
		}

		private async Task<string> HoldSettingsPost(FormFileCollection files = null)
		{
			var (context, passed) = await Guard(Post(SettingsPath, SettingsForm(), files: files));
			passed.Should().BeFalse();

			var returnUrl = (string)((RedirectToRouteResult)context.Result).RouteValues["returnUrl"];
			return QueryHelpers(returnUrl)["id"];
		}

		private static Dictionary<string, string> QueryHelpers(string url) =>
			Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://x" + url).Query)
				.ToDictionary(x => x.Key, x => x.Value.ToString());

		private static Dictionary<string, StringValues> ReplayForm(string id) => new()
		{
			[StepUpFormReplay.FieldName] = id,
			["__RequestVerificationToken"] = "fresh-token"
		};

		[Test]
		public async Task A_form_post_stopped_for_step_up_is_held_and_verify_returns_through_the_resume_page()
		{
			var (context, passed) = await Guard(Post(SettingsPath, SettingsForm()));

			passed.Should().BeFalse();
			var route = context.Result.Should().BeOfType<RedirectToRouteResult>().Subject.RouteValues;
			route["action"].Should().Be("Verify2FA");
			route.ContainsKey("resubmit").Should().BeFalse("the submission was held");

			var returnUrl = (string)route["returnUrl"];
			returnUrl.Should().StartWith(StepUpFormReplay.ResumePath + "?id=");
			QueryHelpers(returnUrl)["back"].Should().Be(SettingsPath, "an expired hold still sends the user back to the page they were on");
			new StepUpVerifyViewModel { ReturnUrl = returnUrl }.HoldingSubmission.Should().BeTrue();

			var held = await StepUpFormReplay.PeekAsync(_cache, _protection, QueryHelpers(returnUrl)["id"]);
			held.Target.Should().Be(SettingsPath);
			held.Fields.Select(f => f.Name).Should().BeEquivalentTo(new[] { "Department.Name", "Use24HourTime" },
				"the stale antiforgery token is never held; the replay brings a fresh one");
		}

		[Test]
		public async Task The_replay_puts_the_held_form_in_place_before_binding_and_runs_once()
		{
			var id = await HoldSettingsPost();

			var replay = Post(SettingsPath, ReplayForm(id));
			var (first, firstPassed) = await Replay(replay);

			firstPassed.Should().BeTrue();
			first.Result.Should().BeNull();
			var form = await replay.Request.ReadFormAsync();
			form["Department.Name"].ToString().Should().Be("Station 9");
			form["Use24HourTime"].ToArray().Should().Equal("true", "false");
			form["__RequestVerificationToken"].ToString().Should().Be("fresh-token");

			var (second, secondPassed) = await Replay(Post(SettingsPath, ReplayForm(id)));
			secondPassed.Should().BeFalse("a held submission saves once, whatever is double-clicked");
			second.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().StartWith(StepUpFormReplay.ResumePath);
		}

		[TestCase("someone-else", SessionId, DepartmentId)]
		[TestCase(UserId, "another-session", DepartmentId)]
		[TestCase(UserId, SessionId, 7)]
		public async Task Only_the_user_session_and_department_that_made_it_may_replay_it(string userId, string sessionId, int departmentId)
		{
			var id = await HoldSettingsPost();

			var (refused, refusedPassed) = await Replay(Post(SettingsPath, ReplayForm(id), Principal(userId, sessionId, departmentId)));
			refusedPassed.Should().BeFalse("the action must never run on the bare replay post");
			refused.Result.Should().NotBeNull();

			var (owner, ownerPassed) = await Replay(Post(SettingsPath, ReplayForm(id)));
			ownerPassed.Should().BeTrue("a refused attempt does not use up the owner's replay");
		}

		[Test]
		public async Task A_held_submission_replays_only_to_the_address_it_was_made_to()
		{
			var id = await HoldSettingsPost();

			var (elsewhere, passed) = await Replay(Post("/User/Personnel/DeletePerson", ReplayForm(id)));

			passed.Should().BeFalse();
			elsewhere.Result.Should().NotBeNull();
		}

		[Test]
		public async Task Uploaded_files_come_back_with_the_replay()
		{
			var bytes = Encoding.UTF8.GetBytes("<EntityDescriptor/>");
			var files = new FormFileCollection
			{
				new FormFile(new MemoryStream(bytes), 0, bytes.Length, "metadata", "idp.xml")
				{
					Headers = new HeaderDictionary { ["Content-Type"] = "text/xml" }
				}
			};
			var id = await HoldSettingsPost(files);

			var replay = Post(SettingsPath, ReplayForm(id));
			var (_, passed) = await Replay(replay);

			passed.Should().BeTrue();
			var file = (await replay.Request.ReadFormAsync()).Files.Single();
			file.Name.Should().Be("metadata");
			file.FileName.Should().Be("idp.xml");
			file.ContentType.Should().Be("text/xml");
			using var reader = new StreamReader(file.OpenReadStream());
			(await reader.ReadToEndAsync()).Should().Be("<EntityDescriptor/>");
		}

		[Test]
		public async Task A_script_call_is_told_where_to_verify_instead_of_following_a_redirect()
		{
			var http = Post("/User/Security/SetPermission", new Dictionary<string, StringValues>(), script: true, query: "?type=0&perm=1");

			var (context, passed) = await Guard(http);

			passed.Should().BeFalse();
			var result = context.Result.Should().BeOfType<JsonResult>().Subject;
			result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			var verifyUrl = http.Response.Headers[RequiresRecentTwoFactorAttribute.StepUpRedirectHeader].ToString();
			verifyUrl.Should().StartWith("/User/TwoFactor/Verify2FA?returnUrl=");

			var resume = QueryHelpers(verifyUrl)["returnUrl"];
			var held = await StepUpFormReplay.PeekAsync(_cache, _protection, QueryHelpers(resume)["id"]);
			held.Script.Should().BeTrue();
			held.Target.Should().Be("/User/Security/SetPermission?type=0&perm=1", "the query carries a script call's arguments");
		}

		[Test]
		public async Task A_submission_that_cannot_be_held_says_so_instead_of_vanishing()
		{
			var refusing = new Mock<ICacheProvider>();
			refusing.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);

			var (cacheDown, _) = await Guard(Post(SettingsPath, SettingsForm(), cache: refusing.Object));
			var route = ((RedirectToRouteResult)cacheDown.Result).RouteValues;
			route["resubmit"].Should().Be("1");
			route["returnUrl"].Should().Be(SettingsPath);

			var oversized = new Dictionary<string, StringValues> { ["Notes"] = new string('x', StepUpFormReplay.MaxFieldCharacters + 1) };
			var (tooLarge, _) = await Guard(Post(SettingsPath, oversized));
			((RedirectToRouteResult)tooLarge.Result).RouteValues["resubmit"].Should().Be("1");

			// A JSON body is not a form: holding it as an empty one would replay the action on blanks.
			var json = Post(SettingsPath, new Dictionary<string, StringValues>());
			json.Request.ContentType = "application/json";
			json.Request.ContentLength = 12;
			json.Features.Set<IFormFeature>(new FormFeature(json.Request)); // read the content type, as a real request does
			var (notAForm, _) = await Guard(json);
			((RedirectToRouteResult)notAForm.Result).RouteValues["resubmit"].Should().Be("1");
			StepUpFormReplay.CanHold(json.Request).Should().BeFalse();
		}

		[Test]
		public async Task A_page_request_is_still_returned_to_itself()
		{
			var http = Post(SettingsPath, new Dictionary<string, StringValues>());
			http.Request.Method = "GET";

			var (context, _) = await Guard(http);

			var route = ((RedirectToRouteResult)context.Result).RouteValues;
			route["returnUrl"].Should().Be(SettingsPath);
			route.ContainsKey("resubmit").Should().BeFalse();
		}

		[Test]
		public async Task The_resume_page_posts_only_its_owners_hold_back_to_where_it_was_made()
		{
			var id = await HoldSettingsPost();

			async Task<StepUpResumeViewModel> Resume(ClaimsPrincipal principal)
			{
				var url = new Mock<IUrlHelper>();
				url.Setup(u => u.IsLocalUrl(It.IsAny<string>())).Returns((string u) => u != null && u.StartsWith("/") && !u.StartsWith("//"));
				url.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("/User/Home/Dashboard");
				var controller = new StepUpResumeController(_cache, _protection, Services().GetRequiredService<UserManager<IdentityUser>>())
				{
					ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } },
					Url = url.Object
				};
				var view = (ViewResult)await controller.Index(id, "/User/Department/Settings");
				return (StepUpResumeViewModel)view.Model;
			}

			var owner = await Resume(Principal());
			owner.Id.Should().Be(id);
			owner.Target.Should().Be(SettingsPath);
			owner.Script.Should().BeFalse();

			var stranger = await Resume(Principal("someone-else"));
			stranger.Id.Should().BeNull();
			stranger.Target.Should().BeNull();
			stranger.BackUrl.Should().Be("/User/Department/Settings");
		}

		// ---- Password re-confirmation ---------------------------------------------------------------------------------

		private TwoFactorController TwoFactor(HttpContext http, ICacheProvider cache = null)
		{
			var url = new Mock<IUrlHelper>();
			url.Setup(u => u.Action(It.Is<UrlActionContext>(c => c.Action == nameof(TwoFactorController.Disable2FA)))).Returns("/User/TwoFactor/Disable2FA");

			// No fresh password evidence: the evidence mock answers false to HasFreshFirstFactorAsync.
			return new TwoFactorController(http.RequestServices.GetRequiredService<UserManager<IdentityUser>>(), null, Mock.Of<ISystemAuditsService>(),
				System.Text.Encodings.Web.UrlEncoder.Default, Mock.Of<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>(),
				Mock.Of<IUserStore<IdentityUser>>(), Mock.Of<Resgrid.Model.Repositories.IUserMfaStateRepository>(), Mock.Of<IUserSessionService>(),
				Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), Mock.Of<Resgrid.Model.Repositories.IUserPasskeyRepository>(),
				Mock.Of<ISecurityNoticeService>(), Mock.Of<IMfaActivityService>(), Mock.Of<IPasskeyService>(), Mock.Of<IMfaApprovalService>(),
				Mock.Of<ISsoBrokerService>(), Mock.Of<ISsoReturnTargetRegistry>(), Mock.Of<IDepartmentSsoService>(), Mock.Of<IDepartmentsService>(),
				cache ?? _cache, _protection)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				Url = url.Object
			};
		}

		[Test]
		public async Task A_form_post_stopped_for_password_reconfirmation_is_held_and_finished_after_confirming()
		{
			const string disablePath = "/User/TwoFactor/Disable2FA";
			var form = new Dictionary<string, StringValues> { ["Code"] = "123456", ["__RequestVerificationToken"] = "token-from-the-original-page" };

			var result = await TwoFactor(Post(disablePath, form)).Disable2FA(new Disable2FAViewModel { Code = "123456" }, CancellationToken.None);

			var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
			redirect.ControllerName.Should().Be("AccountSecurity");
			redirect.ActionName.Should().Be("Reauthenticate");
			redirect.RouteValues["resubmit"].Should().BeNull("the submission was held");
			var returnUrl = (string)redirect.RouteValues["returnUrl"];
			returnUrl.Should().StartWith(StepUpFormReplay.ResumePath + "?id=");
			QueryHelpers(returnUrl)["back"].Should().Be(disablePath);
			new ReauthenticateView { ReturnUrl = returnUrl }.HoldingSubmission.Should().BeTrue();

			// Confirming the password returns through the resume page, whose post the global filter restores.
			var replay = Post(disablePath, ReplayForm(QueryHelpers(returnUrl)["id"]));
			var (_, passed) = await Replay(replay);
			passed.Should().BeTrue();
			(await replay.Request.ReadFormAsync())["Code"].ToString().Should().Be("123456");
		}

		[Test]
		public async Task A_submission_the_reconfirmation_cannot_hold_asks_to_resubmit()
		{
			var refusing = new Mock<ICacheProvider>();
			refusing.Setup(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);
			var http = Post("/User/TwoFactor/Disable2FA", new Dictionary<string, StringValues> { ["Code"] = "123456" });

			var result = await TwoFactor(http, refusing.Object).Disable2FA(new Disable2FAViewModel { Code = "123456" }, CancellationToken.None);

			var redirect = (RedirectToActionResult)result;
			redirect.RouteValues["resubmit"].Should().Be("1");
			redirect.RouteValues["returnUrl"].Should().Be("/User/TwoFactor/Disable2FA");
		}

		[Test]
		public async Task A_page_request_sent_to_reconfirm_returns_to_itself()
		{
			var http = Post("/User/TwoFactor/Disable2FA", new Dictionary<string, StringValues>());
			http.Request.Method = "GET";

			var result = await TwoFactor(http).Disable2FA();

			var redirect = (RedirectToActionResult)result;
			redirect.RouteValues["returnUrl"].Should().Be("/User/TwoFactor/Disable2FA");
			redirect.RouteValues["resubmit"].Should().BeNull();
		}

		// ---- The global filter's reach ------------------------------------------------------------------------------

		[Test]
		public async Task Posts_that_cannot_be_a_replay_are_passed_through_unread()
		{
			var id = await HoldSettingsPost();

			// An upload (multipart) carrying the field: never the resume page's post.
			var upload = Post(SettingsPath, ReplayForm(id), files: new FormFileCollection());
			var (uploadContext, uploadPassed) = await Replay(upload);
			uploadPassed.Should().BeTrue();
			uploadContext.Result.Should().BeNull();
			(await upload.Request.ReadFormAsync()).ContainsKey("Department.Name").Should().BeFalse("nothing was restored into it");

			// A signed-out caller (a webhook) is never a replay.
			var anonymous = Post(SettingsPath, ReplayForm(id), new ClaimsPrincipal(new ClaimsIdentity()));
			var (_, anonymousPassed) = await Replay(anonymous);
			anonymousPassed.Should().BeTrue();

			// Neither used up the owner's replay.
			var (_, ownerPassed) = await Replay(Post(SettingsPath, ReplayForm(id)));
			ownerPassed.Should().BeTrue();
		}
	}
}
