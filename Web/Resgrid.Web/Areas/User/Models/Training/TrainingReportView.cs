using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Training
{
	public class TrainingReportView
	{
		public Model.Training Training { get; set; }
		public Dictionary<string, string> UserGroups { get; set; }
		public Department Department { get; set; }
	}
}