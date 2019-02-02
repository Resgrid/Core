namespace Resgrid.Model
{
	public class TextCommandPayload
	{
		public TextCommandTypes Type { get; set; }
		public string Data { get; set; }

		public ActionTypes GetActionType()
		{
			return (ActionTypes) int.Parse(Data);
		}

		public UserStateTypes GetStaffingType()
		{
			return (UserStateTypes)int.Parse(Data);
		}

		public int GetCustomActionType()
		{
			return int.Parse(Data);
		}

		public int GetCustomStaffingType()
		{
			return int.Parse(Data);
		}
	}
}