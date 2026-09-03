using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>Which rendition a DepartmentProfileMedia row holds (RMS plan section 4.10.1).</summary>
	public enum DepartmentProfileMediaKind
	{
		/// <summary>The re-encoded upload.</summary>
		PrimaryLogo = 1,
		/// <summary>Fitted to 1200x400 for print headers.</summary>
		PrintHeader = 2,
		/// <summary>188 px wide (the 94 px email slot at 2x density).</summary>
		EmailMasthead = 3,
		/// <summary>Fitted to 128x128.</summary>
		Thumbnail = 4,
		/// <summary>Optional print-specific art uploaded by an administrator.</summary>
		PrintHeaderOverride = 5
	}

	/// <summary>
	/// The department's branding media: the uploaded logo and its server-generated renditions (migration M0172).
	/// Bytes live in the row per the existing profile-image convention. MediaKey is the per-department opaque
	/// key for the anonymous email-masthead endpoint; regenerating it invalidates every previously sent link.
	/// </summary>
	[Table("DepartmentProfileMedia")]
	public class DepartmentProfileMedia : IEntity
	{
		[Key]
		[Required]
		public string DepartmentProfileMediaId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary><see cref="DepartmentProfileMediaKind"/>.</summary>
		public int Kind { get; set; }

		public string ContentType { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public long ByteSize { get; set; }

		public string Checksum { get; set; }

		public byte[] Data { get; set; }

		public string UploadedByUserId { get; set; }

		public DateTime UploadedOn { get; set; }

		public string MediaKey { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return DepartmentProfileMediaId; }
			set { DepartmentProfileMediaId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "DepartmentProfileMedia";

		[NotMapped]
		public string IdName => "DepartmentProfileMediaId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// The resolved identity block a print header, an email masthead or the profile page renders. Assembled from
	/// DepartmentProfile (falling back to the Department row) plus the media metadata; never carries bytes.
	/// </summary>
	public class DepartmentBranding
	{
		public int DepartmentId { get; set; }
		public DepartmentProfile Profile { get; set; }
		public string DisplayName { get; set; }
		public string ShortName { get; set; }
		public string Code { get; set; }
		public string AddressText { get; set; }
		public string PhoneNumber { get; set; }
		public string Website { get; set; }
		public bool UseDepartmentBrandingInEmails { get; set; }
		public string MediaKey { get; set; }
		public List<DepartmentProfileMedia> Media { get; set; } = new List<DepartmentProfileMedia>();

		public bool HasLogo => Media != null && Media.Exists(m => m.Kind == (int)DepartmentProfileMediaKind.PrimaryLogo);

		public DepartmentProfileMedia Rendition(DepartmentProfileMediaKind kind)
		{
			return Media?.Find(m => m.Kind == (int)kind);
		}
	}
}
