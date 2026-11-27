using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Localization.Areas.User.ContentNotices;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// The text of every notice the certification expiry sweep sends (worker 34, Workforce &amp; Business Operations plan D5): the holder's
	/// expiring notice, the role removal and the removal-pending warning, and the admin digest. Each is composed in the recipient's language
	/// (the member's profile language, else the department default, else English: <see cref="ContentNoticeCultures"/>) from the
	/// ContentNotices area, "Certification.*" keys, 10 locales. Certification type, role, member and unit names are department data and are
	/// never translated; dates are written the reader's way.
	/// </summary>
	public static class CertificationSweepNotices
	{
		/// <summary>The most entries one digest line names (the rest are counted in the summary).</summary>
		public const int DigestNamesPerLine = 25;

		private static string Date(DateTime value, CultureInfo culture) => value.Date.ToString("d", culture ?? ContentNoticeText.English);

		/// <summary>The holder's notice on a lead day: the certification expires in <paramref name="days"/> days.</summary>
		public static (string Title, string Message) Expiring(CultureInfo culture, string typeName, int days, DateTime expiresOn) =>
			(ContentNoticeText.Get("Certification.Title", culture),
				days == 1
					? ContentNoticeText.Format("Certification.Expiring.BodyOne", culture, typeName, Date(expiresOn, culture))
					: ContentNoticeText.Format("Certification.Expiring.Body", culture, typeName, days, Date(expiresOn, culture)));

		/// <summary>The holder's notice when enforcement removed them from a role.</summary>
		public static (string Title, string Message) RoleRemoved(CultureInfo culture, string roleName, string typeName) =>
			(ContentNoticeText.Get("Certification.Title", culture), ContentNoticeText.Format("Certification.RoleRemoved.Body", culture, roleName, typeName));

		/// <summary>The holder's warning, on a lead day, that their role membership ends on <paramref name="endsOn"/>.</summary>
		public static (string Title, string Message) RemovalPending(CultureInfo culture, string roleName, DateTime endsOn, string typeName) =>
			(ContentNoticeText.Get("Certification.Title", culture),
				ContentNoticeText.Format("Certification.RemovalPending.Body", culture, roleName, Date(endsOn, culture), typeName));

		/// <summary>The admin digest of one sweep, in the admin's language.</summary>
		public static (string Title, string Message) Digest(CultureInfo culture, DateTime today, CertificationSweepResult result)
		{
			result ??= new CertificationSweepResult();
			var entries = result.Entries ?? new List<CertificationSweepEntry>();
			var lines = new List<string>
			{
				ContentNoticeText.Format("Certification.Digest.Summary", culture, Date(today, culture), result.Expired + result.UnitsExpired,
					result.ExpiringNotified + result.UnitsExpiringNotified, result.InGrace, result.Removed)
			};

			void Line(CertificationSweepEntryKinds kind, string key)
			{
				var names = entries.Where(e => e != null && e.Kind == kind).Take(DigestNamesPerLine).Select(e => Entry(culture, e)).ToList();
				if (names.Count > 0)
					lines.Add(ContentNoticeText.Format(key, culture, string.Join("; ", names)));
			}

			Line(CertificationSweepEntryKinds.Expired, "Certification.Digest.Expired");
			Line(CertificationSweepEntryKinds.Expiring, "Certification.Digest.Expiring");
			Line(CertificationSweepEntryKinds.InGrace, "Certification.Digest.InGrace");
			Line(CertificationSweepEntryKinds.Removed, "Certification.Digest.Removed");
			return (ContentNoticeText.Get("Certification.Digest.Title", culture), string.Join(" ", lines));
		}

		/// <summary>One digest entry: who (or which unit), the certification type, and the role, days left or deadline it concerns.</summary>
		public static string Entry(CultureInfo culture, CertificationSweepEntry entry)
		{
			var subject = !string.IsNullOrWhiteSpace(entry.SubjectName) ? entry.SubjectName
				: entry.UnitId.HasValue ? ContentNoticeText.Format("Certification.Digest.UnknownUnit", culture, entry.UnitId.Value) : string.Empty;
			switch (entry.Kind)
			{
				case CertificationSweepEntryKinds.Expiring:
					return ContentNoticeText.Format("Certification.Digest.ExpiringEntry", culture, subject, entry.TypeName, entry.DaysUntilExpiry ?? 0);
				case CertificationSweepEntryKinds.InGrace:
					return entry.Until.HasValue
						? ContentNoticeText.Format("Certification.Digest.InGraceEntry", culture, subject, entry.RoleName, entry.TypeName, Date(entry.Until.Value, culture))
						: $"{subject} – {entry.RoleName} ({entry.TypeName})";
				case CertificationSweepEntryKinds.Removed:
					return $"{subject} – {entry.RoleName} ({entry.TypeName})";
				default:
					return $"{subject} – {entry.TypeName}";
			}
		}
	}
}
