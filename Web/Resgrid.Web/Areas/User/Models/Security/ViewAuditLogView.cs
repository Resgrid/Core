using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class ViewAuditLogView
	{
		public AuditLog AuditLog {get;set;}
		public Department Department {get;set;}
		public AuditLogTypes Type {get;set;}
	}
}
