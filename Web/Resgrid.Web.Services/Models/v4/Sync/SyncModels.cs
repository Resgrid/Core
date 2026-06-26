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
}
