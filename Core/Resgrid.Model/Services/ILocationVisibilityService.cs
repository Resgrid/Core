using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Applies the unit/personnel location visibility matrix to realtime location updates, with the same
	/// outcome as <see cref="IAuthorizationService.CanUserViewUnitLocationViaMatrixAsync"/> and
	/// <see cref="IAuthorizationService.CanUserViewPersonLocationViaMatrixAsync"/> but cheap enough to run
	/// for every location ping.
	/// </summary>
	public interface ILocationVisibilityService
	{
		/// <summary>Who may receive the unit's location updates.</summary>
		Task<LocationAudience> GetUnitLocationAudienceAsync(int departmentId, int unitId);

		/// <summary>
		/// Who may receive the person's location updates. A person may always see their own location, which
		/// is not part of the audience: deliver it to them separately when the audience is restricted.
		/// </summary>
		Task<LocationAudience> GetPersonnelLocationAudienceAsync(int departmentId, string userId);

		/// <summary>Every visibility set (unit or personnel) that lists the viewer.</summary>
		Task<IReadOnlyCollection<string>> GetVisibilitySetKeysForViewerAsync(int departmentId, string viewerUserId);

		Task<bool> CanViewUnitLocationAsync(int departmentId, int unitId, string viewerUserId);

		Task<bool> CanViewPersonnelLocationAsync(int departmentId, string userId, string viewerUserId);
	}
}
