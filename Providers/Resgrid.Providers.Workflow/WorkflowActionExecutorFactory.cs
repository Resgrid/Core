using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Workflow
{
	public class WorkflowActionExecutorFactory : IWorkflowActionExecutorFactory
	{
		private readonly Dictionary<WorkflowActionType, IWorkflowActionExecutor> _executors;

		public WorkflowActionExecutorFactory(IEnumerable<IWorkflowActionExecutor> executors)
		{
			_executors = executors.ToDictionary(e => e.ActionType);
		}

		public IWorkflowActionExecutor GetExecutor(WorkflowActionType actionType)
		{
			if (_executors.TryGetValue(actionType, out var executor))
				return executor;

			throw new ArgumentException($"No executor registered for WorkflowActionType '{actionType}' ({(int)actionType}).", nameof(actionType));
		}
	}
}

