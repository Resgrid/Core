using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Web.Models.AccountViewModels;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The lost-authenticator recovery is capped per address: over the cap no recovery code is checked or spent, the sign-in stays open
	/// for after the wait, and the page says how long that is.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		[Test]
		public async Task Recovery_codes_over_the_address_cap_are_not_checked_and_the_sign_in_waits()
		{
			var secret = await SignInWithPassword();
			for (var i = 0; i < SessionSecurityConfig.FactorRecoveryIpLimitPerHour; i++)
				await _formLimits.RecordAsync(PublicFormLimit.FactorRecovery, "127.0.0.1");

			var page = Browser(secret);
			var result = await page.Controller.LostFactor(new LostFactorViewModel { Code = "RC-1111" }, CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<LostFactorViewModel>().Which.CanRecover.Should().BeTrue();
			page.Http.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
			page.Controller.ModelState.Values.SelectMany(v => v.Errors).Should().ContainSingle()
				.Which.ErrorMessage.Should().StartWith("Too many authenticator recovery attempts");
			_users.Verify(u => u.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never, "the code is not spent");
			Row.Attempts.Should().Be(0, "a refused post is not a wrong code");
			_recoveryRows.Rows.Should().BeEmpty();
		}
	}
}
