using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 9: the return-target registry and the brokered SSO service. The server runs the IdP
	/// round trip against a fake IdP that signs real id_tokens, and hands the client only a one-time code at a registered
	/// return target, redeemable once with the client's PKCE verifier.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SsoBrokerServiceTests
	{
		private const int DepartmentId = 42;
		private const string UserId = "user-1";
		private const string Targets = "web=https://app.resgrid.test/Account/SsoReturn;unit=resgridunit://sso-return,https://unit.resgrid.test/sso-return,http://127.0.0.1:*/sso-return;" +
			"responder=resgrid://sso-return";
		private const string UnitReturn = "resgridunit://sso-return";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = DateTime.UtcNow;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private bool _gate;
		private string _apiBase;
		private Clock _clock;
		private InMemorySsoLoginTransactionRepository _rows;
		private InMemoryBrokerReplayRepository _replay;
		private FakeOidcProvider _idp;
		private Mock<IDepartmentSsoService> _sso;
		private Mock<IMfaPolicyService> _policy;
		private DepartmentSsoConfig _oidc;
		private DepartmentSsoConfig _saml;
		private string _policyViolation;
		private SsoBrokerService _service;

		[SetUp]
		public void SetUp()
		{
			_gate = SsoConfig.BrokeredSsoEnabled;
			_apiBase = SystemBehaviorConfig.ResgridApiBaseUrl;
			SsoConfig.BrokeredSsoEnabled = true;
			SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.test";

			_clock = new Clock();
			_rows = new InMemorySsoLoginTransactionRepository();
			_replay = new InMemoryBrokerReplayRepository();
			_idp = new FakeOidcProvider();
			_policyViolation = null;
			_policy = new Mock<IMfaPolicyService>();
			_oidc = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "oidc-config", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
				Authority = FakeOidcProvider.Authority, ClientId = "resgrid-client", EncryptedClientSecret = "client-secret"
			};
			_saml = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = "saml-config", DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Saml2, IsEnabled = true,
				EntityId = "https://api.resgrid.test/saml/saml-config", AssertionConsumerServiceUrl = "https://api.resgrid.test/api/v4/connect/saml-mobile-callback",
				EncryptedIdpCertificate = "idp-cert", IdpSsoUrl = "https://idp.example.test/saml2/sso"
			};
			_service = Service(_oidc);
		}

		[TearDown]
		public void TearDown()
		{
			SsoConfig.BrokeredSsoEnabled = _gate;
			SystemBehaviorConfig.ResgridApiBaseUrl = _apiBase;
		}

		private SsoBrokerService Service(DepartmentSsoConfig active)
		{
			_sso = new Mock<IDepartmentSsoService>();
			_sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { active });
			_sso.Setup(s => s.GetSsoConfigForDepartmentAsync(DepartmentId, (SsoProviderType)active.SsoProviderType, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => active);
			_sso.Setup(s => s.ProvisionOrLinkUserAsync(DepartmentId, It.IsAny<ClaimsPrincipal>(), active, "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, ClaimsPrincipal principal, DepartmentSsoConfig _, string _, CancellationToken _) =>
					principal.FindFirst(ClaimTypes.NameIdentifier)?.Value == "external-user" ? new IdentityUser { Id = UserId } : null);
			_sso.Setup(s => s.FindLinkedUserIdAsync(DepartmentId, It.IsAny<ClaimsPrincipal>(), active, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int _, ClaimsPrincipal principal, DepartmentSsoConfig _, CancellationToken _) =>
					principal.FindFirst(ClaimTypes.NameIdentifier)?.Value == "external-user" ? UserId : null);
			_sso.Setup(s => s.EnforceSecurityPolicyAsync(DepartmentId, UserId, It.IsAny<string>(), true, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => _policyViolation);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string value) => "enc:" + value);
			encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string value) => value.StartsWith("enc:") ? value[4..] : throw new CryptographicException());
			encryption.Setup(e => e.DecryptForDepartment(It.IsAny<string>(), DepartmentId, "DEPT")).Returns((string value, int _, string _) => value);

			return new SsoBrokerService(_rows, _sso.Object, departments.Object, Mock.Of<IIdentityUserRepository>(), encryption.Object, _idp,
				new SsoReturnTargetRegistry(Targets), _replay, _policy.Object, _clock);
		}

		private static string NewVerifier() => FakeOidcProvider.Base64Url(RandomNumberGenerator.GetBytes(32));
		private static string Challenge(string verifier) => FakeOidcProvider.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

		private Task<SsoBeginResult> Begin(string verifier, SsoTransactionPurpose purpose = SsoTransactionPurpose.Login, string target = UnitReturn,
			string challengeMethod = "S256", string sessionId = null, string userId = null, bool shared = false) =>
			_service.BeginAsync(new SsoBeginRequest
			{
				DepartmentId = DepartmentId, DepartmentCode = "DEPT", Purpose = purpose, ClientApplication = UserSessionClientApplication.Unit,
				Platform = "ios", ReturnTarget = target, ClientState = "client-csrf", CodeChallenge = Challenge(verifier), CodeChallengeMethod = challengeMethod,
				SessionId = sessionId, UserId = userId, AuthenticationGeneration = sessionId == null ? null : 4, SharedInstallation = shared
			});

		/// <summary>Begins an OIDC sign-in and plays the IdP: the token carries the nonce the authorize URL asked for.</summary>
		private async Task<(string State, SsoBeginResult Begun)> BeginAtIdp(string verifier, Func<string, string> token = null,
			SsoTransactionPurpose purpose = SsoTransactionPurpose.Login, bool shared = false)
		{
			var begun = await Begin(verifier, purpose, sessionId: purpose == SsoTransactionPurpose.Reauthentication ? "session-1" : null,
				userId: purpose == SsoTransactionPurpose.Reauthentication ? UserId : null, shared: shared);
			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			var (state, nonce, challenge, _, _) = FakeOidcProvider.Authorize(begun.AuthorizeUrl);
			_idp.ExpectedChallenge = challenge;
			_idp.NextIdToken = () => token == null ? _idp.Token(nonce) : token(nonce);
			return (state, begun);
		}

		private static string Query(string url, string name) => HttpUtility.ParseQueryString(new Uri(url).Query)[name];

		/// <summary>Gives the configuration a provider step-up mapping at version 3, tested unless told otherwise.</summary>
		private static void Mapped(DepartmentSsoConfig config, bool tested = true)
		{
			config.FederatedMfaMappingJson = (SsoProviderType)config.SsoProviderType == SsoProviderType.Oidc
				? new FederatedMfaMapping
				{
					RequestAcrValues = new() { "urn:okta:loa:2fa:any" }, RequestClaims = "{\"id_token\":{\"acrs\":{\"essential\":true,\"value\":\"c1\"}}}",
					AcceptAmr = new() { "mfa" }, AcceptAcrs = new() { "c1" }
				}.Serialize()
				: new FederatedMfaMapping
				{
					RequestAuthnContextClassRefs = new() { "https://refeds.org/profile/mfa" }, AcceptAuthnContextClassRefs = new() { "https://refeds.org/profile/mfa" }
				}.Serialize();
			config.FederatedMfaMappingVersion = 3;
			config.FederatedMfaTestedVersion = tested ? 3 : null;
		}

		private void AcceptFederated(bool accepted = true) =>
			_policy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, It.IsAny<MfaMethodScope>(), MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()))
				.ReturnsAsync(accepted);

		private Task<SsoBeginResult> BeginStepUp(string verifier, string operation = MfaStepUpOperations.SecurityChange, string sessionId = "session-1",
			string userId = UserId, string loginTransactionId = null, SsoTransactionPurpose purpose = SsoTransactionPurpose.StepUp) =>
			_service.BeginAsync(new SsoBeginRequest
			{
				DepartmentId = DepartmentId, DepartmentCode = "DEPT", Purpose = purpose, ClientApplication = UserSessionClientApplication.Unit,
				Platform = "ios", ReturnTarget = UnitReturn, ClientState = "client-csrf", CodeChallenge = Challenge(verifier), CodeChallengeMethod = "S256",
				SessionId = sessionId, UserId = userId, AuthenticationGeneration = 4, Operation = operation, LoginTransactionId = loginTransactionId
			});

		/// <summary>Plays the IdP for a begun OIDC round trip and returns the callback result.</summary>
		private Task<SsoCallbackResult> AtIdp(SsoBeginResult begun, Func<string, string> token)
		{
			var (state, nonce, challenge, _, _) = FakeOidcProvider.Authorize(begun.AuthorizeUrl);
			_idp.ExpectedChallenge = challenge;
			_idp.NextIdToken = () => token(nonce);
			return _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null);
		}

		// ---- Return targets ------------------------------------------------------------------------------------------

		[TestCase(UserSessionClientApplication.Unit, "resgridunit://sso-return", true)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test/sso-return", true)]
		[TestCase(UserSessionClientApplication.Unit, "http://127.0.0.1:53123/sso-return", true)]
		[TestCase(UserSessionClientApplication.Unit, "http://127.0.0.1:8080/sso-return", true)]
		[TestCase(UserSessionClientApplication.Unit, "HTTPS://UNIT.RESGRID.TEST/sso-return", true)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test/sso-return/", false)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test/SSO-RETURN", false)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test:8443/sso-return", false)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test.evil.test/sso-return", false)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test/sso-return?next=x", false)]
		[TestCase(UserSessionClientApplication.Unit, "https://unit.resgrid.test/sso-return#x", false)]
		[TestCase(UserSessionClientApplication.Unit, "http://127.0.0.2:53123/sso-return", false)]
		[TestCase(UserSessionClientApplication.Unit, "http://127.0.0.1:53123/other", false)]
		[TestCase(UserSessionClientApplication.Unit, "resgridunit://sso-return:99", false)]
		[TestCase(UserSessionClientApplication.Unit, "resgrid://sso-return", false)]
		[TestCase(UserSessionClientApplication.Responder, "resgridunit://sso-return", false)]
		[TestCase(UserSessionClientApplication.Dispatch, "resgridunit://sso-return", false)]
		public void Return_targets_match_exactly_for_their_own_app(UserSessionClientApplication client, string target, bool allowed) =>
			new SsoReturnTargetRegistry(Targets).IsAllowed(client, target).Should().Be(allowed);

		[TestCase("unit=resgrid://sso-return;responder=resgrid://sso-return", TestName = "Two apps sharing a custom scheme")]
		[TestCase("unit=http://unit.resgrid.test/sso-return", TestName = "Plain http off loopback")]
		[TestCase("unit=javascript://alert", TestName = "A script scheme")]
		[TestCase("unit=https://unit.resgrid.test/sso-return?x=1", TestName = "A query")]
		[TestCase("unit=https://*.resgrid.test/sso-return", TestName = "A host wildcard")]
		[TestCase("pager=https://pager.resgrid.test/sso-return", TestName = "An unknown client")]
		public void A_registry_with_any_problem_allows_nothing(string configuration)
		{
			var registry = new SsoReturnTargetRegistry(configuration + ";web=https://app.resgrid.test/Account/SsoReturn");
			registry.IsReady.Should().BeFalse();
			registry.IsAllowed(UserSessionClientApplication.Web, "https://app.resgrid.test/Account/SsoReturn").Should().BeFalse();
			registry.Problems.Should().NotBeEmpty();
		}

		[Test]
		public void An_empty_registry_allows_nothing()
		{
			var registry = new SsoReturnTargetRegistry("");
			registry.IsReady.Should().BeFalse();
			registry.IsAllowed(UserSessionClientApplication.Unit, UnitReturn).Should().BeFalse();
			new SsoReturnTargetRegistry().IsReady.Should().BeFalse("the deployment default is empty, so brokered SSO starts off");
		}

		// ---- Begin ---------------------------------------------------------------------------------------------------

		[Test]
		public async Task Begin_sends_the_idp_state_nonce_and_its_own_pkce_and_keeps_only_hashes()
		{
			var begun = await Begin(NewVerifier());

			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			begun.ExpiresInSeconds.Should().Be(SsoConfig.BrokeredTransactionLifetimeSeconds);
			begun.AuthorizeUrl.Should().StartWith(FakeOidcProvider.AuthorizationEndpoint + "?");
			Query(begun.AuthorizeUrl, "response_type").Should().Be("code");
			Query(begun.AuthorizeUrl, "client_id").Should().Be("resgrid-client");
			Query(begun.AuthorizeUrl, "redirect_uri").Should().Be("https://api.resgrid.test/api/v4/connect/oidc-callback");
			Query(begun.AuthorizeUrl, "code_challenge_method").Should().Be("S256");
			Query(begun.AuthorizeUrl, "prompt").Should().BeNull("a plain sign-in does not force reauthentication");

			var (state, nonce, idpChallenge, _, _) = FakeOidcProvider.Authorize(begun.AuthorizeUrl);
			var row = _rows.Rows.Single();
			row.SsoLoginTransactionId.Should().Be(begun.TransactionId);
			row.StateHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
			row.NonceHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));
			row.EncryptedIdpCodeVerifier.Should().StartWith("enc:");
			Challenge(row.EncryptedIdpCodeVerifier[4..]).Should().Be(idpChallenge, "the server holds its own verifier toward the IdP");
			row.ReturnTarget.Should().Be(UnitReturn);
			row.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			typeof(SsoLoginTransaction).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(row))
				.Should().NotContain(new[] { state, nonce });
		}

		[Test]
		public async Task Begin_refuses_what_it_cannot_bind()
		{
			(await Begin(NewVerifier(), target: "https://attacker.example/steal")).Outcome.Should().Be(SsoBrokerOutcome.ReturnTargetNotAllowed);
			(await Begin(NewVerifier(), target: "resgrid://sso-return")).Outcome.Should().Be(SsoBrokerOutcome.ReturnTargetNotAllowed,
				"Responder's return target is not Unit's");
			(await Begin(NewVerifier(), challengeMethod: "plain")).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest);
			(await Begin(NewVerifier(), SsoTransactionPurpose.StepUp)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"a step-up needs the session or sign-in it serves");
			(await Begin(NewVerifier(), SsoTransactionPurpose.Reauthentication)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"reauthentication needs the signed-in session");

			SsoConfig.BrokeredSsoEnabled = false;
			(await Begin(NewVerifier())).Outcome.Should().Be(SsoBrokerOutcome.Unavailable);
			_rows.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Reauthentication_asks_the_idp_to_authenticate_again()
		{
			var begun = await Begin(NewVerifier(), SsoTransactionPurpose.Reauthentication, sessionId: "session-1", userId: UserId);

			Query(begun.AuthorizeUrl, "prompt").Should().Be("login");
			Query(begun.AuthorizeUrl, "max_age").Should().Be(SsoConfig.ReauthenticationMaxAgeSeconds.ToString());
			_rows.Rows.Single().SessionId.Should().Be("session-1");
			_rows.Rows.Single().ExpectedUserId.Should().Be(UserId);
		}

		[Test]
		public async Task A_shared_installation_makes_the_idp_authenticate_the_operator_again()
		{
			// Plan section 12.5.2: the previous operator's provider session must never sign the next operator in. An account
			// chooser is not enough where the installation's browser keeps that session: the next one could pick it.
			var login = await Begin(NewVerifier(), shared: true);
			Query(login.AuthorizeUrl, "prompt").Should().Be("login");
			Query(login.AuthorizeUrl, "max_age").Should().Be("0");
			_rows.Rows.Last().SharedInstallation.Should().BeTrue("the callback checks the provider's sign-in is fresh");

			var personalLogin = await Begin(NewVerifier());
			Query(personalLogin.AuthorizeUrl, "prompt").Should().BeNull("a personal sign-in is unchanged");
			Query(personalLogin.AuthorizeUrl, "max_age").Should().BeNull();
			_rows.Rows.Last().SharedInstallation.Should().BeFalse();

			var reauth = await Begin(NewVerifier(), SsoTransactionPurpose.Reauthentication, sessionId: "session-1", userId: UserId, shared: true);
			Query(reauth.AuthorizeUrl, "prompt").Should().Be("login select_account");
			Query(reauth.AuthorizeUrl, "max_age").Should().Be(SsoConfig.ReauthenticationMaxAgeSeconds.ToString());

			using var spKey = RSA.Create(2048);
			_saml.EncryptedSigningCertificate = spKey.ExportRSAPrivateKeyPem();
			_service = Service(_saml);
			var saml = await Begin(NewVerifier(), shared: true);
			using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(Query(saml.AuthorizeUrl, "SAMLRequest"))), CompressionMode.Decompress);
			var request = new XmlDocument();
			request.Load(inflate);
			request.DocumentElement!.GetAttribute("ForceAuthn").Should().Be("true", "SAML has no account chooser, so the provider authenticates again");

			var personal = await Begin(NewVerifier());
			using var personalInflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(Query(personal.AuthorizeUrl, "SAMLRequest"))),
				CompressionMode.Decompress);
			var personalRequest = new XmlDocument();
			personalRequest.Load(personalInflate);
			personalRequest.DocumentElement!.HasAttribute("ForceAuthn").Should().BeFalse("a personal sign-in is unchanged");
		}

		[Test]
		public async Task A_saml_sign_in_sends_a_signed_authn_request_bound_to_the_transaction()
		{
			using var spKey = RSA.Create(2048);
			_saml.EncryptedSigningCertificate = spKey.ExportRSAPrivateKeyPem();
			_service = Service(_saml);

			var begun = await Begin(NewVerifier(), SsoTransactionPurpose.Reauthentication, sessionId: "session-1", userId: UserId);

			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			begun.AuthorizeUrl.Should().StartWith(_saml.IdpSsoUrl + "?SAMLRequest=");
			var relayState = Query(begun.AuthorizeUrl, "RelayState");
			relayState.Should().StartWith("rgsso.");
			_service.IsBrokeredRelayState(relayState).Should().BeTrue();

			using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(Query(begun.AuthorizeUrl, "SAMLRequest"))), CompressionMode.Decompress);
			var request = new XmlDocument();
			request.Load(inflate);
			var root = request.DocumentElement!;
			root.LocalName.Should().Be("AuthnRequest");
			root.GetAttribute("ID").Should().Be(_rows.Rows.Single().SamlRequestId);
			root.GetAttribute("AssertionConsumerServiceURL").Should().Be(_saml.AssertionConsumerServiceUrl);
			root.GetAttribute("Destination").Should().Be(_saml.IdpSsoUrl);
			root.GetAttribute("ForceAuthn").Should().Be("true", "reauthentication forces a new IdP sign-in");
			root.FirstChild!.InnerText.Should().Be(_saml.EntityId);

			var signed = begun.AuthorizeUrl[(begun.AuthorizeUrl.IndexOf('?') + 1)..begun.AuthorizeUrl.IndexOf("&Signature=", StringComparison.Ordinal)];
			spKey.VerifyData(Encoding.UTF8.GetBytes(signed), Convert.FromBase64String(Query(begun.AuthorizeUrl, "Signature")),
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().BeTrue("the redirect binding signs the exact query string");
		}

		[Test]
		public void A_legacy_saml_sign_in_starts_at_the_idp_with_the_apps_own_relay_state_and_stores_nothing()
		{
			using var spKey = RSA.Create(2048);
			_saml.EncryptedSigningCertificate = spKey.ExportRSAPrivateKeyPem();
			_service = Service(_saml);

			var url = _service.LegacySamlSignInUrl(_saml, "DEPT", "unit.0123456789abcdef", false);

			url.Should().StartWith(_saml.IdpSsoUrl + "?SAMLRequest=");
			Query(url, "RelayState").Should().Be("unit.0123456789abcdef", "the relay returns the member to the app this names");
			using var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(Query(url, "SAMLRequest"))), CompressionMode.Decompress);
			var request = new XmlDocument();
			request.Load(inflate);
			var root = request.DocumentElement!;
			root.LocalName.Should().Be("AuthnRequest");
			root.GetAttribute("ID").Should().StartWith("_");
			root.GetAttribute("AssertionConsumerServiceURL").Should().Be(_saml.AssertionConsumerServiceUrl);
			root.GetAttribute("Destination").Should().Be(_saml.IdpSsoUrl);
			root.HasAttribute("ForceAuthn").Should().BeFalse();
			root.FirstChild!.InnerText.Should().Be(_saml.EntityId);
			var signed = url[(url.IndexOf('?') + 1)..url.IndexOf("&Signature=", StringComparison.Ordinal)];
			spKey.VerifyData(Encoding.UTF8.GetBytes(signed), Convert.FromBase64String(Query(url, "Signature")),
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().BeTrue();
			_rows.Rows.Should().BeEmpty("the legacy relay validates the response at the exchange; nothing waits for it here");
		}

		[Test]
		public void A_legacy_saml_sign_in_starts_only_from_a_usable_saml_configuration_and_an_app_relay_state()
		{
			_service = Service(_saml);
			_service.LegacySamlSignInUrl(_saml, "DEPT", "unit.0123456789abcdef", false).Should().NotBeNull();

			_service.LegacySamlSignInUrl(_oidc, "DEPT", "unit.0123456789abcdef", false).Should().BeNull();
			_service.LegacySamlSignInUrl(null, "DEPT", "unit.0123456789abcdef", false).Should().BeNull();
			_service.LegacySamlSignInUrl(_saml, "DEPT", "", false).Should().BeNull();
			_service.LegacySamlSignInUrl(_saml, "DEPT", "rgsso.0123456789abcdef", false).Should().BeNull("a broker RelayState belongs to the broker's own round trip");

			_saml.IdpSsoUrl = "http://idp.example.test/saml2/sso";
			_service.LegacySamlSignInUrl(_saml, "DEPT", "unit.0123456789abcdef", false).Should().BeNull("the IdP's sign-in page must be https");
			_saml.IdpSsoUrl = "https://idp.example.test/saml2/sso";
			_saml.EncryptedIdpCertificate = null;
			_service.LegacySamlSignInUrl(_saml, "DEPT", "unit.0123456789abcdef", false).Should().BeNull("without the IdP's certificate no response could be validated");
			_saml.EncryptedIdpCertificate = "idp-cert";
			_saml.IsEnabled = false;
			_service.LegacySamlSignInUrl(_saml, "DEPT", "unit.0123456789abcdef", false).Should().BeNull();
		}

		[Test]
		public async Task A_saml_provider_without_its_sign_in_url_stays_on_the_legacy_relay()
		{
			_saml.IdpSsoUrl = null;
			_service = Service(_saml);
			(await Begin(NewVerifier())).Outcome.Should().Be(SsoBrokerOutcome.Unavailable);
		}

		// ---- OIDC callback -------------------------------------------------------------------------------------------

		[Test]
		public async Task A_valid_idp_result_returns_a_one_time_code_to_the_registered_target()
		{
			var verifier = NewVerifier();
			var (state, begun) = await BeginAtIdp(verifier);

			var callback = await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, "203.0.113.9");

			callback.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			callback.RedirectUrl.Should().StartWith(UnitReturn + "?sso_code=");
			Query(callback.RedirectUrl, "state").Should().Be("client-csrf", "the client's own CSRF value is echoed");
			callback.RedirectUrl.Should().NotContain("eyJ", "no id_token ever reaches the URL");

			var exchange = _idp.Exchanges.Single();
			exchange["redirect_uri"].Should().Be("https://api.resgrid.test/api/v4/connect/oidc-callback");
			exchange["client_secret"].Should().Be("client-secret", "a configured client secret is used for the exchange");
			_sso.Verify(s => s.EnforceSecurityPolicyAsync(DepartmentId, UserId, "203.0.113.9", true, true, It.IsAny<CancellationToken>()));

			var row = _rows.Rows.Single();
			row.TransactionState.Should().Be(SsoLoginTransactionState.Authenticated);
			row.UserId.Should().Be(UserId);
			row.CodeHash.Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(Query(callback.RedirectUrl, "sso_code"))));

			var redeemed = await _service.RedeemAsync(begun.TransactionId, Query(callback.RedirectUrl, "sso_code"), verifier, UserSessionClientApplication.Unit);
			redeemed.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			redeemed.Transaction.UserId.Should().Be(UserId);
		}

		[Test]
		public async Task The_id_token_must_answer_this_transaction()
		{
			async Task<SsoCallbackResult> Callback(Func<string, string> token)
			{
				var (state, _) = await BeginAtIdp(NewVerifier(), token);
				return await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null);
			}

			using var foreignKey = RSA.Create(2048);
			(await Callback(_ => _idp.Token("another-nonce"))).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);
			(await Callback(_ => _idp.Token(null))).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);
			(await Callback(nonce => _idp.Token(nonce, audience: "another-client"))).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);
			(await Callback(nonce => _idp.Token(nonce, signingKey: foreignKey))).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);
			(await Callback(nonce => _idp.Token(nonce, expires: DateTime.UtcNow.AddMinutes(-5)))).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);

			var failed = await Callback(_ => _idp.Token("another-nonce"));
			Query(failed.RedirectUrl, "error").Should().Be("sso_verification_failed");
			Query(failed.RedirectUrl, "sso_code").Should().BeNull();
			_rows.Rows.Should().OnlyContain(r => r.TransactionState == SsoLoginTransactionState.Failed);
		}

		[Test]
		public async Task An_id_token_signs_in_once()
		{
			string captured = null;
			var (first, _) = await BeginAtIdp(NewVerifier(), nonce => captured ??= _idp.Token(nonce));
			(await _service.CompleteOidcCallbackAsync(first, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);

			// The same token again, even under a transaction whose nonce happens to match, is refused.
			(await _service.TryRecordIdTokenUseAsync(captured, DateTime.UtcNow.AddMinutes(10))).Should().BeFalse();
		}

		[Test]
		public async Task A_state_is_used_once_and_only_while_it_lasts()
		{
			var (state, _) = await BeginAtIdp(NewVerifier());
			(await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			var again = await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null);
			again.Outcome.Should().Be(SsoBrokerOutcome.AlreadyUsed);
			Query(again.RedirectUrl, "sso_code").Should().BeNull();

			var (late, _) = await BeginAtIdp(NewVerifier());
			_clock.Now = _clock.Now.AddSeconds(SsoConfig.BrokeredTransactionLifetimeSeconds + 1);
			var expired = await _service.CompleteOidcCallbackAsync(late, FakeOidcProvider.Code, null, null);
			Query(expired.RedirectUrl, "error").Should().Be("sso_transaction_expired");

			var unknown = await _service.CompleteOidcCallbackAsync("forged-state", FakeOidcProvider.Code, null, null);
			unknown.Outcome.Should().Be(SsoBrokerOutcome.TransactionInvalid);
			unknown.RedirectUrl.Should().BeNull("an unknown state has no trusted place to send the browser");
		}

		[Test]
		public async Task Idp_refusals_and_department_policy_end_the_sign_in()
		{
			var (declined, _) = await BeginAtIdp(NewVerifier());
			Query((await _service.CompleteOidcCallbackAsync(declined, null, "access_denied", null)).RedirectUrl, "error").Should().Be("access_denied");

			_policyViolation = "Login from IP address 198.51.100.1 is not permitted by the department's security policy.";
			var (blocked, _) = await BeginAtIdp(NewVerifier());
			var denied = await _service.CompleteOidcCallbackAsync(blocked, FakeOidcProvider.Code, null, "198.51.100.1");
			denied.Outcome.Should().Be(SsoBrokerOutcome.AccessDenied);
			denied.RedirectUrl.Should().NotContain("198.51.100.1", "the reason stays on the server");

			_policyViolation = null;
			var (stranger, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, subject: "unlinked-user"));
			(await _service.CompleteOidcCallbackAsync(stranger, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.AccessDenied);
		}

		[Test]
		public async Task A_shared_installations_sign_in_needs_a_fresh_idp_sign_in()
		{
			// The provider answering from the session it remembers (the last operator's, perhaps) is refused.
			var (stale, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, authTime: DateTime.UtcNow.AddHours(-2)), shared: true);
			(await _service.CompleteOidcCallbackAsync(stale, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh);
			_rows.Rows.Last().FailureCode.Should().Be("auth_time_not_fresh");

			var (missing, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce), shared: true);
			(await _service.CompleteOidcCallbackAsync(missing, FakeOidcProvider.Code, null, null)).Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh, "a provider that cannot prove freshness is refused");

			var (fresh, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, authTime: DateTime.UtcNow.AddSeconds(-20)), shared: true);
			(await _service.CompleteOidcCallbackAsync(fresh, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);

			var (personal, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, authTime: DateTime.UtcNow.AddHours(-2)));
			(await _service.CompleteOidcCallbackAsync(personal, FakeOidcProvider.Code, null, null)).Outcome
				.Should().Be(SsoBrokerOutcome.Succeeded, "a personal sign-in may use the provider's session, as before");
		}

		[Test]
		public async Task A_shared_installations_saml_sign_in_needs_a_fresh_authn_instant()
		{
			_service = Service(_saml);
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "external-user") }, "SAML2"));
			var authenticatedAt = DateTime.UtcNow.AddHours(-2);
			_sso.Setup(s => s.ValidateBrokeredSamlResponseAsync(DepartmentId, "response", "DEPT", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new SsoIdentityAssertion { Principal = principal, AuthenticatedAtUtc = authenticatedAt });

			var stale = await Begin(NewVerifier(), shared: true);
			(await _service.CompleteSamlCallbackAsync(Query(stale.AuthorizeUrl, "RelayState"), "response", null)).Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh);
			_rows.Rows.Last().FailureCode.Should().Be("authn_instant_not_fresh");

			var personal = await Begin(NewVerifier());
			(await _service.CompleteSamlCallbackAsync(Query(personal.AuthorizeUrl, "RelayState"), "response", null)).Outcome
				.Should().Be(SsoBrokerOutcome.Succeeded, "a personal sign-in is unchanged");

			authenticatedAt = DateTime.UtcNow.AddSeconds(-20);
			var fresh = await Begin(NewVerifier(), shared: true);
			(await _service.CompleteSamlCallbackAsync(Query(fresh.AuthorizeUrl, "RelayState"), "response", null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
		}

		[Test]
		public async Task Reauthentication_needs_a_fresh_idp_sign_in_by_the_same_account_and_never_provisions()
		{
			var (stale, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, authTime: DateTime.UtcNow.AddHours(-2)),
				SsoTransactionPurpose.Reauthentication);
			(await _service.CompleteOidcCallbackAsync(stale, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh);

			var (missing, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce), SsoTransactionPurpose.Reauthentication);
			(await _service.CompleteOidcCallbackAsync(missing, FakeOidcProvider.Code, null, null)).Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh, "a provider that cannot prove freshness is refused");

			var (other, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, subject: "someone-else", authTime: DateTime.UtcNow),
				SsoTransactionPurpose.Reauthentication);
			(await _service.CompleteOidcCallbackAsync(other, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.IdentityMismatch);

			var (fresh, _) = await BeginAtIdp(NewVerifier(), nonce => _idp.Token(nonce, authTime: DateTime.UtcNow.AddSeconds(-20)),
				SsoTransactionPurpose.Reauthentication);
			(await _service.CompleteOidcCallbackAsync(fresh, FakeOidcProvider.Code, null, null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			_rows.Rows.Last().AuthenticatedOnUtc.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(-20), TimeSpan.FromSeconds(2),
				"the evidence time is the IdP's own sign-in");

			_sso.Verify(s => s.ProvisionOrLinkUserAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- SAML callback -------------------------------------------------------------------------------------------

		[Test]
		public async Task A_saml_response_must_answer_this_transactions_request()
		{
			_service = Service(_saml);
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "external-user") }, "SAML2"));
			_sso.Setup(s => s.ValidateBrokeredSamlResponseAsync(DepartmentId, "good-response", "DEPT", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new SsoIdentityAssertion { Principal = principal, AuthenticatedAtUtc = DateTime.UtcNow });

			var begun = await Begin(NewVerifier());
			var relayState = Query(begun.AuthorizeUrl, "RelayState");

			(await _service.CompleteSamlCallbackAsync(relayState, "bad-response", null)).Outcome.Should().Be(SsoBrokerOutcome.VerificationFailed);

			var retry = Query((await Begin(NewVerifier())).AuthorizeUrl, "RelayState");
			var result = await _service.CompleteSamlCallbackAsync(retry, "good-response", null);
			result.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			result.RedirectUrl.Should().StartWith(UnitReturn + "?sso_code=");
			_sso.Verify(s => s.ValidateBrokeredSamlResponseAsync(DepartmentId, "good-response", "DEPT", _rows.Rows.Last().SamlRequestId,
				It.IsAny<CancellationToken>()), Times.Once, "the response is checked against this transaction's AuthnRequest id");

			(await _service.CompleteSamlCallbackAsync("legacy-relay-state", "good-response", null)).Outcome.Should().Be(SsoBrokerOutcome.TransactionInvalid);
			_service.IsBrokeredRelayState("legacy-relay-state").Should().BeFalse();
		}

		// ---- Provider step-up (slice 10) -----------------------------------------------------------------------------

		[Test]
		public async Task Provider_step_up_forces_a_fresh_sign_in_and_asks_for_the_mapped_mfa()
		{
			Mapped(_oidc);
			AcceptFederated();

			var begun = await BeginStepUp(NewVerifier());

			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			Query(begun.AuthorizeUrl, "prompt").Should().Be("login");
			Query(begun.AuthorizeUrl, "max_age").Should().Be("0", "the provider must authenticate now, not reuse a session");
			Query(begun.AuthorizeUrl, "acr_values").Should().Be("urn:okta:loa:2fa:any");
			Query(begun.AuthorizeUrl, "claims").Should().Be("{\"id_token\":{\"acrs\":{\"essential\":true,\"value\":\"c1\"}}}");
			var row = _rows.Rows.Single();
			row.Operation.Should().Be(MfaStepUpOperations.SecurityChange);
			row.FederatedMappingVersion.Should().Be(3);
			row.SessionId.Should().Be("session-1");
			row.ExpectedUserId.Should().Be(UserId);
			row.AuthenticationGeneration.Should().Be(4);
			_policy.Verify(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.SecurityChange, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()));
		}

		[Test]
		public async Task A_shared_session_unlock_is_a_session_bound_step_up_under_the_sign_in_rules()
		{
			// Plan section 12.5.3: unlocking a shared session follows the sign-in method rules, like a passkey unlock does.
			Mapped(_oidc);
			_policy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);

			var begun = await BeginStepUp(NewVerifier(), SsoLoginTransaction.SharedUnlockOperation);
			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			var row = _rows.Rows.Single();
			row.Operation.Should().Be(SsoLoginTransaction.SharedUnlockOperation);
			row.SessionId.Should().Be("session-1");
			row.FederatedMappingVersion.Should().Be(3, "the tested mapping is requested because sign-in accepts provider MFA");
			_policy.Verify(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()));

			(await BeginStepUp(NewVerifier(), SsoLoginTransaction.SharedUnlockOperation, sessionId: null)).Outcome
				.Should().Be(SsoBrokerOutcome.InvalidRequest, "an unlock is always bound to the locked session");
		}

		[Test]
		public async Task Provider_step_up_is_bound_to_what_it_serves_and_offered_only_where_it_counts()
		{
			Mapped(_oidc);
			AcceptFederated();

			(await BeginStepUp(NewVerifier(), MfaStepUpOperations.AccountSecurity)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"provider step-up never manages account factors");
			(await BeginStepUp(NewVerifier(), "export_everything")).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest);
			(await BeginStepUp(NewVerifier(), sessionId: null)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest, "a session step-up needs the session");
			(await BeginStepUp(NewVerifier(), SsoLoginTransaction.LoginOperation)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"completing a sign-in needs its login transaction");
			(await BeginStepUp(NewVerifier(), MfaStepUpOperations.SecurityChange, null, null, "login-row")).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"a sign-in step-up names the operation login");
			(await BeginStepUp(NewVerifier(), SsoLoginTransaction.LoginOperation, null, null, "login-row")).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"and the account it signs in");
			(await BeginStepUp(NewVerifier(), sessionId: null, purpose: SsoTransactionPurpose.AdpStepUp)).Outcome.Should().Be(SsoBrokerOutcome.InvalidRequest,
				"a protected data step-up is bound to the session asking");
			_rows.Rows.Should().BeEmpty();

			AcceptFederated(false);
			(await BeginStepUp(NewVerifier())).Outcome.Should().Be(SsoBrokerOutcome.Unavailable, "the department does not accept provider MFA here");

			AcceptFederated();
			Mapped(_oidc, tested: false);
			(await BeginStepUp(NewVerifier())).Outcome.Should().Be(SsoBrokerOutcome.Unavailable, "an untested mapping counts for nothing");
			_rows.Rows.Should().BeEmpty();

			Mapped(_oidc);
			var signIn = await BeginStepUp(NewVerifier(), SsoLoginTransaction.LoginOperation, null, UserId, "login-row");
			signIn.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			var row = _rows.Rows.Single();
			row.LoginTransactionId.Should().Be("login-row");
			row.Operation.Should().Be(SsoLoginTransaction.LoginOperation);
			row.ExpectedUserId.Should().Be(UserId);
			row.SessionId.Should().BeNull();
			_policy.Verify(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()));
		}

		[Test]
		public async Task A_step_up_needs_a_fresh_provider_sign_in_of_the_same_account_with_a_mapped_value()
		{
			Mapped(_oidc);
			AcceptFederated();

			var accepted = await AtIdp(await BeginStepUp(NewVerifier()), nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "pwd", "mfa" }));
			accepted.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			var row = _rows.Rows.Last();
			row.FederatedMfaValue.Should().Be("amr:mfa");
			row.UserId.Should().Be(UserId);
			row.AuthenticatedOnUtc.Should().BeCloseTo(_clock.Now, TimeSpan.FromSeconds(1), "evidence uses the provider's own authentication time");
			_sso.Verify(s => s.ProvisionOrLinkUserAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never, "a step-up never provisions or links an account");

			async Task<(SsoCallbackResult Result, string Failure)> Refused(Func<string, string> token)
			{
				var result = await AtIdp(await BeginStepUp(NewVerifier()), token);
				return (result, _rows.Rows.Last().FailureCode);
			}

			var noMfa = await Refused(nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "pwd" }));
			noMfa.Result.Outcome.Should().Be(SsoBrokerOutcome.FederatedNotSatisfied);
			noMfa.Failure.Should().Be("mfa_not_asserted");
			Query(noMfa.Result.RedirectUrl, "error").Should().Be("federated_mfa_not_satisfied");

			(await Refused(nonce => _idp.Token(nonce, authTime: _clock.Now.AddMinutes(-10), amr: new[] { "mfa" }))).Result.Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh, "a provider session from before the step-up began is not a new sign-in");
			(await Refused(nonce => _idp.Token(nonce, amr: new[] { "mfa" }))).Result.Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh, "without auth_time the sign-in cannot be shown to be fresh");
			(await Refused(nonce => _idp.Token(nonce, authTime: _clock.Now.AddMinutes(10), amr: new[] { "mfa" }))).Result.Outcome
				.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh, "an authentication time in the future is refused");
			(await Refused(nonce => _idp.Token(nonce, subject: "someone-else", authTime: _clock.Now, amr: new[] { "mfa" }))).Result.Outcome
				.Should().Be(SsoBrokerOutcome.IdentityMismatch);
			(await Refused(nonce => _idp.Token(nonce, authTime: _clock.Now, acrs: new[] { "c1" }))).Result.Outcome
				.Should().Be(SsoBrokerOutcome.Succeeded, "any accepted kind counts");

			var begun = await BeginStepUp(NewVerifier());
			_oidc.FederatedMfaMappingVersion = 4;
			_oidc.FederatedMfaTestedVersion = 4;
			(await AtIdp(begun, nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "mfa" }))).Outcome.Should().Be(SsoBrokerOutcome.FederatedNotSatisfied);
			_rows.Rows.Last().FailureCode.Should().Be("mapping_changed", "what was requested is no longer the department's mapping");
		}

		[Test]
		public async Task A_step_up_code_redeems_only_where_that_step_up_is_served()
		{
			Mapped(_oidc);
			AcceptFederated();
			var verifier = NewVerifier();
			var begun = await BeginStepUp(verifier);
			var code = Query((await AtIdp(begun, nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "mfa" }))).RedirectUrl, "sso_code");

			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(SsoBrokerOutcome.TransactionInvalid, "Sso/Redeem serves sign-in and reauthentication only");
			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit, CancellationToken.None,
				SsoTransactionPurpose.MappingTest)).Outcome.Should().Be(SsoBrokerOutcome.TransactionInvalid);
			_rows.Rows.Single().TransactionState.Should().Be(SsoLoginTransactionState.Authenticated, "a refused purpose does not spend the code");

			var redeemed = await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit, CancellationToken.None,
				SsoTransactionPurpose.StepUp);
			redeemed.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			redeemed.Transaction.FederatedMfaValue.Should().Be("amr:mfa");
		}

		[Test]
		public async Task A_protected_data_step_up_is_a_fresh_mapped_sign_in_of_the_session_account_under_the_adp_switch()
		{
			// Slice 18 (Phase 2): provider step-up for a Protected Data Grant, redeemed only at DataProtection/CompleteFederated.
			Mapped(_oidc);
			AcceptFederated();
			var verifier = NewVerifier();

			var begun = await BeginStepUp(verifier, operation: null, purpose: SsoTransactionPurpose.AdpStepUp);

			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			Query(begun.AuthorizeUrl, "prompt").Should().Be("login");
			Query(begun.AuthorizeUrl, "max_age").Should().Be("0", "protected data needs the provider's MFA now");
			Query(begun.AuthorizeUrl, "acr_values").Should().Be("urn:okta:loa:2fa:any");
			var row = _rows.Rows.Single();
			row.TransactionPurpose.Should().Be(SsoTransactionPurpose.AdpStepUp);
			row.SessionId.Should().Be("session-1");
			row.ExpectedUserId.Should().Be(UserId);
			row.Operation.Should().BeNull();
			_policy.Verify(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>()),
				"the ADP switch decides, not the sign-in one");

			(await AtIdp(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.AdpStepUp),
				nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "pwd" }))).Outcome.Should().Be(SsoBrokerOutcome.FederatedNotSatisfied);
			(await AtIdp(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.AdpStepUp),
				nonce => _idp.Token(nonce, authTime: _clock.Now.AddMinutes(-10), amr: new[] { "mfa" }))).Outcome.Should().Be(SsoBrokerOutcome.ReauthenticationNotFresh);
			(await AtIdp(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.AdpStepUp),
				nonce => _idp.Token(nonce, subject: "someone-else", authTime: _clock.Now, amr: new[] { "mfa" }))).Outcome.Should().Be(SsoBrokerOutcome.IdentityMismatch);
			_sso.Verify(s => s.ProvisionOrLinkUserAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never, "a step-up never provisions or links an account");

			var code = Query((await AtIdp(begun, nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "mfa" }))).RedirectUrl, "sso_code");
			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit, CancellationToken.None,
				SsoTransactionPurpose.StepUp)).Outcome.Should().Be(SsoBrokerOutcome.TransactionInvalid, "an ordinary step-up never redeems it");
			var redeemed = await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit, CancellationToken.None,
				SsoTransactionPurpose.AdpStepUp);
			redeemed.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			redeemed.Transaction.FederatedMfaValue.Should().Be("amr:mfa");

			_policy.Setup(p => p.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Federated, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.AdpStepUp)).Outcome.Should().Be(SsoBrokerOutcome.Unavailable,
				"a department that does not accept provider MFA for protected data is never asked");
		}

		[Test]
		public async Task A_sign_in_asks_for_the_mapped_mfa_where_the_department_accepts_it_and_records_what_came_back()
		{
			Mapped(_oidc);
			AcceptFederated();

			var begun = await Begin(NewVerifier());
			Query(begun.AuthorizeUrl, "acr_values").Should().Be("urn:okta:loa:2fa:any");
			Query(begun.AuthorizeUrl, "prompt").Should().BeNull("a sign-in is not forced to reauthenticate");
			_rows.Rows.Single().FederatedMappingVersion.Should().Be(3);
			(await AtIdp(begun, nonce => _idp.Token(nonce, amr: new[] { "mfa" }))).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			_rows.Rows.Single().FederatedMfaValue.Should().Be("amr:mfa");

			(await AtIdp(await Begin(NewVerifier()), nonce => _idp.Token(nonce))).Outcome.Should().Be(SsoBrokerOutcome.Succeeded,
				"a sign-in without provider MFA still signs in; login MFA follows as usual");
			_rows.Rows.Last().FederatedMfaValue.Should().BeNull();

			AcceptFederated(false);
			var unmapped = await Begin(NewVerifier());
			Query(unmapped.AuthorizeUrl, "acr_values").Should().BeNull();
			(await AtIdp(unmapped, nonce => _idp.Token(nonce, amr: new[] { "mfa" }))).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			_rows.Rows.Last().FederatedMappingVersion.Should().BeNull();
			_rows.Rows.Last().FederatedMfaValue.Should().BeNull("nothing counts where the department does not accept provider MFA");
		}

		[Test]
		public async Task A_mapping_test_uses_the_saved_mapping_and_authenticates_nobody()
		{
			Mapped(_oidc, tested: false);

			var begun = await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.MappingTest);
			begun.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			Query(begun.AuthorizeUrl, "acr_values").Should().Be("urn:okta:loa:2fa:any", "the test exercises the mapping before it is effective");
			Query(begun.AuthorizeUrl, "max_age").Should().Be("0");
			_rows.Rows.Single().Operation.Should().BeNull();

			var tested = await AtIdp(begun, nonce => _idp.Token(nonce, subject: "any-provider-account", authTime: _clock.Now, amr: new[] { "mfa" }));
			tested.Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			_rows.Rows.Single().UserId.Should().Be(UserId, "the test belongs to the member who ran it, whichever provider account they used");
			_rows.Rows.Single().FederatedMfaValue.Should().Be("amr:mfa");
			_sso.Verify(s => s.FindLinkedUserIdAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>()),
				Times.Never);
			_sso.Verify(s => s.ProvisionOrLinkUserAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);

			(await AtIdp(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.MappingTest),
				nonce => _idp.Token(nonce, authTime: _clock.Now, amr: new[] { "pwd" }))).Outcome.Should().Be(SsoBrokerOutcome.FederatedNotSatisfied,
				"a test the provider does not answer with MFA fails");

			_oidc.FederatedMfaMappingJson = null;
			(await BeginStepUp(NewVerifier(), operation: null, purpose: SsoTransactionPurpose.MappingTest)).Outcome.Should().Be(SsoBrokerOutcome.Unavailable);
		}

		[Test]
		public async Task A_saml_step_up_requests_the_mapped_context_and_accepts_only_it()
		{
			Mapped(_saml);
			AcceptFederated();
			_service = Service(_saml);
			AcceptFederated();
			var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "external-user") }, "SAML2"));
			var contexts = new[] { "urn:oasis:names:tc:SAML:2.0:ac:classes:PasswordProtectedTransport" };
			_sso.Setup(s => s.ValidateBrokeredSamlResponseAsync(DepartmentId, "response", "DEPT", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => new SsoIdentityAssertion { Principal = principal, AuthenticatedAtUtc = _clock.Now, AuthnContextClassRefs = contexts });

			var begun = await BeginStepUp(NewVerifier());
			using (var inflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(Query(begun.AuthorizeUrl, "SAMLRequest"))), CompressionMode.Decompress))
			{
				var request = new XmlDocument();
				request.Load(inflate);
				request.DocumentElement!.GetAttribute("ForceAuthn").Should().Be("true");
				var requested = request.GetElementsByTagName("RequestedAuthnContext", "urn:oasis:names:tc:SAML:2.0:protocol").Cast<XmlElement>().Single();
				requested.GetAttribute("Comparison").Should().Be("exact");
				requested.InnerText.Should().Be("https://refeds.org/profile/mfa");
			}

			(await _service.CompleteSamlCallbackAsync(Query(begun.AuthorizeUrl, "RelayState"), "response", null)).Outcome
				.Should().Be(SsoBrokerOutcome.FederatedNotSatisfied, "password transport is not the mapped MFA context");

			contexts = new[] { "https://refeds.org/profile/mfa" };
			var again = await BeginStepUp(NewVerifier());
			(await _service.CompleteSamlCallbackAsync(Query(again.AuthorizeUrl, "RelayState"), "response", null)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			_rows.Rows.Last().FederatedMfaValue.Should().Be("authncontext:https://refeds.org/profile/mfa");
		}

		// ---- Redemption ----------------------------------------------------------------------------------------------

		[Test]
		public async Task A_code_redeems_once_for_the_app_holding_the_verifier()
		{
			var verifier = NewVerifier();
			var (state, begun) = await BeginAtIdp(verifier);
			(await _service.RedeemAsync(begun.TransactionId, "no-code-yet", verifier, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(SsoBrokerOutcome.TransactionInvalid, "nothing is redeemable before the IdP returns");

			var code = Query((await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null)).RedirectUrl, "sso_code");

			(await _service.RedeemAsync(begun.TransactionId, code, NewVerifier(), UserSessionClientApplication.Unit)).Outcome
				.Should().Be(SsoBrokerOutcome.TransactionInvalid, "an intercepted code is useless without the verifier");
			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Responder)).Outcome
				.Should().Be(SsoBrokerOutcome.TransactionInvalid, "another app cannot redeem it");
			(await _service.RedeemAsync(begun.TransactionId, "wrong-code-value", verifier, UserSessionClientApplication.Unit)).Outcome
				.Should().Be(SsoBrokerOutcome.TransactionInvalid);

			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit)).Outcome.Should().Be(SsoBrokerOutcome.Succeeded);
			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit)).Outcome.Should().Be(SsoBrokerOutcome.AlreadyUsed);
		}

		[Test]
		public async Task A_code_expires_quickly()
		{
			var verifier = NewVerifier();
			var (state, begun) = await BeginAtIdp(verifier);
			var code = Query((await _service.CompleteOidcCallbackAsync(state, FakeOidcProvider.Code, null, null)).RedirectUrl, "sso_code");

			_clock.Now = _clock.Now.AddSeconds(SsoConfig.BrokeredCodeLifetimeSeconds + 1);
			(await _service.RedeemAsync(begun.TransactionId, code, verifier, UserSessionClientApplication.Unit)).Outcome.Should().Be(SsoBrokerOutcome.Expired);
		}
	}
}
