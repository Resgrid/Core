using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Text of learner e-mails composed for someone else (worker 95, a manager's action): read culture-explicitly from the LmsExternalLearners
	/// resources in the recipient's language (outbound-message-localization-gap), never from the current thread culture.
	/// </summary>
	public static class LmsLearnerMailText
	{
		private static readonly ResourceManager Strings = new("Resgrid.Localization.Areas.User.LmsExternalLearners.LmsExternalLearners", typeof(Resgrid.Localization.SupportedLocales).Assembly);

		/// <summary>A supported culture for the language, or null when it is empty or not supported.</summary>
		public static CultureInfo SupportedCulture(string language)
		{
			if (string.IsNullOrWhiteSpace(language))
				return null;
			try
			{
				var culture = CultureInfo.GetCultureInfo(language.Trim());
				return Resgrid.Localization.SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName)
					? CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName) : null;
			}
			catch (CultureNotFoundException)
			{
				return null;
			}
		}

		/// <summary>The learner's language, else the department's, else English.</summary>
		public static CultureInfo CultureFor(string learnerLanguage, string departmentLanguage) =>
			SupportedCulture(learnerLanguage) ?? SupportedCulture(departmentLanguage) ?? CultureInfo.GetCultureInfo("en");

		public static string Get(string key, CultureInfo culture)
		{
			if (string.IsNullOrEmpty(key))
				return string.Empty;
			var language = CultureInfo.GetCultureInfo((culture ?? CultureInfo.GetCultureInfo("en")).TwoLetterISOLanguageName);
			try
			{
				return Strings.GetResourceSet(language, true, false)?.GetString(key)
					?? Strings.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)?.GetString(key)
					?? key;
			}
			catch (MissingManifestResourceException)
			{
				return key;
			}
		}

		public static string Format(string key, CultureInfo culture, params object[] args)
		{
			var text = Get(key, culture);
			try
			{
				return args == null || args.Length == 0 ? text : string.Format(culture ?? CultureInfo.InvariantCulture, text, args);
			}
			catch (FormatException)
			{
				return text;
			}
		}
	}

	/// <summary>One composed learner e-mail (pure, so the wording, language and links can be tested).</summary>
	public sealed class LmsLearnerMail
	{
		public string Subject { get; set; }
		public string Html { get; set; }
		public CultureInfo Culture { get; set; }
	}

	/// <summary>
	/// Round 7B: class e-mails to external learners (<see cref="ILmsLearnerClassMailer"/>). Each mail is claimed in LmsLearnerNotices before it
	/// is sent (the unique key makes it once only, across worker runs and racing requests); a learner who is not active or has no address gets
	/// nothing; reminders honour the learner's unsubscribe and carry the unsubscribe link. Department-branded: the department's name, logo
	/// and portal accent. Never throws.
	/// </summary>
	public class LmsLearnerClassMailer : ILmsLearnerClassMailer
	{
		private readonly ILmsExternalLearnersRepository _learners;
		private readonly ILmsLearnerNoticesRepository _notices;
		private readonly ILmsLearnerPortalsRepository _portals;
		private readonly ILmsClassEnrollmentsRepository _enrollments;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentProfileMediaService _profiles;
		private readonly IUserProfileService _userProfiles;
		private readonly IEmailSender _email;

		public LmsLearnerClassMailer(ILmsExternalLearnersRepository learners, ILmsLearnerNoticesRepository notices, ILmsLearnerPortalsRepository portals,
			ILmsClassEnrollmentsRepository enrollments, IDepartmentsService departments, IDepartmentProfileMediaService profiles, IUserProfileService userProfiles,
			IEmailSender email)
		{
			_learners = learners;
			_notices = notices;
			_portals = portals;
			_enrollments = enrollments;
			_departments = departments;
			_profiles = profiles;
			_userProfiles = userProfiles;
			_email = email;
		}

		public async Task<int> SendAsync(int departmentId, LmsClassSession session, IEnumerable<string> learnerUserIds, LmsLearnerNoticeKinds kind,
			CancellationToken cancellationToken = default)
		{
			var ids = (learnerUserIds ?? Enumerable.Empty<string>()).Where(LmsExternalLearnerIds.IsExternal).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (departmentId <= 0 || session == null || session.DepartmentId != departmentId || ids.Count == 0)
				return 0;
			try
			{
				if (!ConfigHelper.CanTransmit(departmentId))
					return 0;
				var learners = (await _learners.GetByLearnerUserIdsAsync(departmentId, ids) ?? new List<LmsExternalLearner>()).Where(l => l.DepartmentId == departmentId).ToList();
				if (learners.Count == 0)
					return 0;

				var roster = await _enrollments.GetForSessionAsync(departmentId, session.LmsClassSessionId) ?? new List<LmsClassEnrollment>();
				var portal = await _portals.GetByDepartmentAsync(departmentId);
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var (name, logo) = await BrandingAsync(departmentId, department);
				var branding = LmsExternalLearnerRules.Branding(portal, name, logo);
				branding.TimeZone = department?.TimeZone;
				string departmentLanguage = null;
				var departmentLanguageRead = false;
				var sent = 0;

				foreach (var learner in learners)
				{
					cancellationToken.ThrowIfCancellationRequested();
					// Never to a learner who is not active (pending, suspended, removed, erased) or has no address; a reminder never after unsubscribing.
					if (LmsLearnerNoticeRules.SkipReason(learner, kind) != null)
						continue;
					var enrollment = roster.FirstOrDefault(e => string.Equals(e.UserId, learner.LearnerUserId, StringComparison.OrdinalIgnoreCase));
					var notice = new LmsLearnerNotice
					{
						DepartmentId = departmentId,
						LmsExternalLearnerId = learner.LmsExternalLearnerId,
						LmsClassSessionId = session.LmsClassSessionId,
						Kind = (int)kind,
						NoticeKey = LmsLearnerNoticeRules.Key(kind, session, enrollment),
						ClaimedOn = DateTime.UtcNow
					};
					// Claimed before sending: a second caller (a retry, the next worker run) finds the claim and sends nothing.
					if (!await _notices.TryClaimAsync(notice, cancellationToken))
						continue;

					if (!departmentLanguageRead)
					{
						departmentLanguage = await DepartmentLanguageAsync(department);
						departmentLanguageRead = true;
					}
					if (kind == LmsLearnerNoticeKinds.Reminder && string.IsNullOrWhiteSpace(learner.UnsubscribeKey))
					{
						// Only that column (security review follow-up, M0412): this batch's copy of the row is never written back whole.
						var fresh = LmsLearnerNoticeRules.NewUnsubscribeKey();
						learner.UnsubscribeKey = await _learners.EnsureUnsubscribeKeyAsync(departmentId, learner.LmsExternalLearnerId, fresh, cancellationToken) ?? fresh;
					}

					var mail = Compose(kind, session, enrollment, roster, learner, branding, portal, LmsLearnerMailText.CultureFor(learner.Language, departmentLanguage));
					var ok = await SendMailAsync(learner.Email, branding.DisplayName, mail);
					await _notices.MarkAsync(departmentId, notice.LmsLearnerNoticeId, ok ? DateTime.UtcNow : (DateTime?)null, ok ? null : "send_failed", cancellationToken);
					if (ok)
						sent++;
				}

				return sent;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Learner class e-mails ({kind}) for class session {session.LmsClassSessionId} failed.");
				return 0;
			}
		}

		private async Task<(string Name, string Logo)> BrandingAsync(int departmentId, Department department)
		{
			string name = null, logo = null;
			try
			{
				var branding = await _profiles.GetBrandingAsync(departmentId);
				name = branding?.DisplayName;
				var masthead = branding?.Rendition(DepartmentProfileMediaKind.EmailMasthead);
				if (masthead != null && !string.IsNullOrWhiteSpace(masthead.MediaKey))
					logo = DepartmentProfileMediaService.PublicMastheadUrl(masthead.MediaKey);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Department branding could not be read for a learner e-mail.");
			}
			return (string.IsNullOrWhiteSpace(name) ? department?.Name : name, logo);
		}

		/// <summary>The department's language: its managing member's (the department has no language of its own), else none.</summary>
		private async Task<string> DepartmentLanguageAsync(Department department)
		{
			try
			{
				return string.IsNullOrWhiteSpace(department?.ManagingUserId) ? null : (await _userProfiles.GetProfileByUserIdAsync(department.ManagingUserId))?.Language;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private async Task<bool> SendMailAsync(string to, string fromName, LmsLearnerMail mail)
		{
			try
			{
				using var message = new MailMessage();
				message.To.Add(to);
				message.From = new MailAddress("DO-NOT-REPLY@resgrid.com", string.IsNullOrWhiteSpace(fromName) ? "Resgrid" : fromName);
				message.Subject = mail.Subject;
				message.Body = mail.Html;
				message.IsBodyHtml = true;
				message.BodyEncoding = Encoding.UTF8;
				message.SubjectEncoding = Encoding.UTF8;
				return await _email.SendEmail(message);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A learner class e-mail could not be sent.");
				return false;
			}
		}

		/// <summary>Composes one e-mail (pure): subject and branded HTML in the culture; every piece of text is HTML-encoded.</summary>
		public static LmsLearnerMail Compose(LmsLearnerNoticeKinds kind, LmsClassSession session, LmsClassEnrollment enrollment, IEnumerable<LmsClassEnrollment> roster,
			LmsExternalLearner learner, LmsLearnerPortalBranding branding, LmsLearnerPortal portal, CultureInfo culture)
		{
			culture ??= CultureInfo.GetCultureInfo("en");
			branding ??= LmsExternalLearnerRules.Branding(portal, null, null);
			var title = session?.Title ?? string.Empty;
			var when = branding.Local(session?.StartsOn ?? DateTime.UtcNow).ToString("f", culture);
			string T(string key, params object[] args) => LmsLearnerMailText.Format(key, culture, args);

			var lines = new List<string>();
			string subject;
			switch (kind)
			{
				case LmsLearnerNoticeKinds.Enrolled:
					subject = T("Mail.Enrolled.Subject", title);
					lines.Add(T("Mail.Enrolled.Body", title, when));
					break;
				case LmsLearnerNoticeKinds.Waitlisted:
					subject = T("Mail.Waitlisted.Subject", title);
					lines.Add(T("Mail.Waitlisted.Body", title, when, LmsLearnerNoticeRules.WaitlistPosition(roster, enrollment) ?? 1));
					break;
				case LmsLearnerNoticeKinds.Promoted:
					subject = T("Mail.Promoted.Subject", title);
					lines.Add(T("Mail.Promoted.Body", title, when));
					break;
				case LmsLearnerNoticeKinds.Reminder:
					subject = T("Mail.Reminder.Subject", title);
					lines.Add(T("Mail.Reminder.Body", title, when));
					break;
				case LmsLearnerNoticeKinds.Changed:
					subject = T("Mail.Changed.Subject", title);
					lines.Add(T("Mail.Changed.Body", title, when));
					break;
				case LmsLearnerNoticeKinds.Cancelled:
					subject = T("Mail.Cancelled.Subject", title);
					lines.Add(T("Mail.Cancelled.Body", title, when));
					if (!string.IsNullOrWhiteSpace(session?.CancelReason))
						lines.Add(T("Mail.CancelReason", session.CancelReason.Trim()));
					break;
				case LmsLearnerNoticeKinds.SignedOff:
					subject = T("Mail.SignedOff.Subject", title);
					lines.Add(T("Mail.SignedOff.Body", title));
					break;
				default:
					subject = T("Mail.CompletionRecorded.Subject", title);
					lines.Add(T("Mail.CompletionRecorded.Body", title));
					break;
			}

			if (kind is LmsLearnerNoticeKinds.Enrolled or LmsLearnerNoticeKinds.Promoted or LmsLearnerNoticeKinds.Reminder or LmsLearnerNoticeKinds.Changed)
			{
				if (!string.IsNullOrWhiteSpace(session?.Location))
					lines.Add(T("Mail.Where", session.Location.Trim()));
				if (!string.IsNullOrWhiteSpace(session?.MeetingUrl))
					lines.Add(T("Mail.Online", session.MeetingUrl.Trim()));
			}

			var portalUrl = portal != null && portal.IsEnabled && !string.IsNullOrWhiteSpace(portal.PortalKey)
				? (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/') + "/Learn/" + portal.PortalKey : null;
			var unsubscribeUrl = kind == LmsLearnerNoticeKinds.Reminder && portalUrl != null && !string.IsNullOrWhiteSpace(learner?.UnsubscribeKey)
				? portalUrl + "/Unsubscribe?key=" + Uri.EscapeDataString(learner.UnsubscribeKey) : null;

			var html = new StringBuilder();
			string E(string text) => WebUtility.HtmlEncode(text ?? string.Empty);
			var accent = LmsExternalLearnerRules.NormalizeAccent(branding.Accent) ?? LmsExternalLearnerRules.DefaultAccent;
			var rtl = culture.TextInfo.IsRightToLeft;
			html.Append("<!DOCTYPE html><html lang=\"").Append(E(culture.TwoLetterISOLanguageName)).Append("\" dir=\"").Append(rtl ? "rtl" : "ltr").Append("\"><body style=\"margin:0;padding:0;background:#f3f3f4;font-family:Arial,Helvetica,sans-serif;color:#333;\">");
			html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
			html.Append("<table role=\"presentation\" width=\"100%\" style=\"max-width:600px;background:#fff;border-top:4px solid ").Append(E(accent)).Append(";\" cellpadding=\"0\" cellspacing=\"0\">");
			html.Append("<tr><td style=\"padding:20px 24px;\">");
			if (!string.IsNullOrWhiteSpace(branding.LogoUrl))
				html.Append("<img src=\"").Append(E(branding.LogoUrl)).Append("\" alt=\"").Append(E(branding.DisplayName)).Append("\" style=\"max-height:48px;display:block;margin-bottom:12px;\" />");
			html.Append("<div style=\"font-size:18px;font-weight:bold;margin-bottom:16px;\">").Append(E(branding.DisplayName)).Append("</div>");
			html.Append("<p>").Append(E(T("Mail.Greeting", learner?.FirstName ?? string.Empty))).Append("</p>");
			foreach (var line in lines)
				html.Append("<p>").Append(E(line)).Append("</p>");
			if (portalUrl != null)
				html.Append("<p><a href=\"").Append(E(portalUrl + "/My")).Append("\" style=\"display:inline-block;padding:10px 16px;background:").Append(E(accent))
					.Append(";color:").Append(E(branding.AccentText ?? "#ffffff")).Append(";text-decoration:none;border-radius:3px;\">").Append(E(T("Mail.OpenPortal"))).Append("</a></p>");
			html.Append("<p style=\"font-size:12px;color:#777;margin-top:24px;\">").Append(E(T("Mail.Footer", branding.DisplayName))).Append("</p>");
			if (unsubscribeUrl != null)
				html.Append("<p style=\"font-size:12px;color:#777;\"><a href=\"").Append(E(unsubscribeUrl)).Append("\" style=\"color:#777;\">").Append(E(T("Mail.Unsubscribe"))).Append("</a></p>");
			html.Append("</td></tr></table></td></tr></table></body></html>");

			return new LmsLearnerMail { Subject = subject, Html = html.ToString(), Culture = culture };
		}
	}
}
