using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DepartmentProfiles")]
	public class DepartmentProfile : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentProfileId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[ProtoMember(3)]
		public bool Disabled { get; set; }

		[Required]
		[ProtoMember(4)]
		public string Name { get; set; }

		[Required]
		[ProtoMember(5)]
		public string Code { get; set; }

		[ProtoMember(6)]
		public string ShortName { get; set; }

		[ProtoMember(7)]
		public string Description { get; set; }

		[ProtoMember(8)]
		public string InCaseOfEmergency { get; set; }

		[ProtoMember(9)]
		public string ServiceArea { get; set; }

		[ProtoMember(10)]
		public string ServicesProvided { get; set; }

		[ProtoMember(11)]
		public DateTime? Founded { get; set; }

		public byte[] Logo { get; set; }

		[ProtoMember(12)]
		public string Keywords { get; set; }

		[ProtoMember(13)]
		public bool InviteOnly { get; set; }

		[ProtoMember(14)]
		public bool AllowMessages { get; set; }

		[ProtoMember(15)]
		public bool VolunteerPositionsAvailable { get; set; }

		[ProtoMember(16)]
		public bool ShareStats { get; set; }

		[ProtoMember(17)]
		public string VolunteerKeywords { get; set; }

		[ProtoMember(18)]
		public string VolunteerDescription { get; set; }

		[ProtoMember(19)]
		public string VolunteerContactName { get; set; }

		[ProtoMember(20)]
		public string VolunteerContactInfo { get; set; }

		[ProtoMember(21)]
		public string Geofence { get; set; }

		[ProtoMember(22)]
		public int? AddressId { get; set; }

		[ProtoMember(23)]
		[ForeignKey("AddressId")]
		public virtual Address Address { get; set; }

		[ProtoMember(24)]
		public string Latitude { get; set; }

		[ProtoMember(25)]
		public string Longitude { get; set; }

		[ProtoMember(26)]
		public string What3Words { get; set; }

		[ProtoMember(27)]
		public string Facebook { get; set; }

		[ProtoMember(28)]
		public string Twitter { get; set; }

		[ProtoMember(29)]
		public string GooglePlus { get; set; }

		[ProtoMember(30)]
		public string LinkedIn { get; set; }

		[ProtoMember(31)]
		public string Instagram { get; set; }

		[ProtoMember(32)]
		public string YouTube { get; set; }

		[ProtoMember(33)]
		public string Website { get; set; }

		[ProtoMember(34)]
		public string PhoneNumber { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentProfileId; }
			set { DepartmentProfileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentProfiles";

		[NotMapped]
		public string IdName => "DepartmentProfileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Address" };
	}
}
