using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Realtime events for one signed-in session (passkey workbook section 7.4). Each connection with a session joins that
	/// session's group, so an event reaches only the app that session belongs to. Events carry no secret and no personal
	/// data; the app reads anything else through its own authenticated API calls.
	/// </summary>
	public static class SessionEvents
	{
		/// <summary>A Responder approval the session asked for was decided: <c>{ approvalRequestId, state }</c>.</summary>
		public const string MfaApprovalChanged = "mfaApprovalChanged";

		/// <summary>
		/// Internal: the session locked or ended, so every host closes its open connections now instead of at the next sweep.
		/// Never sent to a client.
		/// </summary>
		public const string SessionClosed = "sessionClosed";

		public static string GroupFor(string sessionId) => "session:" + sessionId;

		public static bool IsKnown(string name) => name is MfaApprovalChanged or SessionClosed;
	}

	public sealed class SessionEventMessage
	{
		public string Name { get; set; }
		public string ApprovalRequestId { get; set; }
		public string State { get; set; }
	}

	/// <summary>Publishes a session event to the hosts that hold realtime connections. Never throws; delivery is best effort.</summary>
	public interface ISessionEventPublisher
	{
		Task PublishAsync(string sessionId, SessionEventMessage message, CancellationToken cancellationToken = default);
	}
}
