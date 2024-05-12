using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments;

public class DeleteDepartmentView
{
	public Department Department { get; set; }
	public QueueItem CurrentDeleteRequest { get; set; }
	public UserProfile Profile { get; set; }

	[Required]
	public bool AreYouSure { get; set; }
}
