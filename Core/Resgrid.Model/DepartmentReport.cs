using System;

namespace Resgrid.Model
{
	public class DepartmentReport
	{
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public DateTime? CreatedOn { get; set; }
		public int Groups { get; set; }
		public int Users { get; set; }
		public int Units { get; set; }
		public int Calls { get; set; }
		public int Roles { get; set; }
		public int Notifications { get; set; }
		public int UnitTypes { get; set; }
		public int CallTypes { get; set; }
		public int CertTypes { get; set; }
		public int Settings { get; set; }
	}
}