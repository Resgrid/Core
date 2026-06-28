using Resgrid.Web.Services.Models.v4;

namespace Resgrid.Web.Services.Models.v4.Sync
{
	/// <summary>
	/// Delta sync payload for offline clients: incident-command rows changed since the client's cursor. The client
	/// stores <c>Data.ServerTimestampMs</c> and passes it back as the next `since`.
	/// </summary>
	public class SyncChangesResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommandChanges Data { get; set; }
	}

	/// <summary>
	/// Shift-start aggregate pull for offline clients: a render-ready board (incl. computed accountability / PAR) for
	/// every active incident in the caller's department, plus active ad-hoc resources and the next-sync cursor, in a
	/// single call. The client stores <c>Data.ServerTimestampMs</c> and passes it as the next <c>Changes</c> `since`.
	/// </summary>
	public class SyncBundleResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommandBundle Data { get; set; }
	}

	/// <summary>
	/// Shift-start REFERENCE payload: the slowly-changing department configuration + a safe personnel roster an IC/Unit
	/// app needs to start and run an incident offline. Pull once per shift / on manual refresh; the live incident state
	/// comes from /Sync/Bundle (active boards) and /Sync/Changes (deltas).
	/// </summary>
	public class SyncReferenceResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.SyncReferenceData Data { get; set; }
	}
}
