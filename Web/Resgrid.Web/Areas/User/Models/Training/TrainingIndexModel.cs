using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Training
{
	public class TrainingIndexModel
	{
		public List<Model.Training> Trainings { get; set; }

		/// <summary>Which of the listed trainings the signed-in member manages; the row actions follow it.</summary>
		public TrainingManagementScope Scope { get; set; }
	}
}
