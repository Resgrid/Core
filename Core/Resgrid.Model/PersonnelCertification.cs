using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("PersonnelCertifications")]
	public class PersonnelCertification : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelCertificationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		[Required]
		public string Name { get; set; }

		public string Number { get; set; }

		public string Type { get; set; }

		public string Area { get; set; }

		public string IssuedBy { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? RecievedOn { get; set; }

		public string Filetype { get; set; }

		public string Filename { get; set; }

		public byte[] Data { get; set; }

		/// <summary>
		/// True when this row's cataloged values carry rgdp envelopes (ADP plan 5.1 Personnel family:
		/// certification numbers and documents). Drives the migration cursor and the
		/// double-encryption guard.
		/// </summary>
		public bool IsProtected { get; set; }

		// ---- Phase D typed records (M0213, additive; plan D1.3). Routing metadata only: never cataloged for ADP,
		// and the D1.7 evaluator reads nothing else off this row.

		/// <summary>The catalog type; null for a legacy free-text record.</summary>
		public int? DepartmentCertificationTypeId { get; set; }

		/// <summary><see cref="PersonnelCertificationStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime? StatusChangedOn { get; set; }

		public string StatusChangedByUserId { get; set; }

		/// <summary>Why the status was last changed (suspension / revocation reason). Not a cataloged field.</summary>
		public string StatusReason { get; set; }

		public string VerifiedByUserId { get; set; }

		public DateTime? VerifiedOn { get; set; }

		public bool IsDeleted { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsTyped => DepartmentCertificationTypeId.HasValue && DepartmentCertificationTypeId.Value > 0;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PersonnelCertificationId; }
			set { PersonnelCertificationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PersonnelCertifications";

		[NotMapped]
		public string IdName => "PersonnelCertificationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "User", "IsTyped" };
	}
}
