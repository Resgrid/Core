using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Dispatch
{
	/// <summary>
	/// DCall Templates for Quick Dispatch
	/// </summary>
	public class GetCallTemplatesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<GetCallTemplatesResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public GetCallTemplatesResult()
		{
			Data = new List<GetCallTemplatesResultData>();
		}
	}

	/// <summary>
	/// Call Template
	/// </summary>
	public class GetCallTemplatesResultData
	{
		/// <summary>
		/// Call Template Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Is template disabled
		/// </summary>
		public bool IsDisabled { get; set; }

		/// <summary>
		/// Call Template name
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Name the call
		/// </summary>
		public string CallName { get; set; }

		/// <summary>
		/// Nature of the call
		/// </summary>
		public string CallNature { get; set; }

		/// <summary>
		/// Type of the call
		/// </summary>
		public string CallType { get; set; }

		/// <summary>
		/// Priority of the call
		/// </summary>
		public int CallPriority { get; set; }

		/// <summary>
		/// Who created the template
		/// </summary>
		public string CreatedByUserId { get; set; }

		/// <summary>
		/// When was it created
		/// </summary>
		public DateTime CreatedOn { get; set; }
	}
}
