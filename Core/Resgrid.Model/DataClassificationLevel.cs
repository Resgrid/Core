namespace Resgrid.Model
{
	/// <summary>
	/// Government/enterprise data classification levels for department security policy.
	/// </summary>
	public enum DataClassificationLevel
	{
		/// <summary>Unclassified — publicly releasable data.</summary>
		Unclassified = 0,

		/// <summary>Controlled Unclassified Information (CUI).</summary>
		Cui = 1,

		/// <summary>Confidential — restricted distribution.</summary>
		Confidential = 2
	}
}

