using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Authentication;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Resgrid.Tests.Security.Live
{
	/// <summary>What a seeded member has. Each member gets a department of their own, so its policy is theirs.</summary>
	internal sealed class LiveUserSpec
	{
		public bool Totp { get; set; }
		/// <summary>Apps (responder, unit, dispatch, ic, web) that hold a passkey for this member on this device.</summary>
		public List<string> PasskeyFor { get; set; } = new();
		/// <summary>The member's Responder passkey may approve sign-ins and step-ups on other apps.</summary>
		public bool ResponderApprover { get; set; }
		/// <summary>The department signs in through its OIDC provider (the fake IdP here).</summary>
		public bool Sso { get; set; }
		/// <summary>With <see cref="Sso"/>: the provider is a SAML IdP (the fake one here) instead of OIDC.</summary>
		public bool Saml { get; set; }
		/// <summary>The department has a tested mapping that accepts the provider's <c>mfa</c> amr, and allows it at sign-in.</summary>
		public bool ProviderMfa { get; set; }
		public bool RequireMfa { get; set; }
		/// <summary>Apps the department always runs as shared workstations (flags: Unit 1, IC 2, Dispatch 4).</summary>
		public int SharedModeRequiredApps { get; set; }
		/// <summary>Protected data may reuse the MFA of a recent sign-in (the department default).</summary>
		public bool AcceptRecentLoginMfaForAdp { get; set; } = true;
		/// <summary>
		/// Seat this member in that member's department instead of a new one: the same policy and SSO configuration, with an
		/// account and provider identity of their own (the next operator on a shared installation, say).
		/// </summary>
		public string ColleagueOf { get; set; }
	}

	/// <summary>A seeded member, as a test or an app sees it. The TOTP secret lets the caller compute codes.</summary>
	internal sealed class LiveUser
	{
		public string UserId { get; init; }
		public string Username { get; init; }
		public string Password { get; init; }
		public int DepartmentId { get; init; }
		public string DepartmentCode { get; init; }
		public string DepartmentToken { get; init; }
		public string TotpSecret { get; init; }
		public string ExternalSubject { get; init; }
		public List<string> RecoveryCodes { get; init; } = new();
		public List<string> Passkeys { get; init; } = new();
	}

	/// <summary>
	/// The real API sign-in pipeline on a real port, for end-to-end checks of the sign-in journeys and for the apps' own
	/// client code to talk to: OpenIddict's token endpoint and bearer validation, the session validation middleware, and
	/// the real login transaction, passkey, approval, broker, session and shared-session services over in-memory stores.
	/// Only identity storage (users, departments, SSO configuration) and the outside world are stand-ins: the identity
	/// provider is <see cref="FakeOidcProvider"/>, a device's passkey is <see cref="SoftPasskeyAuthenticator"/>, and a
	/// member's Responder on another phone is played by <see cref="ApproveAsResponderAsync"/>. The deployment switches
	/// are the real static configuration, so only one server may run at a time; <see cref="DisposeAsync"/> restores them.
	/// </summary>
	internal sealed class LiveSignInServer : IAsyncDisposable
	{
		public const string ApiPath = "/api/v4";
		public const string RelyingParties =
			"web=web.resgrid.test|https://web.resgrid.test;responder=responder.resgrid.test|https://responder.resgrid.test;" +
			"unit=unit.resgrid.test|https://unit.resgrid.test;dispatch=dispatch.resgrid.test|https://dispatch.resgrid.test;" +
			"ic=ic.resgrid.test|https://ic.resgrid.test";
		public const string ReturnTargets =
			"responder=resgrid://sso-return;unit=resgridunit://sso-return;dispatch=resgriddispatch://sso-return;ic=resgridic://sso-return";

		private sealed class Account
		{
			public IdentityUser User;
			public string Password;
			public string TotpSecret;
			public Department Department;
			public DepartmentSecurityPolicy Policy;
			public DepartmentSsoConfig Sso;
			public string ExternalSubject;
			public List<string> RecoveryCodes = new();
			public readonly Dictionary<string, string> IdentityTokens = new();
			public string ResponderAccessToken;
			public bool MemberDisabled;
		}

		private readonly ConcurrentDictionary<string, Account> _accounts = new();
		private readonly ConcurrentDictionary<string, (SoftPasskeyAuthenticator Authenticator, UserPasskey Passkey)> _authenticators = new();
		private readonly Dictionary<string, object> _savedConfig = new();
		private readonly RelyingPartyRegistry _registry = new(RelyingParties);
		private readonly FakeOidcProvider _idp = new();
		private readonly InMemoryUserMfaStateRepository _mfaState = new();
		private readonly InMemoryUserPasskeyRepository _passkeys = new();
		private int _nextDepartment = 1000;
		private WebApplication _app;
		private ECDsa _grantKey;
		private System.Security.Cryptography.X509Certificates.X509Certificate2 _grantCertificate;
		private HttpClient _self;
		private IHttpContextAccessor _previousAccessor;

		public InMemoryUserSessionsRepository Sessions { get; } = new();

		/// <summary>The sign-ins waiting for a second factor (or a required setup), as the server holds them.</summary>
		public InMemoryMfaLoginTransactionRepository LoginTransactions { get; } = new();
		public string BaseUrl { get; private set; }

		// ---- Switches -------------------------------------------------------------------------------------------------

		/// <summary>Every switch the eight journeys depend on, together: off is today's deployment default, on is the full plan.</summary>
		public void SetGates(bool transaction, bool brokeredSso, bool passkeys, bool approval, bool providerStepUp, bool sharedDevice)
		{
			TwoFactorConfig.LoginMfaTransactionEnabled = transaction;
			SsoConfig.BrokeredSsoEnabled = brokeredSso;
			PasskeyConfig.LoginAcceptanceEnabled = passkeys;
			PasskeyConfig.RegistrationEnabled = passkeys;
			PasskeyConfig.AdpAcceptanceEnabled = passkeys;
			// Version 2 protected-data grants carry the method and session binding that passkeys, approval and reuse need.
			PasskeyConfig.EmitGrantV2 = passkeys;
			PasskeyConfig.ResponderApprovalEnabled = approval;
			PasskeyConfig.ProviderStepUpEnabled = providerStepUp;
			PasskeyConfig.SharedDeviceModeEnabled = sharedDevice;
		}

		public void AllGates(bool on) => SetGates(on, on, on, on, on, on);

		private void SaveConfig()
		{
			void Save(string name, object value) => _savedConfig[name] = value;
			Save(nameof(TwoFactorConfig.LoginMfaTransactionEnabled), TwoFactorConfig.LoginMfaTransactionEnabled);
			Save(nameof(TwoFactorConfig.RequireMfaEnforcementEnabled), TwoFactorConfig.RequireMfaEnforcementEnabled);
			Save(nameof(SsoConfig.BrokeredSsoEnabled), SsoConfig.BrokeredSsoEnabled);
			Save(nameof(SsoConfig.BrokeredReturnTargets), SsoConfig.BrokeredReturnTargets);
			Save(nameof(PasskeyConfig.RelyingParties), PasskeyConfig.RelyingParties);
			Save(nameof(PasskeyConfig.LoginAcceptanceEnabled), PasskeyConfig.LoginAcceptanceEnabled);
			Save(nameof(PasskeyConfig.RegistrationEnabled), PasskeyConfig.RegistrationEnabled);
			Save(nameof(PasskeyConfig.AdpAcceptanceEnabled), PasskeyConfig.AdpAcceptanceEnabled);
			Save(nameof(PasskeyConfig.EmitGrantV2), PasskeyConfig.EmitGrantV2);
			Save(nameof(PasskeyConfig.ResponderApprovalEnabled), PasskeyConfig.ResponderApprovalEnabled);
			Save(nameof(PasskeyConfig.ProviderStepUpEnabled), PasskeyConfig.ProviderStepUpEnabled);
			Save(nameof(PasskeyConfig.SharedDeviceModeEnabled), PasskeyConfig.SharedDeviceModeEnabled);
			Save(nameof(SessionSecurityConfig.TrackingEnabled), SessionSecurityConfig.TrackingEnabled);
			Save(nameof(SystemBehaviorConfig.ResgridApiBaseUrl), SystemBehaviorConfig.ResgridApiBaseUrl);
		}

		private void RestoreConfig()
		{
			T Get<T>(string name) => (T)_savedConfig[name];
			TwoFactorConfig.LoginMfaTransactionEnabled = Get<bool>(nameof(TwoFactorConfig.LoginMfaTransactionEnabled));
			TwoFactorConfig.RequireMfaEnforcementEnabled = Get<bool>(nameof(TwoFactorConfig.RequireMfaEnforcementEnabled));
			SsoConfig.BrokeredSsoEnabled = Get<bool>(nameof(SsoConfig.BrokeredSsoEnabled));
			SsoConfig.BrokeredReturnTargets = Get<string>(nameof(SsoConfig.BrokeredReturnTargets));
			PasskeyConfig.RelyingParties = Get<string>(nameof(PasskeyConfig.RelyingParties));
			PasskeyConfig.LoginAcceptanceEnabled = Get<bool>(nameof(PasskeyConfig.LoginAcceptanceEnabled));
			PasskeyConfig.RegistrationEnabled = Get<bool>(nameof(PasskeyConfig.RegistrationEnabled));
			PasskeyConfig.AdpAcceptanceEnabled = Get<bool>(nameof(PasskeyConfig.AdpAcceptanceEnabled));
			PasskeyConfig.EmitGrantV2 = Get<bool>(nameof(PasskeyConfig.EmitGrantV2));
			PasskeyConfig.ResponderApprovalEnabled = Get<bool>(nameof(PasskeyConfig.ResponderApprovalEnabled));
			PasskeyConfig.ProviderStepUpEnabled = Get<bool>(nameof(PasskeyConfig.ProviderStepUpEnabled));
			PasskeyConfig.SharedDeviceModeEnabled = Get<bool>(nameof(PasskeyConfig.SharedDeviceModeEnabled));
			SessionSecurityConfig.TrackingEnabled = Get<bool>(nameof(SessionSecurityConfig.TrackingEnabled));
			SystemBehaviorConfig.ResgridApiBaseUrl = Get<string>(nameof(SystemBehaviorConfig.ResgridApiBaseUrl));
		}

		// ---- Members --------------------------------------------------------------------------------------------------

		public async Task<LiveUser> AddUserAsync(LiveUserSpec spec)
		{
			var colleague = spec.ColleagueOf == null ? null : _accounts[spec.ColleagueOf];
			var departmentId = colleague?.Department.DepartmentId ?? Interlocked.Increment(ref _nextDepartment);
			var userId = "live-" + Guid.NewGuid().ToString("N")[..12];
			var username = colleague == null ? "member" + departmentId : "member" + departmentId + "-" + userId[5..9];
			var account = new Account
			{
				User = new IdentityUser { Id = userId, UserName = username, Email = username + "@example.test", AuthenticationGeneration = 1, TwoFactorEnabled = spec.Totp },
				Password = "Pass-" + Guid.NewGuid().ToString("N")[..10],
				TotpSecret = spec.Totp ? NewBase32Secret() : null,
				Department = new Department { DepartmentId = departmentId, Code = "DEPT" + departmentId, Name = "Department " + departmentId },
				Policy = new DepartmentSecurityPolicy
				{
					DepartmentId = departmentId, MfaPolicyVersion = 1, RequireMfa = spec.RequireMfa, AllowFederatedMfaForLoginMfa = spec.ProviderMfa,
					SharedModeRequiredApps = spec.SharedModeRequiredApps, AcceptRecentLoginMfaForAdp = spec.AcceptRecentLoginMfaForAdp
				},
				ExternalSubject = spec.Sso ? "ext-" + userId : null
			};
			if (spec.Totp)
				account.RecoveryCodes.AddRange(new[] { "RC" + departmentId + "-1", "RC" + departmentId + "-2" });
			if (colleague != null)
			{
				account.Department = colleague.Department;
				account.Policy = colleague.Policy;
			}
			if (spec.Sso && spec.Saml)
			{
				// Everything a SAML sign-in needs: the IdP's sign-in page, this deployment's entity and ACS (the legacy relay, with
				// the department token its IdP posts back), and the IdP's certificate.
				account.Sso = new DepartmentSsoConfig
				{
					DepartmentSsoConfigId = "sso-" + departmentId, DepartmentId = departmentId, SsoProviderType = (int)SsoProviderType.Saml2, IsEnabled = true,
					IdpSsoUrl = SamlIdpSsoUrl, EntityId = "https://api.resgrid.test/saml/" + departmentId, EncryptedIdpCertificate = "idp-cert",
					AssertionConsumerServiceUrl = $"{BaseUrl}{ApiPath}/connect/saml-mobile-callback?departmentToken={Uri.EscapeDataString(DepartmentToken(account.Department))}",
					AllowLocalLogin = true
				};
			}
			else if (spec.Sso)
			{
				account.Sso = new DepartmentSsoConfig
				{
					DepartmentSsoConfigId = "sso-" + departmentId, DepartmentId = departmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
					Authority = FakeOidcProvider.Authority, ClientId = "resgrid-client", AllowLocalLogin = true
				};
				if (spec.ProviderMfa)
				{
					account.Sso.FederatedMfaMappingJson = new FederatedMfaMapping { AcceptAmr = new() { "mfa" } }.Serialize();
					account.Sso.FederatedMfaMappingVersion = 1;
					account.Sso.FederatedMfaTestedVersion = 1;
				}
			}
			if (colleague != null)
				account.Sso = colleague.Sso;
			_accounts[userId] = account;

			var passkeys = new List<string>();
			foreach (var client in spec.PasskeyFor.Distinct())
				passkeys.Add(await RegisterPasskeyAsync(account, Client(client), approval: false));
			if (spec.ResponderApprover && !spec.PasskeyFor.Contains("responder"))
				passkeys.Add(await RegisterPasskeyAsync(account, UserSessionClientApplication.Responder, approval: true));
			else if (spec.ResponderApprover)
				foreach (var row in _passkeys.Rows.Where(p => p.UserId == userId && p.ClientApplication == (int)UserSessionClientApplication.Responder))
					row.ApprovalEnabled = true;

			// The member's Responder is signed in on their phone, as an approver's is: only a signed-in Responder can approve.
			if (spec.ResponderApprover && TwoFactorConfig.LoginMfaTransactionEnabled && PasskeyConfig.LoginAcceptanceEnabled)
				await ResponderTokenAsync(account);

			return Describe(account, passkeys);
		}

		private LiveUser Describe(Account account, List<string> passkeys) => new()
		{
			UserId = account.User.Id, Username = account.User.UserName, Password = account.Password, DepartmentId = account.Department.DepartmentId,
			DepartmentCode = account.Department.Code, DepartmentToken = DepartmentToken(account.Department), TotpSecret = account.TotpSecret,
			ExternalSubject = account.ExternalSubject, RecoveryCodes = account.RecoveryCodes.ToList(), Passkeys = passkeys
		};

		/// <summary>The department token discovery hands out: the broker's own encoding, through this host's encryption.</summary>
		private static string DepartmentToken(Department department) => "enc:" + department.DepartmentId + ":" + department.Code;

		private async Task<string> RegisterPasskeyAsync(Account account, UserSessionClientApplication client, bool approval)
		{
			var provider = new Fido2PasskeyProvider(_registry);
			var authenticator = new SoftPasskeyAuthenticator();
			var handle = RandomNumberGenerator.GetBytes(32);
			var options = provider.CreateRegistrationOptions(client, handle, account.User.UserName, account.User.UserName, Array.Empty<byte[]>(), false);
			var registered = await provider.VerifyRegistrationAsync(client, options, authenticator.Register(options, Origin(client)));
			if (!registered.Succeeded)
				throw new InvalidOperationException("The soft authenticator's registration did not verify for " + client);

			var passkey = new UserPasskey
			{
				UserPasskeyId = "pk-" + Guid.NewGuid().ToString("N")[..12], UserId = account.User.Id, ClientApplication = (int)client,
				RpId = _registry.Get(client).RpId, CredentialId = registered.CredentialId, CredentialIdHash = SHA256.HashData(registered.CredentialId),
				PublicKey = registered.PublicKey, UserHandle = registered.UserHandle, DisplayName = client + " passkey", CreatedOnUtc = DateTime.UtcNow,
				IsBackupEligible = true, IsBackedUp = true, StateVersion = 1, ApprovalEnabled = approval
			};
			await _passkeys.TryInsertAsync(passkey);
			_authenticators[SoftPasskeyAuthenticator.B64(registered.CredentialId)] = (authenticator, passkey);
			return passkey.UserPasskeyId;
		}

		public static UserSessionClientApplication Client(string name) => name?.ToLowerInvariant() switch
		{
			"web" => UserSessionClientApplication.Web,
			"responder" => UserSessionClientApplication.Responder,
			"unit" => UserSessionClientApplication.Unit,
			"dispatch" => UserSessionClientApplication.Dispatch,
			"ic" or "command" => UserSessionClientApplication.Command,
			_ => throw new ArgumentException("Unknown app " + name)
		};

		public static string ClientName(UserSessionClientApplication client) => client == UserSessionClientApplication.Command ? "ic" : client.ToString().ToLowerInvariant();

		private string Origin(UserSessionClientApplication client) => "https://" + _registry.Get(client).RpId;

		// ---- Codes, the provider and the authenticator -----------------------------------------------------------------

		private static string NewBase32Secret()
		{
			const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
			var bytes = RandomNumberGenerator.GetBytes(20);
			var builder = new StringBuilder();
			foreach (var b in bytes)
				builder.Append(alphabet[b % 32]);
			return builder.ToString();
		}

		/// <summary>The member's current authenticator code, as their phone shows it.</summary>
		public string TotpCode(string userId, int stepOffset = 0)
		{
			var secret = _accounts[userId].TotpSecret;
			var step = TotpCalculator.CurrentTimeStep(DateTime.UtcNow) + stepOffset;
			return TotpCalculator.ComputeCode(TotpCalculator.Base32Decode(secret), step).ToString("D6");
		}

		/// <summary>What the provider's sign-in page signs in next: the member's external identity, with these methods.</summary>
		public void NextProviderSignIn(string externalSubject, IEnumerable<string> amr = null) => _nextProviderSignIn = (externalSubject, amr?.ToList());
		private (string Subject, List<string> Amr) _nextProviderSignIn;

		private (string Subject, DateTime AuthenticatedAtUtc)? _rememberedProviderSession;

		/// <summary>
		/// The installation's browser still holds a provider session for this member (an earlier operator's, say), signed in
		/// at that time. The provider answers from it, as real ones do, unless asked to authenticate again (OIDC
		/// <c>prompt=login</c> or <c>max_age=0</c>; SAML <c>ForceAuthn</c>). Null forgets it.
		/// </summary>
		public void RememberProviderSession(string externalSubject, DateTime authenticatedAtUtc) =>
			_rememberedProviderSession = externalSubject == null ? null : (externalSubject, authenticatedAtUtc);

		/// <summary>Who the provider signs in, and when: its remembered session, unless the request forces a new sign-in.</summary>
		private (string Subject, DateTime AuthenticatedAtUtc) ProviderAnswer(bool forced) =>
			!forced && _rememberedProviderSession is { } remembered ? remembered : (_nextProviderSignIn.Subject, DateTime.UtcNow);

		private static bool ForcesSignIn(string prompt, string maxAge) =>
			(prompt ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("login") || maxAge == "0";

		/// <summary>The provider's sign-in page: reads the broker's authorize URL and sends the browser back to the callback.</summary>
		public string ProviderSignIn(string authorizeUrl)
		{
			var (state, nonce, challenge, prompt, maxAge) = FakeOidcProvider.Authorize(authorizeUrl);
			var (subject, authenticatedAt) = ProviderAnswer(ForcesSignIn(prompt, maxAge));
			var amr = _nextProviderSignIn.Amr;
			_idp.ExpectedChallenge = challenge;
			_idp.NextIdToken = () => _idp.Token(nonce, subject: subject, authTime: authenticatedAt, amr: amr);
			return $"{BaseUrl}{ApiPath}/connect/oidc-callback?state={Uri.EscapeDataString(state)}&code={FakeOidcProvider.Code}";
		}

		private readonly ConcurrentDictionary<string, (string Subject, DateTime AuthenticatedAtUtc, string Challenge, string ClientId, string RedirectUri)> _appCodes = new();

		/// <summary>
		/// The provider's sign-in page for an app that runs the code flow itself (legacy OIDC, as the desktop app's main
		/// process does): back to the app's own redirect URI with a one-time code and the app's state.
		/// </summary>
		public string AppProviderSignIn(IQueryCollection query)
		{
			var code = "app-code-" + Guid.NewGuid().ToString("N");
			var (subject, authenticatedAt) = ProviderAnswer(ForcesSignIn(query["prompt"], query["max_age"]));
			_appCodes[code] = (subject, authenticatedAt, query["code_challenge"], query["client_id"], query["redirect_uri"]);
			return $"{query["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(query["state"].ToString())}";
		}

		/// <summary>
		/// The provider's token endpoint for such an app: S256 PKCE, the same client and redirect URI, each code once. As
		/// providers do for a client registered as a native app, it refuses a cross-origin (browser page) redemption.
		/// </summary>
		private async Task<IResult> AppTokenAsync(HttpRequest http)
		{
			if (http.Headers.ContainsKey("Origin"))
				return Results.BadRequest(new { error = "invalid_request", error_description = "Cross-origin token redemption is not allowed for this client." });

			var form = await http.ReadFormAsync();
			var verifier = form["code_verifier"].ToString();
			if (form["grant_type"] != "authorization_code" || !_appCodes.TryRemove(form["code"].ToString(), out var issued) || issued.ClientId != form["client_id"] ||
				issued.RedirectUri != form["redirect_uri"] || verifier.Length < 43 ||
				FakeOidcProvider.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != issued.Challenge)
				return Results.BadRequest(new { error = "invalid_grant" });

			return Results.Json(new
			{
				id_token = LegacyIdToken(issued.Subject, issued.AuthenticatedAtUtc), access_token = "idp-access-token", token_type = "Bearer", expires_in = 3600
			});
		}

		/// <summary>The fake SAML IdP's sign-in page (HTTP-Redirect binding), as a department's configuration names it.</summary>
		public const string SamlIdpSsoUrl = "https://idp.example.test/saml2/sso";

		private readonly ConcurrentDictionary<string, bool> _usedAssertions = new();

		/// <summary>
		/// The SAML IdP's sign-in page: reads the AuthnRequest (where to post, and that it was addressed here), signs in the
		/// member <see cref="NextProviderSignIn"/> named, and posts the response and the RelayState to the ACS, as the browser
		/// would. Returns where the ACS sent the browser next (the legacy relay's link to the app).
		/// </summary>
		public async Task<string> SamlProviderSignInAsync(string samlRequest, string relayState)
		{
			using var inflate = new System.IO.Compression.DeflateStream(new System.IO.MemoryStream(Convert.FromBase64String(samlRequest)),
				System.IO.Compression.CompressionMode.Decompress);
			var request = new System.Xml.XmlDocument();
			request.Load(inflate);
			var root = request.DocumentElement!;
			if (root.LocalName != "AuthnRequest" || root.GetAttribute("Destination") != SamlIdpSsoUrl)
				throw new InvalidOperationException("Not an AuthnRequest for this IdP");

			var (subject, authenticatedAt) = ProviderAnswer(root.GetAttribute("ForceAuthn") == "true");
			var assertion = "live-saml|" + subject + "|" + Guid.NewGuid().ToString("N") + "|" + ProviderSignInTime.ClaimValue(authenticatedAt);
			var posted = await _self.PostAsync(root.GetAttribute("AssertionConsumerServiceURL"), new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["SAMLResponse"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(assertion)), ["RelayState"] = relayState
			}));
			return posted.Headers.Location?.OriginalString ?? throw new InvalidOperationException("The ACS answered " + (int)posted.StatusCode);
		}

		/// <summary>A password change or revocation: the account's authentication generation moves on.</summary>
		public void AdvanceAuthenticationGeneration(string userId) => _accounts[userId].User.AuthenticationGeneration++;

		/// <summary>The department disables the member.</summary>
		public void DisableMember(string userId) => _accounts[userId].MemberDisabled = true;

		/// <summary>The department turns its SSO configuration off.</summary>
		public void DisableSso(string userId) => _accounts[userId].Sso.IsEnabled = false;

		/// <summary>The department turns its SSO configuration back on.</summary>
		public void EnableSso(string userId) => _accounts[userId].Sso.IsEnabled = true;

		/// <summary>The department replaces its SSO configuration with another one.</summary>
		public void ReplaceSso(string userId) => _accounts[userId].Sso.DepartmentSsoConfigId += "-replaced";

		/// <summary>The member removes their authenticator.</summary>
		public void RemoveAuthenticator(string userId) => _accounts[userId].User.TwoFactorEnabled = false;

		/// <summary>An id_token for the app-side (legacy) OIDC flow, as the provider hands it to the app.</summary>
		public string LegacyIdToken(string externalSubject, DateTime? authenticatedAtUtc = null) =>
			_idp.Token("legacy-" + Guid.NewGuid().ToString("N"), subject: externalSubject, authTime: authenticatedAtUtc);

		/// <summary>The device's passkey answering the server's request options, for whichever of its credentials is allowed.</summary>
		public string Assert(string optionsJson, UserSessionClientApplication client)
		{
			var options = JObject.Parse(optionsJson);
			var allowed = options["allowCredentials"]?.Select(c => c.Value<string>("id")).ToList() ?? new List<string>();
			var entry = allowed.Select(id => _authenticators.TryGetValue(id, out var found) ? found : default).FirstOrDefault(found => found.Authenticator != null);
			if (entry.Authenticator == null)
				throw new InvalidOperationException("No passkey on this device answers these options");
			return entry.Authenticator.Assert(optionsJson, Origin(client), entry.Passkey.UserHandle);
		}

		// ---- The member's other devices --------------------------------------------------------------------------------

		private HttpRequestMessage Request(HttpMethod method, string path, UserSessionClientApplication client, object body = null, string bearer = null,
			bool form = false, bool shared = false, string deviceName = null)
		{
			var request = new HttpRequestMessage(method, ApiPath + path);
			request.Headers.Add("X-Resgrid-Client", ClientName(client));
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
			return request;
		}

		private async Task<JObject> SendAsync(HttpRequestMessage request, bool allowFailure = false)
		{
			var response = await _self.SendAsync(request);
			var text = await response.Content.ReadAsStringAsync();
			var json = string.IsNullOrWhiteSpace(text) ? new JObject() : JToken.Parse(text) as JObject ?? new JObject();
			if (!allowFailure && !response.IsSuccessStatusCode)
				throw new InvalidOperationException($"{request.Method} {request.RequestUri} answered {(int)response.StatusCode}: {text}");
			json["__status"] = (int)response.StatusCode;
			return json;
		}

		/// <summary>
		/// Signs the member's Responder in with its own passkey (password, then the Responder passkey on the login
		/// transaction), as the phone that approves requests. Needs the transaction and passkey switches on.
		/// </summary>
		private async Task<string> ResponderTokenAsync(Account account)
		{
			if (account.ResponderAccessToken != null)
				return account.ResponderAccessToken;

			var first = await SendAsync(Request(HttpMethod.Post, "/connect/token", UserSessionClientApplication.Responder, new Dictionary<string, string>
			{
				["grant_type"] = "password", ["username"] = account.User.UserName, ["password"] = account.Password, ["scope"] = "openid offline_access",
				["mfa_flow"] = "transaction"
			}, form: true), allowFailure: true);
			var transaction = first.Value<string>("mfa_transaction") ?? throw new InvalidOperationException("Responder's sign-in opened no transaction: " + first);
			var ceremony = (JObject)(await SendAsync(Request(HttpMethod.Post, "/Authentication/PasskeyOptions", UserSessionClientApplication.Responder,
				new { Transaction = transaction })))["Data"];
			var credential = JObject.Parse(Assert(ceremony["Options"].ToString(), UserSessionClientApplication.Responder));
			var completion = (JObject)(await SendAsync(Request(HttpMethod.Post, "/Authentication/CompletePasskey", UserSessionClientApplication.Responder,
				new { Transaction = transaction, RequestId = ceremony.Value<string>("RequestId"), Credential = credential })))["Data"];
			var tokens = await SendAsync(Request(HttpMethod.Post, "/connect/token", UserSessionClientApplication.Responder, new Dictionary<string, string>
			{
				["grant_type"] = MfaLoginTransactions.CompletionGrantType, ["transaction"] = transaction, ["completion_code"] = completion.Value<string>("CompletionCode")
			}, form: true));
			return account.ResponderAccessToken = tokens.Value<string>("access_token");
		}

		/// <summary>
		/// The member's Responder, on their own phone: finds the pending request, types the number the requesting screen
		/// shows, and approves with its passkey (or denies). Returns the state the server reports.
		/// </summary>
		public async Task<string> ApproveAsResponderAsync(string userId, string matchNumber, bool deny = false, string denyReason = "declined")
		{
			var account = _accounts[userId];
			var token = await ResponderTokenAsync(account);
			JObject pending = null;
			for (var attempt = 0; attempt < 20 && pending == null; attempt++)
			{
				var answer = await SendAsync(Request(HttpMethod.Get, "/MfaApproval/Pending", UserSessionClientApplication.Responder, bearer: token));
				pending = answer["Data"] as JObject;
				if (pending == null)
					await Task.Delay(100);
			}
			if (pending == null)
				throw new InvalidOperationException("Responder has nothing pending to approve");

			var id = pending.Value<string>("ApprovalRequestId");
			if (deny)
				return (await SendAsync(Request(HttpMethod.Post, "/MfaApproval/Deny", UserSessionClientApplication.Responder, new { ApprovalRequestId = id, Reason = denyReason },
					token)))["Data"]?.Value<string>("State");

			var ceremony = (JObject)(await SendAsync(Request(HttpMethod.Post, "/MfaApproval/Options", UserSessionClientApplication.Responder, new { ApprovalRequestId = id },
				token)))["Data"];
			var credential = JObject.Parse(Assert(ceremony["Options"].ToString(), UserSessionClientApplication.Responder));
			var approved = await SendAsync(Request(HttpMethod.Post, "/MfaApproval/Approve", UserSessionClientApplication.Responder,
				new { ApprovalRequestId = id, MatchNumber = matchNumber, RequestId = ceremony.Value<string>("RequestId"), Credential = credential }, token));
			return approved["Data"]?.Value<string>("State");
		}

		/// <summary>What Responder's pending-approval card shows for the member's current request, or null.</summary>
		public async Task<JObject> PendingForResponderAsync(string userId) =>
			(await SendAsync(Request(HttpMethod.Get, "/MfaApproval/Pending", UserSessionClientApplication.Responder, bearer: await ResponderTokenAsync(_accounts[userId]))))["Data"] as JObject;

		/// <summary>
		/// Another app on another device starts a sign-in and asks the member's Responder to approve it: a shared workstation
		/// sends the shared-installation header and its label, as the apps do.
		/// </summary>
		public async Task<(string Transaction, string ApprovalRequestId, string MatchNumber)> StartApprovalSignInAsync(string userId, UserSessionClientApplication client,
			bool shared = false, string deviceName = null)
		{
			var account = _accounts[userId];
			var first = await SendAsync(Request(HttpMethod.Post, "/connect/token", client, new Dictionary<string, string>
			{
				["grant_type"] = "password", ["username"] = account.User.UserName, ["password"] = account.Password, ["scope"] = "openid offline_access",
				["mfa_flow"] = "transaction"
			}, form: true, shared: shared, deviceName: deviceName), allowFailure: true);
			var transaction = first.Value<string>("mfa_transaction") ?? throw new InvalidOperationException("No transaction: " + first);
			var started = (JObject)(await SendAsync(Request(HttpMethod.Post, "/MfaApproval/Request", client, new { Purpose = "login", Transaction = transaction })))["Data"];
			return (transaction, started.Value<string>("ApprovalRequestId"), started.Value<string>("MatchNumber"));
		}

		/// <summary>That other app finishes its sign-in with the approval: the server's answer to the final token request.</summary>
		public async Task<JObject> FinishApprovalSignInAsync(string transaction, string approvalRequestId, UserSessionClientApplication client)
		{
			var completion = await SendAsync(Request(HttpMethod.Post, "/Authentication/CompleteApproval", client,
				new { Transaction = transaction, ApprovalRequestId = approvalRequestId }), allowFailure: true);
			if ((int)completion["__status"] != 200)
				return completion;
			return await SendAsync(Request(HttpMethod.Post, "/connect/token", client, new Dictionary<string, string>
			{
				["grant_type"] = MfaLoginTransactions.CompletionGrantType, ["transaction"] = transaction,
				["completion_code"] = completion["Data"].Value<string>("CompletionCode")
			}, form: true), allowFailure: true);
		}

		/// <summary>The member's sessions, as the server holds them (what signed in, how, and whether it is a shared one).</summary>
		public IReadOnlyList<UserSession> SessionsOf(string userId) => Sessions.Rows.Where(s => s.UserId == userId).ToList();

		// ---- Host -----------------------------------------------------------------------------------------------------

		public static async Task<LiveSignInServer> StartAsync(string url = "http://127.0.0.1:0")
		{
			var server = new LiveSignInServer();
			server.SaveConfig();
			SessionSecurityConfig.TrackingEnabled = true;
			TwoFactorConfig.RequireMfaEnforcementEnabled = true;
			SsoConfig.BrokeredReturnTargets = ReturnTargets;
			PasskeyConfig.RelyingParties = RelyingParties;
			server.AllGates(false);
			await server.BuildAsync(url);
			return server;
		}

		private async Task BuildAsync(string url)
		{
			var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
			builder.Logging.ClearProviders();
			builder.WebHost.UseUrls(url);
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddApiVersioning();
			builder.Services.AddOpenIddict()
				.AddServer(options =>
				{
					Resgrid.Web.Services.Helpers.ResgridTokenEndpoints.UseResgridTokenEndpoints(options);
					options.AcceptAnonymousClients();
					options.EnableDegradedMode();
					options.DisableAccessTokenEncryption();
					options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
					options.UseAspNetCore().EnableTokenEndpointPassthrough().DisableTransportSecurityRequirement();
					options.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler => handler.UseInlineHandler(_ => default));
				})
				.AddValidation(options =>
				{
					options.UseLocalServer();
					options.UseAspNetCore();
				});
			builder.Services.AddAuthentication(OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
			builder.Services.AddAuthorization();
			builder.Services.AddControllers().AddApplicationPart(typeof(ConnectController).Assembly)
				.AddNewtonsoftJson(o => o.SerializerSettings.ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver());

			var time = TimeProvider.System;
			var departments = Departments();
			var sso = DepartmentSso();
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => _accounts.TryGetValue(id, out var a) ? a.User : null);
			var links = new Mock<IExternalIdentityLinkService>();
			links.Setup(l => l.IsLocalLoginAllowedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			links.Setup(l => l.IsLocalLoginAllowedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns((string value) => "enc:" + value);
			encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string value) => value.StartsWith("enc:") ? value[4..] : throw new CryptographicException());
			var users = Users();
			var signIn = SignIns(users.Object);
			var audits = Mock.Of<ISystemAuditsService>();
			var notices = Mock.Of<ISecurityNoticeService>();
			var cache = new InMemoryCacheProvider();

			var gates = new PasskeyFeatureGates(_registry);
			var policy = new MfaPolicyService(sso.Object, _mfaState, gates);
			var userSessions = new UserSessionService(Sessions, identity.Object, Mock.Of<IIdentityRepository>(), departments.Object, sso.Object,
				new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>(), gates, audits, Mock.Of<ISessionEventPublisher>(), time);
			var activityRows = new InMemoryMfaActivityRepository();
			var evidence = new MfaEvidenceService(new InMemoryMfaEvidenceRepository(), _mfaState, _passkeys, Sessions, activityRows, time);
			var activity = new MfaActivityService(activityRows, Sessions, userSessions, notices, audits, time);
			var challenges = new AuthenticationChallengeService(new InMemoryAuthenticationChallengeRepository(), time);
			var approvalRows = new InMemoryMfaApprovalRequestRepository();
			var passkeys = new PasskeyService(_passkeys, new Fido2PasskeyProvider(_registry), _registry, gates, challenges, evidence, policy, Sessions,
				userSessions, audits, approvalRows, notices, time);
			var loginRows = LoginTransactions;
			var approvals = new MfaApprovalService(approvalRows, _passkeys, Sessions, identity.Object, loginRows, passkeys, _registry, gates, policy,
				departments.Object, Mock.Of<INovuProvider>(), Mock.Of<IIpLocationProvider>(), audits, notices, Mock.Of<ISessionEventPublisher>(), activity, time);
			var transactions = new MfaLoginTransactionService(loginRows, policy, sso.Object, identity.Object, passkeys, approvals, gates, time);
			var broker = new SsoBrokerService(new InMemorySsoLoginTransactionRepository(), sso.Object, departments.Object, identity.Object, encryption.Object,
				_idp, new SsoReturnTargetRegistry(ReturnTargets), new InMemoryBrokerReplayRepository(), policy, time);
			var keyStore = new Mock<IUserStore<IdentityUser>>();
			keyStore.As<IUserAuthenticatorKeyStore<IdentityUser>>()
				.Setup(s => s.SetAuthenticatorKeyAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((IdentityUser user, string key, CancellationToken _) => _accounts[user.Id].TotpSecret = key)
				.Returns(Task.CompletedTask);

			builder.Services.AddSingleton(Mock.Of<IUsersService>());
			builder.Services.AddSingleton(Mock.Of<IUserProfileService>());
			builder.Services.AddSingleton(departments.Object);
			builder.Services.AddSingleton(signIn.Object);
			builder.Services.AddSingleton(users.Object);
			builder.Services.AddSingleton(audits);
			builder.Services.AddSingleton(sso.Object);
			builder.Services.AddSingleton(encryption.Object);
			builder.Services.AddSingleton<ICacheProvider>(cache);
			builder.Services.AddSingleton<IUserSessionService>(userSessions);
			builder.Services.AddSingleton<IUserSessionsRepository>(Sessions);
			builder.Services.AddSingleton(links.Object);
			builder.Services.AddSingleton<IMfaPolicyService>(policy);
			builder.Services.AddSingleton<IMfaEvidenceService>(evidence);
			builder.Services.AddSingleton<IPasskeyService>(passkeys);
			builder.Services.AddSingleton<IMfaLoginTransactionService>(transactions);
			builder.Services.AddSingleton<IMfaApprovalService>(approvals);
			builder.Services.AddSingleton<IMfaActivityService>(activity);
			// Protected data: the real step-up issuer and v2 grants, signed with a certificate made for this server. Every
			// department protects data and asks for step-up; the rest of the protected-data surface plays no part here.
			_grantKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_grantCertificate = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=live-adp-issuer", _grantKey, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
			var grants = new ProtectedDataGrantService(() => _grantCertificate, () => _grantCertificate);
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => new DepartmentDataProtectionPolicy { DepartmentId = id, PolicyEpoch = 1, StepUpWindowMinutes = 15 });
			protection.Setup(p => p.GetStepUpDecisionForClientAsync(It.IsAny<int>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<bool>()))
				.ReturnsAsync(new AdpStepUpDecision { StepUpRequired = true, PolicyEpoch = 1, StepUpWindowMinutes = 15 });
			var ssoConfigs = new Mock<IDepartmentSsoConfigRepository>();
			ssoConfigs.Setup(r => r.GetAllByDepartmentIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => AccountByDepartment(id)?.Sso is { } config ? new[] { config } : Array.Empty<DepartmentSsoConfig>());
			var credentials = new MfaCredentialStateService(_passkeys, Sessions, ssoConfigs.Object, time);
			var adpAudit = Mock.Of<IAdpAuditRepository>();
			builder.Services.AddSingleton<IAdpStepUpService>(new AdpStepUpService(grants, protection.Object, policy, evidence, passkeys, approvals, broker, sso.Object,
				credentials, activity, adpAudit, gates, time));
			builder.Services.AddSingleton<IProtectedDataGrantService>(grants);
			builder.Services.AddSingleton(protection.Object);
			builder.Services.AddSingleton<IMfaCredentialStateService>(credentials);
			builder.Services.AddSingleton(adpAudit);
			builder.Services.AddSingleton(Mock.Of<IDepartmentLockService>());
			builder.Services.AddSingleton(Mock.Of<IProtectedFieldCatalog>());
			builder.Services.AddSingleton(Mock.Of<IFeatureToggleService>());
			builder.Services.AddSingleton(Mock.Of<IAdpReleaseService>());
			builder.Services.AddSingleton(keyStore.Object);
			builder.Services.AddSingleton<IUserMfaStateRepository>(_mfaState);
			builder.Services.AddSingleton(notices);
			builder.Services.AddSingleton<IFactorRecoveryService>(new FactorRecoveryService(new InMemoryFactorRecoveryTransactionRepository(), identity.Object, time));
			builder.Services.AddSingleton<IUserPasskeyRepository>(_passkeys);
			builder.Services.AddSingleton<IAuthenticationChallengeService>(challenges);
			builder.Services.AddSingleton<IMfaApprovalRequestRepository>(approvalRows);
			builder.Services.AddSingleton<ISsoBrokerService>(broker);
			builder.Services.AddSingleton<ISharedSessionService>(new SharedSessionService(Sessions, userSessions, evidence, sso.Object, audits, activity, time));
			builder.Services.AddSingleton(identity.Object);
			builder.Services.AddSingleton<IRelyingPartyRegistry>(_registry);
			builder.Services.AddSingleton<IPasskeyFeatureGates>(gates);

			_app = builder.Build();
			_app.UseRouting();
			_app.UseAuthentication();
			_app.UseMiddleware<Resgrid.Web.Services.Middleware.SessionValidationMiddleware>();
			_app.UseAuthorization();
			MapControl(_app);
			_app.MapControllers();

			_previousAccessor = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _app.Services.GetRequiredService<IHttpContextAccessor>();
			await _app.StartAsync();
			BaseUrl = _app.Urls.Single().TrimEnd('/');
			SystemBehaviorConfig.ResgridApiBaseUrl = BaseUrl;
			_self = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(BaseUrl) };
		}

		private Account AccountByName(string username) =>
			_accounts.Values.FirstOrDefault(a => string.Equals(a.User.UserName, username, StringComparison.OrdinalIgnoreCase));

		private Account AccountByDepartment(int departmentId) => _accounts.Values.FirstOrDefault(a => a.Department.DepartmentId == departmentId);

		/// <summary>The department's member with this provider identity.</summary>
		private Account AccountBySubject(int departmentId, string subject) =>
			subject == null ? null : _accounts.Values.FirstOrDefault(a => a.Department.DepartmentId == departmentId && a.ExternalSubject == subject);

		private Mock<IDepartmentsService> Departments()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string id, bool _) => _accounts.TryGetValue(id, out var a) ? a.Department : null);
			departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((string id, int departmentId, bool _) => _accounts.TryGetValue(id, out var a) && a.Department.DepartmentId == departmentId
					? new DepartmentMember { UserId = id, DepartmentId = departmentId, IsDefault = true, IsActive = true, IsDisabled = a.MemberDisabled }
					: null);
			departments.Setup(d => d.GetAllDepartmentsForUserAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => _accounts.TryGetValue(id, out var a)
					? new List<DepartmentMember> { new() { UserId = id, DepartmentId = a.Department.DepartmentId, IsDefault = true, IsActive = true } }
					: new List<DepartmentMember>());
			departments.Setup(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
				.ReturnsAsync((int id, bool _) => AccountByDepartment(id)?.Department);
			departments.Setup(d => d.GetDepartmentByNameAsync(It.IsAny<string>()))
				.ReturnsAsync((string code) => _accounts.Values.Select(a => a.Department).FirstOrDefault(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase)));
			return departments;
		}

		private Mock<IDepartmentSsoService> DepartmentSso()
		{
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, CancellationToken _) => AccountByDepartment(id)?.Sso is { } config ? new[] { config } : Array.Empty<DepartmentSsoConfig>());
			sso.Setup(s => s.GetSsoConfigForDepartmentAsync(It.IsAny<int>(), It.IsAny<SsoProviderType>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, SsoProviderType type, CancellationToken _) =>
					AccountByDepartment(id)?.Sso is { } config && config.SsoProviderType == (int)type ? config : null);
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, CancellationToken _) => AccountByDepartment(id)?.Policy);
			sso.Setup(s => s.IsSsoEnabledForDepartmentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, CancellationToken _) => AccountByDepartment(id)?.Sso?.IsEnabled == true);
			sso.Setup(s => s.IsRequireMfaPolicyActiveAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, CancellationToken _) => AccountByDepartment(id)?.Policy?.RequireMfa == true);
			sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, CancellationToken _) => AccountByDepartment(id)?.Sso is { } config && FederatedMfaMapping.IsTested(config) ? config : null);
			sso.Setup(s => s.IsFederatedMfaAvailableAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, string userId, CancellationToken _) =>
					AccountByDepartment(id) is { } a && a.User.Id == userId && a.ExternalSubject != null && a.Sso != null && FederatedMfaMapping.IsTested(a.Sso));
			sso.Setup(s => s.FindLinkedUserIdAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, ClaimsPrincipal principal, DepartmentSsoConfig _, CancellationToken _) =>
					AccountBySubject(id, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value)?.User.Id);
			sso.Setup(s => s.ProvisionOrLinkUserAsync(It.IsAny<int>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, ClaimsPrincipal principal, DepartmentSsoConfig _, string _, CancellationToken _) =>
					AccountBySubject(id, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value)?.User);
			// The app-side (legacy) exchange: the provider's id_token, read as the real service reads it after validating it.
			sso.Setup(s => s.ValidateExternalTokenAsync(It.IsAny<int>(), SsoProviderType.Oidc, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, SsoProviderType _, string token, string _, CancellationToken _) =>
				{
					var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
					if (AccountBySubject(id, jwt.Subject) == null)
						return null;
					var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, jwt.Subject) };
					// The id_token's auth_time, under the name the real handler maps it to.
					if (jwt.Payload.TryGetValue("auth_time", out var authTime))
						claims.Add(new Claim(ClaimTypes.AuthenticationInstant, Convert.ToString(authTime, System.Globalization.CultureInfo.InvariantCulture),
							ClaimValueTypes.Integer64));
					return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
				});
			// A SAML response (from this server's fake IdP), read as the real service reads a validated one, and like it, used
			// once: a second validation of the same assertion fails.
			sso.Setup(s => s.ValidateExternalTokenAsync(It.IsAny<int>(), SsoProviderType.Saml2, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int id, SsoProviderType _, string response, string _, CancellationToken _) =>
				{
					var parts = Encoding.UTF8.GetString(Convert.FromBase64String(response)).Split('|');
					if (parts.Length != 4 || parts[0] != "live-saml" || !_usedAssertions.TryAdd(parts[2], true) || AccountBySubject(id, parts[1]) == null)
						return null;
					// The assertion's AuthnInstant, as the real validation adds it.
					return new ClaimsPrincipal(new ClaimsIdentity(new[]
					{
						new Claim(ClaimTypes.NameIdentifier, parts[1]), new Claim(ProviderSignInTime.ClaimType, parts[3], ClaimValueTypes.Integer64)
					}, "saml2"));
				});
			return sso;
		}

		private Mock<UserManager<IdentityUser>> Users()
		{
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((string name) => AccountByName(name)?.User);
			users.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((string _) => null);
			users.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => _accounts.TryGetValue(id, out var a) ? a.User : null);
			users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) => user.TwoFactorEnabled);
			users.Setup(m => m.SetTwoFactorEnabledAsync(It.IsAny<IdentityUser>(), It.IsAny<bool>()))
				.ReturnsAsync((IdentityUser user, bool enabled) => { user.TwoFactorEnabled = enabled; return IdentityResult.Success; });
			users.Setup(m => m.GetAuthenticatorKeyAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) => _accounts[user.Id].TotpSecret);
			users.Setup(m => m.GenerateNewAuthenticatorKey()).Returns(NewBase32Secret);
			users.Setup(m => m.GetEmailAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) => user.Email);
			users.Setup(m => m.GetUserNameAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) => user.UserName);
			users.Setup(m => m.SetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser user, string provider, string name, string value) =>
				{
					_accounts[user.Id].IdentityTokens[provider + "/" + name] = value;
					return IdentityResult.Success;
				});
			users.Setup(m => m.GetAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser user, string provider, string name) => _accounts[user.Id].IdentityTokens.GetValueOrDefault(provider + "/" + name));
			users.Setup(m => m.RemoveAuthenticationTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser user, string provider, string name) =>
				{
					_accounts[user.Id].IdentityTokens.Remove(provider + "/" + name);
					return IdentityResult.Success;
				});
			users.Setup(m => m.UpdateSecurityStampAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			users.Setup(m => m.AccessFailedAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
			users.Setup(m => m.IsLockedOutAsync(It.IsAny<IdentityUser>())).ReturnsAsync(false);
			// The authenticator check the real token provider makes: the code for a time step near now, and each step once.
			users.Setup(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()))
				.Returns(async (IdentityUser user, string _, string code) =>
				{
					var secret = _accounts[user.Id].TotpSecret;
					var step = secret == null ? null : TotpCalculator.FindMatchingTimeStep(secret, code ?? "", DateTime.UtcNow);
					return step != null && await _mfaState.TryConsumeTotpTimeStepAsync(user.Id, step.Value, DateTime.UtcNow);
				});
			users.Setup(m => m.RedeemTwoFactorRecoveryCodeAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()))
				.ReturnsAsync((IdentityUser user, string code) => _accounts[user.Id].RecoveryCodes.Remove(code) ? IdentityResult.Success : IdentityResult.Failed());
			users.Setup(m => m.CountRecoveryCodesAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) => _accounts[user.Id].RecoveryCodes.Count);
			return users;
		}

		private Mock<SignInManager<IdentityUser>> SignIns(UserManager<IdentityUser> users)
		{
			var signIn = new Mock<SignInManager<IdentityUser>>(users, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(), null, null, null, null);
			signIn.Setup(s => s.CheckPasswordSignInAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((IdentityUser user, string password, bool _) => _accounts[user.Id].Password == password ? SignInResult.Success : SignInResult.Failed);
			signIn.Setup(s => s.CanSignInAsync(It.IsAny<IdentityUser>())).ReturnsAsync(true);
			signIn.Setup(s => s.ValidateSecurityStampAsync(It.IsAny<ClaimsPrincipal>()))
				.ReturnsAsync((ClaimsPrincipal principal) => _accounts.TryGetValue(principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value ?? "", out var a) ? a.User : null);
			// The claims Resgrid's principal factory puts on a member (user and department ids, which the API and the session
			// middleware read from the access token).
			signIn.Setup(s => s.CreateUserPrincipalAsync(It.IsAny<IdentityUser>())).ReturnsAsync((IdentityUser user) =>
				new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(OpenIddictConstants.Claims.Subject, user.Id), new Claim(OpenIddictConstants.Claims.Name, user.UserName),
					new Claim(ClaimTypes.PrimarySid, user.Id), new Claim(ClaimTypes.PrimaryGroupSid, _accounts[user.Id].Department.DepartmentId.ToString())
				}, "Identity.Application", OpenIddictConstants.Claims.Name, OpenIddictConstants.Claims.Role)));
			return signIn;
		}

		// ---- Control endpoints (for the apps' live tests) ---------------------------------------------------------------

		private void MapControl(WebApplication app)
		{
			app.MapPost("/__live/gates", async (HttpRequest http) =>
			{
				var body = await Body(http);
				SetGates(body.Value<bool?>("transaction") ?? false, body.Value<bool?>("brokeredSso") ?? false, body.Value<bool?>("passkeys") ?? false,
					body.Value<bool?>("approval") ?? false, body.Value<bool?>("providerStepUp") ?? false, body.Value<bool?>("sharedDevice") ?? false);
				return Results.Ok();
			});
			app.MapPost("/__live/users", async (HttpRequest http) =>
				Results.Json(await AddUserAsync((await Body(http)).ToObject<LiveUserSpec>())));
			app.MapPost("/__live/totp", async (HttpRequest http) =>
			{
				var body = await Body(http);
				return Results.Json(new { code = TotpCode(body.Value<string>("userId"), body.Value<int?>("stepOffset") ?? 0) });
			});
			app.MapPost("/__live/idp/next", async (HttpRequest http) =>
			{
				var body = await Body(http);
				NextProviderSignIn(body.Value<string>("subject"), body["amr"]?.Values<string>());
				return Results.Ok();
			});
			// The provider's sign-in page, as the browser reaches it from the broker's authorize URL, or from an app that runs the
			// code flow itself (its redirect URI is the app's own scheme).
			app.MapGet("/__live/idp/authorize", (HttpRequest http) =>
				Results.Redirect(LegacyAppCallbacks.All.Any(a => a.Callback == http.Query["redirect_uri"])
					? AppProviderSignIn(http.Query)
					: ProviderSignIn(FakeOidcProvider.AuthorizationEndpoint + http.QueryString.Value)));
			app.MapGet("/__live/idp/.well-known/openid-configuration", () => Results.Json(new
			{
				issuer = FakeOidcProvider.Issuer,
				authorization_endpoint = FakeOidcProvider.AuthorizationEndpoint,
				token_endpoint = FakeOidcProvider.TokenEndpoint
			}));
			app.MapPost("/__live/idp/token", (HttpRequest http) => AppTokenAsync(http));
			// The SAML IdP's sign-in page, as the browser reaches it from the server's legacy SAML start page.
			app.MapGet("/__live/idp/saml", async (HttpRequest http) =>
				Results.Redirect(await SamlProviderSignInAsync(http.Query["SAMLRequest"], http.Query["RelayState"])));
			app.MapPost("/__live/idp/legacy-token", async (HttpRequest http) =>
			{
				var body = await Body(http);
				var minutesAgo = body.Value<double?>("authenticatedMinutesAgo");
				return Results.Json(new { id_token = LegacyIdToken(body.Value<string>("subject"), minutesAgo == null ? null : DateTime.UtcNow.AddMinutes(-minutesAgo.Value)) });
			});
			// The installation's browser holds a provider session (subject, signed in minutesAgo); no subject forgets it.
			app.MapPost("/__live/idp/session", async (HttpRequest http) =>
			{
				var body = await Body(http);
				RememberProviderSession(body.Value<string>("subject"), DateTime.UtcNow.AddMinutes(-(body.Value<double?>("minutesAgo") ?? 0)));
				return Results.Ok();
			});
			app.MapPost("/__live/authenticator/assert", async (HttpRequest http) =>
			{
				var body = await Body(http);
				var options = body["options"] is JValue text ? text.Value<string>() : body["options"].ToString();
				return Results.Content(Assert(options, Client(body.Value<string>("client"))), "application/json");
			});
			app.MapPost("/__live/responder/approve", async (HttpRequest http) =>
			{
				var body = await Body(http);
				var state = await ApproveAsResponderAsync(body.Value<string>("userId"), body.Value<string>("matchNumber"), body.Value<bool?>("deny") ?? false,
					body.Value<string>("reason") ?? "declined");
				return Results.Json(new { state });
			});
			app.MapPost("/__live/responder/pending", async (HttpRequest http) =>
				Results.Content((await PendingForResponderAsync((await Body(http)).Value<string>("userId")))?.ToString() ?? "null", "application/json"));
			app.MapPost("/__live/requester/start", async (HttpRequest http) =>
			{
				var body = await Body(http);
				var (transaction, id, number) = await StartApprovalSignInAsync(body.Value<string>("userId"), Client(body.Value<string>("client")),
					body.Value<bool?>("shared") ?? false, body.Value<string>("deviceName"));
				return Results.Json(new { transaction, approvalRequestId = id, matchNumber = number });
			});
			app.MapPost("/__live/requester/finish", async (HttpRequest http) =>
			{
				var body = await Body(http);
				var answer = await FinishApprovalSignInAsync(body.Value<string>("transaction"), body.Value<string>("approvalRequestId"), Client(body.Value<string>("client")));
				return Results.Content(answer.ToString(), "application/json");
			});
			app.MapPost("/__live/sessions", async (HttpRequest http) =>
				Results.Json(SessionsOf((await Body(http)).Value<string>("userId")).Select(s => new
				{
					s.UserSessionId, client = ClientName((UserSessionClientApplication)s.ClientApplication), s.SharedMode, s.State, s.LockVersion,
					authentication = ((UserSessionAuthenticationMethod)s.AuthenticationMethod).ToString(),
					loginMfa = s.LoginMfaMethod == null ? null : MfaMethodNames.From((MfaEvidenceMethod)s.LoginMfaMethod.Value), s.LoginMfaFactorReference,
					s.DeviceName
				})));
		}

		private static async Task<JObject> Body(HttpRequest http)
		{
			using var reader = new System.IO.StreamReader(http.Body);
			var text = await reader.ReadToEndAsync();
			return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
		}

		public async ValueTask DisposeAsync()
		{
			_self?.Dispose();
			if (_app != null)
			{
				await _app.StopAsync();
				await _app.DisposeAsync();
			}
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;
			_grantCertificate?.Dispose();
			_grantKey?.Dispose();
			RestoreConfig();
		}
	}
}
