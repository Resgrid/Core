using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentCertificationTypes")]
	public class DepartmentCertificationType : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentCertificationTypeId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		/// <summary>Display name and the match key for untyped legacy records (plan D1.1 keeps it as the name).</summary>
		[Required]
		[MaxLength(100)]
		public string Type { get; set; }

		// ---- Phase D typed catalog (M0213, additive; plan D1.1). Code is the stable join key (decision 25):
		// unique per department while not deleted, backfilled from Type, immutable once a requirement references it.

		[MaxLength(50)]
		public string Code { get; set; }

		/// <summary><see cref="CertificationCategories"/>.</summary>
		public int Category { get; set; }

		/// <summary><see cref="CertificationAppliesTo"/>; immutable once records of the type exist.</summary>
		public int AppliesTo { get; set; }

		public string Description { get; set; }

		[MaxLength(200)]
		public string IssuingAuthority { get; set; }

		/// <summary>Suggested validity used to pre-fill a new record's expiry; null when unknown.</summary>
		public int? DefaultValidityMonths { get; set; }

		/// <summary>Records of this type never expire (FEMA ICS/IS courses, ELDT, road test).</summary>
		public bool NeverExpires { get; set; }

		/// <summary>Continuing-education hours a holder needs per renewal cycle (person-scoped only).</summary>
		public decimal? RenewalCreditHoursRequired { get; set; }

		/// <summary>New records stay PendingVerification until a supervisor signs them off (person-scoped only).</summary>
		public bool RequiresVerification { get; set; }

		public bool IsActive { get; set; } = true;

		public bool IsDeleted { get; set; }

		public DateTime? AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime? EditedOn { get; set; }

		public string EditedByUserId { get; set; }

		[NotMapped]
		public bool IsUnitScoped => AppliesTo == (int)CertificationAppliesTo.Unit;

		/// <summary>The display name: legacy Type text.</summary>
		[NotMapped]
		public string Name => Type;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentCertificationTypeId; }
			set { DepartmentCertificationTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentCertificationTypes";

		[NotMapped]
		public string IdName => "DepartmentCertificationTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "IsUnitScoped", "Name" };
	}
}
