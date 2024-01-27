namespace Resgrid.Model
{
	public class DepartmentPlanCount
	{
		public int UsersCount { get; set; }
		public int GroupsCount { get; set; }
		public int UnitsCount { get; set; }

		public int GetEntitiesCount()
		{
			return UsersCount + UnitsCount;
		}

		public int GetPersonnelLimit()
		{
			return UsersCount;
		}

		public int GetUnitsLimit()
		{
			return UnitsCount;
		}
	}
}
