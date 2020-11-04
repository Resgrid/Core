using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Profile
{
	public class UpdateProfileInput
	{
		public string Id { get; set; }

		[Required]
		public string FirstName { get; set; }

		[Required]
		public string LastName { get; set; }

		[Required]
		public string Email { get; set; }
		public string HomePhone { get; set; }
		public string MobilePhone { get; set; }
		public int MobileCarrier { get; set; }
		public bool SendCallSms { get; set; }
		public bool SendCallPush { get; set; }
		public bool SendCallEmail { get; set; }
		public bool SendMessageSms { get; set; }
		public bool SendMessagePush { get; set; }
		public bool SendMessageEmail { get; set; }
		public bool SendNotificationSms { get; set; }
		public bool SendNotificationPush { get; set; }
		public bool SendNotificationEmail { get; set; }
	}
}