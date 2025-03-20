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
	}
}
