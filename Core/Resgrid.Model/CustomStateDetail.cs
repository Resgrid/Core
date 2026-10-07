using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Resgrid.Model
{
	[Table("CustomStateDetails")]
	[ProtoContract]
	public class CustomStateDetail : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CustomStateDetailId { get; set; }

		[Required]
		[ForeignKey("CustomState"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CustomStateId { get; set; }

		public virtual CustomState CustomState { get; set; }

		[Required]
		[ProtoMember(3)]
		public string ButtonText { get; set; }

		[Required]
		[ProtoMember(4)]
		public string ButtonColor { get; set; }

		[ProtoMember(5)]
		public string TextColor { get; set; }

		[ProtoMember(6)]
		public bool GpsRequired { get; set; }

		[ProtoMember(7)]
		public int NoteType { get; set; }

		[ProtoMember(8)]
		public int DetailType { get; set; }

		[ProtoMember(9)]
		public int Order { get; set; }

		[ProtoMember(10)]
		public bool IsDeleted { get; set; }

		[ProtoMember(11)]
		public int BaseType { get; set; }

		[ProtoMember(12)]
		public int TTL { get; set; }

		/// <summary>
		/// Comma-separated <see cref="CustomStateDetailId"/>s of the options in the same set that the apps offer
		/// next while this option is the current status (e.g. "Departed" -> "On Scene"). Null or empty means no
		/// restriction. A display hint for the apps only; the server accepts any status.
		/// </summary>
		[ProtoMember(13)]
		public string NextStateDetailIds { get; set; }

		/// <summary>The parsed <see cref="NextStateDetailIds"/>, without duplicates, blanks or this option's own id.</summary>
		public List<int> GetNextStateDetailIds()
		{
			var ids = new List<int>();

			if (string.IsNullOrWhiteSpace(NextStateDetailIds))
				return ids;

			foreach (var part in NextStateDetailIds.Split(','))
			{
				if (int.TryParse(part.Trim(), out var id) && id > 0 && id != CustomStateDetailId && !ids.Contains(id))
					ids.Add(id);
			}

			return ids;
		}

		/// <summary>Stores <paramref name="ids"/> as <see cref="NextStateDetailIds"/>; an empty list clears the restriction.</summary>
		public void SetNextStateDetailIds(IEnumerable<int> ids)
		{
			var distinct = new List<int>();

			if (ids != null)
			{
				foreach (var id in ids)
				{
					if (id > 0 && id != CustomStateDetailId && !distinct.Contains(id))
						distinct.Add(id);
				}
			}

			NextStateDetailIds = distinct.Count > 0 ? string.Join(",", distinct) : null;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomStateDetailId; }
			set { CustomStateDetailId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CustomStateDetails";

		[NotMapped]
		public string IdName => "CustomStateDetailId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CustomState" };

		public string ButtonClassToColor()
		{
			switch (ButtonColor)
			{
				case "label-default":
					return "#777";
				case "label-warning":
					return "#f0ad4e";
				case "label-danger":
					return "#ff0000";
				case "label-info":
					return "#5bc0de";
				case "label-success":
					return "#5cb85c";
				case "label-inverse":
					return "#000";
				default:
					return ButtonColor;

			}
		}
	}
}
