using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Builds a dynamic template context (as a dictionary) for Scriban rendering.
	/// Returns an object that is cast to Scriban.Runtime.ScriptObject in the service layer.
	/// </summary>
	public interface IWorkflowTemplateContextBuilder
	{
		/// <summary>
		/// Builds the full Scriban template context for the given department, event type and serialized payload.
		/// Returns a <c>Scriban.Runtime.ScriptObject</c> as <c>object</c> to avoid a Scriban dependency on Resgrid.Model.
		/// </summary>
		Task<object> BuildContextAsync(
			int departmentId,
			WorkflowTriggerEventType eventType,
			string eventPayloadJson,
			CancellationToken cancellationToken);

		/// <summary>Returns a list of available variable names for the given event type (for UI documentation).</summary>
		IReadOnlyList<TemplateVariableDescriptor> GetVariableDescriptors(WorkflowTriggerEventType eventType);
	}
}

