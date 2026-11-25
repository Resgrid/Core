using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class ComposeMessageModel : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Message Message { get; set; }
		public bool SendToAll { get; set; }
		public bool SendToMatchOnly { get; set; }
		public MessageTypes MessageType { get; set; }
		public SelectList Types { get; set; }
		public SelectList Shifts { get; set; }

		/// <summary>The LMS credential-status audience is available (Training T3, LMS add-on): the picker shows.</summary>
		public bool CredentialAudienceAvailable { get; set; }

		/// <summary>Person-scoped certification types for the credential-status audience picker.</summary>
		public SelectList CertificationTypes { get; set; }

		/// <summary>The LMS event-roster audience is available (Training T4, LMS add-on): the class picker shows.</summary>
		public bool RosterAudienceAvailable { get; set; }

		/// <summary>Recent and upcoming class sessions for the event-roster audience picker.</summary>
		public System.Collections.Generic.List<EventRosterAudienceSession> RosterSessions { get; set; } = new System.Collections.Generic.List<EventRosterAudienceSession>();
	}
}
