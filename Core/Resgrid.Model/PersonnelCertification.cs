using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("PersonnelCertifications")]
	public class PersonnelCertification : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelCertificationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[Required]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		[Required]
		public string Name { get; set; }

		public string Number { get; set; }

		public string Type { get; set; }

		public string Area { get; set; }

		public string IssuedBy { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime? RecievedOn { get; set; }

		public string Filetype { get; set; }

		public string Filename { get; set; }

		public byte[] Data { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PersonnelCertificationId; }
			set { PersonnelCertificationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PersonnelCertifications";

		[NotMapped]
		public string IdName => "PersonnelCertificationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "User" };
	}
}
