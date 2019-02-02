using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class EmailTestInput
	{
		[Required]
		public string Hostname { get; set; }

		[Required]
		public string Username { get; set; }

		[Required]
		public string Password { get; set; }
		public int Port { get; set; }
		public bool Encryption { get; set; }
	}
}