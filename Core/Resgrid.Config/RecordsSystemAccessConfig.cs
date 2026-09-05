namespace Resgrid.Config
{
	/// <summary>
	/// Explicitly scoped Record access for system principals — the SMTP relay API key and the
	/// client_credentials service accounts (Identifier Allocation Registry section 4.4). A system principal
	/// receives no Record claim at all unless a grant below names its department, and the most a grant can
	/// ever give is <c>Record_View</c>: every mutating and restricted Record policy stays denied by
	/// construction, not by configuration. Empty (the default) means no system principal reads Records.
	/// Environment key: RESGRID:RecordsSystemAccessConfig:Grants.
	/// </summary>
	public static class RecordsSystemAccessConfig
	{
		/// <summary>
		/// Semicolon-delimited grants, each <c>departmentId|purpose|scope</c>, where scope is either
		/// <c>DepartmentWide</c> or <c>Groups:1,2,3</c>. The purpose is required, is written to the Record
		/// access audit on every read, and is what makes the grant explicit rather than ambient.
		/// Example: <c>12|NerisAudit|DepartmentWide;45|StationReporting|Groups:101,102</c>.
		/// </summary>
		public static string Grants = "";
	}
}
