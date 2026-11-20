using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>The destination the server fills into a new call (<see cref="IDestinationAutoFillService.ChooseDestinationAsync"/>).</summary>
	public class DestinationAutoFillChoice
	{
		/// <summary>The destination POI to fill (<see cref="Call.DestinationPoiId"/>).</summary>
		public int PoiId { get; set; }

		/// <summary>The POI's display name, for the call note.</summary>
		public string PoiName { get; set; }

		/// <summary>The destination rules that matched the call and applied.</summary>
		public List<string> RuleNames { get; set; } = new List<string>();
	}

	/// <summary>
	/// Server-side destination fill (plan D8): with the department's destination suggestions set to Suggest and fill, a new call
	/// nobody entered in a form (email import, chatbot, intake channels, voice intake, recurring schedules) takes the destination
	/// the department's destination rules suggest. Only an empty destination is ever filled (no destination POI, no free
	/// destination, no route), only when the call has a usable point, and only when at least one destination rule matched the
	/// call, so nothing is filled because nothing matched. <see cref="ICallsService.SaveCallAsync"/> asks before a new call is
	/// saved and records a system call note naming the destination.
	/// </summary>
	public interface IDestinationAutoFillService
	{
		/// <summary>The destination POI to fill into the new call, or null to leave it as it is. Never throws.</summary>
		Task<int?> ChooseAsync(Call call, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>As <see cref="ChooseAsync"/>, with the POI's name and the rules that matched. Never throws.</summary>
		Task<DestinationAutoFillChoice> ChooseDestinationAsync(Call call, CancellationToken cancellationToken = default(CancellationToken));
	}
}
