namespace Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities
{
	public class CallPriorityResult
	{
		public int Id { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Color { get; set; }
		public int Sort { get; set; }
		public bool IsDeleted { get; set; }
		public bool IsDefault { get; set; }
		public int Tone { get; set; }
	}
}
