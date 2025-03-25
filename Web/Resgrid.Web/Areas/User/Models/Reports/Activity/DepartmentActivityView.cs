using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Reports.Activity
{
	public class DepartmentActivityView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public List<Tuple<string, int>> CallTypeCount { get; set; }
		public List<Tuple<int, int>> TrainingMonthCount { get; set; }
		public int TotalCalls { get; set; }

		public List<PersonnelResponse> Responses { get; set; }
		public List<PersonnelTraining> Trainings { get; set; } 
	}

	public class PersonnelResponse
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int TotalCalls { get; set; }
		public List<Tuple<string, int>> CallTypeCount { get; set; }
	}

	public class PersonnelTraining
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public int Total { get; set; }
		public int Attended { get; set; }
	}
}