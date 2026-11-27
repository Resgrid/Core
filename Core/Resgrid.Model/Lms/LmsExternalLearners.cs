using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Training T5 item 2 (round 6E): external learners (extended enterprise), paid public classes and the branded learner portal. An external
	// learner is a separate identity kind: a row here, never an AspNetUsers account and never a department member. Its "user id" is a synthetic
	// id with the reserved "xl-" prefix, so the LMS runtime (enrolments, progress, attempts, class rosters, learning records) can carry it while
	// nothing member-shaped (personnel, dispatch, messages, the v4 API) can ever resolve it. LMS tables reference baseline ids only (§17.3).
	// Persisted enums are append only.

	#region Enums

	public enum LmsExternalLearnerStatuses
	{
		/// <summary>Self-registered; the e-mail address is not verified yet. Does not count toward fair use.</summary>
		Pending = 0,
		/// <summary>Invited by the department; the invitation is not accepted yet. Counts toward fair use.</summary>
		Invited = 1,
		Active = 2,
		/// <summary>Suspended by the department (the place is freed; enrolments stay).</summary>
		Suspended = 3,
		/// <summary>Removed by the department (open enrolments withdrawn).</summary>
		Removed = 4,
		/// <summary>Erased on request (GDPR): every personal field is gone.</summary>
		Erased = 5
	}

	public enum LmsExternalLearnerSources
	{
		Invited = 0,
		SelfRegistered = 1
	}

	public enum LmsExternalLearnerTokenPurposes
	{
		Invite = 0,
		VerifyEmail = 1,
		ResetPassword = 2
	}

	/// <summary>Whether external learners may take a department course.</summary>
	public enum LmsExternalCourseAccessKinds
	{
		/// <summary>Members only (the default).</summary>
		None = 0,
		/// <summary>The department may enrol external learners; not listed on the portal.</summary>
		Assigned = 1,
		/// <summary>Listed on the learner portal; signed-in learners enrol themselves.</summary>
		Catalog = 2
	}

	/// <summary>How a paid public class bills its external learners through Business Operations.</summary>
	public enum LmsClassInvoiceModes
	{
		/// <summary>One invoice per learner (the learner's own customer).</summary>
		PerLearner = 0,
		/// <summary>One invoice per sponsoring organization (a Business Operations customer).</summary>
		PerOrganization = 1
	}

	#endregion Enums

	#region Entities

	/// <summary>The department's learner portal: its public key (the URL), switches and branding overrides.</summary>
	[Table("LmsLearnerPortals")]
	public class LmsLearnerPortal : IEntity
	{
		public const int KeyLength = 16;
		public const int WelcomeMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLearnerPortalId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The random key in the portal URL (/Learn/{key}); never the department id, so portals cannot be enumerated.</summary>
		public string PortalKey { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>People may create a learner account themselves (for a public class or a catalog course).</summary>
		public bool SelfRegistration { get; set; }

		/// <summary>Signed-out visitors see the open public classes and catalog courses.</summary>
		public bool PublicListing { get; set; }

		/// <summary>#rrggbb; empty for the Resgrid accent.</summary>
		public string AccentColor { get; set; }

		/// <summary>Plain text shown at the top of the portal.</summary>
		public string WelcomeText { get; set; }

		/// <summary>Hide the department logo (the department name shows instead).</summary>
		public bool HideLogo { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLearnerPortalId; }
			set { LmsLearnerPortalId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLearnerPortals";

		[NotMapped]
		public string IdName => "LmsLearnerPortalId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An external learner: a learner-only identity of one department (password and e-mail verification of its own).</summary>
	[Table("LmsExternalLearners")]
	public class LmsExternalLearner : IEntity
	{
		public const int EmailMaxLength = 256;
		public const int NameMaxLength = 100;
		public const int OrganizationMaxLength = 200;
		public const int MaxFailedSignIns = 5;
		public static readonly TimeSpan LockoutLength = TimeSpan.FromMinutes(15);

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsExternalLearnerId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The synthetic "xl-" id the LMS runtime carries (<see cref="LmsExternalLearnerIds"/>).</summary>
		public string LearnerUserId { get; set; }

		/// <summary>Normalized (trimmed, lower case); null once erased.</summary>
		public string Email { get; set; }

		public string FirstName { get; set; }

		public string LastName { get; set; }

		/// <summary>Who the learner trains for (an agency, an employer); free text.</summary>
		public string Organization { get; set; }

		/// <summary>The Business Operations customer (contact id) that sponsors this learner's classes.</summary>
		public string SponsorContactId { get; set; }

		/// <summary>The learner's own Business Operations customer (contact id) for per-learner invoices.</summary>
		public string BillingContactId { get; set; }

		/// <summary><see cref="LmsExternalLearnerStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary><see cref="LmsExternalLearnerSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>ASP.NET Core Identity's password hash (PasswordHasher); null until a password is chosen.</summary>
		public string PasswordHash { get; set; }

		/// <summary>Rotated on every password change, suspension, removal and erasure: signs every portal session out.</summary>
		public string SecurityStamp { get; set; }

		public DateTime? EmailConfirmedOn { get; set; }

		public int AccessFailedCount { get; set; }

		public DateTime? LockoutEndsOn { get; set; }

		public DateTime? LastSignInOn { get; set; }

		public string InvitedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public DateTime? ErasedOn { get; set; }

		/// <summary>Round 7B (M0402): the learner's language for e-mails and the portal (a supported locale); null falls back to the department's.</summary>
		public string Language { get; set; }

		/// <summary>Round 7B (M0402): the learner unsubscribed from class reminders (transactional e-mails still go out).</summary>
		public bool RemindersOptOut { get; set; }

		/// <summary>Round 7B (M0402): the random key of the reminder unsubscribe link (no sign-in needed; never the security stamp).</summary>
		public string UnsubscribeKey { get; set; }

		/// <summary>
		/// Security review follow-up (M0412): bumped by every write of the row. Every update is a compare-and-set on it, so a copy read before
		/// another save (a stamp rotation, a password change, a suspension) can never write the old values back.
		/// </summary>
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public string Name => string.Join(" ", new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));

		/// <summary>Takes a place in the fair-use count (§17.4): invited or active.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool IsCounted => Status == (int)LmsExternalLearnerStatuses.Invited || Status == (int)LmsExternalLearnerStatuses.Active;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsExternalLearnerId; }
			set { LmsExternalLearnerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsExternalLearners";

		[NotMapped]
		public string IdName => "LmsExternalLearnerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Name", "IsCounted" };
	}

	/// <summary>A single-use e-mailed token (invitation, e-mail verification, password reset); only its SHA-256 is stored.</summary>
	[Table("LmsExternalLearnerTokens")]
	public class LmsExternalLearnerToken : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsExternalLearnerTokenId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsExternalLearnerId { get; set; }

		/// <summary><see cref="LmsExternalLearnerTokenPurposes"/>.</summary>
		public int Purpose { get; set; }

		/// <summary>Lower-case hex SHA-256 of the token the e-mail carried.</summary>
		public string TokenHash { get; set; }

		/// <summary>A public class to register the learner for once the e-mail is verified (self-registration for a class).</summary>
		public int? LmsClassSessionId { get; set; }

		/// <summary>
		/// Security review M1 (2026-11-25, M0411): <see cref="LmsExternalLearnerRules.StampHash"/> of the learner's security stamp when the token
		/// was issued. A token is refused once the stamp has changed (a password reset, a sign-out, a suspension or an erasure).
		/// </summary>
		public string StampHash { get; set; }

		public DateTime ExpiresOn { get; set; }

		public DateTime? UsedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsExternalLearnerTokenId; }
			set { LmsExternalLearnerTokenId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsExternalLearnerTokens";

		[NotMapped]
		public string IdName => "LmsExternalLearnerTokenId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A class session offered to the public (T5 item 2, R-C13): listed on the portal, with an optional fee per external learner.</summary>
	[Table("LmsPublicClasses")]
	public class LmsPublicClass : IEntity
	{
		public const decimal MaxFee = 100000m;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsPublicClassId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsClassSessionId { get; set; }

		/// <summary>Listed on the learner portal; external learners may register.</summary>
		public bool IsPublic { get; set; }

		/// <summary>The fee per external learner; null for a free class. The LMS never takes payment.</summary>
		public decimal? FeeAmount { get; set; }

		/// <summary>ISO 4217.</summary>
		public string Currency { get; set; }

		/// <summary>The Business Operations rate item the invoice lines carry (optional).</summary>
		public string RateCardItemId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool HasFee => FeeAmount.HasValue && FeeAmount.Value > 0m;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsPublicClassId; }
			set { LmsPublicClassId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsPublicClasses";

		[NotMapped]
		public string IdName => "LmsPublicClassId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "HasFee" };
	}

	/// <summary>
	/// The fee state of one external learner on a class roster (its own table, so a roster save never overwrites an invoice link): the fee
	/// at registration, a waiver, the sponsoring organization and the Business Operations invoice raised for it.
	/// </summary>
	[Table("LmsClassFees")]
	public class LmsClassFee : IEntity
	{
		public const string ClaimPrefix = "claim:";

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsClassFeeId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsClassSessionId { get; set; }

		public int LmsClassEnrollmentId { get; set; }

		public string LearnerUserId { get; set; }

		public decimal Amount { get; set; }

		public string Currency { get; set; }

		public bool Waived { get; set; }

		/// <summary>The sponsoring organization (a Business Operations customer's contact id) billed for this learner.</summary>
		public string SponsorContactId { get; set; }

		/// <summary>The invoice raised; "claim:…" while an invoice run holds the row.</summary>
		public string InvoiceId { get; set; }

		public DateTime? InvoicedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsInvoiced => !string.IsNullOrWhiteSpace(InvoiceId) && !InvoiceId.StartsWith(ClaimPrefix, StringComparison.Ordinal);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsClassFeeId; }
			set { LmsClassFeeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsClassFees";

		[NotMapped]
		public string IdName => "LmsClassFeeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsInvoiced" };
	}

	/// <summary>Whether external learners may take a department course (its own table; the course row is not touched).</summary>
	[Table("LmsExternalCourseAccess")]
	public class LmsExternalCourseAccess : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsExternalCourseAccessId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCourseId { get; set; }

		/// <summary><see cref="LmsExternalCourseAccessKinds"/>.</summary>
		public int Access { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsExternalCourseAccessId; }
			set { LmsExternalCourseAccessId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsExternalCourseAccess";

		[NotMapped]
		public string IdName => "LmsExternalCourseAccessId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	#endregion Entities

	#region Rules

	/// <summary>The synthetic user ids of external learners: a reserved prefix no member id (a GUID) can carry.</summary>
	public static class LmsExternalLearnerIds
	{
		/// <summary>One source with the ledger's own filter (<see cref="LearningRecordQuery.ExternalLearnerUserIdPrefix"/>).</summary>
		public const string Prefix = LearningRecordQuery.ExternalLearnerUserIdPrefix;

		public static string New() => Prefix + Guid.NewGuid().ToString("N");

		public static bool IsExternal(string userId) =>
			!string.IsNullOrEmpty(userId) && userId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The fair-use answer for adding external learners (§17.4, decision 10).</summary>
	public sealed class LmsFairUse
	{
		public bool AddonActive { get; set; }
		public int PurchasedSeats { get; set; }
		public int EnabledMemberSeats { get; set; }
		/// <summary>External learners counted now (invited or active).</summary>
		public int CountedLearners { get; set; }
		/// <summary>Included free: 25% of purchased seats, minimum 25.</summary>
		public int Allowance { get; set; }
		/// <summary>Learners beyond the allowance: they hold member seats.</summary>
		public int Overflow => Math.Max(0, CountedLearners - Allowance);
		public int FreeSeats => Math.Max(0, PurchasedSeats - EnabledMemberSeats - Overflow);
	}

	public sealed class LmsFairUseDecision
	{
		public bool Allowed { get; set; }
		/// <summary>lms_xl_addon_inactive or lms_xl_seats_full; null when allowed.</summary>
		public string Refusal { get; set; }
		/// <summary>The packs to add before the next learner fits (seats full).</summary>
		public int PacksNeeded { get; set; }
		/// <summary>The new learner(s) would hold member seats (beyond the allowance).</summary>
		public bool CountsInPacks { get; set; }
	}

	/// <summary>Pure rules of external learners: fair use, credentials, tokens, branding, public class registration, roster export, invoicing.</summary>
	public static class LmsExternalLearnerRules
	{
		public const int FairUseMinimum = 25;
		public const int FairUsePercent = 25;
		public const int PasswordMinLength = 8;
		public const int PasswordMaxLength = 128;
		public const string DefaultAccent = "#1ab394";
		public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);
		public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(48);
		public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(2);

		/// <summary>External learners included free: 25% of purchased seats (rounded down), minimum 25 (§17.4).</summary>
		public static int Allowance(int purchasedSeats) => Math.Max(FairUseMinimum, Math.Max(0, purchasedSeats) * FairUsePercent / 100);

		/// <summary>
		/// Whether <paramref name="additional"/> more external learners fit: within the allowance they are free; beyond it each one counts
		/// toward packs like a member, so it needs a purchased seat no enabled member holds. Without a working add-on, external learners are
		/// suspended (lapse rule 7) and none may be added.
		/// </summary>
		public static LmsFairUseDecision Decide(LmsFairUse use, int additional = 1, int packSize = 25)
		{
			if (use == null || !use.AddonActive)
				return new LmsFairUseDecision { Allowed = false, Refusal = "lms_xl_addon_inactive" };

			var wanted = use.CountedLearners + Math.Max(1, additional);
			var overflow = Math.Max(0, wanted - use.Allowance);
			if (overflow == 0)
				return new LmsFairUseDecision { Allowed = true };

			var seatsNeeded = use.EnabledMemberSeats + overflow;
			if (seatsNeeded <= use.PurchasedSeats)
				return new LmsFairUseDecision { Allowed = true, CountsInPacks = true };

			var missing = seatsNeeded - Math.Max(0, use.PurchasedSeats);
			return new LmsFairUseDecision
			{
				Allowed = false,
				Refusal = "lms_xl_seats_full",
				CountsInPacks = true,
				PacksNeeded = packSize <= 0 ? 1 : (int)Math.Ceiling(missing / (double)packSize)
			};
		}

		/// <summary>External learners beyond the allowance of <paramref name="purchasedSeats"/>: the seats they take in the packs.</summary>
		public static int SeatsTaken(int countedLearners, int purchasedSeats) => Math.Max(0, countedLearners - Allowance(purchasedSeats));

		/// <summary>The fewest packs (at least <paramref name="floor"/>) that seat the members and the external learners beyond the allowance those packs give.</summary>
		public static int MinimumPacks(int members, int countedLearners, int packSize, int floor)
		{
			var size = Math.Max(1, packSize);
			var packs = Math.Max(Math.Max(0, floor), (int)Math.Ceiling(Math.Max(0, members) / (double)size));
			while (Math.Max(0, members) + SeatsTaken(countedLearners, packs * size) > packs * size)
				packs++;
			return packs;
		}

		public static string NormalizeEmail(string email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

		public static bool IsValidEmail(string email)
		{
			var value = NormalizeEmail(email);
			if (value == null || value.Length > LmsExternalLearner.EmailMaxLength || value.Contains(' ') || value.IndexOf('@') <= 0)
				return false;
			try
			{
				return new MailAddress(value).Address == value;
			}
			catch (FormatException)
			{
				return false;
			}
		}

		/// <summary>The department's password rules for members, applied to learners: 8-128 characters with a digit, a lower and an upper case letter.</summary>
		public static string PasswordRefusal(string password)
		{
			if (string.IsNullOrEmpty(password) || password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
				return "lms_xl_password_length";
			if (!password.Any(char.IsDigit) || !password.Any(char.IsLower) || !password.Any(char.IsUpper))
				return "lms_xl_password_weak";
			return null;
		}

		/// <summary>A new random token for an e-mailed link (32 bytes, URL-safe).</summary>
		public static string NewToken()
		{
			var bytes = RandomNumberGenerator.GetBytes(32);
			return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		}

		public static string HashToken(string token)
		{
			if (string.IsNullOrWhiteSpace(token))
				return null;
			var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
			return Convert.ToHexString(hash).ToLowerInvariant();
		}

		/// <summary>Security review M1: what an e-mailed token stores of the learner's security stamp (a separate hash domain; null for no stamp).</summary>
		public static string StampHash(string securityStamp) => string.IsNullOrEmpty(securityStamp) ? null : HashToken("stamp:" + securityStamp);

		/// <summary>A new portal key: 16 lower-case letters and digits (no look-alikes).</summary>
		public static string NewPortalKey()
		{
			const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
			var bytes = RandomNumberGenerator.GetBytes(LmsLearnerPortal.KeyLength);
			return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
		}

		public static bool IsPortalKeyShape(string key) =>
			!string.IsNullOrEmpty(key) && key.Length == LmsLearnerPortal.KeyLength && key.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));

		/// <summary>The accent colour as "#rrggbb", or null when the value is not one (the Resgrid accent is used then).</summary>
		public static string NormalizeAccent(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			if (!text.StartsWith("#", StringComparison.Ordinal))
				text = "#" + text;
			if (text.Length == 4 && text.Skip(1).All(Uri.IsHexDigit))
				text = "#" + string.Concat(text.Skip(1).Select(c => new string(c, 2)));
			return text.Length == 7 && text.Skip(1).All(Uri.IsHexDigit) ? text.ToLowerInvariant() : null;
		}

		/// <summary>Black or white text, whichever reads on the accent (WCAG relative luminance).</summary>
		public static string TextOn(string accent)
		{
			var color = NormalizeAccent(accent) ?? DefaultAccent;
			double Channel(int offset)
			{
				var c = int.Parse(color.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
				return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
			}
			var luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
			return luminance > 0.179 ? "#000000" : "#ffffff";
		}

		/// <summary>The portal's branding: the department logo and name, the accent, each falling back to Resgrid's when missing.</summary>
		public static LmsLearnerPortalBranding Branding(LmsLearnerPortal portal, string departmentName, string logoUrl)
		{
			var accent = NormalizeAccent(portal?.AccentColor) ?? DefaultAccent;
			return new LmsLearnerPortalBranding
			{
				DisplayName = string.IsNullOrWhiteSpace(departmentName) ? "Resgrid" : departmentName.Trim(),
				LogoUrl = portal != null && portal.HideLogo ? null : (string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl),
				Accent = accent,
				AccentText = TextOn(accent),
				WelcomeText = string.IsNullOrWhiteSpace(portal?.WelcomeText) ? null : portal.WelcomeText.Trim(),
				IsDefaultAccent = NormalizeAccent(portal?.AccentColor) == null
			};
		}

		/// <summary>Why an external learner cannot register for a class; null when they can.</summary>
		public static string ClassRegistrationRefusal(LmsClassSession session, LmsPublicClass publicClass, DateTime utcNow)
		{
			if (session == null || session.IsDeleted || publicClass == null || !publicClass.IsPublic || publicClass.LmsClassSessionId != session.LmsClassSessionId)
				return "lms_class_not_found";
			if (session.Status != (int)LmsClassSessionStatuses.Scheduled || session.StartsOn <= utcNow)
				return "lms_class_not_open";
			if (!session.EnrollmentOpen)
				return "lms_class_enrollment_closed";
			if (session.CutoffDays > 0 && utcNow > session.StartsOn.AddDays(-session.CutoffDays))
				return "lms_class_enrollment_cutoff";
			return null;
		}

		/// <summary>A fee is due for this roster row: an external learner holding a seat (or on the roster when the class has run), not waived.</summary>
		public static bool IsBillable(LmsClassEnrollment enrollment, LmsClassFee fee)
		{
			if (enrollment == null || fee == null || fee.Waived || fee.Amount <= 0m || !LmsExternalLearnerIds.IsExternal(enrollment.UserId))
				return false;
			if (enrollment.Status != (int)LmsClassEnrollmentStatuses.Enrolled)
				return false;
			return enrollment.Attendance != (int)LmsClassAttendanceStates.Excused;
		}

		private static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;
			// Formula injection: a cell a spreadsheet would evaluate is quoted as text.
			var text = "=+-@\t\r".IndexOf(value[0]) >= 0 ? "'" + value : value;
			return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
		}

		/// <summary>The roster export (always available, §25.4a item 12): learner, organization, attendance and fee per row, for the department's own billing.</summary>
		public static string RosterCsv(IEnumerable<LmsClassBillingRow> rows, Func<string, string> label)
		{
			label ??= key => key;
			var builder = new StringBuilder();
			builder.Append(string.Join(",", new[] { "Learner", "Email", "Organization", "Kind", "Roster", "Attendance", "Hours", "Fee", "Currency", "FeeStatus", "Invoice" }.Select(h => Csv(label("Csv." + h)))));
			builder.Append("\r\n");
			foreach (var row in rows ?? Enumerable.Empty<LmsClassBillingRow>())
			{
				var fee = row.Fee;
				var feeStatus = fee == null ? string.Empty : fee.Waived ? label("FeeStatus.Waived") : fee.IsInvoiced ? label("FeeStatus.Invoiced") : row.Billable ? label("FeeStatus.Due") : label("FeeStatus.NotDue");
				builder.Append(string.Join(",", new[]
				{
					Csv(row.Name),
					Csv(row.Email),
					Csv(row.Organization),
					Csv(label(row.IsExternal ? "Kind.External" : "Kind.Member")),
					Csv(label("Roster." + ((LmsClassEnrollmentStatuses)row.Enrollment.Status))),
					Csv(label("Attendance." + ((LmsClassAttendanceStates)row.Enrollment.Attendance))),
					(row.Enrollment.Hours ?? 0m).ToString("0.##", CultureInfo.InvariantCulture),
					fee == null ? string.Empty : fee.Amount.ToString("0.00", CultureInfo.InvariantCulture),
					Csv(fee?.Currency),
					Csv(feeStatus),
					Csv(fee != null && fee.IsInvoiced ? row.InvoiceLabel ?? fee.InvoiceId : null)
				}));
				builder.Append("\r\n");
			}
			return builder.ToString();
		}

		/// <summary>
		/// Groups the billable rows into invoices: per learner (keyed by the learner's id) or per sponsoring organization (keyed by the sponsor's
		/// customer; rows without a sponsor are skipped with lms_xl_no_sponsor). Already invoiced or claimed rows never appear.
		/// </summary>
		public static (Dictionary<string, List<LmsClassBillingRow>> Groups, List<LmsClassBillingRow> Skipped) GroupForInvoices(IEnumerable<LmsClassBillingRow> rows, LmsClassInvoiceModes mode)
		{
			var groups = new Dictionary<string, List<LmsClassBillingRow>>(StringComparer.OrdinalIgnoreCase);
			var skipped = new List<LmsClassBillingRow>();
			foreach (var row in (rows ?? Enumerable.Empty<LmsClassBillingRow>()).Where(r => r.Billable && r.Fee != null && string.IsNullOrWhiteSpace(r.Fee.InvoiceId)))
			{
				var key = mode == LmsClassInvoiceModes.PerOrganization ? row.Fee.SponsorContactId ?? row.Learner?.SponsorContactId : row.Enrollment.UserId;
				if (string.IsNullOrWhiteSpace(key))
				{
					skipped.Add(row);
					continue;
				}
				if (!groups.TryGetValue(key, out var list))
					groups[key] = list = new List<LmsClassBillingRow>();
				list.Add(row);
			}
			return (groups, skipped);
		}

		public static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var text = value.Trim();
			return text.Length > max ? text.Substring(0, max) : text;
		}
	}

	#endregion Rules

	#region Views and inputs

	public sealed class LmsLearnerPortalBranding
	{
		public string DisplayName { get; set; }
		public string LogoUrl { get; set; }
		public string Accent { get; set; }
		public string AccentText { get; set; }
		public string WelcomeText { get; set; }
		public bool IsDefaultAccent { get; set; }
		/// <summary>The department's time zone (dates on the portal show in it).</summary>
		public string TimeZone { get; set; }

		/// <summary>A UTC time in the department's time zone (UTC when the zone is unknown).</summary>
		public DateTime Local(DateTime utc)
		{
			var value = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
			if (string.IsNullOrWhiteSpace(TimeZone))
				return value;
			try
			{
				return TimeZoneInfo.ConvertTimeFromUtc(value, TimeZoneInfo.FindSystemTimeZoneById(TimeZone));
			}
			catch (Exception)
			{
				return value;
			}
		}
	}

	/// <summary>An outcome: the learner (when there is one) or a service code ("lms_xl_*", "lms_class_*") the pages show as text.</summary>
	public sealed class LmsExternalLearnerResult
	{
		public LmsExternalLearner Learner { get; set; }
		public string Error { get; set; }
		public int PacksNeeded { get; set; }
		public object Value { get; set; }
		public bool Succeeded => Error == null;

		public static LmsExternalLearnerResult Fail(string error, int packsNeeded = 0) => new LmsExternalLearnerResult { Error = error, PacksNeeded = packsNeeded };
	}

	public sealed class LmsExternalLearnerInvite
	{
		public string Email { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string Organization { get; set; }
		public string SponsorContactId { get; set; }
		public List<int> CourseIds { get; set; } = new List<int>();
		public List<int> ClassSessionIds { get; set; } = new List<int>();
	}

	public sealed class LmsLearnerRegistration
	{
		public string Email { get; set; }
		public string FirstName { get; set; }
		public string LastName { get; set; }
		public string Organization { get; set; }
		public string Password { get; set; }
		/// <summary>A public class to register for once the e-mail is verified.</summary>
		public int? LmsClassSessionId { get; set; }
		/// <summary>Round 7B: the portal language the learner registered in (their e-mail language until they change it).</summary>
		public string Language { get; set; }
	}

	public sealed class LmsLearnerPortalInput
	{
		public bool IsEnabled { get; set; }
		public bool SelfRegistration { get; set; }
		public bool PublicListing { get; set; }
		public string AccentColor { get; set; }
		public string WelcomeText { get; set; }
		public bool HideLogo { get; set; }
	}

	/// <summary>One open public class on the portal.</summary>
	public sealed class LmsLearnerClassItem
	{
		public LmsClassSession Session { get; set; }
		public LmsPublicClass PublicClass { get; set; }
		public int SeatsTaken { get; set; }
		public bool IsFull => Session != null && Session.Capacity > 0 && SeatsTaken >= Session.Capacity;
		/// <summary>The signed-in learner's roster row, when there is one.</summary>
		public LmsClassEnrollment Mine { get; set; }
		public string Refusal { get; set; }
		/// <summary>Round 7B: the learner's place on the waitlist (1 is next), when waitlisted.</summary>
		public int? WaitlistPosition { get; set; }
	}

	/// <summary>One catalog course on the portal.</summary>
	public sealed class LmsLearnerCourseItem
	{
		public LmsCourse Course { get; set; }
		public LmsCourseVersion Version { get; set; }
		public LmsEnrollment Mine { get; set; }
	}

	public sealed class LmsLearnerCatalog
	{
		public List<LmsLearnerClassItem> Classes { get; set; } = new List<LmsLearnerClassItem>();
		public List<LmsLearnerCourseItem> Courses { get; set; } = new List<LmsLearnerCourseItem>();
	}

	/// <summary>The signed-in learner's own learning: courses, classes, the transcript and certificates.</summary>
	public sealed class LmsLearnerMyLearning
	{
		public List<LmsMyCourse> Courses { get; set; } = new List<LmsMyCourse>();
		public List<LmsLearnerClassItem> Classes { get; set; } = new List<LmsLearnerClassItem>();
		public List<LearningRecord> Records { get; set; } = new List<LearningRecord>();
	}

	/// <summary>One roster row with its fee for the class billing page and the roster export.</summary>
	public sealed class LmsClassBillingRow
	{
		public LmsClassEnrollment Enrollment { get; set; }
		public LmsExternalLearner Learner { get; set; }
		public LmsClassFee Fee { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }
		public string Organization { get; set; }
		public bool IsExternal { get; set; }
		public bool Billable { get; set; }
		public string InvoiceLabel { get; set; }
	}

	public sealed class LmsClassBillingView
	{
		public LmsClassSession Session { get; set; }
		public LmsPublicClass PublicClass { get; set; }
		public List<LmsClassBillingRow> Rows { get; set; } = new List<LmsClassBillingRow>();
		/// <summary>Business Operations invoicing is active for the department (invoices can be raised).</summary>
		public bool InvoicingAvailable { get; set; }
		public decimal DueTotal => Rows.Where(r => r.Billable && r.Fee != null && !r.Fee.IsInvoiced).Sum(r => r.Fee.Amount);
		public decimal InvoicedTotal => Rows.Where(r => r.Fee != null && r.Fee.IsInvoiced).Sum(r => r.Fee.Amount);
	}

	public sealed class LmsClassInvoiceRun
	{
		public List<string> InvoiceIds { get; set; } = new List<string>();
		public int LearnersInvoiced { get; set; }
		/// <summary>Rows left out, with the reason code.</summary>
		public List<KeyValuePair<string, string>> Skipped { get; set; } = new List<KeyValuePair<string, string>>();
		public string Error { get; set; }
	}

	#endregion Views and inputs
}
