namespace Resgrid.Config
{
	/// <summary>
	/// Config settings for the operation of the auditing sub-system
	/// </summary>
	public static class AuditConfig
	{
		/// <summary>
		/// Connection string to the audit database
		/// </summary>
		public static string ConnectionString = "Server=rgdevserver;Database=ResgridAudit;User Id=resgrid_audit;Password=resgrid123;MultipleActiveResultSets=True;";
	}
}
