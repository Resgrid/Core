using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("UserProfiles")]
	public class UserProfile: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int UserProfileId { get; set; }

		[Required]
		[ProtoMember(2)]
		public string UserId { get; set; }

		[ForeignKey("UserId")]
		[ProtoMember(27)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(3)]
		public string FirstName { get; set; }

		[ProtoMember(4)]
		public string LastName { get; set; }

		[ProtoMember(5)]
		public string TimeZone { get; set; }

		[ProtoMember(6)]
		public string MobileNumber { get; set; }

		[ProtoMember(7)]
		public int MobileCarrier { get; set; }

		[ProtoMember(8)]
		public string HomeNumber { get; set; }

		[ProtoMember(9)]
		public int? HomeAddressId { get; set; }

		[ProtoMember(10)]
		public int? MailingAddressId { get; set; }

		[ProtoMember(11)]
		public bool SendEmail { get; set; }

		[ProtoMember(12)]
		public bool SendPush { get; set; }

		[ProtoMember(13)]
		public bool SendSms { get; set; }

		[ProtoMember(14)]
		public bool SendMessageEmail { get; set; }

		[ProtoMember(15)]
		public bool SendMessagePush { get; set; }

		[ProtoMember(16)]
		public bool SendMessageSms { get; set; }

		[ProtoMember(19)]
		public bool SendNotificationEmail { get; set; }

		[ProtoMember(20)]
		public bool SendNotificationPush { get; set; }

		[ProtoMember(21)]
		public bool SendNotificationSms { get; set; }

		//[ProtoMember(17)]
		public bool DoNotRecieveNewsletters { get; set; }

		[ProtoMember(18)]
		public string IdentificationNumber { get; set; }

		[ProtoMember(22)]
		public bool VoiceForCall { get; set; }

		[ProtoMember(23)]
		public bool VoiceCallMobile { get; set; }

		[ProtoMember(24)]
		public bool VoiceCallHome { get; set; }

		[ProtoMember(25)]
		public byte[] Image { get; set; }

		[ProtoMember(26)]
		public DateTime? LastUpdated { get; set; }

		[ProtoMember(28)]
		public bool CustomPushSounds { get; set; }

		[NotMapped]
		[ProtoMember(29)]
		public string MembershipEmail { get; set; }

		[ProtoMember(30)]
		public DateTime? StartDate { get; set; }

		[ProtoMember(31)]
		public DateTime? EndDate { get; set; }

		[ProtoMember(32)]
		public string Language { get; set; }

		public string GetPhoneNumber()
		{
			if (!String.IsNullOrEmpty(MobileNumber))
			{
				return
					MobileNumber.Trim().Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Replace("+", "").Replace(".", "");
			}

			return string.Empty;
		}

		public string GetHomePhoneNumber()
		{
			if (!String.IsNullOrEmpty(HomeNumber))
			{
				return
					HomeNumber.Trim().Replace("(", "").Replace(")", "").Replace("-", "").Replace(" ", "").Replace("+", "").Replace(".", "");
			}

			return string.Empty;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UserProfileId; }
			set { UserProfileId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UserProfiles";

		[NotMapped]
		public string IdName => "UserProfileId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "User", "MembershipEmail" };

		[NotMapped]
		public FullNameFormat FullName
		{
			get
			{
				return new FullNameFormat(FirstName, LastName);
			}
		}
	}

	public class FullNameFormat
	{
		private string _lastName;
		private string _firstName;

		public FullNameFormat(string firstName, string lastName)
		{
			_firstName = firstName ?? string.Empty;
			_lastName = lastName ?? string.Empty;
		}
		public string AsFirstNameLastName
		{
			get
			{
				return string.Format("{0} {1}", _firstName, _lastName);
			}
		}
	}
}
