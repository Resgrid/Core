using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Addresses")]
	public class Address : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int AddressId { get; set; }

		[Required]
		[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
		[Display(Name = "Street Address")]
		[ProtoMember(2)]
		public string Address1 { get; set; }

		[Required]
		[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
		[ProtoMember(3)]
		public string City { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
		[ProtoMember(4)]
		public string State { get; set; }

		[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
		[ProtoMember(5)]
		public string PostalCode { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
		[ProtoMember(6)]
		public string Country { get; set; }

		[NotMapped]
		public string TableName => "Addresses";

		[NotMapped]
		public string IdName => "AddressId";

		[NotMapped]
		public int IdType => 0;

		public string FormatAddress()
		{
			return string.Format("{0} {1} {2} {3} {4}", Address1, City, State, PostalCode, Country);
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AddressId; }
			set { AddressId = (int)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
