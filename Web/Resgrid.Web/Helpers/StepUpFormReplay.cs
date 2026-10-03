using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// What someone submitted when a verification gate stopped them (a 2FA step-up from
	/// <see cref="Attributes.RequiresRecentTwoFactorAttribute"/>, or a password re-confirmation), held so that verifying finishes
	/// the save instead of discarding it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The flow: the gate holds the submission (encrypted, in the shared cache, for <see cref="Lifetime"/>) and sends the user to
	/// verify with a return address on <see cref="ResumePath"/>. Every way of verifying (code, passkey, Responder approval,
	/// provider step-up, password or SSO re-confirmation) already ends by redirecting to that address. The resume page posts back
	/// to the original action carrying only <see cref="FieldName"/> and a fresh antiforgery token; the global
	/// <see cref="Filters.HeldSubmissionReplayFilter"/>, running before model binding, swaps the held fields and files in as the
	/// request's form. The action then runs exactly as if the first post had been allowed, and its gate checks the new evidence
	/// as it always does: replay never skips a verification.
	/// </para>
	/// <para>
	/// Nothing held reaches the browser again, so passwords and secrets in the form are never written into a page. A held
	/// submission replays once, only for the user, session and active department that made it, and only to the same address.
	/// </para>
	/// </remarks>
	public static class StepUpFormReplay
	{
		/// <summary>The form field the resume page posts; it names the held submission.</summary>
		public const string FieldName = "__stepUpReplay";

		/// <summary>Where verification returns to when a submission is being held.</summary>
		public const string ResumePath = "/User/StepUpResume";

		/// <summary>
		/// The largest post the replay filter will open to look for <see cref="FieldName"/>. The resume page's post is an id and a
		/// token, url-encoded; anything bigger, or an upload, is never a replay and is left unread.
		/// </summary>
		public const long MaxReplayPostBytes = 4096;

		/// <summary>How long a held submission waits for its verification.</summary>
		public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

		/// <summary>Submissions larger than these are not held; the guard falls back to sending the user back to resubmit.</summary>
		public const int MaxFieldCharacters = 1_000_000;
		public const long MaxFileBytes = 10L * 1024 * 1024;

		internal const string HttpItemKey = "Resgrid.StepUpFormReplay";
		private const string AntiforgeryFieldName = "__RequestVerificationToken";
		private const string CacheKeyFormat = "StepUpReplay_{0}";
		private const string ClaimKeyFormat = "StepUpReplayClaim_{0}";
		private const string ProtectorPurpose = "Resgrid.Web.StepUpFormReplay.v1";

		/// <summary>
		/// Whether this request is a script call (jQuery sets the header) rather than a page navigation: a script cannot follow a
		/// redirect to Verify2FA, so it is told where to send the user instead.
		/// </summary>
		public static bool IsScriptRequest(HttpRequest request) =>
			string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Whether the request's body is something the replay can carry: a form, or provably nothing (a script call whose arguments
		/// are all in the query). A body of any other kind (JSON, or an unsized stream) is never held as "empty", since replaying
		/// it as a blank form would run the action on blanks.
		/// </summary>
		public static bool CanHold(HttpRequest request) =>
			!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
			(request.HasFormContentType || request.ContentLength == 0 ||
			 (request.ContentLength == null && string.IsNullOrEmpty(request.ContentType) && !request.Headers.ContainsKey("Transfer-Encoding")));

		/// <summary>
		/// For a gate about to send the user away to verify: where verification should return to, and whether what they submitted
		/// was lost. A page request returns to <paramref name="back"/> as it always has; a submission is held and returns through
		/// the resume page; a submission that cannot be held returns to <paramref name="back"/> with <c>SubmissionLost</c> set so
		/// the verify page can say to submit again.
		/// </summary>
		public static async Task<(string ReturnUrl, bool SubmissionLost)> HoldForVerificationAsync(HttpContext httpContext, ICacheProvider cache,
			IDataProtectionProvider protection, string userId, string back)
		{
			var request = httpContext.Request;
			if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
				return (back, false);

			var heldId = await HoldAsync(httpContext, cache, protection, userId, back);
			return heldId != null ? (ResumeUrl(request.PathBase, heldId, back), false) : (back, true);
		}

		/// <summary>
		/// Holds this request's submission; returns its id, or null when it cannot be held (too large, a body that is not a form,
		/// or the cache is unavailable) and the caller must fall back to asking the user to submit again.
		/// </summary>
		public static async Task<string> HoldAsync(HttpContext httpContext, ICacheProvider cache, IDataProtectionProvider protection,
			string userId, string back)
		{
			var request = httpContext.Request;
			if (cache == null || protection == null || string.IsNullOrWhiteSpace(userId) || !CanHold(request))
				return null;

			var state = new StepUpFormReplayState
			{
				UserId = userId,
				// Bound to exactly what the replay filter checks, read the same way.
				SessionKey = MfaEvidenceSession.KeyFor(httpContext.User, httpContext),
				DepartmentId = ActiveDepartmentOf(httpContext.User),
				Target = TargetOf(request),
				// A replayed submission that needs verifying again keeps the page the user was really on.
				Back = (httpContext.Items[HttpItemKey] as StepUpFormReplayState)?.Back ?? back,
				Script = IsScriptRequest(request),
				CreatedOnUtc = DateTime.UtcNow
			};

			if (request.HasFormContentType)
			{
				var form = await request.ReadFormAsync(httpContext.RequestAborted);

				long characters = 0;
				foreach (var field in form)
				{
					if (string.Equals(field.Key, AntiforgeryFieldName, StringComparison.Ordinal) || string.Equals(field.Key, FieldName, StringComparison.Ordinal))
						continue;

					var values = field.Value.ToArray();
					characters += field.Key.Length + values.Sum(v => v?.Length ?? 0);
					if (characters > MaxFieldCharacters)
						return null;

					state.Fields.Add(new StepUpFormReplayField { Name = field.Key, Values = values });
				}

				long fileBytes = 0;
				foreach (var file in form.Files)
				{
					fileBytes += file.Length;
					if (fileBytes > MaxFileBytes)
						return null;

					using var buffer = new MemoryStream();
					await using (var stream = file.OpenReadStream())
						await stream.CopyToAsync(buffer, httpContext.RequestAborted);

					state.Files.Add(new StepUpFormReplayFile
					{
						Name = file.Name,
						FileName = file.FileName,
						ContentType = file.ContentType,
						ContentDisposition = file.ContentDisposition,
						Content = buffer.ToArray()
					});
				}
			}

			var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
			var payload = protection.CreateProtector(ProtectorPurpose).Protect(JsonSerializer.Serialize(state));

			try
			{
				return await cache.SetStringAsync(string.Format(CacheKeyFormat, id), payload, Lifetime) ? id : null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "A submission could not be held for step-up; the user will be asked to submit again.");
				return null;
			}
		}

		/// <summary>The held submission, still held (the resume page reads it to know where to post); null when gone or tampered with.</summary>
		public static async Task<StepUpFormReplayState> PeekAsync(ICacheProvider cache, IDataProtectionProvider protection, string id)
		{
			if (cache == null || protection == null || !IsWellFormedId(id))
				return null;

			string payload;
			try
			{
				payload = await cache.GetStringAsync(string.Format(CacheKeyFormat, id));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "A held step-up submission could not be read.");
				return null;
			}

			if (string.IsNullOrWhiteSpace(payload))
				return null;

			try
			{
				var state = JsonSerializer.Deserialize<StepUpFormReplayState>(protection.CreateProtector(ProtectorPurpose).Unprotect(payload));
				return state != null && DateTime.UtcNow - state.CreatedOnUtc <= Lifetime ? state : null;
			}
			catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
			{
				return null;
			}
		}

		/// <summary>
		/// Takes the held submission for its one replay. The claim is an atomic counter, so a double-clicked Continue or two tabs
		/// cannot both replay it; a cache that cannot count refuses rather than risk a second save.
		/// </summary>
		public static async Task<StepUpFormReplayState> ClaimAsync(ICacheProvider cache, IDataProtectionProvider protection, string id)
		{
			var state = await PeekAsync(cache, protection, id);
			if (state == null)
				return null;

			try
			{
				if (await cache.IncrementAsync(string.Format(ClaimKeyFormat, id), Lifetime) != 1)
					return null;

				await cache.RemoveAsync(string.Format(CacheKeyFormat, id));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "A held step-up submission could not be claimed.");
				return null;
			}

			return state;
		}

		/// <summary>
		/// Whether a post could be the resume page's replay, decided from headers alone so nothing else has its body opened: a
		/// signed-in user's small url-encoded post. Uploads, large forms, raw bodies and anonymous callers (webhooks) are never
		/// replays.
		/// </summary>
		public static bool MayBeReplay(HttpContext httpContext)
		{
			var request = httpContext.Request;
			return HttpMethods.IsPost(request.Method) &&
				httpContext.User?.Identity?.IsAuthenticated == true &&
				request.ContentLength is > 0 and <= MaxReplayPostBytes &&
				request.ContentType != null &&
				request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Whether the held submission was made by this user, in this session, in this active department.</summary>
		public static bool BelongsTo(StepUpFormReplayState state, string userId, string sessionKey, int? departmentId) =>
			state != null &&
			!string.IsNullOrWhiteSpace(userId) && string.Equals(state.UserId, userId, StringComparison.Ordinal) &&
			string.Equals(state.SessionKey, sessionKey, StringComparison.Ordinal) &&
			state.DepartmentId == departmentId;

		/// <summary>The signed-in user's active department, from the claim the step-up evidence is scoped to.</summary>
		public static int? ActiveDepartmentOf(ClaimsPrincipal principal) =>
			int.TryParse(principal?.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var department) ? department : null;

		/// <summary>Whether this request is the replay's own post: the address the submission was made to, and nothing else.</summary>
		public static bool IsFor(StepUpFormReplayState state, HttpRequest request) =>
			state != null && string.Equals(state.Target, TargetOf(request), StringComparison.Ordinal);

		/// <summary>The held fields and files as a form, carrying the replay request's own (fresh) antiforgery token.</summary>
		public static IFormCollection Restore(StepUpFormReplayState state, StringValues antiforgeryToken)
		{
			var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
			foreach (var field in state.Fields ?? new List<StepUpFormReplayField>())
				fields[field.Name] = new StringValues(field.Values ?? Array.Empty<string>());

			if (!StringValues.IsNullOrEmpty(antiforgeryToken))
				fields[AntiforgeryFieldName] = antiforgeryToken;

			FormFileCollection files = null;
			if (state.Files?.Count > 0)
			{
				files = new FormFileCollection();
				foreach (var held in state.Files)
				{
					var content = held.Content ?? Array.Empty<byte>();
					files.Add(new FormFile(new MemoryStream(content), 0, content.Length, held.Name, held.FileName)
					{
						Headers = new HeaderDictionary
						{
							["Content-Type"] = held.ContentType ?? "application/octet-stream",
							["Content-Disposition"] = held.ContentDisposition ?? $"form-data; name=\"{held.Name}\"; filename=\"{held.FileName}\""
						}
					});
				}
			}

			return new FormCollection(fields, files);
		}

		/// <summary>Makes <paramref name="form"/> the request's form, so model binding reads the held submission.</summary>
		public static void UseAsRequestForm(HttpContext httpContext, IFormCollection form) =>
			httpContext.Features.Set<IFormFeature>(new FormFeature(form));

		/// <summary>The resume address Verify2FA returns to, carrying the page to fall back to if the hold has expired.</summary>
		public static string ResumeUrl(string pathBase, string id, string back) =>
			$"{pathBase}{ResumePath}?id={Uri.EscapeDataString(id)}" + (string.IsNullOrWhiteSpace(back) ? "" : $"&back={Uri.EscapeDataString(back)}");

		private static string TargetOf(HttpRequest request) => $"{request.PathBase}{request.Path}{request.QueryString}";

		private static bool IsWellFormedId(string id) =>
			!string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
	}

	public sealed class StepUpFormReplayState
	{
		public string UserId { get; set; }
		public string SessionKey { get; set; }
		public int? DepartmentId { get; set; }

		/// <summary>Path and query the submission was made to; the replay posts here and nowhere else.</summary>
		public string Target { get; set; }

		/// <summary>The local page the user submitted from: where a script call's replay returns, and where an expired hold sends them.</summary>
		public string Back { get; set; }

		/// <summary>A script call (no page navigation): the resume page replays it in the background and returns to <see cref="Back"/>.</summary>
		public bool Script { get; set; }

		public DateTime CreatedOnUtc { get; set; }
		public List<StepUpFormReplayField> Fields { get; set; } = new List<StepUpFormReplayField>();
		public List<StepUpFormReplayFile> Files { get; set; } = new List<StepUpFormReplayFile>();
	}

	public sealed class StepUpFormReplayField
	{
		public string Name { get; set; }
		public string[] Values { get; set; }
	}

	public sealed class StepUpFormReplayFile
	{
		public string Name { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public string ContentDisposition { get; set; }
		public byte[] Content { get; set; }
	}
}
