using System.Collections.Generic;
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
        public object IdValue
        {
            get { return PushTemplateId; }
            set { PushTemplateId = (int)value; }
        }

        [NotMapped]
        public string TableName => "PushTemplates";

        [NotMapped]
        public string IdName => "PushTemplateId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName" };
    }
}
