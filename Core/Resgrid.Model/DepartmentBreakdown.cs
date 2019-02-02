namespace Resgrid.Model
{
	public class DepartmentBreakdown
	{
		public int VolunteerFire { get; set; }
		public int CareerFire { get; set; }
		public int SearchAndRecue { get; set; }
		public int HAZMAT { get; set; }
		public int EMS { get; set; }
		public int Private { get; set; }
		public int Other { get; set; }
		public int Unknown { get; set; }

		public int TotalCount()
		{
			return VolunteerFire + CareerFire + SearchAndRecue + HAZMAT + EMS + Private + Other + Unknown;
		}
	}
}