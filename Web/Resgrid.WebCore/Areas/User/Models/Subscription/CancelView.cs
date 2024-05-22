using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Subscription
{
	public class CancelView
	{
		public Plan Plan { get; set; }
		public Payment Payment { get; set; }

		public string Reason { get; set; }

		[Required]
		public bool Confirm { get; set; }
	}
}