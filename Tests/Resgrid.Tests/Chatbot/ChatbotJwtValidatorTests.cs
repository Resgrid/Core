using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture, NonParallelizable]
	public class ChatbotJwtValidatorTests
	{
		private const string AppId = "6bbec07f-39a4-4d73-9e3a-f75bfd44ace1";
		private const string TenantId = "58b7a025-de29-4cd6-97da-701c1e338205";
		private const string ServiceUrl = "https://smba.trafficmanager.net/amer/";
		private const string TeamsIssuer = "https://api.botframework.com";
		private const string GoogleAudience = "https://resgrid.example/api/v4/ChatbotPlatforms/GoogleChat";
		private RSA _rsa;
		private RsaSecurityKey _signingKey;
		private JsonWebKey _publicKey;
		private FixedMetadata _metadata;
		private ChatbotJwtValidator _validator;
		private (string AppId, string Password, string TenantId, string ServiceUrls) _previousConfig;

		[SetUp]
		public void SetUp()
		{
			_previousConfig = (ChatbotConfig.TeamsAppId, ChatbotConfig.TeamsAppPassword,
				ChatbotConfig.TeamsTenantId, ChatbotConfig.TeamsServiceUrls);
			ChatbotConfig.TeamsAppId = AppId;
			ChatbotConfig.TeamsAppPassword = "test-password";
			ChatbotConfig.TeamsTenantId = TenantId;
			ChatbotConfig.TeamsServiceUrls = ServiceUrl;
			_rsa = RSA.Create(2048);
			_signingKey = new RsaSecurityKey(_rsa) { KeyId = "provider-test-key" };
			_publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(
				new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = _signingKey.KeyId });
			_publicKey.AdditionalData["endorsements"] = new[] { "msteams" };
			var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = new JsonWebKeySet() };
			configuration.JsonWebKeySet.Keys.Add(_publicKey);
			configuration.SigningKeys.Add(_publicKey);
			_metadata = new FixedMetadata(configuration);
			_validator = new ChatbotJwtValidator(_metadata, _metadata);
		}

		[TearDown]
		public void TearDown()
		{
			ChatbotConfig.TeamsAppId = _previousConfig.AppId;
			ChatbotConfig.TeamsAppPassword = _previousConfig.Password;
			ChatbotConfig.TeamsTenantId = _previousConfig.TenantId;
			ChatbotConfig.TeamsServiceUrls = _previousConfig.ServiceUrls;
			_rsa?.Dispose();
		}

		[Test]
		public async Task Teams_ValidSignedToken_WithMatchingRouteAndEndorsement_IsAccepted()
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(), ServiceUrl)).Should().BeTrue();
		}

		[Test]
		public async Task Teams_KeyWithoutOptionalEndorsements_IsAccepted()
		{
			_publicKey.AdditionalData.Remove("endorsements");
			(await _validator.ValidateTeamsAsync(TeamsToken(), ServiceUrl)).Should().BeTrue();
		}

		[TestCase("different-app")]
		[TestCase(AppId + "/")]
		public async Task Teams_WrongAudience_IsRejected(string audience)
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(audience: audience), ServiceUrl)).Should().BeFalse();
		}

		[Test]
		public async Task Teams_WrongIssuer_IsRejected()
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(issuer: "https://untrusted.example"), ServiceUrl)).Should().BeFalse();
		}

		[Test]
		public async Task Teams_ServiceUrlClaimMustMatchAuthenticatedActivity()
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(serviceUrl: "https://smba.trafficmanager.net/emea/"), ServiceUrl))
				.Should().BeFalse();
		}

		[Test]
		public async Task Teams_SignedServiceUrlOutsideConfiguredAllowlist_IsRejected()
		{
			const string untrusted = "https://untrusted.example/";
			(await _validator.ValidateTeamsAsync(TeamsToken(serviceUrl: untrusted), untrusted)).Should().BeFalse();
			_metadata.Reads.Should().Be(0, "unapproved destinations should be rejected before token verification");
		}

		[Test]
		public async Task Teams_ExpiredToken_IsRejected()
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(expires: DateTime.UtcNow.AddMinutes(-5)), ServiceUrl)).Should().BeFalse();
		}

		[Test]
		public async Task Teams_ValidSignatureWithDisallowedAlgorithm_IsRejected()
		{
			(await _validator.ValidateTeamsAsync(TeamsToken(algorithm: SecurityAlgorithms.RsaSha512), ServiceUrl)).Should().BeFalse();
		}

		[Test]
		public async Task Teams_SignatureFromAnUntrustedKey_IsRejected()
		{
			using var otherRsa = RSA.Create(2048);
			var otherKey = new RsaSecurityKey(otherRsa) { KeyId = _signingKey.KeyId };
			var bearer = Sign(TeamsIssuer, AppId, new[] { new Claim("serviceurl", ServiceUrl) }, key: otherKey);
			(await _validator.ValidateTeamsAsync(bearer, ServiceUrl)).Should().BeFalse();
		}

		[TestCase("webchat")]
		[TestCase("")]
		public async Task Teams_PresentEndorsementsMustIncludeTeams(string endorsement)
		{
			_publicKey.AdditionalData["endorsements"] = string.IsNullOrEmpty(endorsement)
				? Array.Empty<string>() : new[] { endorsement };
			(await _validator.ValidateTeamsAsync(TeamsToken(), ServiceUrl)).Should().BeFalse();
		}

		[TestCase(null)]
		[TestCase("Basic invalid")]
		[TestCase("Bearer invalid")]
		public async Task InvalidAuthorizationHeader_IsRejectedWithoutMetadataFetch(string header)
		{
			(await _validator.ValidateTeamsAsync(header, ServiceUrl)).Should().BeFalse();
			(await _validator.ValidateGoogleChatAsync(header, GoogleAudience)).Should().BeFalse();
			_metadata.Reads.Should().Be(0);
		}

		[TestCase("https://accounts.google.com")]
		[TestCase("accounts.google.com")]
		public async Task Google_ValidSignedTokenFromVerifiedChatIdentity_IsAccepted(string issuer)
		{
			(await _validator.ValidateGoogleChatAsync(GoogleToken(issuer: issuer), GoogleAudience)).Should().BeTrue();
		}

		[TestCase("https://another-app.example")]
		[TestCase(GoogleAudience + "/")]
		public async Task Google_AudienceMustMatchExactly(string audience)
		{
			(await _validator.ValidateGoogleChatAsync(GoogleToken(audience: audience), GoogleAudience)).Should().BeFalse();
		}

		[TestCase("other-service@system.gserviceaccount.com", "true")]
		[TestCase("chat@system.gserviceaccount.com", "false")]
		[TestCase("chat@system.gserviceaccount.com", null)]
		public async Task Google_RequiresVerifiedChatServiceAccount(string email, string verified)
		{
			(await _validator.ValidateGoogleChatAsync(GoogleToken(email: email, verified: verified), GoogleAudience)).Should().BeFalse();
		}

		[Test]
		public async Task Google_WrongIssuer_IsRejectedEvenWithChatEmail()
		{
			(await _validator.ValidateGoogleChatAsync(GoogleToken(issuer: "https://untrusted.example"), GoogleAudience)).Should().BeFalse();
		}

		private string TeamsToken(string audience = AppId, string issuer = TeamsIssuer, string serviceUrl = ServiceUrl,
			DateTime? expires = null, string algorithm = SecurityAlgorithms.RsaSha256)
			=> Sign(issuer, audience, new[] { new Claim("serviceurl", serviceUrl) }, expires, algorithm);

		private string GoogleToken(string audience = GoogleAudience, string issuer = "https://accounts.google.com",
			string email = "chat@system.gserviceaccount.com", string verified = "true")
		{
			var claims = new List<Claim> { new Claim("email", email) };
			if (verified != null) claims.Add(new Claim("email_verified", verified, ClaimValueTypes.Boolean));
			return Sign(issuer, audience, claims);
		}

		private string Sign(string issuer, string audience, IEnumerable<Claim> claims, DateTime? expires = null,
			string algorithm = SecurityAlgorithms.RsaSha256, SecurityKey key = null)
		{
			var token = new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow.AddHours(-1),
				expires ?? DateTime.UtcNow.AddMinutes(5), new SigningCredentials(key ?? _signingKey, algorithm));
			return "Bearer " + new JwtSecurityTokenHandler().WriteToken(token);
		}

		private sealed class FixedMetadata : IConfigurationManager<OpenIdConnectConfiguration>
		{
			private readonly OpenIdConnectConfiguration _configuration;
			public int Reads { get; private set; }
			public FixedMetadata(OpenIdConnectConfiguration configuration) { _configuration = configuration; }
			public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
			{ Reads++; return Task.FromResult(_configuration); }
			public void RequestRefresh() { }
		}
	}
}
