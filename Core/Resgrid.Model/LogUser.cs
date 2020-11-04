using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{

	[Table("LogUsers")]
	public class LogUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LogUserId { get; set; }

		[ForeignKey("Log"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int LogId { get; set; }

		public virtual Log Log { get; set; }
		
		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? UnitId { get; set; }

		public virtual Unit Unit { get; set; }

		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }


		[NotMapped]
		public object IdValue
		{
			get { return LogUserId; }
			set { LogUserId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LogUsers";

		[NotMapped]
		public string IdName => "LogUserId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Log", "Unit", "User" };
	}
}
