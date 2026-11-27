using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Controllers;
using Resgrid.Web.Models.LmsLearn;
using Resgrid.WebCore.Attributes;
using Resgrid.WebCore.Models;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Round 7B (product-owner decision 2026-11-25): every anonymous learner-portal form that creates or e-mails something carries the
	/// Account/Register reCAPTCHA standard: the same model base and validation attribute, the same widget and script, the same behaviour when
	/// the keys are blank (the check is refused). A missing or failed answer is refused before anything is created or sent. The guard lists
	/// every POST action of the portal, so a new one has to say why it needs no CAPTCHA. No call reaches Google from these tests.
	/// </summary>
	[TestFixture]
	public class LmsLearnerPortalCaptchaTests
	{
		/// <summary>Anonymous forms that create or e-mail something: the reCAPTCHA standard.</summary>
		private static readonly string[] Captcha = { "Register", "Forgot", "Resend" };

		/// <summary>Need a single-use e-mailed token (or the unsubscribe key): nothing is created or sent without it.</summary>
		private static readonly string[] TokenGated = { "Accept", "Reset", "Unsubscribe", "Verify" };

		/// <summary>The sign-in itself (throttled per address and per account): creates nothing, sends nothing.</summary>
		private static readonly string[] SignIn = { "SignIn" };

		/// <summary>Need the signed-in learner's own session.</summary>
		private static readonly string[] Session =
		{
			"SignOutLearner", "RegisterClass", "WithdrawClass", "EnrollCourse", "Progress", "Complete", "Survey", "StartTest", "Test", "EraseMine", "Preferences"
		};

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}

		private static IEnumerable<MethodInfo> PostActions() =>
			typeof(LmsLearnController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null);

		[Test]
		public void Every_anonymous_post_of_the_portal_that_creates_or_emails_carries_the_register_captcha_standard()
		{
			var posts = PostActions().ToList();
			posts.Select(m => m.Name).Distinct().Should().BeEquivalentTo(Captcha.Concat(TokenGated).Concat(SignIn).Concat(Session),
				"a new POST action of the portal must be classified: CAPTCHA, token, sign-in or the learner's own session");

			foreach (var action in posts.Where(m => Captcha.Contains(m.Name)))
			{
				var model = action.GetParameters().Select(p => p.ParameterType).SingleOrDefault(t => typeof(GoogleReCaptchaModelBase).IsAssignableFrom(t));
				model.Should().NotBeNull(action.Name + " binds a GoogleReCaptchaModelBase model (the Account/Register standard)");
				model.GetProperty(nameof(GoogleReCaptchaModelBase.GoogleReCaptchaResponse)).GetCustomAttribute<GoogleReCaptchaValidationAttribute>()
					.Should().NotBeNull("the answer is validated by the same attribute as Account/Register");
				model.GetProperty("SiteKey").Should().NotBeNull("the page renders the configured public site key");
				action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull(action.Name);
			}
			typeof(GoogleReCaptchaModelBase).IsAssignableFrom(typeof(Resgrid.Web.Models.AccountViewModels.RegisterViewModel)).Should().BeTrue("the standard this portal follows");
		}

		[Test]
		public void The_captcha_pages_include_the_widget_the_hidden_answer_and_the_google_script_and_the_settings_page_no_longer_says_there_is_none()
		{
			var root = Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Views", "LmsLearn");
			File.ReadAllText(Path.Combine(root, "_Captcha.cshtml")).Should().Contain("class=\"g-recaptcha\"").And.Contain("data-sitekey=\"@Model\"").And.Contain("name=\"GoogleReCaptchaResponse\"");
			foreach (var page in new[] { "Register.cshtml", "Forgot.cshtml", "Resend.cshtml" })
			{
				var text = File.ReadAllText(Path.Combine(root, page));
				text.Should().Contain("<partial name=\"_Captcha\" model=\"Model.SiteKey\" />", page);
				text.Should().Contain("https://www.google.com/recaptcha/api.js", page + " loads the same script as Account/Register");
				text.Should().Contain("resgrid.recaptcha-field.js", page);
				text.Should().Contain("data-submit-once", page);
			}
			File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Views", "LmsExternalLearners", "Settings.cshtml"))
				.Should().NotContain("CaptchaNote");
		}

		[Test]
		public void A_missing_answer_is_refused_by_the_attribute_before_any_call_to_google()
		{
			var model = new LmsLearnForgotModel { Email = "pat@example.org" };
			var results = new List<ValidationResult>();
			Validator.TryValidateObject(model, new ValidationContext(model), results, true).Should().BeFalse();
			results.Should().Contain(r => r.MemberNames.Contains(nameof(GoogleReCaptchaModelBase.GoogleReCaptchaResponse)));
		}

		#region The controller refuses a failed answer before creating or sending anything

		private sealed class Portal
		{
			public readonly Mock<ILmsExternalLearnerService> Learners = new();
			public readonly LmsLearnController Controller;

			public Portal()
			{
				var portal = new LmsLearnerPortal { LmsLearnerPortalId = 1, DepartmentId = 4901, PortalKey = "abcdefghjkmnpqrs", IsEnabled = true, SelfRegistration = true };
				Learners.Setup(l => l.GetPortalByKeyAsync("abcdefghjkmnpqrs")).ReturnsAsync(portal);
				Learners.Setup(l => l.GetBrandingAsync(portal)).ReturnsAsync(LmsExternalLearnerRules.Branding(portal, "Lakeside", null));
				Learners.Setup(l => l.IsPortalOpenAsync(portal)).ReturnsAsync(true);
				var limits = new Mock<IPublicFormRateLimiter>();
				limits.Setup(l => l.TryAcquireAsync(It.IsAny<PublicFormLimit>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				limits.Setup(l => l.IsLimitedAsync(It.IsAny<PublicFormLimit>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
				var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners>>();
				localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
				var trainingLocalizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings>>();
				Controller = new LmsLearnController(Learners.Object, Mock.Of<ILmsLearnerService>(), Mock.Of<ILmsAssessmentService>(), Mock.Of<ILmsMediaService>(), Mock.Of<ILmsGateService>(),
					Mock.Of<ILmsTrainingService>(), Mock.Of<ILoginThrottleService>(), limits.Object, Mock.Of<IPdfProvider>(), localizer.Object, trainingLocalizer.Object);

				var authentication = new Mock<IAuthenticationService>();
				authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
				var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider() };
				http.Connection.RemoteIpAddress = IPAddress.Loopback;
				var route = new RouteData();
				route.Values["portal"] = "abcdefghjkmnpqrs";
				Controller.ControllerContext = new ControllerContext(new ActionContext(http, route, new ControllerActionDescriptor()));
				Controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
			}

			public async Task OpenAsync()
			{
				var context = new ActionExecutingContext(Controller.ControllerContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), Controller);
				await Controller.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null));
				context.Result.Should().BeNull("the portal is open");
			}
		}

		[Test]
		public async Task A_failed_answer_refuses_registration_password_reset_and_resend_before_anything_is_created_or_sent()
		{
			var p = new Portal();
			await p.OpenAsync();
			p.Controller.ModelState.AddModelError(nameof(GoogleReCaptchaModelBase.GoogleReCaptchaResponse), "Please complete the check");

			var register = await p.Controller.Register(new LmsLearnRegisterModel
			{
				Email = "pat@example.org", FirstName = "Pat", LastName = "Doe", Password = "Correct-Horse-9", ConfirmPassword = "Correct-Horse-9"
			}, CancellationToken.None);
			((LmsLearnRegisterModel)((ViewResult)register).Model).SiteKey.Should().Be(Resgrid.Config.WebConfig.RecaptchaPublicKey, "the page is shown again with the widget");
			var forgot = await p.Controller.Forgot(new LmsLearnForgotModel { Email = "pat@example.org" }, CancellationToken.None);
			var resend = await p.Controller.Resend(new LmsLearnResendModel { Email = "pat@example.org" }, CancellationToken.None);

			register.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Register");
			forgot.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Forgot");
			resend.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Resend");
			p.Learners.Verify(l => l.RegisterAsync(It.IsAny<int>(), It.IsAny<LmsLearnerRegistration>(), It.IsAny<CancellationToken>()), Times.Never);
			p.Learners.Verify(l => l.BeginPasswordResetAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			p.Learners.Verify(l => l.ResendAccessEmailAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_valid_answer_lets_the_resend_through_and_the_page_says_the_same_whatever_the_address()
		{
			var p = new Portal();
			await p.OpenAsync();
			var result = await p.Controller.Resend(new LmsLearnResendModel { Email = "nobody@example.org" }, CancellationToken.None);
			result.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Message");
			p.Learners.Verify(l => l.ResendAccessEmailAsync(4901, "nobody@example.org", It.IsAny<CancellationToken>()), Times.Once);
		}

		#endregion
	}
}
