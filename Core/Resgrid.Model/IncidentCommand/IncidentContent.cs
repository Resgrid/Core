using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Audience for incident notes and attachments.</summary>
	public enum IncidentContentVisibility
	{
		Internal = 0,
		Public = 1
	}

	/// <summary>Operational classification for an incident status note.</summary>
	public enum IncidentNoteType
	{
		General = 0,
		SituationUpdate = 1,
		Containment = 2,
		ForwardProgress = 3,
		Safety = 4,
		Evacuation = 5,
		Shelter = 6,
		DamageAssessment = 7,
		PublicInformation = 8,
		ResourceNeed = 9,
		Weather = 10
	}

	/// <summary>
	/// A status update on an incident. Notes are retained for the operational record and may be marked public for
	/// inclusion in the incident's opt-in public information feed.
	/// </summary>
	public class IncidentNote : IEntity, IChangeTracked
	{
		public string IncidentNoteId { get; set; }
		public string IncidentCommandId { get; set; }
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public int NoteType { get; set; }
		public int Visibility { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }

		/// <summary>Optional structured containment value (0-100) for fire/disaster status reporting.</summary>
		public decimal? ContainmentPercent { get; set; }

		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? DeletedOn { get; set; }
		public string DeletedByUserId { get; set; }
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentNotes";

		[NotMapped]
		public string IdName => "IncidentNoteId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentNoteId; }
			set { IncidentNoteId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Incident-level file metadata and content. Binary data is intentionally excluded from JSON board/sync responses
	/// and is available only from the authenticated or public-token download endpoints.
	/// </summary>
	public class IncidentAttachment : IEntity, IChangeTracked
	{
		public string IncidentAttachmentId { get; set; }
		public string IncidentCommandId { get; set; }
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public int Visibility { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ContentLength { get; set; }
		public string Sha256Hash { get; set; }
		public string Description { get; set; }

		[JsonIgnore]
		[System.Text.Json.Serialization.JsonIgnore]
		public byte[] Data { get; set; }

		public string UploadedByUserId { get; set; }
		public DateTime UploadedOn { get; set; }
		public DateTime? DeletedOn { get; set; }
		public string DeletedByUserId { get; set; }
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentAttachments";

		[NotMapped]
		public string IdName => "IncidentAttachmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentAttachmentId; }
			set { IncidentAttachmentId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Safe public projection resolved from an enabled incident share token.</summary>
	public class IncidentPublicInformation
	{
		public string IncidentCommandId { get; set; }
		public DateTime EstablishedOn { get; set; }
		public int Status { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime? LastUpdatedOn { get; set; }
		public List<PublicIncidentNote> Notes { get; set; } = new List<PublicIncidentNote>();
		public List<PublicIncidentAttachment> Attachments { get; set; } = new List<PublicIncidentAttachment>();
	}

	/// <summary>Public-note projection that omits department, call, and user identifiers.</summary>
	public class PublicIncidentNote
	{
		public string IncidentNoteId { get; set; }
		public int NoteType { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public decimal? ContainmentPercent { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>Public attachment projection; the opaque id is used with the token-scoped download route.</summary>
	public class PublicIncidentAttachment
	{
		public string IncidentAttachmentId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ContentLength { get; set; }
		public string Sha256Hash { get; set; }
		public string Description { get; set; }
		public DateTime UploadedOn { get; set; }
	}
}
