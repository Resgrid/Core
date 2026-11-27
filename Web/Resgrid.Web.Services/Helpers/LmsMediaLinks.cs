using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// Round 10 (Omni): short-lived signed links to a hosted LMS video's playlist and caption tracks for players that cannot send the bearer
	/// token (a native video player loads the playlist URL itself; a web &lt;track&gt; loads its file). The member asks for the links with their
	/// bearer token (Lms/GetLessonPlaylist). A link names the department, the enrollment, the lesson, the kind (and a caption track's locale)
	/// and an expiry, all covered by its HMAC, so it opens only that lesson of that enrollment (whose learner it runs as) until it expires
	/// (<see cref="Lifetime"/>). The signature is checked before anything is read; every use then runs the player's rules again as the
	/// enrollment's learner (module, lapse, lock, seat, hosted-video playability). The key is derived from the external-link passphrase (a
	/// map URL or any other signed value never verifies here); without the passphrase no link is issued or accepted.
	/// </summary>
	public static class LmsMediaLinks
	{
		public const string PlaylistKind = "playlist";
		public const string CaptionsKind = "captions";

		public const string DepartmentParameter = "d";
		public const string EnrollmentParameter = "en";
		public const string LessonParameter = "ls";
		public const string LocaleParameter = "loc";
		public const string ExpiresParameter = "e";
		public const string SignatureParameter = "sig";

		/// <summary>The playlist route ends in .m3u8 so players that pick the format from the path (ExoPlayer) treat it as HLS.</summary>
		public const string PlaylistRoute = "HostedPlaylist.m3u8";
		public const string CaptionsRoute = "HostedCaptions.vtt";

		/// <summary>How long a link opens: the LMS signed-URL lifetime (at least 5 minutes), the same as the segment URLs inside the playlist.</summary>
		public static TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Max(5, Config.LmsConfig.SignedUrlMinutes));

		public static bool IsConfigured => !string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase);

		/// <summary>The absolute signed URL, or null when links cannot be signed (no passphrase) or the input does not name a lesson (or a caption locale).</summary>
		public static string Build(string kind, int departmentId, int enrollmentId, int lessonId, string locale, DateTime expiresUtc)
		{
			if (!IsConfigured || departmentId <= 0 || enrollmentId <= 0 || lessonId <= 0 || !IsKind(kind))
				return null;
			var normalizedLocale = kind == CaptionsKind ? NormalizeLocale(locale) : null;
			if (kind == CaptionsKind && normalizedLocale == null)
				return null;

			var expires = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
			var query = new StringBuilder()
				.Append(DepartmentParameter).Append('=').Append(departmentId.ToString(CultureInfo.InvariantCulture))
				.Append('&').Append(EnrollmentParameter).Append('=').Append(enrollmentId.ToString(CultureInfo.InvariantCulture))
				.Append('&').Append(LessonParameter).Append('=').Append(lessonId.ToString(CultureInfo.InvariantCulture));
			if (normalizedLocale != null)
				query.Append('&').Append(LocaleParameter).Append('=').Append(Uri.EscapeDataString(normalizedLocale));
			query.Append('&').Append(ExpiresParameter).Append('=').Append(expires.ToString(CultureInfo.InvariantCulture))
				.Append('&').Append(SignatureParameter).Append('=').Append(Sign(kind, departmentId, enrollmentId, lessonId, normalizedLocale, expires));

			var route = kind == PlaylistKind ? PlaylistRoute : CaptionsRoute;
			return (Config.SystemBehaviorConfig.ResgridApiBaseUrl ?? string.Empty).TrimEnd('/') + "/api/v4/Lms/" + route + "?" + query;
		}

		/// <summary>
		/// True when the link is well formed, not expired, not further out than this server issues, and its signature matches the kind, the ids
		/// and (captions) the locale; returns them.
		/// </summary>
		public static bool TryValidate(string kind, string departmentText, string enrollmentText, string lessonText, string localeText, string expiresText, string signature,
			DateTime utcNow, out int departmentId, out int enrollmentId, out int lessonId, out string locale)
		{
			departmentId = enrollmentId = lessonId = 0;
			locale = null;
			if (!IsConfigured || !IsKind(kind) || string.IsNullOrWhiteSpace(signature))
				return false;
			if (!int.TryParse(departmentText, NumberStyles.None, CultureInfo.InvariantCulture, out var department) || department <= 0 ||
				!int.TryParse(enrollmentText, NumberStyles.None, CultureInfo.InvariantCulture, out var enrollment) || enrollment <= 0 ||
				!int.TryParse(lessonText, NumberStyles.None, CultureInfo.InvariantCulture, out var lesson) || lesson <= 0 ||
				!long.TryParse(expiresText, NumberStyles.None, CultureInfo.InvariantCulture, out var expires))
				return false;

			var now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
			if (now > expires || expires > now + (long)Lifetime.TotalSeconds + 60)
				return false;

			var normalizedLocale = kind == CaptionsKind ? NormalizeLocale(localeText) : null;
			if (kind == CaptionsKind && normalizedLocale == null)
				return false;

			var expected = Sign(kind, department, enrollment, lesson, normalizedLocale, expires);
			if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim())))
				return false;

			departmentId = department;
			enrollmentId = enrollment;
			lessonId = lesson;
			locale = normalizedLocale;
			return true;
		}

		/// <summary>A caption locale as the media service stores it (ASCII letters, digits and '-', at most 16), lower case; null otherwise.</summary>
		public static string NormalizeLocale(string locale)
		{
			if (string.IsNullOrWhiteSpace(locale))
				return null;
			var value = locale.Trim();
			if (value.Length > 16 || !value.All(c => (c < 128 && char.IsLetterOrDigit(c)) || c == '-'))
				return null;
			return value.ToLowerInvariant();
		}

		private static bool IsKind(string kind) => kind == PlaylistKind || kind == CaptionsKind;

		private static string Sign(string kind, int departmentId, int enrollmentId, int lessonId, string locale, long expires)
		{
			var payload = string.Join("|", "lms-media", kind, departmentId.ToString(CultureInfo.InvariantCulture), enrollmentId.ToString(CultureInfo.InvariantCulture),
				lessonId.ToString(CultureInfo.InvariantCulture), locale ?? string.Empty, expires.ToString(CultureInfo.InvariantCulture));
			using var hmac = new HMACSHA256(Key());
			var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
			return Convert.ToBase64String(hash, 0, 24).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		private static byte[] Key() =>
			// Derived, so an LMS media signature can never be replayed as a map URL or any other value signed with the passphrase.
			SHA256.HashData(Encoding.UTF8.GetBytes("resgrid-lms-media|" + (Config.SystemBehaviorConfig.ExternalLinkUrlParamPassphrase ?? string.Empty)));
	}
}
