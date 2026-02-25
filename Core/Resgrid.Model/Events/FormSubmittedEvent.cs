using System;

namespace Resgrid.Model.Events
{
	public class FormSubmittedEvent
	{
		public int DepartmentId { get; set; }
		public Form Form { get; set; }
		public string SubmittedData { get; set; }
		public string SubmittedByUserId { get; set; }
		public DateTime SubmittedOn { get; set; }
	}
}

