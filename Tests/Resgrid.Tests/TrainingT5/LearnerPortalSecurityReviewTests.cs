using System;
using System.Collections.Generic;
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
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Tests.TrainingT2;
using Resgrid.Web.Controllers;
using Resgrid.Web.Helpers;
using Resgrid.Web.Models.LmsLearn;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>
	/// Security review 2026-11-25 (round 9D) on the learner portal: e-mail verification is a POST behind a Confirm page (M2), sign-out ends
	/// every copy of the session (L2), and a self-registered learner never sees a protocol a public page could not show (M3).
	/// </summary>
	[TestFixture]
	public class LearnerPortalSecurityReviewTests
	{
		private const int PortalDepartment = 4901;

		private sealed class Portal
		{
			public readonly Mock<ILmsExternalLearnerService> Learners = new();
			public readonly LmsLearnController Controller;

			public Portal(LmsExternalLearner signedIn = null)
			{
				var portal = new LmsLearnerPortal { LmsLearnerPortalId = 1, DepartmentId = PortalDepartment, PortalKey = "abcdefghjkmnpqrs", IsEnabled = true, SelfRegistration = true };
				Learners.Setup(l => l.GetPortalByKeyAsync("abcdefghjkmnpqrs")).ReturnsAsync(portal);
				Learners.Setup(l => l.GetBrandingAsync(portal)).ReturnsAsync(LmsExternalLearnerRules.Branding(portal, "Lakeside", null));
				Learners.Setup(l => l.IsPortalOpenAsync(portal)).ReturnsAsync(true);
				var limits = new Mock<IPublicFormRateLimiter>();
				limits.Setup(l => l.TryAcquireAsync(It.IsAny<PublicFormLimit>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
				var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners>>();
				localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
				Controller = new LmsLearnController(Learners.Object, Mock.Of<ILmsLearnerService>(), Mock.Of<ILmsAssessmentService>(), Mock.Of<ILmsMediaService>(), Mock.Of<ILmsGateService>(),
					Mock.Of<ILmsTrainingService>(), Mock.Of<ILoginThrottleService>(), limits.Object, Mock.Of<IPdfProvider>(), localizer.Object,
					new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.Trainings.Trainings>>().Object);

				var authentication = new Mock<IAuthenticationService>();
				if (signedIn == null)
					authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>())).ReturnsAsync(AuthenticateResult.NoResult());
				else
				{
					authentication.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), LmsLearnerAuth.Scheme))
						.ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(LmsLearnerAuth.CreatePrincipal(signedIn), LmsLearnerAuth.Scheme)));
					Learners.Setup(l => l.ValidateSessionAsync(PortalDepartment, signedIn.LmsExternalLearnerId, signedIn.SecurityStamp)).ReturnsAsync(signedIn);
				}
				var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider() };
				http.Connection.RemoteIpAddress = IPAddress.Loopback;
				var route = new RouteData();
				route.Values["portal"] = "abcdefghjkmnpqrs";
				Controller.ControllerContext = new ControllerContext(new ActionContext(http, route, new ControllerActionDescriptor()));
				Controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
				Controller.Url = Mock.Of<IUrlHelper>();
			}

			public async Task OpenAsync()
			{
				var context = new ActionExecutingContext(Controller.ControllerContext, new List<IFilterMetadata>(), new Dictionary<string, object>(), Controller);
				await Controller.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null));
				context.Result.Should().BeNull("the portal is open");
			}
		}

		#region M2: verification is a POST

		[Test]
		public async Task Security_review_M2_opening_the_verification_link_only_shows_a_confirm_page_and_the_post_activates()
		{
			var p = new Portal();
			await p.OpenAsync();
			var pending = new LmsExternalLearner { LmsExternalLearnerId = 7, DepartmentId = PortalDepartment, FirstName = "Pat", LastName = "Lee", CreatedOn = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc) };
			p.Learners.Setup(l => l.GetVerificationAsync(PortalDepartment, "tok")).ReturnsAsync(new LmsExternalLearnerResult { Learner = pending });
			p.Learners.Setup(l => l.VerifyEmailAsync(PortalDepartment, "tok", "Own3rPassword", "Pat", "Lee", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new LmsExternalLearnerResult { Learner = pending });

			var page = await p.Controller.Verify("tok", CancellationToken.None);

			var view = page.Should().BeOfType<ViewResult>().Subject;
			view.ViewName.Should().Be("Verify");
			var shown = view.Model.Should().BeOfType<LmsLearnVerifyModel>().Subject;
			shown.Token.Should().Be("tok");
			shown.RegisteredName.Should().Be("Pat Lee", "the page names the registration, so someone who did not register can tell");
			shown.RegisteredOn.Should().Be(pending.CreatedOn);
			shown.Password.Should().BeNull("the person who opened the link chooses the password");
			p.Learners.Verify(l => l.VerifyEmailAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
				Times.Never, "a GET (a mail scanner) activates nothing");

			var posted = await p.Controller.Verify(new LmsLearnVerifyModel
			{
				Token = "tok", FirstName = "Pat", LastName = "Lee", Password = "Own3rPassword", ConfirmPassword = "Own3rPassword"
			}, CancellationToken.None);

			posted.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Message");
			p.Learners.Verify(l => l.VerifyEmailAsync(PortalDepartment, "tok", "Own3rPassword", "Pat", "Lee", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Security_review_M1_the_confirm_post_needs_a_matching_password_and_a_refusal_shows_the_page_again()
		{
			var p = new Portal();
			await p.OpenAsync();
			var pending = new LmsExternalLearner { LmsExternalLearnerId = 7, DepartmentId = PortalDepartment, FirstName = "Mallory", LastName = "X", CreatedOn = DateTime.UtcNow };
			p.Learners.Setup(l => l.GetVerificationAsync(PortalDepartment, "tok")).ReturnsAsync(new LmsExternalLearnerResult { Learner = pending });
			p.Learners.Setup(l => l.VerifyEmailAsync(PortalDepartment, "tok", "short", "Pat", "Lee", It.IsAny<CancellationToken>()))
				.ReturnsAsync(LmsExternalLearnerResult.Fail("lms_xl_password_length"));

			var mismatch = await p.Controller.Verify(new LmsLearnVerifyModel { Token = "tok", FirstName = "Pat", LastName = "Lee", Password = "Own3rPassword", ConfirmPassword = "Other" },
				CancellationToken.None);
			var again = mismatch.Should().BeOfType<ViewResult>().Subject;
			again.ViewName.Should().Be("Verify");
			((LmsLearnVerifyModel)again.Model).RegisteredName.Should().Be("Mallory X", "the registration's name is read again from the token, never posted");
			((LmsLearnVerifyModel)again.Model).Password.Should().BeNull();
			p.Learners.Verify(l => l.VerifyEmailAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			p.Controller.ModelState.Clear();
			var refused = await p.Controller.Verify(new LmsLearnVerifyModel { Token = "tok", FirstName = "Pat", LastName = "Lee", Password = "short", ConfirmPassword = "short" },
				CancellationToken.None);
			refused.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("Verify", "a refused password shows the page again");
			p.Controller.ModelState.ErrorCount.Should().BeGreaterThan(0);
		}

		[Test]
		public void Security_review_M2_no_get_action_of_the_portal_activates_or_changes_an_account()
		{
			var gets = typeof(LmsLearnController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
				.Where(m => m.GetCustomAttribute<HttpGetAttribute>() != null).ToList();
			var verifyPost = typeof(LmsLearnController).GetMethods().Single(m => m.Name == "Verify" && m.GetCustomAttribute<HttpPostAttribute>() != null);
			verifyPost.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull("the activation carries antiforgery, like Unsubscribe and /Sign");
			gets.Should().NotContain(m => m.GetCustomAttribute<HttpPostAttribute>() != null, "no action answers both GET and POST");

			// The token links of the e-mails (verify, accept an invitation, reset, unsubscribe) only show a page on GET.
			var source = System.IO.File.ReadAllText(System.IO.Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Controllers", "LmsLearnController.cs"));
			foreach (var mutation in new[] { "VerifyEmailAsync", "AcceptInviteAsync", "ResetPasswordAsync", "UnsubscribeAsync", "SignOutAsync" })
				foreach (var get in gets)
					BodyOf(source, get).Should().NotContain(mutation, get.Name + " (GET) must not call " + mutation);
		}

		private static string RepositoryRoot()
		{
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new System.IO.DirectoryNotFoundException("Repository root unavailable.");
		}

		/// <summary>The source text of a GET action: from its [HttpGet] attribute to the next attribute line or region of the controller.</summary>
		private static string BodyOf(string source, MethodInfo action)
		{
			var route = action.GetCustomAttribute<HttpGetAttribute>().Template;
			var start = source.IndexOf("[HttpGet(\"" + route + "\")]", StringComparison.Ordinal);
			start.Should().BeGreaterThan(0, action.Name);
			var next = source.IndexOf("[Http", start + 10, StringComparison.Ordinal);
			return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
		}

		#endregion

		#region L2: sign-out ends every copy of the session

		[Test]
		public async Task Security_review_L2_signing_out_of_the_portal_rotates_the_learners_stamp()
		{
			var learner = new LmsExternalLearner { LmsExternalLearnerId = 9, DepartmentId = PortalDepartment, LearnerUserId = LmsExternalLearnerIds.New(), Status = (int)LmsExternalLearnerStatuses.Active, SecurityStamp = "stamp-1" };
			var p = new Portal(learner);
			await p.OpenAsync();

			(await p.Controller.SignOutLearner(CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();

			p.Learners.Verify(l => l.SignOutAsync(PortalDepartment, 9, It.IsAny<CancellationToken>()), Times.Once,
				"the stamp the cookie carries is replaced, so a copied cookie is refused on its next request");
		}

		#endregion

		#region M3: protocol lessons for external learners follow the public-page rules

		private const int ProtocolId = 5;
		private const int ProtocolVersionId = 77;

		private static async Task<(LmsWorld World, LmsEnrollment External, LmsEnrollment Member, LmsLesson Lesson, DispatchProtocol Protocol, ProtocolVersion Version)> ProtocolCourseAsync()
		{
			var world = new LmsWorld();
			var version = new ProtocolVersion
			{
				ProtocolVersionId = ProtocolVersionId, DepartmentId = LmsWorld.DepartmentId, DispatchProtocolId = ProtocolId, Status = (int)ProtocolVersionStatuses.Published,
				ContentHtml = "<p>Chest pain</p>"
			};
			var protocol = new DispatchProtocol
			{
				DispatchProtocolId = ProtocolId, DepartmentId = LmsWorld.DepartmentId, Status = (int)ProtocolStatuses.Published, CurrentVersionId = ProtocolVersionId
			};
			world.ProtocolVersions.Setup(r => r.GetByIdForDepartmentAsync(LmsWorld.DepartmentId, ProtocolVersionId)).ReturnsAsync(version);
			world.DispatchProtocols.Setup(r => r.GetByIdAsync(ProtocolId)).ReturnsAsync(protocol);
			world.ProtocolAudiences.Setup(r => r.GetForProtocolAsync(LmsWorld.DepartmentId, ProtocolId)).ReturnsAsync(new List<ProtocolAudience>());

			var (course, courseVersion, lessons) = await world.PublishedCourseAsync("Chest pain",
				new LmsLesson { Kind = (int)LmsLessonKinds.ProtocolVersion, Title = "Chest pain protocol", ProtocolVersionId = ProtocolVersionId, IsRequired = true });
			var external = (await world.Learner.EnrollExternalAsync(LmsWorld.DepartmentId, course.LmsCourseId, LmsExternalLearnerIds.New(), "officer", false)).Enrollment;
			var member = new LmsEnrollment
			{
				DepartmentId = LmsWorld.DepartmentId, LmsCourseId = course.LmsCourseId, LmsCourseVersionId = courseVersion.LmsCourseVersionId, UserId = LmsWorld.Member,
				Status = (int)LmsEnrollmentStatuses.NotStarted, AssignedOn = DateTime.UtcNow
			};
			await world.Enrollments.InsertAsync(member, CancellationToken.None);
			return (world, external, member, lessons.Single(), protocol, version);
		}

		[Test]
		public async Task Security_review_M3_an_external_learner_sees_a_protocol_lesson_only_when_a_public_page_could_show_it()
		{
			var (world, external, member, lesson, protocol, version) = await ProtocolCourseAsync();

			var open = await world.Learner.GetPlayerAsync(LmsWorld.DepartmentId, external.UserId, external.LmsEnrollmentId, lesson.LmsLessonId);
			open.Protocol.Should().BeSameAs(version, "an eligible protocol is shown");
			open.ProtocolWithheld.Should().BeFalse();

			protocol.IsLicensedContent = true;
			var licensed = await world.Learner.GetPlayerAsync(LmsWorld.DepartmentId, external.UserId, external.LmsEnrollmentId, lesson.LmsLessonId);
			licensed.Protocol.Should().BeNull("licensed content never reaches a self-registered learner");
			licensed.ProtocolWithheld.Should().BeTrue();
			(await world.Learner.CompleteLessonAsync(LmsWorld.DepartmentId, external.UserId, external.LmsEnrollmentId, lesson.LmsLessonId, new LmsLessonCompletionInput()))
				.Error.Should().Be(LmsErrors.NotAuthorized, "nor is it completed unseen");
			(await world.Learner.GetPlayerAsync(LmsWorld.DepartmentId, member.UserId, member.LmsEnrollmentId, lesson.LmsLessonId)).Protocol.Should().BeSameAs(version, "members are unaffected");
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.LicensedContent);

			protocol.IsLicensedContent = false;
			world.ProtocolAudiences.Setup(r => r.GetForProtocolAsync(LmsWorld.DepartmentId, ProtocolId))
				.ReturnsAsync(new List<ProtocolAudience> { new ProtocolAudience { DepartmentId = LmsWorld.DepartmentId, DispatchProtocolId = ProtocolId, Kind = (int)ContentAudienceKinds.Role, RefId = "3" } });
			(await world.Learner.GetPlayerAsync(LmsWorld.DepartmentId, external.UserId, external.LmsEnrollmentId, lesson.LmsLessonId)).ProtocolWithheld.Should().BeTrue("audience-restricted");
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.AudienceRestricted);

			world.ProtocolAudiences.Setup(r => r.GetForProtocolAsync(LmsWorld.DepartmentId, ProtocolId)).ReturnsAsync(new List<ProtocolAudience>());
			version.ContentKind = (int)ProtocolVersionContentKinds.Document;
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.DocumentBacked);
			version.ContentKind = (int)ProtocolVersionContentKinds.Html;
			protocol.IsDisabled = true;
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.Disabled);
			protocol.IsDisabled = false;
			protocol.CurrentVersionId = ProtocolVersionId + 1;
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.NotPublished, "a superseded version is not public either");
			protocol.CurrentVersionId = ProtocolVersionId;
			(await world.Learner.GetExternalProtocolEligibilityAsync(LmsWorld.DepartmentId, lesson.LmsCourseVersionId)).Should().Be(ProtocolPublicEligibility.Eligible);
		}

		#endregion
	}
}
