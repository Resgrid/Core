using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Outcome of one department's pass of the legacy member-profile relocation (ADP plan section
	/// 5.1): what moved off the global UserProfiles row onto the department-scoped
	/// DepartmentMemberSensitiveData row, and what could not be moved this pass.
	/// </summary>
	public sealed class MemberProfileRelocationResult
	{
		public int DepartmentId { get; set; }

		/// <summary>Members whose relocation marker was still unset when the pass started.</summary>
		public int MembersExamined { get; set; }

		/// <summary>Members who had no department-scoped row until this pass created one.</summary>
		public int RowsCreated { get; set; }

		public int IdentificationNumbersMoved { get; set; }

		public int AddressesMoved { get; set; }

		/// <summary>
		/// Members whose move threw. Their marker stays unset, so the next pass retries them; the
		/// pass itself does not fail, because one bad member must not strand a whole department.
		/// </summary>
		public int Failures { get; set; }

		/// <summary>True when the pass moved or marked anything at all.</summary>
		public bool DidWork => MembersExamined > 0;

		public override string ToString() =>
			$"department {DepartmentId}: examined {MembersExamined}, created {RowsCreated}, ids {IdentificationNumbersMoved}, addresses {AddressesMoved}, failures {Failures}";
	}
}
