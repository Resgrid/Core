using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public class SystemAudit : IEntity
	{
		public string SystemAuditId { get; set; }

		public int Type { get; set; }

		public int System { get; set; }

		public int? DepartmentId { get; set; }

		public string UserId { get; set; }

		public string Username { get; set; }

		public string IpAddress { get; set; }

		public string Data { get; set; }

		public bool Successful { get; set; }

		public string ServerName { get; set; }

		public DateTime LoggedOn { get; set; }

		public object IdValue
		{
			get { return SystemAuditId; }
			set { SystemAuditId = value.ToString(); }
		}

		[NotMapped]
		public string TableName => "SystemAudits";

		[NotMapped]
		public string IdName => "SystemAuditId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
