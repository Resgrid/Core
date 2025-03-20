using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class StaffingReportRow
	{
		public string Name { get; set; }
		public string Group { get; set; }
		public string Roles { get; set; }
		public int ActionTypeId { get; set; }
		public int Staffing { get; set; }
		public string StaffingTimestamp { get; set; }
		public string LastAction { get; set; }
		public int NextStaffing { get; set; }
		public string NextStaffingTimestamp { get; set; }
		public string StaffingNote { get; set; }
	}

	public class StaffingReportView
	{
		public Department Department { get; set; }
		public DateTime RunOn { get; set; }
		public List<StaffingReportRow> Rows { get; set; }
		//public StaffingBreakdown StaffingBreakdown { get; set; }
		public List<Tuple<string,int>> CustomStaffingBreakdown { get; set; }

		public List<GroupBreakdown> GroupBreakdowns { get; set; }
		public CustomState CustomStaffing { get; set; }

		public void AddStaffing(int staffingId)
		{
			if (CustomStaffingBreakdown == null)
				CustomStaffingBreakdown = new List<Tuple<string, int>>();


			if (!CustomStaffingBreakdown.Any())
			{
				var data = new Tuple<string,int>(GetStaffingName(staffingId), 1);
				CustomStaffingBreakdown.Add(data);
			}
			else
			{
				Tuple<string, int> newData;

				var name = GetStaffingName(staffingId);
				var data = CustomStaffingBreakdown.FirstOrDefault(x => x.Item1 == name);

				if (data != null)
				{
					newData = new Tuple<string, int>(name, data.Item2 + 1);
					CustomStaffingBreakdown.Remove(data);
				}
				else
					newData = new Tuple<string, int>(name, 1);

				CustomStaffingBreakdown.Add(newData);
			}
		}

		public string GetStaffingName(int staffingId)
		{
			if (staffingId <= 25)
			{
				switch (((UserStateTypes) staffingId))
				{
					case UserStateTypes.Available:
						return "Available";
					case UserStateTypes.Delayed:
						return "Delayed";
					case UserStateTypes.Unavailable:
						return "Unavailable";
					case UserStateTypes.Committed:
						return "Committed";
					case UserStateTypes.OnShift:
						return "OnShift";
					default:
						throw new ArgumentOutOfRangeException(nameof(staffingId), staffingId, null);
				}
			}
			else
			{
				if (CustomStaffing != null)
				{
					var staffing = CustomStaffing.GetActiveDetails().FirstOrDefault(x => x.CustomStateDetailId == staffingId);

					if (staffing != null)
						return staffing.ButtonText;
				}
				else
				{
					return "Unknown";
				}
			}

			return String.Empty;
		}
	}

	public class StaffingBreakdown
	{
		public int Available { get; set; }
		public int Delayed { get; set; }
		public int Unavailable { get; set; }
		public int Committed { get; set; }
		public int OnShift { get; set; }
	}

	//public class GroupBreakdown
	//{
	//	public string Name { get; set; }
	//	public int Available { get; set; }
	//	public int Delayed { get; set; }
	//	public int Unavailable { get; set; }
	//	public int Committed { get; set; }
	//	public int OnShift { get; set; }
	//}

	public class GroupBreakdown
	{
		public string Name { get; set; }
		public List<Tuple<string, int>> CustomStaffingBreakdown { get; set; }

		public GroupBreakdown()
		{
			CustomStaffingBreakdown = new List<Tuple<string, int>>();
		}

		public void AddStaffing(string name)
		{
			if (CustomStaffingBreakdown == null)
				CustomStaffingBreakdown = new List<Tuple<string, int>>();


			if (!CustomStaffingBreakdown.Any())
			{
				var data = new Tuple<string, int>(name, 1);
				CustomStaffingBreakdown.Add(data);
			}
			else
			{
				Tuple<string, int> newData;

				var data = CustomStaffingBreakdown.FirstOrDefault(x => x.Item1 == name);

				if (data != null)
				{
					newData = new Tuple<string, int>(name, data.Item2 + 1);
					CustomStaffingBreakdown.Remove(data);
				}
				else
					newData = new Tuple<string, int>(name, 1);

				CustomStaffingBreakdown.Add(newData);
			}
		}
	}
}