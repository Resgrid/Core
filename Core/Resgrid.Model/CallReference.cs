using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json.Serialization;

namespace Resgrid.Model
{
	/// <summary>
	/// Class CallReference.
	/// Implements the <see cref="Resgrid.Model.IEntity" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.IEntity" />
	[ProtoContract]
	public class CallReference : IEntity
	{
		[ProtoMember(1)]
		public string CallReferenceId { get; set; }

		/// <summary>
		/// The call that is being linked from
		/// </summary>
		[ProtoMember(2)]
		public int SourceCallId { get; set; }

		[ProtoMember(8)]
		public Call SourceCall { get; set; }

		/// <summary>
		/// The call that is being linked too (i.e. parent)
		/// </summary>
		[ProtoMember(3)]
		public int TargetCallId { get; set; }

		[ProtoMember(7)]
		public Call TargetCall { get; set; }

		[ProtoMember(4)]
		public string AddedByUserId { get; set; }

		[ProtoMember(5)]
		public DateTime AddedOn { get; set; }

		[ProtoMember(6)]
		public string Note { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallReferenceId; }
			set { CallReferenceId = (string)value; }
		}

		[NotMapped]
		public string TableName => "CallReferences";

		[NotMapped]
		public string IdName => "CallReferenceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "TargetCall", "SourceCall" };
	}
}
