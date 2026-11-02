using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;

namespace Resgrid.Tests.Security.Live
{
	/// <summary>
	/// The API password grant pauses an address whose failures cover too many different usernames (password spraying, credential
	/// stuffing). The per-account lockout never sees those: each account is tried only a few times.
	/// </summary>
	public partial class SignInJourneyTests
	{
		private Task<Answer> PasswordGrant(string username, string password) =>
			Send("unit", HttpMethod.Post, "/connect/token", new Dictionary<string, string>
			{
				["grant_type"] = "password", ["username"] = username, ["password"] = password, ["scope"] = "openid profile offline_access mobile"
			}, form: true);

		[Test(Description = "Throttle. Failures across many usernames from one address pause the password grant there, correct password or not")]
		public async Task Failures_across_many_usernames_pause_the_password_grant_from_that_address()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec());
			await TokensWork("unit", await PasswordGrant("unit", user, transaction: false));

			for (var i = 1; i < SessionSecurityConfig.LoginThrottleDistinctAccountLimit; i++)
				(await PasswordGrant("sprayed-" + i, "Winter2026!")).Error.Should().Be("invalid_grant");
			await TokensWork("unit", await PasswordGrant("unit", user, transaction: false));

			(await PasswordGrant("sprayed-last", "Winter2026!")).Error.Should().Be("invalid_grant");
			var paused = await PasswordGrant("unit", user, transaction: false);

			paused.Error.Should().Be("invalid_grant", "every client already handles it");
			paused.Body.Value<string>("error_description").Should().Contain("Too many failed sign-in attempts");
			_server.SessionsOf(user.UserId).Should().HaveCount(2, "no password is checked while the address is paused");
		}

		[Test(Description = "Throttle. One account failing over and over (a device on an old password) never pauses its network")]
		public async Task One_account_failing_repeatedly_never_pauses_the_address()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec());
			var other = await _server.AddUserAsync(new LiveUserSpec());

			for (var i = 0; i < SessionSecurityConfig.LoginThrottleDistinctAccountLimit + 5; i++)
				(await PasswordGrant(user.Username, "an-old-password")).Error.Should().Be("invalid_grant");

			await TokensWork("unit", await PasswordGrant("unit", other, transaction: false));
		}
	}
}
