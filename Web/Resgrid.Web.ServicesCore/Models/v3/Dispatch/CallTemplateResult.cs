using System;

namespace Resgrid.Web.Services.Models.v3.Dispatch
{
	public class CallTemplateResult
	{
		public int Id { get; set; }
		public bool IsDisabled { get; set; }
		public string Name { get; set; }
		public string CallName { get; set; }
		public string CallNature { get; set; }
		public string CallType { get; set; }
		public int CallPriority { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
	}
}
