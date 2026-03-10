using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Templates
{
	/// <summary>
	/// Multiple call quick template result
	/// </summary>
	public class CallQuickTemplatesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallQuickTemplateResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallQuickTemplatesResult()
		{
			Data = new List<CallQuickTemplateResultData>();
		}
	}

	/// <summary>
	/// A call quick template used for rapidly dispatching pre-configured calls
	/// </summary>
	public class CallQuickTemplateResultData
	{
		/// <summary>
		/// Call Quick Template Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Is the template disabled
		/// </summary>
		public bool IsDisabled { get; set; }

		/// <summary>
		/// Display name of the template
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Name pre-filled for the call
		/// </summary>
		public string CallName { get; set; }

		/// <summary>
		/// Nature/description pre-filled for the call
		/// </summary>
		public string CallNature { get; set; }

		/// <summary>
		/// Type pre-filled for the call
		/// </summary>
		public string CallType { get; set; }

		/// <summary>
		/// Priority pre-filled for the call
		/// </summary>
		public int CallPriority { get; set; }

		/// <summary>
		/// The user id of who created the template
		/// </summary>
		public string CreatedByUserId { get; set; }

		/// <summary>
		/// When the template was created (UTC)
		/// </summary>
		public DateTime CreatedOn { get; set; }
	}
}

