using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
    [Table("InboundMessageEvents")]
    public class InboundMessageEvent : IEntity
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int InboundMessageEventId { get; set; }

        [Required]
        public int MessageType { get; set; }

        [Required]
        public string CustomerId { get; set; }

        [Required]
        public DateTime RecievedOn { get; set; }

        [Required]
        public string Data { get; set; }

        public string Type { get; set; }

        public bool? Processed { get; set; }

        [NotMapped]
        public object Id
        {
            get { return InboundMessageEventId; }
            set { InboundMessageEventId = (int)value; }
        }
    }
}
