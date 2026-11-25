using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// What the transcript, certificate and report documents print (TrainingDocuments, rendered to PDF through IPdfProvider).
	/// Labels come localized from the caller (resource key → text), dates are already in department time.
	/// </summary>
	public class TrainingDocumentContext
	{
		public string DepartmentName { get; set; }
		/// <summary>Resource key → localized text. A missing key prints the key.</summary>
		public IReadOnlyDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
		/// <summary>The department's time zone id, used to print dates.</summary>
		public string TimeZone { get; set; }
		public string Culture { get; set; } = "en";
		/// <summary>Right-to-left script (Arabic).</summary>
		public bool RightToLeft { get; set; }
		public DateTime GeneratedOnUtc { get; set; }
		public string GeneratedBy { get; set; }
	}

	/// <summary>One printable transcript line.</summary>
	public class TranscriptLine
	{
		public DateTime CompletedOn { get; set; }
		public string Title { get; set; }
		public string CourseCode { get; set; }
		public string Category { get; set; }
		public string Source { get; set; }
		public string Status { get; set; }
		public decimal Hours { get; set; }
		public decimal? CeHours { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public string Method { get; set; }
		public string Instructors { get; set; }
		public bool Counts { get; set; }
	}

	public class TranscriptDocument
	{
		public TrainingDocumentContext Context { get; set; } = new TrainingDocumentContext();
		public string MemberName { get; set; }
		public string MemberIdentifier { get; set; }
		public DateTime? FromUtc { get; set; }
		public DateTime? ToUtc { get; set; }
		public List<TranscriptLine> Lines { get; set; } = new List<TranscriptLine>();
		/// <summary>Hours per category name, counted records only.</summary>
		public Dictionary<string, decimal> HoursByCategory { get; set; } = new Dictionary<string, decimal>();
		public decimal TotalHours { get; set; }
	}

	/// <summary>A standard certificate of completion (free). The LMS adds a verification code and QR (dark, §24).</summary>
	public class CertificateDocument
	{
		public TrainingDocumentContext Context { get; set; } = new TrainingDocumentContext();
		public string MemberName { get; set; }
		public string Course { get; set; }
		public string CourseCode { get; set; }
		public DateTime CompletedOn { get; set; }
		public decimal Hours { get; set; }
		public decimal? CeHours { get; set; }
		public string CeProvider { get; set; }
		public string CeCourseNumber { get; set; }
		public string Category { get; set; }
		public string Instructors { get; set; }
		/// <summary>Who issued it ("Certificates say who issued them", §14): the department, and the signing officer when known.</summary>
		public string IssuedBy { get; set; }
		/// <summary>LMS only: printed with the verify URL and QR; null on the free certificate.</summary>
		public string VerificationCode { get; set; }
		public string VerificationUrl { get; set; }
		/// <summary>LMS only: a data: URL of the QR image for <see cref="VerificationUrl"/>.</summary>
		public string QrImageDataUrl { get; set; }
	}

	public class HoursReportDocument
	{
		public TrainingDocumentContext Context { get; set; } = new TrainingDocumentContext();
		public TrainingHoursReport Report { get; set; }
		/// <summary>The grouping's column title (Category, Member or Month), localized.</summary>
		public string GroupTitle { get; set; }
	}
}
