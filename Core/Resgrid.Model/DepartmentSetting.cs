using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentSettings")]
	public class DepartmentSetting : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentSettingId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public Department Department { get; set; }

		[Required]
		public int SettingType { get; set; }

		[Required]
		public string Setting { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentSettingId; }
			set { DepartmentSettingId = (int)value; }
		}
	}
}
