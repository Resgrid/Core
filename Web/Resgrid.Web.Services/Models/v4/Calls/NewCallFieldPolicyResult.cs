using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// The department's new-call form policy: which built-in fields the call form shows, and which it
	/// requires before a call can be created.
	/// </summary>
	public class NewCallFieldPolicyResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public NewCallFieldPolicyResultData Data { get; set; } = new NewCallFieldPolicyResultData();
	}

	public class NewCallFieldPolicyResultData
	{
		/// <summary>
		/// Rules for the fields the department has configured. A field with no rule here is visible and
		/// optional, which is how Resgrid behaved before this setting existed -- so an empty list means
		/// "stock form" and clients should not hide or require anything.
		/// </summary>
		public List<NewCallFieldRuleData> Rules { get; set; } = new List<NewCallFieldRuleData>();
	}

	public class NewCallFieldRuleData
	{
		/// <summary>
		/// Stable field key. See Resgrid.Model.NewCallFieldKeys for the full set.
		/// </summary>
		public string Key { get; set; }

		/// <summary>False hides the field from the call form entirely.</summary>
		public bool Visible { get; set; }

		/// <summary>True blocks call creation until the field has a value.</summary>
		public bool Required { get; set; }
	}
}
