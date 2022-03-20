using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
    [Table("Affiliates")]
    public class Affiliate : IEntity
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AffiliateId { get; set; }

        public int? AffiliateMailingAddressId { get;set; }

        public string AffiliateCode { get; set; }

        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string EmailAddress { get; set; }

        public string CompanyOrDepartment { get; set; }

        public string Country { get; set; }

        public string Region { get; set; }

        public string TimeZone { get; set; }

        public string Experiance { get; set; }

        public string Qualifications { get; set; }

        public bool Approved { get; set; }

        public string RejectReason { get; set; }

        public string PayPalAddress { get; set; }

        public string TaxIdentifier { get; set; }

        public bool Active { get; set; }

        public string DeactivateReason { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime? ApprovedOn { get; set; }

        public DateTime? DeactivatedOn { get; set; }

		public Guid? UserId { get; set; }

		public bool Rejected { get; set; }

		public DateTime? RejectedOn { get; set; }

        [NotMapped]
        [JsonIgnore]
		public object IdValue
        {
            get { return AffiliateId; }
            set { AffiliateId = (int)value; }
        }

        [NotMapped]
        public string TableName => "Affiliates";

        [NotMapped]
        public string IdName => "AffiliateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
    }
}
