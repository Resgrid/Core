using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Web.Models.AccountViewModels;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Web LogOn pauses an address whose failed sign-ins cover too many different usernames: the next sign-in from it, even with the
	/// right password, is refused with a 429 and a message, before any password is checked.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		[Test]
		public async Task Failed_sign_ins_across_many_usernames_pause_web_sign_in_from_that_address()
		{
			_signIn.Setup(s => s.PasswordSignInAsync(It.IsAny<string>(), "wrong", true, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);
			for (var i = 0; i < SessionSecurityConfig.LoginThrottleDistinctAccountLimit; i++)
			{
				var failed = Browser().Controller;
				(await failed.LogOn(new LoginViewModel { Username = "sprayed-" + i, Password = "wrong" }, CancellationToken.None, null))
					.Should().BeOfType<ViewResult>();
				failed.ModelState[string.Empty]!.Errors.Should().ContainSingle().Which.ErrorMessage.Should().StartWith("Invalid username or password");
			}

			var (controller, http) = Browser();
			var result = await controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null);

			result.Should().BeOfType<ViewResult>();
			http.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			controller.ModelState[string.Empty]!.Errors.Should().ContainSingle().Which.ErrorMessage.Should().StartWith("Too many failed sign-in attempts");
			_signIn.Verify(s => s.PasswordSignInAsync("user1", "pw", It.IsAny<bool>(), It.IsAny<bool>()), Times.Never, "no password is checked while paused");
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => !x.Successful && x.Data.StartsWith("Web LogOn refused")), It.IsAny<CancellationToken>()),
				Times.Once);
			_rows.Rows.Should().BeEmpty("no sign-in was started");
		}

		[Test]
		public async Task A_verified_password_never_counts_against_the_address()
		{
			for (var i = 0; i < SessionSecurityConfig.LoginThrottleDistinctAccountLimit + 2; i++)
				(await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null))
					.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("LoginMfa");

			(await _loginThrottle.IsThrottledAsync("127.0.0.1")).Should().BeFalse();
		}
	}
}
