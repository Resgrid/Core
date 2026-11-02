using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Web.Middleware;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// A password change the department requires (an administrator's flag, an expired password) is recorded on the session every password
	/// sign-in makes, so RequirePasswordChangeMiddleware can hold it on the change page. Before, only the plain password sign-in redirected
	/// there once, any other address skipped it, and a sign-in that finished with a second factor never saw it at all.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		private void ManagedLocally(bool ssoManaged = false) =>
			_links.Setup(l => l.GetSsoManagementStateAsync(UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SsoManagementState { IsSsoManaged = ssoManaged });

		private void PasswordOnly()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			_signIn.Setup(s => s.PasswordSignInAsync("user1", "pw", true, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
		}

		private void VerifySignedIn(Func<ClaimsPrincipal, bool> principal, string because) =>
			_lastAuthentication.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), CookieAuthenticationDefaults.AuthenticationScheme,
				It.Is<ClaimsPrincipal>(p => principal(p)), It.IsAny<AuthenticationProperties>()), Times.Once, because);

		private static bool MustChange(ClaimsPrincipal principal) =>
			principal.HasClaim(RequirePasswordChangeMiddleware.ClaimType, DepartmentId.ToString());

		[Test]
		public async Task A_member_an_administrator_flagged_is_held_for_the_password_change_on_the_new_session()
		{
			PasswordOnly();
			ManagedLocally();
			_membership.MustChangePassword = true;

			var result = await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, "/User/Calls");

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ForcePasswordChange");
			VerifySignedIn(MustChange, "the middleware, not this one redirect, keeps the session on the change page");
		}

		[Test]
		public async Task An_expired_password_is_held_for_the_change_after_a_second_factor_too()
		{
			ManagedLocally();
			_policy.PasswordExpirationDays = 30;
			_membership.PasswordLastSetOn = DateTime.UtcNow.AddDays(-60);
			_sso.Setup(s => s.IsPasswordExpired(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<DateTime?>())).Returns(true);
			var secret = await SignInWithPassword();

			(await Browser(secret).Controller.LoginMfa(new LoginMfaViewModel { Code = "123456", ReturnUrl = "/User/Calls" }, CancellationToken.None))
				.Should().BeOfType<LocalRedirectResult>();

			VerifySignedIn(MustChange, "a sign-in finished with the authenticator used to skip the expiration check entirely");
		}

		[Test]
		public async Task A_member_whose_password_is_current_and_unflagged_signs_in_as_before()
		{
			PasswordOnly();
			ManagedLocally();

			(await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Dashboard");

			VerifySignedIn(p => !p.HasClaim(c => c.Type == RequirePasswordChangeMiddleware.ClaimType), "nothing requires a change");
		}

		[Test]
		public async Task An_account_its_identity_provider_manages_is_never_held_for_a_password_it_cannot_change_here()
		{
			// The change page refuses an SSO-managed account, so holding it there would leave it nowhere to go.
			PasswordOnly();
			ManagedLocally(ssoManaged: true);
			_membership.MustChangePassword = true;

			(await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null))
				.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Dashboard");

			VerifySignedIn(p => !p.HasClaim(c => c.Type == RequirePasswordChangeMiddleware.ClaimType), "its password is not changed here");
		}

		[Test]
		public async Task The_change_page_reads_the_required_change_from_the_session_claim()
		{
			var (controller, http) = Browser(signedIn: true);
			http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
			{
				new Claim(ClaimTypes.NameIdentifier, UserId), new Claim(RequirePasswordChangeMiddleware.ClaimType, DepartmentId.ToString()),
				new Claim(SessionClaimTypes.SessionId, WebSession)
			}, "test"));
			_sso.Setup(s => s.GetEffectiveMinPasswordLengthAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(12);

			var page = await controller.ForcePasswordChange(CancellationToken.None);

			page.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<ForcePasswordChangeViewModel>()
				.Which.MinPasswordLength.Should().Be(12);
		}
	}
}
