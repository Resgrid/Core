using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Shared handling for the inbound-SMS department SWITCH flow — used by both the Twilio and
	/// SignalWire controllers so the plan-gate/STOP/switch behavior and reply wording stay identical
	/// across providers. Methods return the reply body text; the caller wraps it in the
	/// provider-specific response (TwiML or LaML).
	/// </summary>
	public interface ITextDepartmentSwitchService
	{
		/// <summary>
		/// The identified user's ACTIVE department failed the inbound-text plan gate. STOP is honored
		/// first (opt-out must always work), a SWITCH command is processed, any other text gets the
		/// switch options, and with no supported alternatives the plan message. Never returns null.
		/// </summary>
		Task<string> BuildUnsupportedActiveDepartmentResponseAsync(string messageText, int departmentId, UserProfile profile,
			string logPrefix, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Processes a SWITCH [name/number] command: changes the user's active department when the
		/// identifier resolves to one of their SMS-supported memberships (list index, department id,
		/// then exact/partial name or code). Empty identifier returns the options list. Never returns null.
		/// </summary>
		Task<string> BuildSwitchCommandResponseAsync(UserProfile profile, string departmentIdentifier,
			string logPrefix, CancellationToken cancellationToken = default(CancellationToken));
	}
}
