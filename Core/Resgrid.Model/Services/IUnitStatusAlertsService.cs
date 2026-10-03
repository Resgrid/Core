using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Acknowledging, muting and annotating the alerts a unit raises when it sits in a status longer than its
	/// department's <see cref="UnitStatusThresholds"/> allow.
	/// </summary>
	public interface IUnitStatusAlertsService
	{
		/// <summary>
		/// The uncleared acknowledgements for each unit's current status episode. Acknowledgements from episodes
		/// that ended when a unit changed status are not returned.
		/// </summary>
		Task<List<UnitStatusAlertAcknowledgement>> GetCurrentAcknowledgementsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Acknowledges or mutes the alert for the unit's current status episode, replacing any earlier
		/// acknowledgement of that episode.
		/// </summary>
		/// <param name="unitStateId">The state the caller saw. If the unit has moved on since, nothing is written.</param>
		/// <param name="level">The level the caller saw. It is capped at the level the server measures, so an alert cannot be pre-acknowledged.</param>
		/// <param name="muteMinutes">For a mute: minutes to mute for, or 0 to mute until the status changes.</param>
		Task<UnitStatusAlertAcknowledgementResult> AcknowledgeAsync(int departmentId, int unitId, int unitStateId, UnitStatusAlertLevels level,
			UnitStatusAlertAcknowledgementModes mode, int muteMinutes, string note, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>One acknowledgement, cleared or not. Null when it does not exist or belongs to another department.</summary>
		Task<UnitStatusAlertAcknowledgement> GetAcknowledgementByIdAsync(int departmentId, string acknowledgementId);

		/// <summary>Withdraws an acknowledgement, so the alert shows as unacknowledged again.</summary>
		Task<UnitStatusAlertAcknowledgementResult> ClearAsync(int departmentId, string acknowledgementId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
