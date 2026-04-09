using System.Collections.Generic;
using Resgrid.Model;

using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Training
{
	public class EditTrainingModel
	{
		public Resgrid.Model.Training Training { get; set; }
		public string Message { get; set; }
		public bool SendToAll { get; set; }
		public List<string> ExistingUserIds { get; set; }
	}
}
}