namespace Resgrid.Model
{
	public enum UnitLocationWriteStatus
	{
		Inserted = 0,
		Duplicate = 1
	}

	public class UnitLocationWriteResult
	{
		public UnitLocationWriteStatus Status { get; set; }
		public UnitsLocation Location { get; set; }

		public static UnitLocationWriteResult Inserted(UnitsLocation location) =>
			Create(UnitLocationWriteStatus.Inserted, location);

		public static UnitLocationWriteResult Duplicate(UnitsLocation location) =>
			Create(UnitLocationWriteStatus.Duplicate, location);

		private static UnitLocationWriteResult Create(UnitLocationWriteStatus status, UnitsLocation location)
		{
			return new UnitLocationWriteResult
			{
				Status = status,
				Location = location
			};
		}
	}
}
