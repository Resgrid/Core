using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// A department's own routing, live traffic and geocoding accounts (setting 125, Mapping Settings): decrypts them for
	/// the routing chain, geocoder chain and traffic layer, and validates and encrypts what an administrator saves.
	/// Decrypted values are kept in process memory per stored value, so key derivation runs once per change, not per call.
	/// </summary>
	public interface IDepartmentMapProvidersService
	{
		/// <summary>The department's accounts, decrypted; <see cref="DepartmentMapProviders.None"/> when it has none or for department 0.</summary>
		Task<DepartmentMapProviders> GetAsync(int departmentId);

		/// <summary>
		/// The errors <see cref="SaveAsync"/> would return for this update, without saving anything: for a page that saves
		/// other sections too and must refuse the whole save before any part of it is stored.
		/// </summary>
		Task<DepartmentMapProvidersSaveResult> ValidateAsync(int departmentId, DepartmentMapProvidersUpdate update, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Validates, encrypts and saves; nothing is saved when any error is returned.</summary>
		Task<DepartmentMapProvidersSaveResult> SaveAsync(int departmentId, DepartmentMapProvidersUpdate update, CancellationToken cancellationToken = default(CancellationToken));
	}
}
