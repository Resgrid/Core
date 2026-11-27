using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	// Round 7B (product-owner decision 2026-11-25, M0402): e-mails to external learners about their public class places (enrolment confirmed,
	// waitlisted, promoted from the waitlist, the class reminder, the class changed or cancelled, the sign-off). Each e-mail is claimed before it
	// is sent (one row per learner, session, kind and key), so a retry, a second worker run or a racing request never sends it twice. Only to
	// active learners; reminders honour the learner's unsubscribe, transactional mails do not. LMS add-on (§17.2). Persisted enums append only.

	public enum LmsLearnerNoticeKinds
	{
		Enrolled = 0,
		Waitlisted = 1,
		Promoted = 2,
		Reminder = 3,
		Changed = 4,
		Cancelled = 5,
		/// <summary>The class was signed off: the learning record (and certificate) is on the learner's transcript.</summary>
		SignedOff = 6,
		/// <summary>An instructor recorded that the learner completed the class's course.</summary>
		CompletionRecorded = 7
	}

	/// <summary>The claim of one learner e-mail (the unique key makes it once only); <see cref="SentOn"/> when the mail went out.</summary>
	[Table("LmsLearnerNotices")]
	public class LmsLearnerNotice : IEntity
	{
		public const int KeyMaxLength = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLearnerNoticeId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsExternalLearnerId { get; set; }

		public int LmsClassSessionId { get; set; }

		/// <summary><see cref="LmsLearnerNoticeKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>What makes this notice distinct for its kind (<see cref="LmsLearnerNoticeRules.Key"/>): the enrolment, the start time, the change.</summary>
		public string NoticeKey { get; set; }

		public DateTime ClaimedOn { get; set; }

		public DateTime? SentOn { get; set; }

		/// <summary>Why it was not sent (opted out, no address, the mail failed); the claim stays, so it is not retried.</summary>
		public string Skipped { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLearnerNoticeId; }
			set { LmsLearnerNoticeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLearnerNotices";

		[NotMapped]
		public string IdName => "LmsLearnerNoticeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Pure rules of learner e-mails: the claim key, who may get which mail, the waitlist position.</summary>
	public static class LmsLearnerNoticeRules
	{
		/// <summary>
		/// The claim key: an enrolment notice is once per roster row and state change (a re-registration after withdrawing is a new one);
		/// a reminder once per start time (a moved class is reminded again); a change once per new time and place; a cancellation, sign-off
		/// and recorded completion once per session.
		/// </summary>
		public static string Key(LmsLearnerNoticeKinds kind, LmsClassSession session, LmsClassEnrollment enrollment)
		{
			var key = kind switch
			{
				LmsLearnerNoticeKinds.Enrolled or LmsLearnerNoticeKinds.Waitlisted or LmsLearnerNoticeKinds.Promoted =>
					"e:" + (enrollment?.LmsClassEnrollmentId ?? 0).ToString(CultureInfo.InvariantCulture) + ":" + Ticks(enrollment?.PromotedOn ?? enrollment?.EnrolledOn),
				LmsLearnerNoticeKinds.Reminder => "r:" + Ticks(session?.StartsOn),
				LmsLearnerNoticeKinds.Changed => "c:" + LmsClassRules.Hash(Ticks(session?.StartsOn) + "|" + Ticks(session?.EndsOn) + "|" + (session?.Location ?? string.Empty) + "|" +
					(session?.MeetingUrl ?? string.Empty)).Substring(0, 32),
				_ => kind.ToString().ToLowerInvariant()
			};
			return key.Length > LmsLearnerNotice.KeyMaxLength ? key.Substring(0, LmsLearnerNotice.KeyMaxLength) : key;
		}

		private static string Ticks(DateTime? value) => (value?.Ticks ?? 0).ToString(CultureInfo.InvariantCulture);

		/// <summary>A reminder is the only mail a learner can unsubscribe from (the others are about their own place).</summary>
		public static bool IsTransactional(LmsLearnerNoticeKinds kind) => kind != LmsLearnerNoticeKinds.Reminder;

		/// <summary>Why the learner gets no mail; null when they do. Never a removed, suspended, pending or erased learner, never without an address.</summary>
		public static string SkipReason(LmsExternalLearner learner, LmsLearnerNoticeKinds kind)
		{
			if (learner == null)
				return "no_learner";
			if (learner.Status != (int)LmsExternalLearnerStatuses.Active)
				return "inactive";
			if (string.IsNullOrWhiteSpace(learner.Email))
				return "no_address";
			if (!IsTransactional(kind) && learner.RemindersOptOut)
				return "opted_out";
			return null;
		}

		/// <summary>The learner's place on the waitlist (1 is next), counted over the rows still waiting; null when not waitlisted.</summary>
		public static int? WaitlistPosition(IEnumerable<LmsClassEnrollment> roster, LmsClassEnrollment mine)
		{
			if (mine == null || mine.Status != (int)LmsClassEnrollmentStatuses.Waitlisted)
				return null;
			var waiting = (roster ?? Enumerable.Empty<LmsClassEnrollment>()).Where(e => e != null && e.Status == (int)LmsClassEnrollmentStatuses.Waitlisted)
				.OrderBy(e => e.WaitlistPosition ?? int.MaxValue).ThenBy(e => e.EnrolledOn).ThenBy(e => e.LmsClassEnrollmentId).ToList();
			var index = waiting.FindIndex(e => e.LmsClassEnrollmentId == mine.LmsClassEnrollmentId);
			return index < 0 ? (int?)null : index + 1;
		}

		/// <summary>The notice a roster placement sends: enrolled, waitlisted, or none (a pending approval request).</summary>
		public static LmsLearnerNoticeKinds? PlacementNotice(LmsClassEnrollment enrollment) => enrollment?.Status switch
		{
			(int)LmsClassEnrollmentStatuses.Enrolled => LmsLearnerNoticeKinds.Enrolled,
			(int)LmsClassEnrollmentStatuses.Waitlisted => LmsLearnerNoticeKinds.Waitlisted,
			_ => null
		};

		/// <summary>A new random unsubscribe key (URL safe).</summary>
		public static string NewUnsubscribeKey() => LmsExternalLearnerRules.NewToken();
	}
}
