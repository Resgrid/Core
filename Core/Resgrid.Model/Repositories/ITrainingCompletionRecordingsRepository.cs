using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Round 7B (M0400): recorded completions (permission 135). Every read starts at the department.</summary>
	public interface ITrainingCompletionRecordingsRepository : IRepository<TrainingCompletionRecording>
	{
		Task<TrainingCompletionRecording> GetByIdForDepartmentAsync(int departmentId, int trainingCompletionRecordingId);
		Task<List<TrainingCompletionRecording>> GetByIdsAsync(int departmentId, IEnumerable<int> trainingCompletionRecordingIds);
		Task<List<TrainingCompletionRecording>> GetForUserAsync(int departmentId, string userId);
		Task<List<TrainingCompletionRecording>> GetForTargetAsync(int departmentId, int targetKind, int targetId);
		Task<List<TrainingCompletionRecording>> GetForSourceAsync(int departmentId, int sourceKind, string sourceRefId);
	}
}
