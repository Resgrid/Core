using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ICoreEventService
	{
		/// <summary>
		/// Publishes a real-time "incident command updated" notification for a call so connected IC clients
		/// re-sync the board (Tablet Command-style Real Time Sync). Fans out via the CQRS/eventing pipeline
		/// to the per-department SignalR group.
		/// </summary>
		Task IncidentCommandUpdatedAsync(int departmentId, int callId);
	}
}
