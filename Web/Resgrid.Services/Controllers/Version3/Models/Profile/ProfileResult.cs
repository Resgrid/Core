using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Profile
{
	/// <summary>
	/// The representation of a users profile in the Resgrid system
	/// </summary>
	public class ProfileResult
	{
		/// <summary>
		/// User Id the profile is for
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Is the user a Department Admin
		/// </summary>
		public bool Adm { get; set; }

		/// <summary>
		/// Is this user Hidden
		/// </summary>
		public bool Hid { get; set; }

		/// <summary>
		/// Is this user Disabled
		/// </summary>
		public bool Dis { get; set; }

		/// <summary>
		/// Is the user locked out
		/// </summary>
		public bool Lkd { get; set; }

		/// <summary>
		/// First Name
		/// </summary>
		public string Fnm { get; set; }

		/// <summary>
		/// Last Name
		/// </summary>
		public string Lnm { get; set; }

		/// <summary>
		/// Email Address
		/// </summary>
		public string Eml { get; set; }

		/// <summary>
		/// Time Zone
		/// </summary>
		public string Tz { get; set; }

		/// <summary>
		/// Mobile Number
		/// </summary>
		public string Mob { get; set; }

		/// <summary>
		/// Mobile Carrier
		/// </summary>
		public int Moc { get; set; }

		/// <summary>
		/// Home Number
		/// </summary>
		public string Hmn { get; set; }

		/// <summary>
		/// Send Call Emails
		/// </summary>
		public bool Sce { get; set; }

		/// <summary>
		/// Send Call Push Notifications
		/// </summary>
		public bool Scp { get; set; }

		/// <summary>
		/// Send Call SMS/Text Messages
		/// </summary>
		public bool Scs { get; set; }

		/// <summary>
		/// Send Message Email
		/// </summary>
		public bool Sme { get; set; }

		/// <summary>
		/// Send Message Push Notifications
		/// </summary>
		public bool Smp { get; set; }

		/// <summary>
		/// Send Message SMS/Text Messages
		/// </summary>
		public bool Sms { get; set; }

		/// <summary>
		/// Send Notification Email
		/// </summary>
		public bool Sne { get; set; }

		/// <summary>
		/// Send Notification Push Notifications
		/// </summary>
		public bool Snp { get; set; }

		/// <summary>
		/// Send Notification SMS/Text Messages
		/// </summary>
		public bool Sns { get; set; }

		/// <summary>
		/// Department Issued Identification Number
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Is the user allowed to set voice call data (based on the departments plan)
		/// </summary>
		public bool Val { get; set; }

		/// <summary>
		/// Enable Voice/Telephone Dispatch/Notifications
		/// </summary>
		public bool Voc { get; set; }

		/// <summary>
		/// Voice Call your Mobile Phone
		/// </summary>
		public bool Vcm { get; set; }

		/// <summary>
		/// Voice Call your Home Phone
		/// </summary>
		public bool Vch { get; set; }

		/// <summary>
		/// Last Updated
		/// </summary>
		public DateTime? Lup { get; set; }

		/// <summary>
		/// Home Address
		/// </summary>
		public AddressResult Hme { get; set; }

		/// <summary>
		/// Mailing Address
		/// </summary>
		public AddressResult Mal { get; set; }
	}

	public class AddressResult
	{
		/// <summary>
		/// Address Id
		/// </summary>
		public int Aid { get; set; }

		/// <summary>
		/// Street Address
		/// </summary>
		public string Str { get; set; }

		/// <summary>
		/// City
		/// </summary>
		public string Cty { get; set; }

		/// <summary>
		/// State or Province
		/// </summary>
		public string Ste { get; set; }

		/// <summary>
		/// Zip or Postal Code
		/// </summary>
		public string Zip { get; set; }

		/// <summary>
		/// Country
		/// </summary>
		public string Cnt { get; set; }
	}
}