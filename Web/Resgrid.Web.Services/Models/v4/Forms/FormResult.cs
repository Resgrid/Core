using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Forms
{
	/// <summary>
	/// Depicts a user created form.
	/// </summary>
	public class FormResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public FormResultData Data { get; set; }
	}

	/// <summary>
	/// A custom form (user created)
	/// </summary>
	public class FormResultData
	{
		/// <summary>
		/// Form Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Form Name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Type of the Form, i.e. Call
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Form JSON Data (i.e. the data needed to create the form)
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// Automations for the Form
		/// </summary>
		public List<FormDataAutomationResult> Automations { get; set; }
	}

	/// <summary>
	/// Form automations
	/// </summary>
	public class FormDataAutomationResult
	{
		/// <summary>
		/// Form automation id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Form Id the automation is for
		/// </summary>
		public string FormId { get; set; }

		/// <summary>
		/// Field name that triggers this automation
		/// </summary>
		public string TriggerField { get; set; }

		/// <summary>
		/// Value the field needs to be
		/// </summary>
		public string TriggerValue { get; set; }

		/// <summary>
		/// Auotmation operation type
		/// </summary>
		public int OperationType { get; set; }

		/// <summary>
		/// Automation operation value
		/// </summary>
		public string OperationValue { get; set; }
	}
}
