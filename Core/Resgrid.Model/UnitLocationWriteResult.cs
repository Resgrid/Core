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

		public static UnitLocationWriteResult Inserted(UnitsLocation location)
		{
			return new UnitLocationWriteResult
			{
				Status = UnitLocationWriteStatus.Inserted,
				Location = location
			};
		}

		public static UnitLocationWriteResult Duplicate(UnitsLocation location)
		{
			return new UnitLocationWriteResult
			{
				Status = UnitLocationWriteStatus.Duplicate,
				Location = location
			};
		}
	}
}
