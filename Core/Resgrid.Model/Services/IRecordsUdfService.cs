using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IRecordsUdfService
	{
		Task<UdfDefinition> GetForDesignerAsync(int departmentId, string userId, string key, int version);
		Task<RecordUdfSection> GetNewFormAsync(int departmentId, string userId, string key, int version);
		Task<int> GetVisibilityLevelAsync(int departmentId, string userId);
		Task<UdfDefinition> PublishAsync(int departmentId, string userId, string key, int version, string expectedDefinitionId, List<UdfField> fields, CancellationToken ct = default);
		/// <summary>Internal capture; entry points must project with current caller permissions before returning it.</summary>
		Task<RecordUdfSection> CaptureAsync(int departmentId, string recordId, string key, int version, string definitionId);
		Task<RecordUdfSection> ProjectAsync(int departmentId, string userId, RecordUdfSection section, bool mobile = false, bool reportLayout = false);
		/// <summary>Called only under the owning RMS header CAS transaction. Returns the immutable definition pin.</summary>
		Task<string> SaveInTransactionAsync(int departmentId, string userId, string recordId, string key, int version, string pinnedDefinitionId, RecordUdfInput input, CancellationToken ct);
		Task RestoreInTransactionAsync(int departmentId, string recordId, string key, int version, RecordUdfSection section, string userId, CancellationToken ct);
		void ValidateForFinalization(RecordUdfSection section);
	}
}
