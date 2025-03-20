using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class PersonnelHoursView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }
		public string Name { get; set; }

		public List<PersonnelCallHours> CallsHours { get; set; }
		public List<PersonnelWorkHours> WorkHours { get; set; }
		public List<PersonnelTrainingHours> TrainingHours { get; set; } 
	}

	public class PersonnelCallHours
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalCalls { get; set; }
		public double TotalSeconds { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return TimeSpan.FromSeconds(TotalSeconds);
		}
	}

	public class PersonnelWorkHours
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalWorkLogs { get; set; }
		public double TotalSeconds { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return TimeSpan.FromSeconds(TotalSeconds);
		}
	}

	public class PersonnelTrainingHours
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalTrainings { get; set; }
		public double TotalSeconds { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return TimeSpan.FromSeconds(TotalSeconds);
		}
	}
}