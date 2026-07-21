using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Fans incident-command changes out to the affected Resgrid users and units over their configured
	/// channels (push/email/SMS via <c>ICommunicationService</c> for users, push via <c>IPushService</c>
	/// for units). Every method is best-effort: failures are logged and never propagate to the caller,
	/// so a notification outage can't fail the underlying command mutation.
	/// </summary>
	public interface IIncidentCommandNotificationService
	{
		/// <summary>Notifies the assigned resource (user or unit) it was assigned to a lane (or tracked on the incident when the lane is empty).</summary>
		Task NotifyResourceAssignedAsync(ResourceAssignment assignment, string laneName, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Notifies the moved resource (user or unit) it changed lanes.</summary>
		Task NotifyResourceMovedAsync(ResourceAssignment assignment, string fromLaneName, string toLaneName, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Notifies the released resource (user or unit) it was released from the incident.</summary>
		Task NotifyResourceReleasedAsync(ResourceAssignment assignment, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Notifies every active user and unit on the incident that a lane lead changed: who is going off
		/// and who is coming on. Lead user ids resolve to profile names; external leads use the entered name.
		/// </summary>
		Task NotifyLaneLeadChangedAsync(int departmentId, int callId, string laneName, bool isPrimary,
			string previousLeadUserId, string previousLeadName, string newLeadUserId, string newLeadName,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Notifies every active user and unit on the incident (plus both commanders) that command was transferred.</summary>
		Task NotifyCommandTransferredAsync(IncidentCommand command, string fromUserId, string toUserId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
