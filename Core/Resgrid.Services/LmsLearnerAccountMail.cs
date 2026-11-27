using System;
using System.Globalization;
using System.Text;
using Resgrid.Localization.Areas.User.ContentNotices;

namespace Resgrid.Services
{
	/// <summary>The learner portal's account e-mails (round 9C).</summary>
	public enum LmsLearnerAccountMailKinds
	{
		/// <summary>An invitation (or its resend): choose a password.</summary>
		Invite = 0,

		/// <summary>Self-registration: confirm the e-mail address.</summary>
		VerifyEmail = 1,

		/// <summary>A password reset link.</summary>
		ResetPassword = 2,

		/// <summary>Someone registered with an address that already has an account (no enumeration: the mailbox owner is told).</summary>
		AlreadyRegistered = 3,

		/// <summary>An active learner asked for the access e-mail again: sign in or reset the password.</summary>
		SignInReminder = 4
	}

	/// <summary>One composed account e-mail: plain text in the culture it was written for.</summary>
	public sealed class LmsLearnerAccountMailText
	{
		public string Subject { get; set; }

		public string Body { get; set; }

		public CultureInfo Culture { get; set; }
	}

	/// <summary>
	/// Composes the learner portal's account e-mails (invitation, verification, reset, "already registered") from ContentNotices in the
	/// learner's language (round 9C; until then they were English). Pure, so the wording, language and link can be tested. The link sits on
	/// its own line so mail clients keep it whole; the lifetime is written in whole days (invitation) or hours (verification, reset).
	/// </summary>
	public static class LmsLearnerAccountMail
	{
		public static LmsLearnerAccountMailText Compose(LmsLearnerAccountMailKinds kind, string firstName, string departmentName, string url, TimeSpan lifetime,
			CultureInfo culture)
		{
			culture ??= ContentNoticeText.English;
			var body = new StringBuilder();
			string subject;
			switch (kind)
			{
				case LmsLearnerAccountMailKinds.Invite:
					subject = ContentNoticeText.Format("Account.Invite.Subject", culture, departmentName);
					Greeting(body, firstName, culture);
					body.Append(ContentNoticeText.Format("Account.Invite.Body", culture, departmentName)).Append("\r\n").Append(url).Append("\r\n\r\n");
					body.Append(ContentNoticeText.Format("Account.Invite.Lifetime", culture, (int)Math.Max(1, lifetime.TotalDays)));
					break;
				case LmsLearnerAccountMailKinds.VerifyEmail:
					subject = ContentNoticeText.Get("Account.Verify.Subject", culture);
					Greeting(body, firstName, culture);
					body.Append(ContentNoticeText.Get("Account.Verify.Body", culture)).Append("\r\n").Append(url).Append("\r\n\r\n");
					body.Append(ContentNoticeText.Format("Account.Verify.Lifetime", culture, (int)Math.Max(1, lifetime.TotalHours)));
					break;
				case LmsLearnerAccountMailKinds.ResetPassword:
					subject = ContentNoticeText.Get("Account.Reset.Subject", culture);
					Greeting(body, firstName, culture);
					body.Append(ContentNoticeText.Get("Account.Reset.Body", culture)).Append("\r\n").Append(url).Append("\r\n\r\n");
					body.Append(ContentNoticeText.Format("Account.Reset.Lifetime", culture, (int)Math.Max(1, lifetime.TotalHours)));
					break;
				case LmsLearnerAccountMailKinds.SignInReminder:
					subject = ContentNoticeText.Get("Account.Exists.Subject", culture);
					Greeting(body, firstName, culture);
					body.Append(ContentNoticeText.Get("Account.SignIn.Body", culture)).Append("\r\n").Append(url).Append("\r\n\r\n");
					body.Append(ContentNoticeText.Get("Account.Exists.Ignore", culture));
					break;
				default:
					subject = ContentNoticeText.Get("Account.Exists.Subject", culture);
					body.Append(ContentNoticeText.Get("Account.Exists.Body", culture)).Append("\r\n").Append(url).Append("\r\n\r\n");
					body.Append(ContentNoticeText.Get("Account.Exists.Ignore", culture));
					break;
			}

			return new LmsLearnerAccountMailText { Subject = subject, Body = body.ToString(), Culture = culture };
		}

		private static void Greeting(StringBuilder body, string firstName, CultureInfo culture)
		{
			if (!string.IsNullOrWhiteSpace(firstName))
				body.Append(ContentNoticeText.Format("Account.Greeting", culture, firstName.Trim())).Append("\r\n\r\n");
		}
	}
}
