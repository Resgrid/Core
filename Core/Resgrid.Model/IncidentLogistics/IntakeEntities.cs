using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0296 (both dialects).

	/// <summary>Evidence arriving by email, upload, vendor portal or self-service page (plan §9.27). Unknown senders are quarantined; a person always files.</summary>
	public class IncidentIntakeItem : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentIntakeItemId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentIntakeChannels"/>.</summary>
		public int Channel { get; set; }
		/// <summary><see cref="IncidentIntakeSenderKinds"/>.</summary>
		public int SenderKind { get; set; }
		public string SenderReferenceId { get; set; }
		public DateTime ReceivedOn { get; set; }
		public int FileCount { get; set; }
		/// <summary><see cref="IncidentIntakeStatuses"/>.</summary>
		public int Status { get; set; }
		public string TargetType { get; set; }
		public string TargetId { get; set; }
		public string EvidenceArtifactId { get; set; }
		public DateTime? FiledOn { get; set; }
		public string FiledByUserId { get; set; }
		public string ProviderMessageId { get; set; }
		public string RejectReason { get; set; }
		public bool LooksLikeShiftTicket { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string SenderAddress { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string Subject { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentIntakeItems";
		[NotMapped] public string IdName => "IncidentIntakeItemId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentIntakeItemId; set => IncidentIntakeItemId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A file attached to an intake item; classified Restricted by default.</summary>
	public class IncidentIntakeFile : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity
	{
		[Required]
		public string IncidentIntakeFileId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		public string IncidentIntakeItemId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long Size { get; set; }
		public byte[] Data { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentIntakeFiles";
		[NotMapped] public string IdName => "IncidentIntakeFileId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentIntakeFileId; set => IncidentIntakeFileId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
