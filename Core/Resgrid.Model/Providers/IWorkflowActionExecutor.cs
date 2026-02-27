using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IWorkflowActionExecutor
	{
		WorkflowActionType ActionType { get; }

		Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken);
	}
}

