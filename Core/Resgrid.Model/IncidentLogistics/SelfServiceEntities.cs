using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.IncidentLogistics
{
	// Generated from the Incident Back Office schema (large-incident-logistics-finance-plan.md rev 4.1, §11). Keep in step with
	// migration M0294 (both dialects).

	/// <summary>A scoped, expiring, revocable link to a participant's or vendor engagement's own page (plan §9.28). The token is stored hashed; the delivery target is protected.</summary>
	public class IncidentSelfServiceLink : IEntity, IIncidentLedgerEntity, IIncidentScopedEntity, IIncidentVersioned, IIncidentProtectable, IIncidentEditable
	{
		[Required]
		public string IncidentSelfServiceLinkId { get; set; }
		[Required]
		public int DepartmentId { get; set; }
		public string IncidentActivationId { get; set; }
		/// <summary><see cref="IncidentSelfServiceSubjects"/>.</summary>
		public int SubjectType { get; set; }
		public string SubjectId { get; set; }
		public string TokenHash { get; set; }
		public int Purposes { get; set; }
		public DateTime ExpiresOn { get; set; }
		public DateTime? RevokedOn { get; set; }
		public string RevokedByUserId { get; set; }
		public DateTime? LastUsedOn { get; set; }
		public int UseCount { get; set; }
		public string OneTimeCodeHash { get; set; }
		public DateTime? OneTimeCodeExpiresOn { get; set; }
		public DateTime? VerifiedOn { get; set; }
		/// <summary>ADP-protected (Pii, Contacts family).</summary>
		public string DeliveryTarget { get; set; }
		public int RowVersion { get; set; } = 1;
		public bool IsProtected { get; set; }
		public int? ProtectedCatalogVersion { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		[NotMapped] public string TableName => "IncidentSelfServiceLinks";
		[NotMapped] public string IdName => "IncidentSelfServiceLinkId";
		[NotMapped] public int IdType => 1;
		[NotMapped] [JsonIgnore] public object IdValue { get => IncidentSelfServiceLinkId; set => IncidentSelfServiceLinkId = (string)value; }
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
