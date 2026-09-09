using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;

namespace Resgrid.Model.Services
{
	public interface IChecklistAuthorizationService
	{
		Task RequireMemberAsync(ChecklistActor actor);
		Task<bool> CanManageAsync(ChecklistActor actor);
		Task<bool> CanReadAsync(ChecklistActor actor, ChecklistCompletion completion);
		Task<Func<ChecklistCompletion, Task<bool>>> ReadFilterAsync(ChecklistActor actor);
		Task<ChecklistTarget> TargetAsync(ChecklistActor actor, ChecklistTargetType type, string id);
		Task<List<ChecklistTarget>> TargetsAsync(ChecklistActor actor, ChecklistTargetType type);
	}
}
