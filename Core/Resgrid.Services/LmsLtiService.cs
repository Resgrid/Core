using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The department's RS256 platform keys for LTI: generation, the public JWK, signing an id_token and verifying a tool's client assertion.
	/// The private key is PKCS#8, encrypted for the department before it is stored and decrypted only to sign.
	/// </summary>
	public static class LmsLtiCrypto
	{
		public static readonly string[] AcceptedAssertionAlgorithms = { SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512 };

		/// <summary>A new 2048-bit RSA key: its kid, its public JWK (JSON) and its PKCS#8 private key (base64, not yet encrypted).</summary>
		public static (string Kid, string PublicJwkJson, string PrivatePkcs8) NewKey()
		{
			using var rsa = RSA.Create(2048);
			var kid = Guid.NewGuid().ToString("N");
			var parameters = rsa.ExportParameters(false);
			var jwk = new JObject
			{
				["kty"] = "RSA",
				["kid"] = kid,
				["use"] = "sig",
				["alg"] = SecurityAlgorithms.RsaSha256,
				["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
				["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
			};
			return (kid, jwk.ToString(Formatting.None), Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()));
		}

		/// <summary>A signed JWT (RS256, kid in the header) over <paramref name="claims"/> with the standard issuer, audience and lifetime claims.</summary>
		public static string Sign(string privatePkcs8, string kid, string issuer, string audience, IDictionary<string, object> claims, DateTime utcNow, TimeSpan lifetime)
		{
			using var rsa = RSA.Create();
			rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privatePkcs8), out _);
			// Round 10 A8 fix: the default CryptoProviderFactory caches a signature provider per key, holding this RSA instance, which is disposed
			// when Sign returns; the next launch signed with the same platform key in this process then threw ObjectDisposedException. No cache.
			var signingKey = new RsaSecurityKey(rsa) { KeyId = kid, CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
			var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
			return handler.CreateToken(new SecurityTokenDescriptor
			{
				Issuer = issuer,
				Audience = audience,
				IssuedAt = utcNow,
				NotBefore = utcNow.AddSeconds(-5),
				Expires = utcNow.Add(lifetime),
				Claims = new Dictionary<string, object>(claims, StringComparer.Ordinal),
				TokenType = "JWT",
				SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256) { CryptoProviderFactory = signingKey.CryptoProviderFactory }
			});
		}

		/// <summary>The signing keys of a JWKS document (RSA keys only); empty when it does not parse.</summary>
		public static IList<SecurityKey> SigningKeys(string jwksJson)
		{
			if (string.IsNullOrWhiteSpace(jwksJson))
				return new List<SecurityKey>();
			try
			{
				var set = new JsonWebKeySet(jwksJson);
				return set.Keys.Where(k => string.Equals(k.Kty, "RSA", StringComparison.Ordinal) && (string.IsNullOrEmpty(k.Use) || k.Use == "sig")).Cast<SecurityKey>().ToList();
			}
			catch (Exception)
			{
				return new List<SecurityKey>();
			}
		}

		/// <summary>
		/// Verifies a tool's client assertion (RFC 7523): signed with one of its keys (RS256/384/512), issued and subject = its client id, the
		/// audience is the token URL (or the issuer), not expired and at most an hour long, and carrying a jti. Returns the jti, or null.
		/// </summary>
		public static async Task<string> ValidateClientAssertionAsync(string assertion, string clientId, IEnumerable<string> audiences, IList<SecurityKey> keys, DateTime utcNow)
		{
			if (string.IsNullOrWhiteSpace(assertion) || keys == null || keys.Count == 0)
				return null;
			var handler = new JsonWebTokenHandler();
			var result = await handler.ValidateTokenAsync(assertion, new TokenValidationParameters
			{
				ValidIssuer = clientId,
				ValidAudiences = audiences,
				IssuerSigningKeys = keys,
				ValidAlgorithms = AcceptedAssertionAlgorithms,
				RequireExpirationTime = true,
				RequireSignedTokens = true,
				ValidateLifetime = true,
				LifetimeValidator = (notBefore, expires, token, parameters) =>
					expires.HasValue && expires.Value > utcNow.AddMinutes(-1) && expires.Value <= utcNow.AddHours(1).AddMinutes(1) && (!notBefore.HasValue || notBefore.Value <= utcNow.AddMinutes(1)),
				ClockSkew = TimeSpan.FromMinutes(1)
			});
			if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
				return null;
			if (!string.Equals(token.Subject, clientId, StringComparison.Ordinal))
				return null;
			var jti = token.Id;
			return string.IsNullOrWhiteSpace(jti) || jti.Length > 200 ? null : jti;
		}

		/// <summary>The client id a client assertion claims (iss), read without validation to find the tool whose keys verify it.</summary>
		public static string UnverifiedIssuer(string assertion)
		{
			try
			{
				return new JsonWebToken(assertion).Issuer;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// LTI 1.3 tool consumer (T5 item 3; partner CE content, §13a.4): Resgrid is the platform. A department registers a tool (its OIDC login
	/// URL, launch URL, redirect URIs and key set); Resgrid issues the client and deployment ids and signs launches with the department's
	/// rotate-able RS256 key (private half encrypted for the department). A launch is OIDC third-party-initiated: a single-use message hint
	/// bound to the learner and lesson, then an id_token posted to the tool's registered redirect URI. With grade passback the tool's
	/// Assignment and Grade Services score (client-credentials token, assertion verified with the tool's keys) completes the lesson at its
	/// pass mark; without it, the launch itself completes the lesson. Names and Roles is not offered. Everything is LMS (gate fails closed).
	/// </summary>
	public class LmsLtiService : ILmsLtiService
	{
		private static readonly TimeSpan KeySetCacheFor = TimeSpan.FromMinutes(10);
		private static readonly ConcurrentDictionary<string, (string Json, DateTime Until)> ToolKeySets = new ConcurrentDictionary<string, (string, DateTime)>(StringComparer.Ordinal);

		private readonly ILmsLtiToolsRepository _tools;
		private readonly ILmsLtiPlatformKeysRepository _keys;
		private readonly ILmsLtiAccessTokensRepository _tokens;
		private readonly ILmsRuntimeSessionsRepository _sessions;
		private readonly ILmsRuntimeRegistrationsRepository _registrations;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsXapiStatementQueue _xapi;
		private readonly IEncryptionService _encryption;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly ILmsStandardsTransport _transport;
		private readonly IEventAggregator _eventAggregator;

		public LmsLtiService(ILmsLtiToolsRepository tools, ILmsLtiPlatformKeysRepository keys, ILmsLtiAccessTokensRepository tokens, ILmsRuntimeSessionsRepository sessions,
			ILmsRuntimeRegistrationsRepository registrations, ILmsLessonsRepository lessons, ILmsCourseVersionsRepository versions, ILmsCoursesRepository courses,
			ILmsLearnerService learner, ILmsXapiStatementQueue xapi, IEncryptionService encryption, IDepartmentsService departments, IUserProfileService profiles,
			ILmsStandardsTransport transport, IEventAggregator eventAggregator)
		{
			_tools = tools;
			_keys = keys;
			_tokens = tokens;
			_sessions = sessions;
			_registrations = registrations;
			_lessons = lessons;
			_versions = versions;
			_courses = courses;
			_learner = learner;
			_xapi = xapi;
			_encryption = encryption;
			_departments = departments;
			_profiles = profiles;
			_transport = transport;
			_eventAggregator = eventAggregator;
		}

		/// <summary>The clock (a seam for tests).</summary>
		public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

		/// <summary>Forgets the cached lookups (tests).</summary>
		public static void ClearCache() => ToolKeySets.Clear();

		/// <summary>
		/// Managers administer tools with the add-on, or (round 10 A3) while a sponsorship pays for course set copies: those copies' LTI lessons
		/// open only once the department registers its own tool, so a sponsored department must be able to finish that registration.
		/// </summary>
		private static bool MayAdminister(LmsActor actor) => actor != null && actor.MayManage &&
			(actor.Gate?.CanAuthor == true || (actor.Gate?.FlagOn == true && actor.Gate.SponsoredCourses != null && actor.Gate.SponsoredCourses.Values.Any(v => !v.HasValue)));

		#region Tools and keys

		public async Task<List<LmsLtiTool>> GetToolsAsync(int departmentId, bool includeDeleted = false) =>
			await _tools.GetForDepartmentAsync(departmentId, includeDeleted) ?? new List<LmsLtiTool>();

		public Task<LmsLtiTool> GetToolAsync(int departmentId, int lmsLtiToolId) => _tools.GetByIdForDepartmentAsync(departmentId, lmsLtiToolId);

		/// <summary>The input's problems (none when it can be saved): https endpoints, redirect URIs, a usable key set source.</summary>
		internal static bool IsValid(LmsLtiToolInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > LmsLtiTool.NameMaxLength)
				return false;
			if (!LmsStandardsUrls.IsAcceptableHttpsUrl(input.OidcLoginUrl) || !LmsStandardsUrls.IsAcceptableHttpsUrl(input.LaunchUrl))
				return false;
			var redirects = (input.RedirectUris ?? string.Empty).Replace("\r", string.Empty).Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
			if (redirects.Count > 20 || redirects.Any(u => !LmsStandardsUrls.IsAcceptableHttpsUrl(u)))
				return false;
			var hasUrl = !string.IsNullOrWhiteSpace(input.JwksUrl);
			if (hasUrl && !LmsStandardsUrls.IsAcceptableHttpsUrl(input.JwksUrl))
				return false;
			var hasPasted = !string.IsNullOrWhiteSpace(input.PublicJwksJson);
			if (hasPasted && (input.PublicJwksJson.Length > 20000 || LmsLtiCrypto.SigningKeys(input.PublicJwksJson).Count == 0))
				return false;
			if (!hasUrl && !hasPasted)
				return false;
			return (input.CustomParameters?.Length ?? 0) <= 4000;
		}

		public async Task<LmsResult> SaveToolAsync(LmsActor actor, LmsLtiToolInput input, CancellationToken cancellationToken = default)
		{
			if (!MayAdminister(actor))
				return LmsResult.Fail(actor?.MayManage == true ? LmsErrors.AddonRequired : LmsErrors.NotAuthorized);
			if (!IsValid(input))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			var now = Clock();
			LmsLtiTool tool;
			if (input.LmsLtiToolId > 0)
			{
				tool = await _tools.GetByIdForDepartmentAsync(actor.DepartmentId, input.LmsLtiToolId);
				if (tool == null || tool.IsDeleted)
					return LmsResult.Fail(LmsErrors.NotFound);
			}
			else
			{
				tool = new LmsLtiTool
				{
					DepartmentId = actor.DepartmentId,
					ClientId = Guid.NewGuid().ToString(),
					DeploymentId = Guid.NewGuid().ToString("N"),
					CreatedOn = now,
					CreatedByUserId = actor.UserId
				};
			}

			tool.Name = input.Name.Trim();
			tool.OidcLoginUrl = input.OidcLoginUrl.Trim();
			tool.LaunchUrl = input.LaunchUrl.Trim();
			tool.RedirectUris = string.Join("\n", (input.RedirectUris ?? string.Empty).Replace("\r", string.Empty).Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0));
			tool.JwksUrl = string.IsNullOrWhiteSpace(input.JwksUrl) ? null : input.JwksUrl.Trim();
			tool.PublicJwksJson = string.IsNullOrWhiteSpace(input.PublicJwksJson) ? null : input.PublicJwksJson.Trim();
			tool.CustomParameters = string.IsNullOrWhiteSpace(input.CustomParameters) ? null : input.CustomParameters.Trim();
			tool.AllowGradePassback = input.AllowGradePassback;
			tool.ShareName = input.ShareName;
			tool.ShareEmail = input.ShareEmail;
			tool.IsActive = input.IsActive;
			tool.UpdatedOn = now;
			tool.UpdatedByUserId = actor.UserId;
			if (tool.LmsLtiToolId > 0)
				await _tools.UpdateAsync(tool, cancellationToken, true);
			else
				await _tools.InsertAsync(tool, cancellationToken, true);
			if (tool.JwksUrl != null)
				ToolKeySets.TryRemove(tool.JwksUrl, out _);

			// The platform needs a key before any tool can be told where to find it.
			await EnsureKeyAsync(actor.DepartmentId, actor.UserId, cancellationToken);
			Audit(actor, AuditLogTypes.LmsLtiToolChanged, new { tool.LmsLtiToolId, tool.Name, tool.ClientId, tool.IsActive, tool.AllowGradePassback, tool.ShareName, tool.ShareEmail });
			return new LmsResult { Value = tool };
		}

		public async Task<LmsResult> DeleteToolAsync(LmsActor actor, int lmsLtiToolId, CancellationToken cancellationToken = default)
		{
			if (!MayAdminister(actor))
				return LmsResult.Fail(actor?.MayManage == true ? LmsErrors.AddonRequired : LmsErrors.NotAuthorized);
			var tool = await _tools.GetByIdForDepartmentAsync(actor.DepartmentId, lmsLtiToolId);
			if (tool == null || tool.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);
			tool.IsDeleted = true;
			tool.IsActive = false;
			tool.UpdatedOn = Clock();
			tool.UpdatedByUserId = actor.UserId;
			await _tools.UpdateAsync(tool, cancellationToken, true);
			Audit(actor, AuditLogTypes.LmsLtiToolChanged, new { tool.LmsLtiToolId, tool.Name, Deleted = true });
			return new LmsResult { Value = tool };
		}

		public async Task<LmsLtiPlatformDetails> GetPlatformDetailsAsync(int departmentId)
		{
			var active = (await _keys.GetForDepartmentAsync(departmentId) ?? new List<LmsLtiPlatformKey>()).FirstOrDefault(k => k.Status == (int)LmsLtiKeyStatuses.Active);
			return new LmsLtiPlatformDetails
			{
				Issuer = LmsLtiRules.Issuer(departmentId),
				JwksUrl = LmsLtiRules.JwksUrl(departmentId),
				AuthorizationUrl = LmsLtiRules.AuthorizationUrl,
				TokenUrl = LmsLtiRules.TokenUrl(departmentId),
				ActiveKid = active?.Kid
			};
		}

		public async Task<LmsResult> RotateKeyAsync(LmsActor actor, CancellationToken cancellationToken = default)
		{
			if (!MayAdminister(actor))
				return LmsResult.Fail(actor?.MayManage == true ? LmsErrors.AddonRequired : LmsErrors.NotAuthorized);
			var key = await CreateKeyAsync(actor.DepartmentId, actor.UserId, cancellationToken);
			if (key == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			await _keys.RetireOthersAsync(actor.DepartmentId, key.LmsLtiPlatformKeyId, Clock(), cancellationToken);
			Audit(actor, AuditLogTypes.LmsLtiPlatformKeyRotated, new { key.Kid });
			return new LmsResult { Value = key };
		}

		private async Task<LmsLtiPlatformKey> EnsureKeyAsync(int departmentId, string userId, CancellationToken cancellationToken)
		{
			var active = (await _keys.GetForDepartmentAsync(departmentId) ?? new List<LmsLtiPlatformKey>()).FirstOrDefault(k => k.Status == (int)LmsLtiKeyStatuses.Active);
			return active ?? await CreateKeyAsync(departmentId, userId, cancellationToken);
		}

		private async Task<LmsLtiPlatformKey> CreateKeyAsync(int departmentId, string userId, CancellationToken cancellationToken)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return null;
			var (kid, jwk, privateKey) = LmsLtiCrypto.NewKey();
			var key = new LmsLtiPlatformKey
			{
				DepartmentId = departmentId,
				Kid = kid,
				PublicJwkJson = jwk,
				PrivateKeyEncrypted = _encryption.EncryptForDepartment(privateKey, departmentId, department.Code),
				Status = (int)LmsLtiKeyStatuses.Active,
				CreatedOn = Clock(),
				CreatedByUserId = userId
			};
			await _keys.InsertAsync(key, cancellationToken, true);
			return key;
		}

		public async Task<string> GetKeySetJsonAsync(int departmentId, DateTime utcNow)
		{
			var keys = new JArray();
			foreach (var key in await _keys.GetForDepartmentAsync(departmentId) ?? new List<LmsLtiPlatformKey>())
			{
				var published = key.Status == (int)LmsLtiKeyStatuses.Active ||
					(key.Status == (int)LmsLtiKeyStatuses.Retired && key.RetiredOn.HasValue && key.RetiredOn.Value > utcNow - LmsLtiRules.RetiredKeyPublishedFor);
				var jwk = published ? LmsStandardsJson.ParseObject(key.PublicJwkJson) : null;
				if (jwk != null)
					keys.Add(jwk);
			}
			return new JObject { ["keys"] = keys }.ToString(Formatting.None);
		}

		private void Audit(LmsActor actor, AuditLogTypes type, object after) =>
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = type,
				After = JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});

		#endregion Tools and keys

		#region Launch

		public Task<LmsLtiLaunchStep> BeginLaunchAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, CancellationToken cancellationToken = default) =>
			BeginCoreAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId, null, cancellationToken);

		public Task<LmsLtiLaunchStep> BeginAppLaunchAsync(LmsRuntimeSession appSession, CancellationToken cancellationToken = default)
		{
			// Only a live app session, and only its own member, enrollment and lesson.
			if (appSession == null || appSession.Kind != (int)LmsRuntimeKinds.AppSession || appSession.ExpiresOn <= Clock() || string.IsNullOrWhiteSpace(appSession.UserId) ||
				appSession.LmsRuntimeSessionId <= 0)
				return Task.FromResult(new LmsLtiLaunchStep { Error = LmsErrors.CodeExpired });
			return BeginCoreAsync(appSession.DepartmentId, appSession.UserId, appSession.LmsEnrollmentId, appSession.LmsLessonId, appSession.LmsRuntimeSessionId, cancellationToken);
		}

		private async Task<LmsLtiLaunchStep> BeginCoreAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, int? appSessionId, CancellationToken cancellationToken)
		{
			// The learner service decides whether this lesson may be opened now (gate, lapse rules, lesson order): fail closed on anything else.
			var player = await _learner.GetPlayerAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId);
			if (player == null || player.Error != LmsErrors.None)
				return new LmsLtiLaunchStep { Error = player?.Error ?? LmsErrors.NotFound };
			var lesson = player.Lesson;
			if (lesson == null || lesson.LmsLessonId != lmsLessonId)
				return new LmsLtiLaunchStep { Error = LmsErrors.LessonLocked };
			if (lesson.Kind != (int)LmsLessonKinds.LtiLink || !lesson.LmsLtiToolId.HasValue)
				return new LmsLtiLaunchStep { Error = LmsErrors.NotFound };

			var tool = await _tools.GetByIdForDepartmentAsync(departmentId, lesson.LmsLtiToolId.Value);
			if (tool == null || tool.IsDeleted || !tool.IsActive || !LmsStandardsUrls.IsAcceptableHttpsUrl(tool.OidcLoginUrl))
				return new LmsLtiLaunchStep { Error = LmsErrors.MediaUnavailable };
			if (await EnsureKeyAsync(departmentId, null, cancellationToken) == null)
				return new LmsLtiLaunchStep { Error = LmsErrors.MediaUnavailable };

			var now = Clock();
			var subject = LmsLearnerIdentity.Subject(departmentId, player.Enrollment.UserId);
			var registration = await _registrations.GetAsync(departmentId, player.Enrollment.LmsEnrollmentId, lesson.LmsLessonId);
			if (registration == null)
				(registration, _) = await _registrations.InsertOrGetAsync(new LmsRuntimeRegistration
				{
					DepartmentId = departmentId,
					LmsEnrollmentId = player.Enrollment.LmsEnrollmentId,
					LmsLessonId = lesson.LmsLessonId,
					UserId = player.Enrollment.UserId,
					Kind = (int)LmsRuntimeKinds.Lti,
					LmsLtiToolId = tool.LmsLtiToolId,
					RegistrationKey = subject,
					CompletionStatus = "not attempted",
					SuccessStatus = "unknown",
					CreatedOn = now
				}, cancellationToken);

			var hint = LmsLearnerIdentity.NewToken();
			await _sessions.InsertAsync(new LmsRuntimeSession
			{
				DepartmentId = departmentId,
				LmsRuntimeRegistrationId = registration.LmsRuntimeRegistrationId,
				LmsEnrollmentId = registration.LmsEnrollmentId,
				LmsLessonId = registration.LmsLessonId,
				UserId = registration.UserId,
				Kind = (int)LmsRuntimeKinds.Lti,
				ItemIdentifier = tool.ClientId,
				TokenHash = LmsLearnerIdentity.HashToken(hint),
				CreatedOn = now,
				ExpiresOn = now.AddMinutes(Math.Max(1, Config.LmsStandardsConfig.LtiLaunchMinutes)),
				AppSessionId = appSessionId
			}, cancellationToken, true);

			var target = LmsStandardsUrls.IsAcceptableHttpsUrl(lesson.LtiTargetLinkUri) ? lesson.LtiTargetLinkUri.Trim() : tool.LaunchUrl;
			var query = new[]
			{
				("iss", LmsLtiRules.Issuer(departmentId)),
				("login_hint", subject),
				("target_link_uri", target),
				("lti_message_hint", hint),
				("client_id", tool.ClientId),
				("lti_deployment_id", tool.DeploymentId)
			};
			var login = tool.OidcLoginUrl.Trim();
			var url = login + (login.Contains('?') ? "&" : "?") + string.Join("&", query.Select(q => q.Item1 + "=" + Uri.EscapeDataString(q.Item2 ?? string.Empty)));
			return new LmsLtiLaunchStep { Url = url };
		}

		private static string Value(IDictionary<string, string> request, string key) => request != null && request.TryGetValue(key, out var value) ? value?.Trim() : null;

		public async Task<LmsLtiLaunchStep> AuthorizeAsync(int departmentId, string signedInUserId, IDictionary<string, string> request, string locale, CancellationToken cancellationToken = default)
		{
			LmsLtiLaunchStep Refuse(LmsErrors error = LmsErrors.InvalidInput) => new LmsLtiLaunchStep { Error = error };

			var scopes = (Value(request, "scope") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (!scopes.Contains("openid") || Value(request, "response_type") != "id_token" || Value(request, "response_mode") != "form_post")
				return Refuse();
			var nonce = Value(request, "nonce");
			var hint = Value(request, "lti_message_hint");
			if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > 500 || string.IsNullOrWhiteSpace(hint) || hint.Length > 100)
				return Refuse();
			var prompt = Value(request, "prompt");
			if (prompt != null && prompt != "none")
				return Refuse();

			var now = Clock();
			var session = await _sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken(hint));
			if (session == null || session.Kind != (int)LmsRuntimeKinds.Lti || session.ExpiresOn <= now || session.UsedOn.HasValue)
				return Refuse(LmsErrors.CodeExpired);
			// Security review (2026-11-25, LTI SameSite): the member cookie is SameSite=Strict, so the tool's cross-site redirect arrives without it.
			// The hint itself authenticates the request: 256 random bits (only its hash is stored), single use, minutes long, minted for this
			// department, member, tool and lesson. A member session that does come with the request must be that member, in that department.
			if ((!string.IsNullOrWhiteSpace(signedInUserId) && !string.Equals(session.UserId, signedInUserId, StringComparison.OrdinalIgnoreCase)) ||
				(departmentId > 0 && session.DepartmentId != departmentId) || string.IsNullOrWhiteSpace(session.UserId))
				return Refuse(LmsErrors.NotAuthorized);

			var registration = await _registrations.GetByIdForDepartmentAsync(session.DepartmentId, session.LmsRuntimeRegistrationId);
			if (registration == null || !registration.LmsLtiToolId.HasValue || !string.Equals(Value(request, "login_hint"), registration.RegistrationKey, StringComparison.Ordinal))
				return Refuse(LmsErrors.NotAuthorized);
			var tool = await _tools.GetByIdForDepartmentAsync(session.DepartmentId, registration.LmsLtiToolId.Value);
			if (tool == null || tool.IsDeleted || !tool.IsActive || !string.Equals(Value(request, "client_id"), tool.ClientId, StringComparison.Ordinal) ||
				!string.Equals(session.ItemIdentifier, tool.ClientId, StringComparison.Ordinal))
				return Refuse(LmsErrors.NotAuthorized);
			var redirect = Value(request, "redirect_uri");
			if (redirect == null || !LmsLtiRules.RedirectUris(tool).Contains(redirect, StringComparer.Ordinal))
				return Refuse(LmsErrors.NotAuthorized);

			var lesson = await _lessons.GetVisibleByIdAsync(session.DepartmentId, session.LmsLessonId);
			if (lesson == null || lesson.DepartmentId != session.DepartmentId || lesson.Kind != (int)LmsLessonKinds.LtiLink || registration.LmsLessonId != session.LmsLessonId)
				return Refuse(LmsErrors.NotFound);
			var version = await _versions.GetVisibleByIdAsync(session.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(session.DepartmentId, version.LmsCourseId);

			var key = (await _keys.GetForDepartmentAsync(session.DepartmentId) ?? new List<LmsLtiPlatformKey>()).FirstOrDefault(k => k.Status == (int)LmsLtiKeyStatuses.Active);
			var department = key == null ? null : await _departments.GetDepartmentByIdAsync(session.DepartmentId, false);
			if (key == null || department == null)
				return Refuse(LmsErrors.MediaUnavailable);

			// Single use: a second request with the same hint is refused even if the first one did not finish.
			if (!await _sessions.TryMarkUsedAsync(session.LmsRuntimeSessionId, now, cancellationToken))
				return Refuse(LmsErrors.CodeExpired);

			string name = null, given = null, family = null, email = null;
			if (tool.ShareName || tool.ShareEmail)
			{
				var profile = await _profiles.GetProfileByUserIdAsync(session.UserId);
				given = profile?.FirstName?.Trim();
				family = profile?.LastName?.Trim();
				name = string.Join(" ", new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));
				email = profile?.MembershipEmail?.Trim();
			}

			// Round 10 A8: a launch the app's page started gets a signed return URL (that lesson's state only) for the tool to send the learner back to.
			var returnUrl = session.AppSessionId.HasValue ? LmsAppLaunchRules.ReturnUrl(session.DepartmentId, session.AppSessionId.Value, now.Add(LmsAppLaunchRules.SessionLifetime)) : null;
			var claims = LmsLtiRules.LaunchClaims(session.DepartmentId, tool, lesson, course != null ? new LmsCourse { LmsCourseId = course.LmsCourseId, Title = version.Title ?? course.Title } : null,
				registration.RegistrationKey, nonce, locale, name, given, family, email, returnUrl);
			string idToken;
			try
			{
				var privateKey = _encryption.DecryptForDepartment(key.PrivateKeyEncrypted, session.DepartmentId, department.Code);
				idToken = LmsLtiCrypto.Sign(privateKey, key.Kid, LmsLtiRules.Issuer(session.DepartmentId), tool.ClientId, claims, now,
					TimeSpan.FromMinutes(Math.Max(1, Config.LmsStandardsConfig.LtiIdTokenMinutes)));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An LTI id_token could not be signed.");
				return Refuse(LmsErrors.MediaUnavailable);
			}

			// Without grade passback the tool never reports back: opening it is what completes the lesson (like a document lesson).
			if (!tool.AllowGradePassback)
				await SatisfyAsync(registration, null, session.UserId, lesson, course, cancellationToken);

			var step = new LmsLtiLaunchStep { Url = redirect };
			step.Form["id_token"] = idToken;
			var state = Value(request, "state");
			if (!string.IsNullOrEmpty(state))
				step.Form["state"] = state.Length > 2000 ? state.Substring(0, 2000) : state;
			return step;
		}

		#endregion Launch

		#region Assignment and Grade Services

		public async Task<LmsLtiTokenResult> IssueTokenAsync(int departmentId, IDictionary<string, string> form, CancellationToken cancellationToken = default)
		{
			LmsLtiTokenResult Fail(string error) => new LmsLtiTokenResult { OAuthError = error };

			if (Value(form, "grant_type") != "client_credentials")
				return Fail("unsupported_grant_type");
			if (Value(form, "client_assertion_type") != LmsLtiRules.ClientAssertionType)
				return Fail("invalid_request");
			var assertion = Value(form, "client_assertion");
			if (string.IsNullOrWhiteSpace(assertion) || assertion.Length > 16000)
				return Fail("invalid_request");

			var clientId = LmsLtiCrypto.UnverifiedIssuer(assertion);
			var tool = string.IsNullOrWhiteSpace(clientId) ? null : await _tools.GetByClientIdAsync(departmentId, clientId);
			if (tool == null || tool.IsDeleted || !tool.IsActive || !tool.AllowGradePassback)
				return Fail("invalid_client");

			var keys = LmsLtiCrypto.SigningKeys(tool.PublicJwksJson ?? await FetchToolKeySetAsync(tool.JwksUrl, cancellationToken));
			var now = Clock();
			var jti = await LmsLtiCrypto.ValidateClientAssertionAsync(assertion, tool.ClientId,
				new[] { LmsLtiRules.TokenUrl(departmentId), LmsLtiRules.Issuer(departmentId) }, keys, now);
			if (jti == null)
				return Fail("invalid_client");

			var allowed = new[] { LmsLtiRules.ScopeScore, LmsLtiRules.ScopeLineItemReadOnly };
			var granted = (Value(form, "scope") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(s => allowed.Contains(s, StringComparer.Ordinal)).Distinct().ToList();
			if (granted.Count == 0)
				return Fail("invalid_scope");

			var token = LmsLearnerIdentity.NewToken();
			var lifetime = Math.Max(5, Config.LmsStandardsConfig.LtiAccessTokenMinutes);
			if (!await _tokens.TryInsertAsync(new LmsLtiAccessToken
			{
				DepartmentId = departmentId,
				LmsLtiToolId = tool.LmsLtiToolId,
				TokenHash = LmsLearnerIdentity.HashToken(token),
				Scopes = string.Join(" ", granted),
				AssertionJti = jti,
				CreatedOn = now,
				ExpiresOn = now.AddMinutes(lifetime)
			}, cancellationToken))
				return Fail("invalid_grant");                                                   // the assertion was replayed

			return new LmsLtiTokenResult { AccessToken = token, Scope = string.Join(" ", granted), ExpiresIn = lifetime * 60 };
		}

		/// <summary>The tool's key set from its JWKS URL (outbound https through the guarded transport), cached for ten minutes.</summary>
		private async Task<string> FetchToolKeySetAsync(string url, CancellationToken cancellationToken)
		{
			if (!LmsStandardsUrls.IsAcceptableHttpsUrl(url))
				return null;
			var now = Clock();
			if (ToolKeySets.TryGetValue(url, out var cached) && cached.Until > now)
				return cached.Json;
			var response = await _transport.GetAsync(url, cancellationToken);
			if (response == null || response.StatusCode != 200 || string.IsNullOrWhiteSpace(response.Body))
				return null;
			if (ToolKeySets.Count > 1000)
				ToolKeySets.Clear();
			ToolKeySets[url] = (response.Body, now.Add(KeySetCacheFor));
			return response.Body;
		}

		/// <summary>The bearer token's row when it is live, the department's, and holds one of <paramref name="scopes"/>.</summary>
		private async Task<LmsLtiAccessToken> AuthorizeBearerAsync(int departmentId, string bearer, params string[] scopes)
		{
			if (string.IsNullOrWhiteSpace(bearer) || bearer.Length > 100)
				return null;
			var token = await _tokens.GetByHashAsync(departmentId, LmsLearnerIdentity.HashToken(bearer.Trim()));
			if (token == null || token.DepartmentId != departmentId || token.ExpiresOn <= Clock())
				return null;
			var held = (token.Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			return scopes.Any(s => held.Contains(s, StringComparer.Ordinal)) ? token : null;
		}

		private async Task<LmsLesson> LessonForTokenAsync(int departmentId, int lmsLessonId, LmsLtiAccessToken token)
		{
			var lesson = await _lessons.GetVisibleByIdAsync(departmentId, lmsLessonId);
			return lesson != null && lesson.DepartmentId == departmentId && lesson.Kind == (int)LmsLessonKinds.LtiLink && lesson.LmsLtiToolId == token.LmsLtiToolId ? lesson : null;
		}

		public async Task<LmsRuntimeOutcome> GetLineItemAsync(int departmentId, int lmsLessonId, string bearerToken)
		{
			var token = await AuthorizeBearerAsync(departmentId, bearerToken, LmsLtiRules.ScopeLineItemReadOnly, LmsLtiRules.ScopeScore);
			if (token == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			var lesson = await LessonForTokenAsync(departmentId, lmsLessonId, token);
			if (lesson == null)
				return LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound);
			return new LmsRuntimeOutcome
			{
				Value = new JObject
				{
					["id"] = LmsLtiRules.LineItemUrl(departmentId, lesson.LmsLessonId),
					["scoreMaximum"] = 100,
					["label"] = lesson.Title ?? string.Empty,
					["resourceLinkId"] = "lesson-" + lesson.LmsLessonId.ToString(System.Globalization.CultureInfo.InvariantCulture)
				}.ToString(Formatting.None)
			};
		}

		public async Task<LmsRuntimeOutcome> PostScoreAsync(int departmentId, int lmsLessonId, string bearerToken, string body, CancellationToken cancellationToken = default)
		{
			var token = await AuthorizeBearerAsync(departmentId, bearerToken, LmsLtiRules.ScopeScore);
			if (token == null)
				return LmsRuntimeOutcome.Refuse(401, LmsErrors.NotAuthorized);
			var lesson = await LessonForTokenAsync(departmentId, lmsLessonId, token);
			if (lesson == null)
				return LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound);
			if (string.IsNullOrWhiteSpace(body) || body.Length > 20000)
				return LmsRuntimeOutcome.Refuse(400);
			var (subject, score) = LmsLtiRules.ParseScore(LmsStandardsJson.ParseObject(body));
			if (subject == null)
				return LmsRuntimeOutcome.Refuse(400);

			var registration = await _registrations.GetForLessonSubjectAsync(departmentId, lmsLessonId, subject);
			if (registration == null || registration.LmsLtiToolId != token.LmsLtiToolId)
				return LmsRuntimeOutcome.Refuse(404, LmsErrors.NotFound);

			var now = Clock();
			var wasSatisfied = registration.Satisfied;
			var applied = false;
			decimal? percent = null;
			for (var attempt = 0; attempt < 3 && registration != null; attempt++)
			{
				// AGS: a score older than the one on record never replaces it.
				if (score.Timestamp.HasValue && registration.LastResultOn.HasValue && score.Timestamp.Value <= registration.LastResultOn.Value)
					return new LmsRuntimeOutcome { Status = 204, Registration = registration };

				var expected = registration.RowVersion;
				var (satisfied, scorePercent) = LmsLtiRules.Evaluate(score, lesson.PassMark);
				percent = scorePercent;
				registration.StateJson = score.ToJson();
				registration.ScorePercent = scorePercent ?? registration.ScorePercent;
				registration.CompletionStatus = score.ActivityProgress == "Completed" ? "completed" : "incomplete";
				if (score.GradingProgress == "FullyGraded")
					registration.SuccessStatus = satisfied ? "passed" : (lesson.PassMark > 0 && scorePercent.HasValue ? "failed" : registration.SuccessStatus);
				if (satisfied && !registration.Satisfied)
				{
					registration.Satisfied = true;
					registration.SatisfiedOn = now;
				}
				registration.LastResultOn = score.Timestamp ?? now;
				registration.UpdatedOn = now;
				if (await _registrations.TryUpdateAsync(registration, expected, cancellationToken))
				{
					applied = true;
					break;
				}
				registration = await _registrations.GetByIdForDepartmentAsync(departmentId, registration.LmsRuntimeRegistrationId);
			}
			if (!applied)
				return LmsRuntimeOutcome.Refuse(409);

			var outcome = new LmsRuntimeOutcome { Status = 204, Registration = registration };
			if (registration.Satisfied && !wasSatisfied)
			{
				var version = await _versions.GetVisibleByIdAsync(departmentId, lesson.LmsCourseVersionId);
				var course = version == null ? null : await _courses.GetVisibleByIdAsync(departmentId, version.LmsCourseId);
				var result = await SatisfyAsync(registration, percent, registration.UserId, lesson, course, cancellationToken);
				outcome.LessonSatisfied = result?.Succeeded == true;
				outcome.CourseCompleted = result?.Succeeded == true && result.Count > 0;
			}
			else if (score.GradingProgress == "FullyGraded" && registration.SuccessStatus == "failed")
			{
				await QueueXapiAsync(registration, lesson, null, LmsXapiVerbs.Failed, false, percent, cancellationToken);
			}
			return outcome;
		}

		/// <summary>The lesson is satisfied: the learner service completes it (gate and lapse rules apply), and xAPI hears of it.</summary>
		private async Task<LmsResult> SatisfyAsync(LmsRuntimeRegistration registration, decimal? percent, string userId, LmsLesson lesson, LmsCourse course, CancellationToken cancellationToken)
		{
			LmsResult result = null;
			try
			{
				if (!registration.Satisfied)
				{
					for (var attempt = 0; attempt < 3 && registration != null; attempt++)
					{
						var expected = registration.RowVersion;
						registration.Satisfied = true;
						registration.SatisfiedOn = Clock();
						registration.CompletionStatus = "completed";
						registration.UpdatedOn = Clock();
						if (await _registrations.TryUpdateAsync(registration, expected, cancellationToken))
							break;
						registration = await _registrations.GetByIdForDepartmentAsync(lesson.DepartmentId, registration.LmsRuntimeRegistrationId);
					}
				}
				if (registration == null)
					return null;
				result = await _learner.RecordRuntimeOutcomeAsync(registration.DepartmentId, userId, registration.LmsEnrollmentId, registration.LmsLessonId, true, registration.TotalSeconds,
					cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The LTI outcome of registration {registration?.LmsRuntimeRegistrationId} could not be recorded.");
			}
			if (result?.Succeeded == true)
				await QueueXapiAsync(registration, lesson, course, LmsXapiVerbs.Completed, percent.HasValue && lesson.PassMark > 0 ? true : (bool?)null, percent, cancellationToken);
			return result;
		}

		private async Task QueueXapiAsync(LmsRuntimeRegistration registration, LmsLesson lesson, LmsCourse course, string verb, bool? success, decimal? percent, CancellationToken cancellationToken)
		{
			try
			{
				await _xapi.EnqueueAsync(new LmsXapiEvent
				{
					DepartmentId = registration.DepartmentId,
					UserId = registration.UserId,
					VerbId = verb,
					LmsCourseId = course?.LmsCourseId ?? 0,
					CourseTitle = course?.Title,
					LmsLessonId = lesson.LmsLessonId,
					LessonTitle = lesson.Title,
					Completion = verb == LmsXapiVerbs.Completed ? true : (bool?)null,
					Success = success,
					ScorePercent = percent,
					OccurredOn = Clock()
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An LTI xAPI statement could not be queued.");
			}
		}

		public Task<int> PurgeExpiredTokensAsync(DateTime utcNow, CancellationToken cancellationToken = default) => _tokens.DeleteExpiredAsync(utcNow.AddDays(-1), cancellationToken);

		#endregion Assignment and Grade Services
	}
}
