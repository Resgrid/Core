using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Department-owned sensitive personnel attributes that cannot safely stay on global UserProfile
	/// rows — a user in several departments cannot have one global row encrypted under one department
	/// key. One row per (DepartmentId, UserId). Values are plaintext until the department enrolls in
	/// ADP, after which cataloged columns carry rgdp envelopes; IsProtected/ProtectedCatalogVersion
	/// track per-row protection state for the migration cursor and the double-encryption guard.
	/// </summary>
	[Table("DepartmentMemberSensitiveData")]
	public class DepartmentMemberSensitiveData : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentMemberSensitiveDataId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(128)]
		public string UserId { get; set; }

		/// <summary>
		/// Stable random id bound into the AAD of every envelope on this row, so ciphertext cannot be
		/// moved between rows even inside the same department.
		/// </summary>
		[Required]
		[MaxLength(64)]
		public string ProtectionId { get; set; }

		/// <summary>Department-scoped employee/member identification number (moved off UserProfile).</summary>
		public string IdentificationNumber { get; set; }

		public string EmergencyContactName { get; set; }

		public string EmergencyContactPhone { get; set; }

		/// <summary>Free-form department-scoped notes about the member.</summary>
		public string Notes { get; set; }

		/// <summary>True when this row's cataloged values carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		/// <summary>Catalog version the row was protected under; null while plaintext.</summary>
		public int? ProtectedCatalogVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentMemberSensitiveDataId; }
			set { DepartmentMemberSensitiveDataId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentMemberSensitiveData";

		[NotMapped]
		public string IdName => "DepartmentMemberSensitiveDataId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
