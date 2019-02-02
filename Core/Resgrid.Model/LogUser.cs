using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;

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
		public object Id
		{
			get { return LogUserId; }
			set { LogUserId = (int)value; }
		}
	}

	public class LogUser_Mapping : EntityTypeConfiguration<LogUser>
	{
		public LogUser_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
