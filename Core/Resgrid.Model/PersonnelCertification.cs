using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
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
		public object Id
		{
			get { return PersonnelCertificationId; }
			set { PersonnelCertificationId = (int)value; }
		}
	}

	public class PersonnelCertification_Mapping : EntityTypeConfiguration<PersonnelCertification>
	{
		public PersonnelCertification_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}
