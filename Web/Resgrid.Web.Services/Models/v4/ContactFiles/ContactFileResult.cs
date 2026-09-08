using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.ContactFiles
{
	/// <summary>
	/// Files attached to a contact (site documents, pre-plans, floor plans, photos, drawings).
	/// </summary>
	public class ContactFilesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Data payload
		/// </summary>
		public List<ContactFileResultData> Data { get; set; } = new List<ContactFileResultData>();
	}

	/// <summary>
	/// One file attached to a contact. Metadata always; Data only when requested.
	/// </summary>
	public class ContactFileResultData
	{
		/// <summary>
		/// ADP: true when this row belongs to a protection-enforced department (shield indicator).
		/// Protected values here are broker-decrypted plaintext or the exact "REDACTED" placeholder
		/// — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when values are redacted (step_up_required,
		/// grant_expired, grant_revoked, protected_access_denied, broker_unavailable); null when
		/// nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>ADP: stable catalog field ids (catalog v12) whose values are REDACTED on this row.</summary>
		public List<string> RedactedFields { get; set; } = new List<string>();

		/// <summary>Id of the contact file</summary>
		public string Id { get; set; }

		/// <summary>Id of the Contact</summary>
		public string ContactId { get; set; }

		/// <summary>Type of the file (Document = 0, PrePlan = 1, FloorPlan = 2, SitePhoto = 3, SiteDrawing = 4, Other = 5)</summary>
		public int Type { get; set; }

		/// <summary>Type display name</summary>
		public string TypeName { get; set; }

		/// <summary>User friendly title of the file</summary>
		public string Name { get; set; }

		/// <summary>Original file name</summary>
		public string FileName { get; set; }

		/// <summary>Mime type</summary>
		public string Mime { get; set; }

		/// <summary>Size in bytes</summary>
		public int Size { get; set; }

		/// <summary>Base64 file data (only when includeData was requested)</summary>
		public string Data { get; set; }

		/// <summary>
		/// Signed, expiring anonymous URL to download the file instead of using Data. Regenerated on every
		/// authenticated list call; do not persist it (RMS plan: an authorized capture copies the evidence).
		/// Null for a protected department's enveloped file: the anonymous route can never carry a grant, so
		/// attended clients fetch the bytes through GetFilesForContact with includeData and their grant instead.
		/// </summary>
		public string Url { get; set; }

		/// <summary>User Id of the person who uploaded the file</summary>
		public string UserId { get; set; }

		/// <summary>When the file was added (department time)</summary>
		public string Timestamp { get; set; }
	}

	/// <summary>
	/// Input to attach a file to a contact
	/// </summary>
	public class UploadContactFileInput
	{
		[Required]
		public string ContactId { get; set; }

		/// <summary>Type of the file (Document = 0, PrePlan = 1, FloorPlan = 2, SitePhoto = 3, SiteDrawing = 4, Other = 5)</summary>
		public int Type { get; set; }

		/// <summary>User friendly title (defaults to the file name)</summary>
		public string Name { get; set; }

		/// <summary>Original file name, with extension</summary>
		[Required]
		public string FileName { get; set; }

		/// <summary>Base64 encoded file contents</summary>
		[Required]
		public string Data { get; set; }
	}

	public class UploadContactFileResult : StandardApiResponseV4Base
	{
		/// <summary>Id of the new contact file</summary>
		public string Id { get; set; }
	}

	public class DeleteContactFileResult : StandardApiResponseV4Base
	{
	}
}
