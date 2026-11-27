using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NUnit.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Strict separation of external learners from member data (Training T5): the learner signs in with a cookie scheme of its own that no
	/// member controller (web or v4) accepts, whose cookie never leaves /Learn and whose principal carries no member claim; the portal
	/// controller depends on learner-facing services only and never replaces the member principal. A learner is therefore anonymous to
	/// every member endpoint (personnel, dispatch, messages, the v4 API) and is refused wherever those require a signed-in member.
	/// </summary>
	[TestFixture]
	public class LmsLearnerPortalGuardTests
	{
		private static readonly Assembly WebAssembly = typeof(LmsLearnController).Assembly;
		private static readonly Assembly ApiAssembly = typeof(Resgrid.Web.Services.Controllers.v4.LmsController).Assembly;

		private static IEnumerable<Type> Controllers(Assembly assembly)
		{
			Type[] types;
			try { types = assembly.GetTypes(); }
			catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
			return types.Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static ClaimsPrincipal Learner() => LmsLearnerAuth.CreatePrincipal(new LmsExternalLearner
		{
			LmsExternalLearnerId = 12, DepartmentId = 4401, LearnerUserId = LmsExternalLearnerIds.New(), SecurityStamp = "stamp", Email = "casey@example.org"
		});

		[Test]
		public void No_member_controller_or_action_accepts_the_learner_scheme()
		{
			var offenders = new List<string>();
			foreach (var controller in Controllers(WebAssembly).Concat(Controllers(ApiAssembly)))
			{
				var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => (controller.FullName, a))
					.Concat(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(true).Select(a => (controller.FullName + "." + m.Name, a))));
				foreach (var (where, attribute) in attributes)
					if ((attribute.AuthenticationSchemes ?? string.Empty).Split(',').Any(s => s.Trim() == LmsLearnerAuth.Scheme))
						offenders.Add(where);
			}
			offenders.Should().BeEmpty("only the learner portal reads the learner cookie, and it does so itself");
		}

		[Test]
		public void Only_the_portal_and_its_registration_mention_the_learner_sign_in()
		{
			var root = RepositoryRoot();
			var allowed = new[] { "LmsLearnerAuth.cs", "LmsLearnController.cs", "Startup.cs" };
			var files = Directory.EnumerateFiles(Path.Combine(root, "Web"), "*.cs*", SearchOption.AllDirectories)
				.Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
				.Where(f => File.ReadAllText(f).Contains("LmsLearnerAuth"))
				.Select(Path.GetFileName).ToList();
			files.Should().OnlyContain(f => allowed.Contains(f));

			var startup = File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web", "Startup.cs"));
			startup.Should().Contain("DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme", "the member cookie stays the default");
			startup.Should().Contain(".AddCookie(Helpers.LmsLearnerAuth.Scheme");
			File.ReadAllText(Path.Combine(root, "Web", "Resgrid.Web.Services", "Startup.cs")).Should().NotContain("Learner", "the v4 API has no learner sign-in at all");
		}

		[Test]
		public void The_learner_cookie_stays_on_the_portal_path_and_is_not_the_member_cookie()
		{
			var options = new CookieAuthenticationOptions();
			LmsLearnerAuth.Configure(options, false);

			options.Cookie.Path.Should().Be("/Learn");
			options.Cookie.Name.Should().NotBe("RGSITEAUTHCOOKIE").And.Be(LmsLearnerAuth.CookieName);
			options.Cookie.HttpOnly.Should().BeTrue();
			options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
			options.LoginPath.Value.Should().StartWith("/Learn");
			LmsLearnerAuth.Scheme.Should().NotBe(CookieAuthenticationDefaults.AuthenticationScheme).And.NotBe(IdentityConstants.ApplicationScheme);
		}

		[Test]
		public async Task The_learner_principal_carries_no_member_claim_and_satisfies_no_member_policy()
		{
			var principal = Learner();

			principal.FindFirst(ClaimTypes.PrimarySid).Should().BeNull("member code reads the user id from it");
			principal.FindFirst(ClaimTypes.PrimaryGroupSid).Should().BeNull("member code reads the department from it");
			principal.FindFirst(ClaimTypes.NameIdentifier).Should().BeNull();
			principal.FindFirst(ClaimTypes.Role).Should().BeNull();
			principal.Claims.Select(c => c.Type).Should().BeEquivalentTo(new[] { LmsLearnerAuth.LearnerIdClaim, LmsLearnerAuth.DepartmentClaim, LmsLearnerAuth.StampClaim });
			principal.Claims.Should().NotContain(c => c.Value.Contains("@"), "no personal data in the cookie");
			principal.Identity.AuthenticationType.Should().Be(LmsLearnerAuth.Scheme);

			var services = new ServiceCollection().AddLogging().AddAuthorization(o =>
			{
				o.AddPolicy(ResgridResources.Department_View, p => p.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.View));
				o.AddPolicy(ResgridResources.Call_View, p => p.RequireClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				o.AddPolicy(ResgridResources.Log_View, p => p.RequireClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				o.AddPolicy("member-scheme", p => p.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme).RequireClaim(ClaimTypes.PrimarySid));
			}).BuildServiceProvider();
			var authorization = services.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();
			foreach (var policy in new[] { ResgridResources.Department_View, ResgridResources.Call_View, ResgridResources.Log_View, "member-scheme" })
				(await authorization.AuthorizeAsync(principal, policy)).Succeeded.Should().BeFalse(policy);

			LmsLearnerAuth.Read(principal).Should().NotBeNull();
			LmsLearnerAuth.Read(new ClaimsPrincipal(new ClaimsIdentity(principal.Claims, CookieAuthenticationDefaults.AuthenticationScheme))).Should().BeNull("a member cookie never reads as a learner");
			LmsLearnerAuth.Read(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeNull();
		}

		[Test]
		public void The_portal_is_anonymous_to_the_member_app_and_depends_on_learner_facing_services_only()
		{
			typeof(LmsLearnController).GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
			typeof(LmsLearnController).GetCustomAttribute<RouteAttribute>().Template.Should().Be("Learn/{portal}");
			typeof(LmsLearnController).GetMethods().SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Should().BeEmpty();

			var allowed = new HashSet<Type>
			{
				typeof(ILmsExternalLearnerService), typeof(ILmsLearnerService), typeof(ILmsAssessmentService), typeof(ILmsMediaService), typeof(ILmsGateService),
				typeof(ILmsTrainingService), typeof(ILoginThrottleService), typeof(IPublicFormRateLimiter), typeof(IPdfProvider)
			};
			var parameters = typeof(LmsLearnController).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
			parameters.Where(t => !allowed.Contains(t) && !(t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IStringLocalizer<>)))
				.Should().BeEmpty("no member, personnel, dispatch or message service is reachable from the learner portal");
		}

		[Test]
		public void The_portal_never_replaces_the_member_principal_and_signs_in_only_with_its_own_scheme()
		{
			var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Controllers", "LmsLearnController.cs"));
			source.Should().NotMatchRegex(@"HttpContext\.User\s*=");
			Regex.Matches(source, @"HttpContext\.(SignInAsync|SignOutAsync|AuthenticateAsync)\(([^,)]*)").Select(m => m.Groups[2].Value.Trim())
				.Should().NotBeEmpty().And.OnlyContain(arg => arg == "LmsLearnerAuth.Scheme");
			source.Should().NotContain("UserManager").And.NotContain("SignInManager");
		}
	}
}
