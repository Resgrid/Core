namespace Resgrid.Model.Messages
{
	public class StandardPushCall
	{
		public int CallId { get; set; }
		public string Title { get; set; }
		public string SubTitle { get; set; }
		public int Priority { get; set; }
		public int ActiveCallCount { get; set; }
		public string Color { get; set; }
		public int? DepartmentId { get; set; }
		public string DepartmentCode { get; set; }

		/// <summary>Unit pushes only: the response code the unit was sent with ("C3"), when the department shows codes to crews.</summary>
		public string ResponseCode { get; set; }
	}
}
