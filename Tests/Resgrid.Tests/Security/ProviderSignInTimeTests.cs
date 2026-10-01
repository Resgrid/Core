using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// When the provider itself authenticated the member, read from a validated external identity (plan section 12.5.2): a
	/// shared installation's sign-in must be fresh by it.
	/// </summary>
	[TestFixture]
	public class ProviderSignInTimeTests
	{
		[Test]
		public void Reads_an_id_tokens_auth_time_as_the_jwt_handler_maps_it()
		{
			var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
			var authTime = DateTimeOffset.UtcNow.AddMinutes(-7).ToUnixTimeSeconds();
			var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("https://idp.example.test/", "client",
				new[] { new Claim("sub", "external-user"), new Claim("auth_time", authTime.ToString(), ClaimValueTypes.Integer64) },
				DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

			// The legacy validator's own handler, with its default inbound claim mapping.
			var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
			{
				ValidIssuer = "https://idp.example.test/", ValidAudience = "client", IssuerSigningKey = key
			}, out _);

			ProviderSignInTime.Read(principal).Should().Be(DateTimeOffset.FromUnixTimeSeconds(authTime).UtcDateTime);
		}

		[Test]
		public void Says_nothing_without_a_usable_time()
		{
			ProviderSignInTime.Read(null).Should().BeNull();
			ProviderSignInTime.Read(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeNull();
			ProviderSignInTime.Read(Principal("not-a-number")).Should().BeNull();
			ProviderSignInTime.Read(Principal("-5")).Should().BeNull();
			ProviderSignInTime.Read(Principal("99999999999999")).Should().BeNull();
			ProviderSignInTime.Read(Principal("0")).Should().Be(DateTime.UnixEpoch);
		}

		[Test]
		public void Is_fresh_only_within_the_window_and_never_in_the_future()
		{
			var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
			var window = TimeSpan.FromMinutes(5);
			var skew = TimeSpan.FromMinutes(2);

			ProviderSignInTime.IsFresh(now.AddMinutes(-1), now, window, skew).Should().BeTrue();
			ProviderSignInTime.IsFresh(now.AddMinutes(-7), now, window, skew).Should().BeTrue("the window plus the clock skew");
			ProviderSignInTime.IsFresh(now.AddMinutes(-7).AddSeconds(-1), now, window, skew).Should().BeFalse();
			ProviderSignInTime.IsFresh(now.AddMinutes(2), now, window, skew).Should().BeTrue("a provider clock a little ahead");
			ProviderSignInTime.IsFresh(now.AddMinutes(2).AddSeconds(1), now, window, skew).Should().BeFalse();
			ProviderSignInTime.IsFresh(null, now, window, skew).Should().BeFalse();
		}

		[Test]
		public void Writes_the_claim_value_an_id_token_carries()
		{
			ProviderSignInTime.ClaimValue(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)).Should().Be("1790856000");
			ProviderSignInTime.ClaimValue(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Unspecified)).Should().Be("1790856000", "SAML instants are UTC");
		}

		private static ClaimsPrincipal Principal(string authTime) =>
			new(new ClaimsIdentity(new[] { new Claim(ProviderSignInTime.ClaimType, authTime) }));
	}
}
