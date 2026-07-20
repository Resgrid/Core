using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// On-demand PTT tactical voice channels scoped to a single incident (§3.4). Built on top of the existing
	/// department Voice/PTT addon (<see cref="IVoiceService"/>); channels live for the duration of the incident
	/// and are visible to the units/users assigned to it.
	/// </summary>
	public interface IIncidentVoiceService
	{
		/// <summary>Whether the department has the PTT voice addon enabled.</summary>
		Task<bool> CanUseVoiceAsync(int departmentId);

		/// <summary>Creates an on-demand tactical channel scoped to the given call (gated on the voice addon).</summary>
		Task<DepartmentVoiceChannel> CreateIncidentChannelAsync(int departmentId, int callId, string name, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Gets the open on-demand tactical channels for a call.</summary>
		Task<List<DepartmentVoiceChannel>> GetChannelsForCallAsync(int departmentId, int callId);

		/// <summary>Closes (soft-close) all open on-demand tactical channels for a call.</summary>
		Task<bool> CloseIncidentChannelsForCallAsync(int departmentId, int callId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Records one completed PTT transmission (who keyed up, on which channel, start/end).</summary>
		Task<VoiceTransmissionLog> LogTransmissionAsync(VoiceTransmissionLog log, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Gets the transmission log for a call's incident channels, newest first.</summary>
		Task<List<VoiceTransmissionLog>> GetTransmissionLogForCallAsync(int departmentId, int callId);
	}
}
