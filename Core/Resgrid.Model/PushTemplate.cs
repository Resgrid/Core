using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
    [Table("PushTemplates")]
    public class PushTemplate : IEntity
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PushTemplateId { get; set; }

        [Required]
        public int PlatformType { get; set; }

        [MaxLength(1000)]
        public string Template { get; set; }

        [NotMapped]
        public object Id
        {
            get { return PushTemplateId; }
            set { PushTemplateId = (int)value; }
        }
    }
}
