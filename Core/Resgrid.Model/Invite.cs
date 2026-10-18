using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("Invites")]
	public class Invite : IEntity
	{
		[Key]
		[Required]
		public int InviteId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public Guid Code { get; set; }

		[Required]
		public string EmailAddress { get; set; }

		[Required]
		public string SendingUserId { get; set; }

		[Required]
		public DateTime SentOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string CompletedUserId { get; set; }

		/// <summary><see cref="InviteKinds"/> (M0280): classic invites are sent at creation; import invitations are created pending.</summary>
		public int Kind { get; set; }

		/// <summary>The dormant account an imported person's invitation activates (Kind ImportAccount).</summary>
		public string TargetUserId { get; set; }

		/// <summary>The import workspace that created the invitation.</summary>
		public string WorkspaceId { get; set; }

		/// <summary><see cref="InviteSendStates"/>: the batched sender's progress (classic invites are Sent).</summary>
		public int SendState { get; set; }

		/// <summary>Not sent before this time (the administrator's send window).</summary>
		public DateTime? SendAfter { get; set; }

		public int SendAttempts { get; set; }

		/// <summary>The recipient's language for the invitation (null = the department default).</summary>
		public string Locale { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsImport => Kind == (int)InviteKinds.ImportAccount || Kind == (int)InviteKinds.ImportJoin;


		[ForeignKey("SendingUserId")]
		public virtual IdentityUser SendingUser { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return InviteId; }
			set { InviteId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Invites";

		[NotMapped]
		public string IdName => "InviteId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "SendingUser", "Department", "IsImport" };
	}
}
