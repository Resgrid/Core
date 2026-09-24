using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The "nearest available unit" board: for an incident location, every unit (a team, an apparatus or an
	/// individual set up as a unit) and every responder in the viewer's dispatch scope, side by side with
	/// status, live position, ETA, shift coverage and role mix. Unlike the run card recommendation engine
	/// it selects nothing; it ranks everything so a dispatcher can choose.
	/// </summary>
	public interface INearestUnitService
	{
		Task<NearestUnitBoard> GetBoardAsync(NearestUnitRequest request, CancellationToken cancellationToken = default(CancellationToken));
	}
}
