using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security.Live
{
	/// <summary>
	/// The eight sign-in journeys end to end over real HTTP, through the real token endpoint, bearer validation, session
	/// middleware and sign-in services (<see cref="LiveSignInServer"/>): with every new switch off, as deployments run
	/// today, and with the plan's switches on. Each journey signs a member in as an app would and then checks what the
	/// server recorded and that the tokens work.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SignInJourneyTests
	{
		private LiveSignInServer _server;

		[SetUp]
		public async Task SetUp() => _server = await LiveSignInServer.StartAsync();

		[TearDown]
		public async Task TearDown() => await _server.DisposeAsync();

		// ---- Wire helpers ---------------------------------------------------------------------------------------------

		private sealed record Answer(HttpStatusCode Status, JObject Body, HttpResponseMessage Response)
		{
			public JObject Data => Body["Data"] as JObject;
			public string Error => Body.Value<string>("error") ?? Body.Value<string>("type");
		}

		private async Task<Answer> Send(string client, HttpMethod method, string path, object body = null, string bearer = null, bool form = false,
			bool shared = false, string deviceName = null)
		{
			using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_server.BaseUrl) };
			var request = new HttpRequestMessage(method, path.StartsWith("/__live") || path.StartsWith("http") ? path : LiveSignInServer.ApiPath + path);
			request.Headers.Add("X-Resgrid-Client", client);
			if (shared)
				request.Headers.Add(SharedSessionRules.InstallationHeader, "true");
			if (deviceName != null)
				request.Headers.Add("X-Resgrid-Device-Name", deviceName);
			if (bearer != null)
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
			if (body != null)
				request.Content = form
					? new FormUrlEncodedContent((Dictionary<string, string>)body)
					: new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json");
			var response = await http.SendAsync(request);
			var text = await response.Content.ReadAsStringAsync();
			var json = string.IsNullOrWhiteSpace(text) ? new JObject() : JToken.Parse(text) as JObject ?? new JObject { ["value"] = JToken.Parse(text) };
			return new Answer(response.StatusCode, json, response);
		}

		private Task<Answer> PasswordGrant(string client, LiveUser user, bool transaction, string totp = null, bool shared = false, string deviceName = null)
		{
			var form = new Dictionary<string, string>
			{
				["grant_type"] = "password", ["username"] = user.Username, ["password"] = user.Password, ["scope"] = "openid profile offline_access mobile"
			};
			if (totp != null)
				form["totp_code"] = totp;
			else if (transaction)
				form["mfa_flow"] = "transaction";
			return Send(client, HttpMethod.Post, "/connect/token", form, form: true, shared: shared, deviceName: deviceName);
		}

		private Task<Answer> Redeem(string client, string transaction, string completionCode, bool shared = false) =>
			Send(client, HttpMethod.Post, "/connect/token", new Dictionary<string, string>
			{
				["grant_type"] = MfaLoginTransactions.CompletionGrantType, ["transaction"] = transaction, ["completion_code"] = completionCode
			}, form: true, shared: shared);

		private Task<Answer> ExternalToken(string client, string idToken, string departmentToken, string totp = null, bool shared = false)
		{
			var form = new Dictionary<string, string>
			{
				["provider"] = "oidc", ["external_token"] = idToken, ["department_token"] = departmentToken, ["scope"] = "openid email profile offline_access mobile"
			};
			if (totp != null)
				form["totp_code"] = totp;
			return Send(client, HttpMethod.Post, Resgrid.Web.Services.Helpers.ResgridTokenEndpoints.ExternalTokenPath.Replace(LiveSignInServer.ApiPath, ""), form,
				form: true, shared: shared);
		}

		/// <summary>The tokens work: an authenticated call as the new session succeeds.</summary>
		private async Task TokensWork(string client, Answer tokens)
		{
			tokens.Status.Should().Be(HttpStatusCode.OK, tokens.Body.ToString());
			var access = tokens.Body.Value<string>("access_token");
			access.Should().NotBeNullOrEmpty();
			var current = await Send(client, HttpMethod.Get, "/sessions/current", bearer: access);
			current.Status.Should().Be(HttpStatusCode.OK, "the bearer token names a live session: " + current.Body);
		}

		private UserSession OnlySession(LiveUser user, string client) =>
			_server.SessionsOf(user.UserId).Single(s => s.ClientApplication == (int)LiveSignInServer.Client(client));

		/// <summary>The app's browser leg of a brokered sign-in: the provider's page and the broker's callback, to the app's return target.</summary>
		private async Task<(string SsoTransactionId, string SsoCode, string Verifier)> BrokeredRoundTrip(string client, LiveUser user, string purpose = "login",
			string transaction = null, IEnumerable<string> amr = null, bool shared = false)
		{
			var verifier = FakeOidcProvider.Base64Url(RandomNumberGenerator.GetBytes(32));
			var challenge = FakeOidcProvider.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
			var returnTarget = client switch { "responder" => "resgrid://sso-return", "ic" => "resgridic://sso-return", _ => $"resgrid{client}://sso-return" };
			var begin = await Send(client, HttpMethod.Post, "/Sso/Begin", new
			{
				Purpose = purpose, DepartmentToken = transaction == null ? user.DepartmentToken : null, Transaction = transaction, ReturnTarget = returnTarget,
				State = "state-1", CodeChallenge = challenge, CodeChallengeMethod = "S256", Platform = "ios"
			}, shared: shared);
			begin.Status.Should().Be(HttpStatusCode.OK, begin.Body.ToString());

			_server.NextProviderSignIn(user.ExternalSubject, amr);
			var callback = await Send(client, HttpMethod.Get, _server.ProviderSignIn(begin.Data.Value<string>("AuthorizeUrl")));
			callback.Status.Should().Be(HttpStatusCode.Redirect);
			// The header as sent: Uri.ToString() would add a '/' after a custom scheme's host.
			var location = callback.Response.Headers.Location!.OriginalString;
			location.Should().StartWith(returnTarget + "?");
			var query = HttpUtility.ParseQueryString(new Uri(location).Query);
			query["state"].Should().Be("state-1");
			return (begin.Data.Value<string>("SsoTransactionId"), query["sso_code"], verifier);
		}

		private async Task<Answer> PasskeyCompletion(string client, string transaction)
		{
			var ceremony = await Send(client, HttpMethod.Post, "/Authentication/PasskeyOptions", new { Transaction = transaction });
			ceremony.Status.Should().Be(HttpStatusCode.OK, ceremony.Body.ToString());
			var credential = JObject.Parse(_server.Assert(ceremony.Data["Options"].ToString(), LiveSignInServer.Client(client)));
			return await Send(client, HttpMethod.Post, "/Authentication/CompletePasskey",
				new { Transaction = transaction, RequestId = ceremony.Data.Value<string>("RequestId"), Credential = credential });
		}

		private static string[] Methods(JObject body, string name) => (body.Value<string>(name) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

		// ---- 1. Password, no MFA ---------------------------------------------------------------------------------------

		[TestCase(false, TestName = "1. Password only signs in with every switch off")]
		[TestCase(true, TestName = "1. Password only signs in with the plan's switches on")]
		public async Task Password_only(bool gates)
		{
			_server.AllGates(gates);
			var user = await _server.AddUserAsync(new LiveUserSpec());

			var tokens = await PasswordGrant("unit", user, transaction: true);

			await TokensWork("unit", tokens);
			var session = OnlySession(user, "unit");
			session.AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.LocalPassword);
			session.LoginMfaMethod.Should().BeNull();
		}

		[Test(Description = "1. A shared tablet's member who must first set up an authenticator is recorded as that tablet")]
		public async Task A_shared_tablets_required_setup_is_recorded_as_the_tablet()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { RequireMfa = true });

			var first = await PasswordGrant("unit", user, transaction: true, shared: true, deviceName: "Engine 7 tablet");
			first.Error.Should().Be("mfa_enrollment_required");
			first.Body.Value<string>("mfa_setup_transaction").Should().NotBeNullOrEmpty();
			var setup = _server.LoginTransactions.Rows.Single(r => r.UserId == user.UserId);
			setup.SharedMode.Should().BeTrue();
			setup.InstallationLabel.Should().Be("Engine 7 tablet");
		}

		// ---- 2. Password and TOTP ---------------------------------------------------------------------------------------

		[Test(Description = "2. Password and an authenticator code, with every switch off: the code is resent with the password")]
		public async Task Password_and_totp_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true });

			var first = await PasswordGrant("unit", user, transaction: true);
			first.Status.Should().Be(HttpStatusCode.BadRequest);
			first.Error.Should().Be("mfa_required");
			first.Body.ContainsKey("mfa_transaction").Should().BeFalse("today's server has no transaction, so the app resends");

			var wrong = await PasswordGrant("unit", user, transaction: false, totp: "000000");
			wrong.Error.Should().Be("invalid_totp");

			var code = _server.TotpCode(user.UserId);
			await TokensWork("unit", await PasswordGrant("unit", user, transaction: false, totp: code));
			OnlySession(user, "unit").LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);

			(await PasswordGrant("unit", user, transaction: false, totp: code)).Error.Should().Be("invalid_totp", "a code is used once");
		}

		[Test(Description = "2. Password and an authenticator code on the login transaction")]
		public async Task Password_and_totp_on_the_transaction()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true });

			var first = await PasswordGrant("unit", user, transaction: true);
			first.Error.Should().Be("mfa_required");
			var transaction = first.Body.Value<string>("mfa_transaction");
			transaction.Should().NotBeNullOrEmpty();
			Methods(first.Body, "mfa_enrolled").Should().Equal("totp");

			var completion = await Send("unit", HttpMethod.Post, "/Authentication/CompleteTotp", new { Transaction = transaction, Code = _server.TotpCode(user.UserId) });
			completion.Status.Should().Be(HttpStatusCode.OK, completion.Body.ToString());

			await TokensWork("unit", await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode")));
			OnlySession(user, "unit").LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);
		}

		// ---- 3. SSO, no MFA -------------------------------------------------------------------------------------------

		[Test(Description = "3. SSO without MFA on today's server: the app's own provider sign-in, exchanged with discovery's department token")]
		public async Task Sso_without_mfa_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true });

			var discovery = await Send("unit", HttpMethod.Get, "/connect/sso-config-for-user?username=" + user.Username);
			discovery.Data.Value<bool>("SsoEnabled").Should().BeTrue();
			discovery.Data.Value<bool>("BrokeredSsoAvailable").Should().BeFalse();
			discovery.Data.Value<string>("OidcRedirectUri").Should().Be("resgridunit://auth/callback");

			await TokensWork("unit", await ExternalToken("unit", _server.LegacyIdToken(user.ExternalSubject), discovery.Data.Value<string>("DepartmentToken")));
			OnlySession(user, "unit").AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.OidcSso);
		}

		[Test(Description = "3. SSO without MFA from a desktop app that runs the code flow itself: the provider returns to the app's scheme, and only the app's verifier, sent without a page's Origin, redeems the code")]
		public async Task Sso_without_mfa_from_an_app_running_the_code_flow()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true });
			var discovery = await Send("dispatch", HttpMethod.Get, "/connect/sso-config-for-user?username=" + user.Username);
			var redirectUri = discovery.Data.Value<string>("OidcRedirectUri");
			redirectUri.Should().Be("resgriddispatch://auth/callback", "the desktop app uses the redirect URI the department registers for the mobile app");
			(await Send("dispatch", HttpMethod.Get, "/__live/idp/.well-known/openid-configuration")).Body.Value<string>("token_endpoint")
				.Should().Be(FakeOidcProvider.TokenEndpoint);

			const string verifier = "desktop-app-verifier-0123456789-abcdefghijklmnopqrstuvwxyz";
			var challenge = FakeOidcProvider.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
			async Task<string> ProviderSignIn()
			{
				var state = Guid.NewGuid().ToString("N");
				_server.NextProviderSignIn(user.ExternalSubject);
				var page = await Send("dispatch", HttpMethod.Get, "/__live/idp/authorize?response_type=code&client_id=resgrid-client&redirect_uri=" +
					Uri.EscapeDataString(redirectUri) + $"&scope=openid&state={state}&code_challenge={challenge}&code_challenge_method=S256");
				page.Status.Should().Be(HttpStatusCode.Redirect);
				var back = page.Response.Headers.Location!.OriginalString;
				back.Should().StartWith(redirectUri + "?code=").And.EndWith("&state=" + state);
				return HttpUtility.ParseQueryString(back[(back.IndexOf('?') + 1)..])["code"];
			}
			async Task<Answer> RedeemCode(string code, string codeVerifier, string origin = null)
			{
				using var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl) };
				var request = new HttpRequestMessage(HttpMethod.Post, "/__live/idp/token")
				{
					Content = new FormUrlEncodedContent(new Dictionary<string, string>
					{
						["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["client_id"] = "resgrid-client",
						["code_verifier"] = codeVerifier
					})
				};
				if (origin != null)
					request.Headers.Add("Origin", origin);
				var response = await http.SendAsync(request);
				return new Answer(response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()), response);
			}

			(await RedeemCode(await ProviderSignIn(), verifier + "x")).Error.Should().Be("invalid_grant", "only the app's own verifier redeems its code");
			(await RedeemCode(await ProviderSignIn(), verifier, origin: "app://bundle")).Status.Should().Be(HttpStatusCode.BadRequest,
				"a page cannot redeem a native client's code cross-origin");

			var code = await ProviderSignIn();
			var tokens = await RedeemCode(code, verifier);
			tokens.Status.Should().Be(HttpStatusCode.OK);
			(await RedeemCode(code, verifier)).Error.Should().Be("invalid_grant", "a code redeems once");

			await TokensWork("dispatch", await ExternalToken("dispatch", tokens.Body.Value<string>("id_token"), discovery.Data.Value<string>("DepartmentToken")));
			OnlySession(user, "dispatch").AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.OidcSso);
		}

		[Test(Description = "3. SSO without MFA through the broker")]
		public async Task Sso_without_mfa_through_the_broker()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true });
			(await Send("unit", HttpMethod.Get, "/connect/sso-config-for-user?username=" + user.Username)).Data.Value<bool>("BrokeredSsoAvailable").Should().BeTrue();

			var (id, code, verifier) = await BrokeredRoundTrip("unit", user);
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier });
			redeemed.Data.Value<string>("Outcome").Should().Be("completed", redeemed.Body.ToString());

			await TokensWork("unit", await Redeem("unit", redeemed.Data.Value<string>("Transaction"), redeemed.Data.Value<string>("CompletionCode")));
			var session = OnlySession(user, "unit");
			session.AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.OidcSso);
			session.LoginMfaMethod.Should().BeNull();
		}

		/// <summary>
		/// The app's side of a legacy SAML sign-in: discovery's start page with the app's tagged RelayState, the IdP, and the
		/// relay's link back to the app, which carries the relay token, the department token and the echoed RelayState.
		/// </summary>
		private async Task<(string RelayToken, string DepartmentToken)> LegacySamlRoundTrip(string client, LiveUser user, bool forceAuthn = false)
		{
			var discovery = await Send(client, HttpMethod.Get, "/connect/sso-config-for-user?username=" + user.Username);
			discovery.Data.Value<string>("ProviderType").Should().Be("saml2");
			discovery.Data.Value<bool>("BrokeredSsoAvailable").Should().BeFalse();
			var start = discovery.Data.Value<string>("SamlLoginUrl");
			start.Should().StartWith(_server.BaseUrl + "/api/v4/connect/saml-mobile-login?departmentToken=");

			var relayState = client + "." + Guid.NewGuid().ToString("N");
			_server.NextProviderSignIn(user.ExternalSubject);
			var toIdp = await Send(client, HttpMethod.Get, start + "&RelayState=" + relayState + (forceAuthn ? "&forceAuthn=true" : ""));
			toIdp.Status.Should().Be(HttpStatusCode.Redirect, toIdp.Body.ToString());
			var idp = toIdp.Response.Headers.Location!.OriginalString;
			idp.Should().StartWith(LiveSignInServer.SamlIdpSsoUrl + "?SAMLRequest=");
			var query = HttpUtility.ParseQueryString(new Uri(idp).Query);
			query["RelayState"].Should().Be(relayState);

			var back = await _server.SamlProviderSignInAsync(query["SAMLRequest"], query["RelayState"]);
			var callback = LegacyAppCallbacks.ForName(client).Callback;
			back.Should().StartWith(callback + "?");
			var returned = HttpUtility.ParseQueryString(back[(back.IndexOf('?') + 1)..]);
			returned["relay_state"].Should().Be(relayState, "the relay echoes the app's own RelayState");
			return (returned["saml_response"], returned["department_token"]);
		}

		private Task<Answer> SamlExchange(string client, string relayToken, string departmentToken, string totp = null, bool shared = false)
		{
			var form = new Dictionary<string, string>
			{
				["provider"] = "saml2", ["external_token"] = relayToken, ["department_token"] = departmentToken, ["scope"] = "openid email profile offline_access mobile"
			};
			if (totp != null)
				form["totp_code"] = totp;
			return Send(client, HttpMethod.Post, "/connect/external-token", form, form: true, shared: shared);
		}

		[Test(Description = "3. SAML without MFA on today's server: the app opens the server's start page, and the relay brings it back to the exchange")]
		public async Task Saml_without_mfa_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true });

			var (relay, departmentToken) = await LegacySamlRoundTrip("unit", user);

			await TokensWork("unit", await SamlExchange("unit", relay, departmentToken));
			OnlySession(user, "unit").AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.SamlSso);
			(await SamlExchange("unit", relay, departmentToken)).Error.Should().Be("invalid_grant", "a relay token signs in once");
		}

		[Test(Description = "5. SAML then the member's code on today's server: the app resends the same relay token with the code")]
		public async Task Saml_then_totp_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, Totp = true });
			var (relay, departmentToken) = await LegacySamlRoundTrip("dispatch", user);

			var first = await SamlExchange("dispatch", relay, departmentToken);
			first.Status.Should().Be(HttpStatusCode.Unauthorized);
			first.Error.Should().Be("mfa_required");
			(await SamlExchange("dispatch", relay, departmentToken, "000000")).Error.Should().Be("invalid_totp", "a wrong code keeps the sign-in for another try");

			await TokensWork("dispatch", await SamlExchange("dispatch", relay, departmentToken, _server.TotpCode(user.UserId)));
			var session = OnlySession(user, "dispatch");
			session.AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.SamlSso);
			session.LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);
			(await SamlExchange("dispatch", relay, departmentToken, "000000")).Error.Should().Be("invalid_grant",
				"a finished sign-in leaves nothing to guess codes against");
			(await SamlExchange("dispatch", relay, departmentToken, _server.TotpCode(user.UserId, 1))).Error.Should().Be("invalid_grant",
				"the retry finishes the sign-in once");
		}

		[Test(Description = "5. A SAML code retry ends after the attempt limit, and when the account changed in between")]
		public async Task A_saml_code_retry_ends_at_the_limit_or_when_the_account_changes()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, Totp = true });
			var (relay, departmentToken) = await LegacySamlRoundTrip("unit", user);
			(await SamlExchange("unit", relay, departmentToken)).Error.Should().Be("mfa_required");
			for (var attempt = 0; attempt < Resgrid.Config.TwoFactorConfig.LoginMfaTransactionMaxAttempts; attempt++)
				(await SamlExchange("unit", relay, departmentToken, "000000")).Error.Should().Be("invalid_totp");
			(await SamlExchange("unit", relay, departmentToken, _server.TotpCode(user.UserId))).Error.Should().Be("invalid_grant",
				"wrong codes past the limit end the sign-in");

			var (again, token) = await LegacySamlRoundTrip("unit", user);
			(await SamlExchange("unit", again, token)).Error.Should().Be("mfa_required");
			_server.AdvanceAuthenticationGeneration(user.UserId);
			(await SamlExchange("unit", again, token, _server.TotpCode(user.UserId))).Error.Should().Be("invalid_grant",
				"a password change or revocation in between ends the sign-in");
			_server.SessionsOf(user.UserId).Should().BeEmpty();
		}

		[TestCase("member disabled")]
		[TestCase("sso turned off")]
		[TestCase("sso replaced")]
		[TestCase("authenticator removed")]
		public async Task A_saml_code_retry_ends_when_what_allowed_the_sign_in_changed(string change)
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, Totp = true });
			var (relay, departmentToken) = await LegacySamlRoundTrip("ic", user);
			(await SamlExchange("ic", relay, departmentToken)).Error.Should().Be("mfa_required");

			if (change == "member disabled")
				_server.DisableMember(user.UserId);
			else if (change == "sso turned off")
				_server.DisableSso(user.UserId);
			else if (change == "sso replaced")
				_server.ReplaceSso(user.UserId);
			else
				_server.RemoveAuthenticator(user.UserId);

			(await SamlExchange("ic", relay, departmentToken, _server.TotpCode(user.UserId))).Error.Should().Be("invalid_grant");
			_server.SessionsOf(user.UserId).Should().BeEmpty();
		}

		[Test(Description = "5. A refused SAML code retry ends the sign-in: undoing the change does not bring it back")]
		public async Task A_refused_saml_code_retry_stays_ended()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, Totp = true });
			var (relay, departmentToken) = await LegacySamlRoundTrip("unit", user);
			(await SamlExchange("unit", relay, departmentToken)).Error.Should().Be("mfa_required");

			_server.DisableSso(user.UserId);
			(await SamlExchange("unit", relay, departmentToken, _server.TotpCode(user.UserId))).Error.Should().Be("invalid_grant");
			_server.EnableSso(user.UserId);
			(await SamlExchange("unit", relay, departmentToken, _server.TotpCode(user.UserId))).Error.Should().Be("invalid_grant",
				"the refusal ended the sign-in");
			_server.SessionsOf(user.UserId).Should().BeEmpty();
		}

		[Test(Description = "5. An exchange that sends a wrong code with the relay token can still be retried with the right one")]
		public async Task A_saml_exchange_that_starts_with_a_wrong_code_keeps_the_sign_in()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, Totp = true });
			var (relay, departmentToken) = await LegacySamlRoundTrip("responder", user);

			(await SamlExchange("responder", relay, departmentToken, "000000")).Error.Should().Be("invalid_totp");
			await TokensWork("responder", await SamlExchange("responder", relay, departmentToken, _server.TotpCode(user.UserId)));
			OnlySession(user, "responder").LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);
		}

		// ---- Shared installations: the next operator never rides the last one's provider session (plan section 12.5.2) ----

		[Test(Description = "3. A shared installation's brokered SSO: the provider signs the next operator in afresh, though it still remembers the last one")]
		public async Task Brokered_sso_on_a_shared_installation_never_rides_the_last_operators_provider_session()
		{
			_server.AllGates(true);
			var previous = await _server.AddUserAsync(new LiveUserSpec { Sso = true });
			var next = await _server.AddUserAsync(new LiveUserSpec { Sso = true, ColleagueOf = previous.UserId });
			_server.RememberProviderSession(previous.ExternalSubject, DateTime.UtcNow.AddHours(-1));

			// The broker tells the provider to authenticate again, so the operator at the installation signs in as themselves.
			var (id, code, verifier) = await BrokeredRoundTrip("unit", next, shared: true);
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier }, shared: true);
			redeemed.Data.Value<string>("Outcome").Should().Be("completed", redeemed.Body.ToString());
			await TokensWork("unit", await Redeem("unit", redeemed.Data.Value<string>("Transaction"), redeemed.Data.Value<string>("CompletionCode"), shared: true));
			OnlySession(next, "unit").SharedMode.Should().BeTrue();
			_server.SessionsOf(previous.UserId).Should().BeEmpty("the previous operator's provider session signed nobody in");

			// A personal installation's browser is its member's own: there the provider still answers from the session it holds.
			var (personalId, personalCode, personalVerifier) = await BrokeredRoundTrip("dispatch", next);
			var personal = await Send("dispatch", HttpMethod.Post, "/Sso/Redeem",
				new { SsoTransactionId = personalId, SsoCode = personalCode, CodeVerifier = personalVerifier });
			personal.Data.Value<string>("Outcome").Should().Be("completed", personal.Body.ToString());
			await TokensWork("dispatch", await Redeem("dispatch", personal.Data.Value<string>("Transaction"), personal.Data.Value<string>("CompletionCode")));
			OnlySession(previous, "dispatch").Should().NotBeNull();
		}

		[Test(Description = "3. A shared installation's legacy OIDC exchange takes only a fresh provider sign-in, however the installation is shared")]
		public async Task Legacy_sso_on_a_shared_installation_needs_a_fresh_provider_sign_in()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true });
			(await ExternalToken("unit", _server.LegacyIdToken(user.ExternalSubject, DateTime.UtcNow.AddHours(-1)), user.DepartmentToken, shared: true))
				.Error.Should().Be("login_required", "the provider answered from a session it remembered");
			(await ExternalToken("unit", _server.LegacyIdToken(user.ExternalSubject), user.DepartmentToken, shared: true))
				.Error.Should().Be("login_required", "nothing says when the provider authenticated");
			_server.SessionsOf(user.UserId).Should().BeEmpty();

			await TokensWork("unit", await ExternalToken("unit", _server.LegacyIdToken(user.ExternalSubject, DateTime.UtcNow.AddSeconds(-20)),
				user.DepartmentToken, shared: true));
			await TokensWork("dispatch", await ExternalToken("dispatch", _server.LegacyIdToken(user.ExternalSubject, DateTime.UtcNow.AddHours(-1)),
				user.DepartmentToken));

			// An app the department always runs shared is a shared installation, whatever it says.
			var station = await _server.AddUserAsync(new LiveUserSpec { Sso = true, SharedModeRequiredApps = 4 });
			(await ExternalToken("dispatch", _server.LegacyIdToken(station.ExternalSubject, DateTime.UtcNow.AddHours(-1)), station.DepartmentToken))
				.Error.Should().Be("login_required");
			await TokensWork("unit", await ExternalToken("unit", _server.LegacyIdToken(station.ExternalSubject, DateTime.UtcNow.AddHours(-1)),
				station.DepartmentToken));
		}

		[Test(Description = "3. A shared installation's legacy SAML: the start page asks the IdP to authenticate again, and the exchange refuses a remembered session")]
		public async Task Legacy_saml_on_a_shared_installation_never_rides_the_last_operators_provider_session()
		{
			var previous = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true });
			var next = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Saml = true, ColleagueOf = previous.UserId });
			_server.RememberProviderSession(previous.ExternalSubject, DateTime.UtcNow.AddHours(-1));

			// Not asked to authenticate again, the IdP answers as the previous operator; the shared installation's exchange refuses it.
			var (remembered, rememberedToken) = await LegacySamlRoundTrip("unit", next);
			(await SamlExchange("unit", remembered, rememberedToken, shared: true)).Error.Should().Be("login_required");

			var (fresh, freshToken) = await LegacySamlRoundTrip("unit", next, forceAuthn: true);
			await TokensWork("unit", await SamlExchange("unit", fresh, freshToken, shared: true));
			OnlySession(next, "unit").AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.SamlSso);
			_server.SessionsOf(previous.UserId).Should().BeEmpty();
		}

		// ---- 4. SSO with the provider's MFA -----------------------------------------------------------------------------

		[Test(Description = "4. SSO where the provider did the MFA: no Resgrid prompt, even for a member with an authenticator")]
		public async Task Sso_with_the_providers_mfa()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, ProviderMfa = true, Totp = true, RequireMfa = true });

			var (id, code, verifier) = await BrokeredRoundTrip("unit", user, amr: new[] { "pwd", "mfa" });
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier });
			redeemed.Data.Value<string>("Outcome").Should().Be("completed", redeemed.Body.ToString());
			redeemed.Data.Value<string>("MfaSatisfiedBy").Should().Be("federated");

			await TokensWork("unit", await Redeem("unit", redeemed.Data.Value<string>("Transaction"), redeemed.Data.Value<string>("CompletionCode")));
			OnlySession(user, "unit").LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Federated);
		}

		[Test(Description = "4. The same sign-in without the provider's MFA still asks for Resgrid's")]
		public async Task Sso_without_the_providers_mfa_still_asks()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, ProviderMfa = true, Totp = true });

			var (id, code, verifier) = await BrokeredRoundTrip("unit", user, amr: new[] { "pwd" });
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier });
			redeemed.Data.Value<string>("Outcome").Should().Be("mfa_required");
		}

		[Test(Description = "4. On today's server the provider's MFA is not read; a member without an authenticator just signs in")]
		public async Task Sso_with_the_providers_mfa_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, ProviderMfa = true });
			await TokensWork("unit", await ExternalToken("unit", _server.LegacyIdToken(user.ExternalSubject), user.DepartmentToken));
		}

		// ---- 5. SSO then Resgrid TOTP ------------------------------------------------------------------------------------

		[Test(Description = "5. SSO then an authenticator code on today's server: the same id_token is resent with the code")]
		public async Task Sso_then_totp_on_todays_server()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Totp = true });
			var idToken = _server.LegacyIdToken(user.ExternalSubject);

			var first = await ExternalToken("unit", idToken, user.DepartmentToken);
			first.Status.Should().Be(HttpStatusCode.Unauthorized);
			first.Error.Should().Be("mfa_required");

			await TokensWork("unit", await ExternalToken("unit", idToken, user.DepartmentToken, _server.TotpCode(user.UserId)));
			OnlySession(user, "unit").LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);
		}

		[Test(Description = "5. SSO through the broker, then an authenticator code on the login transaction")]
		public async Task Sso_then_totp_through_the_broker()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Totp = true });

			var (id, code, verifier) = await BrokeredRoundTrip("unit", user);
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier });
			redeemed.Data.Value<string>("Outcome").Should().Be("mfa_required", redeemed.Body.ToString());
			redeemed.Data["MfaEnrolled"]!.Values<string>().Should().Equal("totp");
			var transaction = redeemed.Data.Value<string>("Transaction");

			var completion = await Send("unit", HttpMethod.Post, "/Authentication/CompleteTotp", new { Transaction = transaction, Code = _server.TotpCode(user.UserId) });
			await TokensWork("unit", await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode")));
			var session = OnlySession(user, "unit");
			session.AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.OidcSso);
			session.LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Totp);
		}

		// ---- 6. This app's passkey, which also counts as recent MFA -----------------------------------------------------------

		[Test(Description = "6. Password then this app's passkey on this device; the sign-in's MFA then serves a guarded change without asking again")]
		public async Task Password_then_this_apps_passkey()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "unit" } });

			var first = await PasswordGrant("unit", user, transaction: true);
			Methods(first.Body, "mfa_methods").Should().Contain("passkey");
			Methods(first.Body, "mfa_enrolled").Should().Contain("passkey");
			var transaction = first.Body.Value<string>("mfa_transaction");

			var completion = await PasskeyCompletion("unit", transaction);
			completion.Status.Should().Be(HttpStatusCode.OK, completion.Body.ToString());
			var tokens = await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode"));
			await TokensWork("unit", tokens);
			var session = OnlySession(user, "unit");
			session.LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.Passkey);
			session.LoginMfaFactorReference.Should().StartWith("passkey:");

			// A guarded account change within the window takes the sign-in's own MFA: no reauthentication, no step-up.
			var options = await Send("unit", HttpMethod.Post, "/Passkeys/RegistrationOptions", new { }, tokens.Body.Value<string>("access_token"));
			options.Status.Should().Be(HttpStatusCode.OK, "the passkey sign-in is recent MFA for this session: " + options.Body);
		}

		[Test(Description = "6. Another app's passkey is not offered and cannot finish this app's sign-in")]
		public async Task Another_apps_passkey_is_not_this_apps()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "dispatch" } });

			var first = await PasswordGrant("unit", user, transaction: true);
			Methods(first.Body, "mfa_enrolled").Should().NotContain("passkey");
		}

		[Test(Description = "6. With passkeys off, the passkey member signs in with their code, as today")]
		public async Task A_passkey_member_on_todays_server_uses_the_code()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "unit" } });
			(await PasswordGrant("unit", user, transaction: true)).Error.Should().Be("mfa_required");
			await TokensWork("unit", await PasswordGrant("unit", user, transaction: false, totp: _server.TotpCode(user.UserId)));
		}

		/// <summary>The claims of a protected-data grant (a signed JWT the client presents with protected reads).</summary>
		private static JObject GrantClaims(string grant)
		{
			var payload = grant.Split('.')[1].Replace('-', '+').Replace('_', '/');
			payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
			return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
		}

		private async Task<string> SignInWithPasskey(string client, LiveUser user)
		{
			var transaction = (await PasswordGrant(client, user, transaction: true)).Body.Value<string>("mfa_transaction");
			var completion = await PasskeyCompletion(client, transaction);
			return (await Redeem(client, transaction, completion.Data.Value<string>("CompletionCode"))).Body.Value<string>("access_token");
		}

		private async Task<string> SignInWithTotp(string client, LiveUser user)
		{
			var transaction = (await PasswordGrant(client, user, transaction: true)).Body.Value<string>("mfa_transaction");
			var completion = await Send(client, HttpMethod.Post, "/Authentication/CompleteTotp", new { Transaction = transaction, Code = _server.TotpCode(user.UserId) });
			return (await Redeem(client, transaction, completion.Data.Value<string>("CompletionCode"))).Body.Value<string>("access_token");
		}

		[Test(Description = "6. A passkey sign-in serves protected data from its own MFA: the grant names the passkey, and no prompt comes")]
		public async Task A_passkey_sign_in_serves_protected_data_without_a_prompt()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "unit" } });
			var access = await SignInWithPasskey("unit", user);

			var grant = await Send("unit", HttpMethod.Post, "/DataProtection/RequestGrant", new { }, access);
			grant.Status.Should().Be(HttpStatusCode.OK, grant.Body.ToString());
			var claims = GrantClaims(grant.Body.Value<string>("GrantToken"));
			claims.Value<string>("mfa_method").Should().Be("passkey");
			claims.Value<int>("grant_ver").Should().Be(2);
		}

		[Test(Description = "6. A password alone never serves protected data")]
		public async Task A_password_alone_never_serves_protected_data()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec());
			var access = (await PasswordGrant("unit", user, transaction: true)).Body.Value<string>("access_token");

			var grant = await Send("unit", HttpMethod.Post, "/DataProtection/RequestGrant", new { }, access);
			grant.Status.Should().Be(HttpStatusCode.Unauthorized);
			grant.Error.Should().Be("step_up_required");
		}

		// ---- 7. Passkey step-up -----------------------------------------------------------------------------------------

		[Test(Description = "7. A passkey step-up inside a session, for an account change")]
		public async Task Passkey_step_up_in_a_session()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "unit" } });
			var first = await PasswordGrant("unit", user, transaction: true);
			var transaction = first.Body.Value<string>("mfa_transaction");
			var completion = await Send("unit", HttpMethod.Post, "/Authentication/CompleteTotp", new { Transaction = transaction, Code = _server.TotpCode(user.UserId) });
			var access = (await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode"))).Body.Value<string>("access_token");

			var options = await Send("unit", HttpMethod.Get, "/Mfa/StepUpOptions?operation=account_security", bearer: access);
			options.Status.Should().Be(HttpStatusCode.OK, options.Body.ToString());
			options.Data["Methods"]!.Values<string>().Should().Contain("passkey");
			var ceremony = (JObject)options.Data["Passkey"];
			ceremony.Should().NotBeNull();

			var credential = JObject.Parse(_server.Assert(ceremony["Options"].ToString(), UserSessionClientApplication.Unit));
			var verified = await Send("unit", HttpMethod.Post, "/Mfa/VerifyStepUp",
				new { Operation = "account_security", Method = "passkey", RequestId = ceremony.Value<string>("RequestId"), Credential = credential }, access);
			verified.Status.Should().Be(HttpStatusCode.OK, verified.Body.ToString());
			verified.Data.Value<string>("VerifiedAt").Should().NotBeNullOrEmpty();
		}

		[Test(Description = "7. A passkey step-up for protected data, where the department does not reuse the sign-in's MFA")]
		public async Task Passkey_step_up_for_protected_data()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "unit" }, AcceptRecentLoginMfaForAdp = false });
			var access = await SignInWithTotp("unit", user);
			(await Send("unit", HttpMethod.Post, "/DataProtection/RequestGrant", new { }, access)).Error.Should().Be("step_up_required");

			var methods = await Send("unit", HttpMethod.Get, "/DataProtection/StepUpMethods", bearer: access);
			methods.Data["Methods"]!.Values<string>().Should().Contain("passkey");
			var ceremony = await Send("unit", HttpMethod.Post, "/DataProtection/PasskeyOptions", new { }, access);
			ceremony.Status.Should().Be(HttpStatusCode.OK, ceremony.Body.ToString());
			var credential = JObject.Parse(_server.Assert(ceremony.Data["Options"].ToString(), UserSessionClientApplication.Unit));
			var grant = await Send("unit", HttpMethod.Post, "/DataProtection/VerifyPasskey",
				new { RequestId = ceremony.Data.Value<string>("RequestId"), Credential = credential }, access);
			grant.Status.Should().Be(HttpStatusCode.OK, grant.Body.ToString());
			GrantClaims(grant.Body.Value<string>("GrantToken")).Value<string>("mfa_method").Should().Be("passkey");
		}

		[Test(Description = "7. A locked shared workstation is unlocked by its operator's passkey")]
		public async Task Passkey_unlocks_a_shared_workstation()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, PasskeyFor = new() { "dispatch" } });
			var first = await PasswordGrant("dispatch", user, transaction: true, shared: true);
			var transaction = first.Body.Value<string>("mfa_transaction");
			var completion = await PasskeyCompletion("dispatch", transaction);
			var tokens = await Redeem("dispatch", transaction, completion.Data.Value<string>("CompletionCode"), shared: true);
			await TokensWork("dispatch", tokens);
			var access = tokens.Body.Value<string>("access_token");
			OnlySession(user, "dispatch").SharedMode.Should().BeTrue();

			var locked = await Send("dispatch", HttpMethod.Post, "/sessions/lock", new { }, access);
			locked.Status.Should().Be(HttpStatusCode.OK, locked.Body.ToString());
			var lockVersion = locked.Data.Value<long>("LockVersion");
			(await Send("dispatch", HttpMethod.Get, "/sessions/current", bearer: access)).Status.Should().Be(HttpStatusCode.OK, "status is allowed while locked");
			var refused = await Send("dispatch", HttpMethod.Get, "/Mfa/StepUpOptions?operation=account_security", bearer: access);
			refused.Status.Should().Be(HttpStatusCode.Unauthorized);
			refused.Error.Should().Be("shared_session_locked");

			var unlock = await Send("dispatch", HttpMethod.Post, "/sessions/unlock-options", new { LockVersion = lockVersion }, access);
			unlock.Status.Should().Be(HttpStatusCode.OK, unlock.Body.ToString());
			unlock.Data["Methods"]!.Values<string>().Should().Contain("passkey");
			var ceremony = (JObject)unlock.Data["Passkey"];
			var credential = JObject.Parse(_server.Assert(ceremony["Options"].ToString(), UserSessionClientApplication.Dispatch));
			var unlocked = await Send("dispatch", HttpMethod.Post, "/sessions/complete-unlock",
				new { LockVersion = lockVersion, Method = "passkey", RequestId = ceremony.Value<string>("RequestId"), Credential = credential }, access);
			unlocked.Status.Should().Be(HttpStatusCode.OK, unlocked.Body.ToString());
			unlocked.Data.Value<bool>("Locked").Should().BeFalse();
			(await Send("dispatch", HttpMethod.Get, "/Mfa/StepUpOptions?operation=account_security", bearer: access)).Status.Should().Be(HttpStatusCode.OK);
		}

		// ---- 8. Responder approval -------------------------------------------------------------------------------------

		[TestCase(false, TestName = "8. Responder on the member's phone approves a Unit sign-in")]
		[TestCase(true, TestName = "8. Responder on the member's phone approves a sign-in on a shared Unit tablet, which becomes a shared session")]
		public async Task Responder_approves_a_sign_in(bool shared)
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, ResponderApprover = true });

			var label = shared ? "Engine 7 tablet" : "Pat's phone";
			var first = await PasswordGrant("unit", user, transaction: true, shared: shared, deviceName: label);
			Methods(first.Body, "mfa_methods").Should().Contain("passkey_approval");
			Methods(first.Body, "mfa_enrolled").Should().Contain("passkey_approval");
			var transaction = first.Body.Value<string>("mfa_transaction");

			var requested = await Send("unit", HttpMethod.Post, "/MfaApproval/Request", new { Purpose = "login", Transaction = transaction }, shared: shared);
			requested.Status.Should().Be(HttpStatusCode.OK, requested.Body.ToString());
			var id = requested.Data.Value<string>("ApprovalRequestId");
			var number = requested.Data.Value<string>("MatchNumber");

			(await Send("unit", HttpMethod.Post, "/MfaApproval/Status", new { Transaction = transaction, ApprovalRequestId = id })).Data.Value<string>("State").Should().Be("pending");
			var pending = await _server.PendingForResponderAsync(user.UserId);
			pending.Value<string>("RequestingApp").Should().Be("unit");
			pending.Value<bool>("Shared").Should().Be(shared, "Responder shows a shared tablet's sign-in as shared");
			pending.Value<string>("InstallationLabel").Should().Be(label);
			(await _server.ApproveAsResponderAsync(user.UserId, number)).Should().Be("approved");
			(await Send("unit", HttpMethod.Post, "/MfaApproval/Status", new { Transaction = transaction, ApprovalRequestId = id })).Data.Value<string>("State").Should().Be("approved");

			var completion = await Send("unit", HttpMethod.Post, "/Authentication/CompleteApproval", new { Transaction = transaction, ApprovalRequestId = id });
			completion.Status.Should().Be(HttpStatusCode.OK, completion.Body.ToString());
			await TokensWork("unit", await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode"), shared: shared));
			var session = OnlySession(user, "unit");
			session.LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.PasskeyApproval);
			session.SharedMode.Should().Be(shared);
		}

		[Test(Description = "8. A shared tablet's single sign-on, finished by Responder, is shown to Responder as that tablet and becomes a shared session")]
		public async Task Responder_approves_a_shared_tablets_single_sign_on()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Sso = true, Totp = true, ResponderApprover = true });

			// The transaction starts at redemption, so that is where the tablet says it is shared and names itself.
			var (id, code, verifier) = await BrokeredRoundTrip("unit", user);
			var redeemed = await Send("unit", HttpMethod.Post, "/Sso/Redeem", new { SsoTransactionId = id, SsoCode = code, CodeVerifier = verifier },
				shared: true, deviceName: "Engine 7 tablet");
			redeemed.Data.Value<string>("Outcome").Should().Be("mfa_required", redeemed.Body.ToString());
			redeemed.Data["MfaEnrolled"]!.Values<string>().Should().Contain("passkey_approval");
			var transaction = redeemed.Data.Value<string>("Transaction");

			var requested = await Send("unit", HttpMethod.Post, "/MfaApproval/Request", new { Purpose = "login", Transaction = transaction });
			var pending = await _server.PendingForResponderAsync(user.UserId);
			pending.Value<bool>("Shared").Should().BeTrue();
			pending.Value<string>("InstallationLabel").Should().Be("Engine 7 tablet");
			(await _server.ApproveAsResponderAsync(user.UserId, requested.Data.Value<string>("MatchNumber"))).Should().Be("approved");

			var completion = await Send("unit", HttpMethod.Post, "/Authentication/CompleteApproval",
				new { Transaction = transaction, ApprovalRequestId = requested.Data.Value<string>("ApprovalRequestId") });
			await TokensWork("unit", await Redeem("unit", transaction, completion.Data.Value<string>("CompletionCode"), shared: true));
			var session = OnlySession(user, "unit");
			session.SharedMode.Should().BeTrue();
			session.AuthenticationMethod.Should().Be((int)UserSessionAuthenticationMethod.OidcSso);
			session.LoginMfaMethod.Should().Be((int)MfaEvidenceMethod.PasskeyApproval);
		}

		[Test(Description = "8. A wrong number in Responder does not approve; the member can deny")]
		public async Task A_wrong_number_or_a_denial_approves_nothing()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, ResponderApprover = true });
			var transaction = (await PasswordGrant("dispatch", user, transaction: true)).Body.Value<string>("mfa_transaction");
			var requested = await Send("dispatch", HttpMethod.Post, "/MfaApproval/Request", new { Purpose = "login", Transaction = transaction });
			var id = requested.Data.Value<string>("ApprovalRequestId");
			var wrong = requested.Data.Value<string>("MatchNumber") == "11" ? "22" : "11";

			var mismatch = () => _server.ApproveAsResponderAsync(user.UserId, wrong);
			await mismatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*approval_number_mismatch*");
			(await _server.ApproveAsResponderAsync(user.UserId, null, deny: true)).Should().Be("denied");

			(await Send("dispatch", HttpMethod.Post, "/MfaApproval/Status", new { Transaction = transaction, ApprovalRequestId = id })).Data.Value<string>("State").Should().Be("denied");
			(await Send("dispatch", HttpMethod.Post, "/Authentication/CompleteApproval", new { Transaction = transaction, ApprovalRequestId = id })).Status
				.Should().NotBe(HttpStatusCode.OK);
		}

		[Test(Description = "8. A locked shared tablet is unlocked by its operator's Responder")]
		public async Task Responder_unlocks_a_shared_tablet()
		{
			_server.AllGates(true);
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, ResponderApprover = true });
			var transaction = (await PasswordGrant("ic", user, transaction: true, shared: true)).Body.Value<string>("mfa_transaction");
			var completion = await Send("ic", HttpMethod.Post, "/Authentication/CompleteTotp", new { Transaction = transaction, Code = _server.TotpCode(user.UserId) });
			var access = (await Redeem("ic", transaction, completion.Data.Value<string>("CompletionCode"), shared: true)).Body.Value<string>("access_token");
			OnlySession(user, "ic").SharedMode.Should().BeTrue();

			var lockVersion = (await Send("ic", HttpMethod.Post, "/sessions/lock", new { }, access)).Data.Value<long>("LockVersion");
			var requested = await Send("ic", HttpMethod.Post, "/sessions/unlock-approval", new { LockVersion = lockVersion }, access);
			requested.Status.Should().Be(HttpStatusCode.OK, requested.Body.ToString());
			var id = requested.Data.Value<string>("ApprovalRequestId");

			var pending = await _server.PendingForResponderAsync(user.UserId);
			pending.Value<bool>("Shared").Should().BeTrue("Responder shows the request comes from a shared workstation");
			(await _server.ApproveAsResponderAsync(user.UserId, requested.Data.Value<string>("MatchNumber"))).Should().Be("approved");
			(await Send("ic", HttpMethod.Get, "/sessions/unlock-approval/" + id, bearer: access)).Data.Value<string>("State").Should().Be("approved");

			var unlocked = await Send("ic", HttpMethod.Post, "/sessions/complete-unlock",
				new { LockVersion = lockVersion, Method = "passkey_approval", ApprovalRequestId = id }, access);
			unlocked.Status.Should().Be(HttpStatusCode.OK, unlocked.Body.ToString());
			unlocked.Data.Value<bool>("Locked").Should().BeFalse();
		}

		[Test(Description = "8. With approval off, today's server offers no approval and refuses a request")]
		public async Task Approval_on_todays_server_is_not_offered()
		{
			var user = await _server.AddUserAsync(new LiveUserSpec { Totp = true, ResponderApprover = true });
			var first = await PasswordGrant("unit", user, transaction: true);
			first.Body.ContainsKey("mfa_transaction").Should().BeFalse();
		}
	}
}
