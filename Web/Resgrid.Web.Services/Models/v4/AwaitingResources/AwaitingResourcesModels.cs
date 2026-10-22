using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Services.Models.v4.AwaitingResources
{
	/// <summary>
	/// Calls awaiting resources (conditional run cards): active calls still holding a required resource that was not
	/// available when they were dispatched, with what went instead and the units now available that match.
	/// </summary>
	public class AwaitingResourcesResult : StandardApiResponseV4Base
	{
		/// <summary>False when the department does not have conditional run cards; Data is then empty.</summary>
		public bool Enabled { get; set; }

		/// <summary>Response Data</summary>
		public List<AwaitingResourcesCall> Data { get; set; } = new List<AwaitingResourcesCall>();
	}

	/// <summary>A call's open outstanding requirements.</summary>
	public class CallOutstandingRequirementsResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public List<CallOutstandingRequirement> Data { get; set; } = new List<CallOutstandingRequirement>();
	}

	/// <summary>Dismisses an open requirement of a call.</summary>
	public class DismissOutstandingRequirementInput
	{
		/// <summary>The call the requirement belongs to.</summary>
		[Required]
		public int CallId { get; set; }

		/// <summary>The requirement (CallOutstandingRequirementId).</summary>
		[Required]
		public int RequirementId { get; set; }

		/// <summary>Optional note saying why it is no longer needed.</summary>
		[MaxLength(1000)]
		public string Note { get; set; }
	}

	/// <summary>Dispatches a unit to a call awaiting resources in one step.</summary>
	public class DispatchToAwaitingCallInput
	{
		/// <summary>The waiting call.</summary>
		[Required]
		public int CallId { get; set; }

		/// <summary>The unit to add to it.</summary>
		[Required]
		public int UnitId { get; set; }
	}

	/// <summary>Outcome of a dismiss or one-step dispatch.</summary>
	public class AwaitingResourcesActionResult : StandardApiResponseV4Base
	{
		/// <summary>Whether the action took effect.</summary>
		public bool Success { get; set; }

		/// <summary>Plain-English outcome for the dispatcher.</summary>
		public string Message { get; set; }

		/// <summary>How many requirements the dispatch resolved.</summary>
		public int Resolved { get; set; }
	}

	/// <summary>The department's response code catalog (optional; empty when the department uses none).</summary>
	public class ResponseCodesResult : StandardApiResponseV4Base
	{
		/// <summary>Response Data</summary>
		public DispatchResponseCodesConfig Data { get; set; } = new DispatchResponseCodesConfig();
	}
}
