namespace Resgrid.Web.Areas.User.Models.Training
{
	public class NewTrainingModel
	{
		public Model.Training Training { get; set; }
		public string Message { get; set; }
		public bool SendToAll { get; set; }
	}
}