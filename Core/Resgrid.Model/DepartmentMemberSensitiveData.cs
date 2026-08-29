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
		/// Stable opaque row identifier, assigned once on create. The envelope AAD binds this row by
		/// its identity primary key (the row key every other protected table uses), so this column is
		/// not itself an AAD component — it exists so the row can be referred to without leaking the
		/// sequential key, and it is NOT NULL, so anything inserting a row must supply it.
		/// </summary>
		[Required]
		[MaxLength(64)]
		public string ProtectionId { get; set; }

		/// <summary>Department-scoped employee/member identification number (moved off UserProfile).</summary>
		public string IdentificationNumber { get; set; }

		/// <summary>Free-form department-scoped notes about the member.</summary>
		public string Notes { get; set; }

		// Department-scoped member addresses (plan 5.1). Deliberately columns rather than a link to
		// the shared Addresses table: that row has no owner and is reachable from contacts,
		// departments and stations too, so encrypting it for one department would break the others.
		public string HomeAddress1 { get; set; }

		public string HomeCity { get; set; }

		public string HomeState { get; set; }

		public string HomePostalCode { get; set; }

		public string HomeCountry { get; set; }

		public string MailingAddress1 { get; set; }

		public string MailingCity { get; set; }

		public string MailingState { get; set; }

		public string MailingPostalCode { get; set; }

		public string MailingCountry { get; set; }

		/// <summary>True when this row's cataloged values carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		/// <summary>Catalog version the row was protected under; null while plaintext.</summary>
		public int? ProtectedCatalogVersion { get; set; }

		/// <summary>
		/// When this member's legacy global-profile data (identification number and addresses) was
		/// moved onto this row. Null means the move is still outstanding, which is what the
		/// relocation worker sweeps for. Deliberately a marker rather than an emptiness check: a
		/// member who CLEARS their department identification number must not have the legacy value
		/// pushed back onto them by the next pass.
		/// </summary>
		public DateTime? LegacyProfileRelocatedOn { get; set; }

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
