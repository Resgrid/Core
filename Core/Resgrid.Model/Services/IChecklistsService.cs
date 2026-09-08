using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Services
{
	public interface IChecklistsService
	{
		Task<bool> CanManageAsync(ChecklistActor actor);
		Task<List<ChecklistDefinitionView>> ListAsync(ChecklistActor actor, int page = 0);
		Task<ChecklistDefinitionView> GetDefinitionAsync(ChecklistActor actor, string id);
		Task<string> SaveDefinitionAsync(ChecklistActor actor, string id, int revision, ChecklistForm form);
		Task PublishAsync(ChecklistActor actor, string id, int revision);
		Task RetireAsync(ChecklistActor actor, string id, int revision, bool delete = false);
		Task<List<ChecklistTarget>> TargetsAsync(ChecklistActor actor, ChecklistTargetType type);
		Task<string> StartAsync(ChecklistActor actor, string definitionId, string targetId, string completionId);
		Task<ChecklistRunView> GetRunAsync(ChecklistActor actor, string id);
		Task<List<ChecklistHistoryEntry>> HistoryAsync(ChecklistActor actor, string definitionId, int page = 0);
		Task<int> SaveRunAsync(ChecklistActor actor, string id, ChecklistRunInput input, bool submit);
		Task WitnessAsync(ChecklistActor actor, string id, string submissionHash, string attestation);
		Task AddFileAsync(ChecklistActor actor, string id, string itemId, string fileName, string contentType, byte[] data);
		Task<ChecklistCompletionFile> GetFileAsync(ChecklistActor actor, string id);
		Task DeleteFileAsync(ChecklistActor actor, string id);
	}
}
