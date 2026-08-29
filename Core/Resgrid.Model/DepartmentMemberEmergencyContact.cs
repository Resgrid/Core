using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// One emergency contact for a member, scoped to a department (ADP plan section 5.1). A member
	/// may have several, and the set is per department — UserProfile is global to the user and
	/// shared across every department they belong to, so it can neither be encrypted with a single
	/// department's key nor hold values that legitimately differ between departments.
	///
	/// The cataloged columns here ARE encrypted for a protected department. That does not conflict
	/// with the rule that member notification numbers stay plaintext: these numbers are next-of-kin
	/// reference data an authorized human reads, never an outbound channel handed to an SMS or voice
	/// provider.
	/// </summary>
	public class DepartmentMemberEmergencyContact : IEntity
	{
		public int DepartmentMemberEmergencyContactId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public string Name { get; set; }

		public string Relationship { get; set; }

		public string PhoneNumber { get; set; }

		public string AlternatePhoneNumber { get; set; }

		public string Email { get; set; }

		public string Notes { get; set; }

		/// <summary>The contact to try first; a member may mark at most one.</summary>
		public bool IsPrimary { get; set; }

		public int SortOrder { get; set; }

		public bool IsDeleted { get; set; }

		/// <summary>True once this row's cataloged columns carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public object IdValue
		{
			get { return DepartmentMemberEmergencyContactId; }
			set { DepartmentMemberEmergencyContactId = (int)value; }
		}

		public string TableName => "DepartmentMemberEmergencyContacts";

		public string IdName => "DepartmentMemberEmergencyContactId";

		public int IdType => 0;

		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
