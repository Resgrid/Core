namespace Resgrid.Model.Providers
{
	public interface IWorkflowActionExecutorFactory
	{
		IWorkflowActionExecutor GetExecutor(WorkflowActionType actionType);
	}
}

