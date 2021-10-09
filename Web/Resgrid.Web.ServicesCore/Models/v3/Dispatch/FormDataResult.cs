using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v3.Dispatch
{
	public class FormDataResult
	{
		public string Id { get; set; }

		public string Name { get; set; }

		public int Type { get; set; }

		public string Data { get; set; }

		public List<FormDataAutomationResult> Automations { get; set; }
	}

	public class FormDataAutomationResult
	{
		public string Id { get; set; }

		public string FormId { get; set; }

		public string TriggerField { get; set; }

		public string TriggerValue { get; set; }

		public int OperationType { get; set; }

		public string OperationValue { get; set; }
	}
}
