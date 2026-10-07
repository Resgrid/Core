using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Rules shared by every path that closes a call (web, v4 API, chatbot, the auto-close worker): a call that is
	/// being run under an active incident command cannot be closed, and an optional notice tells everyone on the
	/// call that it was closed.
	/// </summary>
	public interface ICallClosureService
	{
		/// <summary>
		/// The active incident command running the call, or null when the call can be closed. A call under an active
		/// command is closed only after the command itself is closed.
		/// </summary>
		Task<IncidentCommand> GetBlockingIncidentCommandAsync(int departmentId, int callId);

		/// <summary>
		/// Tells everyone attached to a call that it was closed: the personnel, groups and roles it was dispatched to
		/// (Responder app, SMS, email per their preferences), the units it was dispatched to (Unit app), and the incident
		/// command team — commander, role holders and the people and units on the command board (IC app). Each person is
		/// told once; the person who closed the call is skipped. Returns the number of people and units notified.
		/// The call must be loaded with its dispatch lists.
		/// </summary>
		Task<int> NotifyCallClosedAsync(Call call, string closedByUserId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
