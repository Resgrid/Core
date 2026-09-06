using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Read-only NFIRS rendering and crosswalk for a Call (RMS-3). Reads only; there is no import, no
	/// authoring and no NFIRS submission path anywhere in Records.
	/// </summary>
	public interface IRecordsNfirsLegacyService
	{
		/// <summary>Null when the Call does not exist in the department; throws UnauthorizedAccessException when the viewer cannot read the source Call.</summary>
		Task<NfirsLegacyRendering> RenderAsync(int departmentId, string viewerUserId, int callId);
	}
}
