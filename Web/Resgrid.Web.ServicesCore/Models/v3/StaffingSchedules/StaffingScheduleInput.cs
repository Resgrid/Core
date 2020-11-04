namespace Resgrid.Web.Services.Controllers.Version3.Models.StaffingSchedules
{
	/// <summary>
	/// Input data to add a staffing schedule in the Resgrid system
	/// </summary>
	public class StaffingScheduleInput
	{
		/// <summary>
		/// Type of staffing schedule (Date = 1, Weekly = 2)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Specific Date to trigger (if Type = 1)
		/// </summary>
		public string Spd { get; set; }

		/// <summary>
		/// Specific time to trigger
		/// </summary>
		public string Spt { get; set; }

		/// <summary>
		/// State the staffing level will get set to
		/// </summary>
		public string Ste { get; set; }

		/// <summary>
		/// Trigger on Sunday (if Type - 2)
		/// </summary>
		public bool Sun { get; set; }

		/// <summary>
		/// Trigger on Monday (if Type - 2)
		/// </summary>
		public bool Mon { get; set; }

		/// <summary>
		/// Trigger on Tuesday (if Type - 2)
		/// </summary>
		public bool Tue { get; set; }

		/// <summary>
		/// Trigger on Wednesday (if Type - 2)
		/// </summary>
		public bool Wed { get; set; }

		/// <summary>
		/// Trigger on Thursday (if Type - 2)
		/// </summary>
		public bool Thu { get; set; }

		/// <summary>
		/// Trigger on Friday (if Type - 2)
		/// </summary>
		public bool Fri { get; set; }

		/// <summary>
		/// Trigger on Saturday (if Type - 2)
		/// </summary>
		public bool Sat { get; set; }

		/// <summary>
		/// Note for this staffing schedule
		/// </summary>
		public string Not { get; set; }
	}
}