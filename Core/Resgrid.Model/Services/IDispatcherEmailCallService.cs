using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// What an emailed dispatch to the department's dispatch address becomes (<see cref="DispatcherDeskConfig.EmailCallMode"/>,
	/// setting 128, dispatchers-on-duty-plan.md D10): dispatched at once (as before), or held as a pending call for a dispatcher,
	/// optionally read by Enhanced AI. Shared by the Postmark dispatch branch (EmailController) and the POP importer
	/// (CallEmailImporterLogic). Group dispatch addresses are not affected.
	/// </summary>
	public interface IDispatcherEmailCallService
	{
		/// <summary>The department's email call mode; AutoDispatch (the behaviour before the setting) when it is missing or cannot be read.</summary>
		Task<DispatcherEmailCallModes> GetModeAsync(int departmentId);

		/// <summary>Holds a new emailed call for a dispatcher: Pending, no dispatch time, not dispatched. Its parsed dispatch lists are kept.</summary>
		void HoldAsPending(Call call);

		/// <summary>
		/// Whether the saved call gets AI enrichment: the department's AI email format (as before), or EnhancedAi mode while Enhanced
		/// AI dispatch is available to the department.
		/// </summary>
		Task<bool> ShouldEnrichAsync(int departmentId, int formatType, DispatcherEmailCallModes mode);

		/// <summary>
		/// A pending emailed call was saved: puts it on the dispatcher desk as an email (NotRouted is fine, the call still waits in
		/// Pending Calls) and refreshes dispatcher screens (CallUpdatedEvent, as v4 SaveCall does for a pending call). Never throws.
		/// </summary>
		Task OnPendingCallSavedAsync(Call savedCall, string sender, string subject, CancellationToken cancellationToken = default);
	}
}
