using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department Profile elevation (RMS plan section 4.10.1): the profile row as the department's identity source
	/// and the logo with its renditions (DepartmentProfileMedia). Not gated by Records.System; branding is useful
	/// before activation and changes nothing for a department that never fills it in.
	/// </summary>
	public interface IDepartmentProfileMediaService
	{
		/// <summary>The profile row, created from the Department row on first use.</summary>
		Task<DepartmentProfile> GetOrCreateProfileAsync(int departmentId, CancellationToken cancellationToken = default);

		Task<DepartmentProfile> SaveProfileAsync(DepartmentProfile profile, CancellationToken cancellationToken = default);

		/// <summary>Identity block plus media metadata (no bytes); falls back to the Department row when no profile exists.</summary>
		Task<DepartmentBranding> GetBrandingAsync(int departmentId);

		/// <summary>Validates, re-encodes (metadata stripped) and stores the logo with its renditions; replaces any previous logo.</summary>
		Task<DepartmentBranding> UploadLogoAsync(int departmentId, string userId, string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default);

		Task<bool> RemoveLogoAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>Issues a new MediaKey, invalidating every previously sent masthead link.</summary>
		Task<string> RegenerateMediaKeyAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>A rendition with its bytes, for authenticated serving and print embedding.</summary>
		Task<DepartmentProfileMedia> GetMediaAsync(int departmentId, DepartmentProfileMediaKind kind);

		/// <summary>The EmailMasthead rendition for the anonymous endpoint; null when the key is unknown.</summary>
		Task<DepartmentProfileMedia> GetPublicMastheadAsync(string mediaKey);

		/// <summary>
		/// Masthead data for the department-scoped operational emails. Cached, and never throws: a branding lookup
		/// failure means the email goes out with Resgrid chrome, not that it goes unsent.
		/// </summary>
		Task<DepartmentEmailBranding> GetEmailBrandingAsync(int departmentId);
	}
}
