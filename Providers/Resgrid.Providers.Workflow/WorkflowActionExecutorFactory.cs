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

		// All HTTP verb action types that are handled by a single HttpApiExecutor instance.
		private static readonly WorkflowActionType[] HttpActionTypes =
		[
			WorkflowActionType.CallApiGet,
			WorkflowActionType.CallApiPost,
			WorkflowActionType.CallApiPut,
			WorkflowActionType.CallApiDelete,
		];

		public WorkflowActionExecutorFactory(IEnumerable<IWorkflowActionExecutor> executors)
		{
			_executors = new Dictionary<WorkflowActionType, IWorkflowActionExecutor>();

			foreach (var executor in executors)
			{
				// Register the executor under its primary ActionType.
				_executors[executor.ActionType] = executor;

				// If this executor handles HTTP API calls, also register it under
				// all other HTTP verb action types so the factory can resolve any of them.
				if (HttpActionTypes.Contains(executor.ActionType))
				{
					foreach (var httpType in HttpActionTypes)
						_executors[httpType] = executor;
				}
			}
		}

		public IWorkflowActionExecutor GetExecutor(WorkflowActionType actionType)
		{
			if (_executors.TryGetValue(actionType, out var executor))
				return executor;

			throw new ArgumentException($"No executor registered for WorkflowActionType '{actionType}' ({(int)actionType}).", nameof(actionType));
		}
	}
}

