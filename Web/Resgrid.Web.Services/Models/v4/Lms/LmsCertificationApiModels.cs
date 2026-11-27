using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Lms
{
	// Round 9A (Omni "My certification progress"): the member's progress on the certifications backed by requirement sets.

	public class LmsCertificationItemProgressData
	{
		public int ItemId { get; set; }
		/// <summary>Course, Training, CategoryHours, Path or Evaluations.</summary>
		public string Kind { get; set; }
		public string Label { get; set; }
		/// <summary>A supervised practical: only a completion an authorized instructor recorded counts.</summary>
		public bool RequireRecordedByInstructor { get; set; }
		public bool Done { get; set; }
		public decimal Progress { get; set; }
		public decimal Required { get; set; }
		public DateTime? LastCompletedOn { get; set; }
		/// <summary>The counting completion was recorded by an instructor (show it as such, never as the member's own).</summary>
		public bool RecordedByInstructor { get; set; }
		/// <summary>The course, training or path id the member can open next (null for hours and evaluations).</summary>
		public int? TargetId { get; set; }
	}

	public class LmsCertificationProgressData
	{
		public int CertificationTypeId { get; set; }
		public string CertificationTypeName { get; set; }
		public string CertificationTypeCode { get; set; }
		/// <summary>The member's record the progress renews (null for a first issue).</summary>
		public int? CertificationId { get; set; }
		/// <summary>Department-local date.</summary>
		public DateTime? ExpiresOn { get; set; }
		public int? DaysUntilExpiry { get; set; }
		/// <summary>NotHeld, Current, DueSoon, InGrace, Lapsed, Blocked or Complete.</summary>
		public string State { get; set; }
		public int Done { get; set; }
		public int Total { get; set; }
		/// <summary>The department-local dates in which completions count.</summary>
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
		/// <summary>The expiry an issue or renewal now would give (department-local date), when the type expires.</summary>
		public DateTime? NextExpiresOn { get; set; }
		public bool IsRenewal { get; set; }
		public List<LmsCertificationItemProgressData> Items { get; set; } = new List<LmsCertificationItemProgressData>();
	}

	public class LmsCertificationProgressResult : StandardApiResponseV4Base
	{
		public List<LmsCertificationProgressData> Data { get; set; } = new List<LmsCertificationProgressData>();
	}
}
