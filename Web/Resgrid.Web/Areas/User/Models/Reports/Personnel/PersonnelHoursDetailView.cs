using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Personnel
{
	public class PersonnelHoursDetailView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }

		public List<CallDetail> CallDetails { get; set; }
		public List<WorkDetail> WorkDetails { get; set; }
		public List<TrainingDetail> TrainingDetails { get; set; } 
	}

	public class CallDetail
	{
		public string CallNumber { get; set; }
		public string Name { get; set; }
		public string Unit { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return (End - Start);
		}
	}

	public class TrainingDetail
	{
		public string Name { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return (End - Start);
		}
	}

	public class WorkDetail
	{
		public string Name { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		public TimeSpan GetTimeSpan()
		{
			return (End - Start);
		}
	}
}
