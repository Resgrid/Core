using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Protocol version translations (P3, M0362). Every read starts at the department; header reads never select content.</summary>
	public interface IProtocolVersionTranslationsRepository : IRepository<ProtocolVersionTranslation>
	{
		/// <summary>The version's translations (every status), content included only when asked.</summary>
		Task<List<ProtocolVersionTranslation>> GetForVersionAsync(int departmentId, int protocolVersionId, bool withContent = false);

		/// <summary>The approved translation headers of the given versions (the offline manifest, language pickers).</summary>
		Task<List<ProtocolVersionTranslation>> GetApprovedHeadersForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds);

		Task<ProtocolVersionTranslation> GetByIdForDepartmentAsync(int departmentId, int protocolVersionTranslationId);

		/// <summary>Saves the translation when its row version is still <paramref name="expectedRowVersion"/>, bumping it; false otherwise.</summary>
		Task<bool> TryUpdateAsync(ProtocolVersionTranslation translation, long expectedRowVersion, CancellationToken cancellationToken = default);
	}
}
