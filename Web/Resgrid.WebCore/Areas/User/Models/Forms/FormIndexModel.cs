using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Forms
{
	public class FormIndexModel
	{
		public List<Form> Forms { get; set; }

		public FormIndexModel()
		{
			Forms = new List<Form>();
		}
	}
}
