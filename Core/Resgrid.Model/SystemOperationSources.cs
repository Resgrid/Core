namespace Resgrid.Model
{
	/// <summary>Who asked for a system operation. Persisted: never renumber.</summary>
	public enum SystemOperationSources
	{
		/// <summary>A staff member on the BackOffice System Operations page.</summary>
		BackOffice = 1,

		/// <summary>The worker found the cache sentinel missing: Redis came back without its data.</summary>
		CacheDataLossDetected = 2
	}
}
