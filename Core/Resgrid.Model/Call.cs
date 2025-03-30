using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Linq;
using ProtoBuf;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Calls")]
	public class Call : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallId { get; set; }

		[ProtoMember(2)]
		public string Number { get; set; }

		[Required]
		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[Required]
		[ProtoMember(4)]
		public string ReportingUserId { get; set; }

		[ProtoMember(5)]
		public int Priority { get; set; }

		public bool IsCritical { get; set; }

		//[MaxLength(100)]
		[ProtoMember(6)]
		public string Type { get; set; }

		//[MaxLength(250)]
		[ProtoMember(7)]
		public string IncidentNumber { get; set; }

		[Required]
		//[MaxLength(100)]
		[ProtoMember(8)]
		public string Name { get; set; }

		[Required]
		//[MaxLength(500)]
		[ProtoMember(9)]
		public string NatureOfCall { get; set; }

		//[MaxLength(100)]
		[ProtoMember(10)]
		public string MapPage { get; set; }

		//[MaxLength(4000)]
		[ProtoMember(11)]
		public string Notes { get; set; }

		//[MaxLength(4000)]
		//[ProtoMember(12)]
		public string CompletedNotes { get; set; }

		//[MaxLength(500)]
		[ProtoMember(13)]
		public string Address { get; set; }

		//[MaxLength(4000)]
		[ProtoMember(14)]
		public string GeoLocationData { get; set; }

		[ProtoMember(15)]
		public DateTime LoggedOn { get; set; }

		[ForeignKey("ClosedByUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(16)]
		public string ClosedByUserId { get; set; }

		[ProtoMember(17)]
		public DateTime? ClosedOn { get; set; }

		[ProtoMember(18)]
		public int State { get; set; }

		[ProtoMember(19)]
		public bool IsDeleted { get; set; }

		[ProtoMember(20)]
		public int CallSource { get; set; }

		[ProtoMember(25)]
		public int DispatchCount { get; set; }

		public DateTime? LastDispatchedOn { get; set; }

		//[MaxLength(500)]
		[ProtoMember(21)]
		public string SourceIdentifier { get; set; }

		[ForeignKey("ReportingUserId")]
		public virtual IdentityUser ReportingUser { get; set; }

		public virtual IdentityUser ClosedByUser { get; set; }

		//[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[ProtoMember(22)]
		public virtual ICollection<CallDispatch> Dispatches { get; set; }

		[ProtoMember(23)]
		public virtual ICollection<CallAttachment> Attachments { get; set; }

		[ProtoMember(24)]
		public virtual ICollection<CallNote> CallNotes { get; set; }

		[ProtoMember(26)]
		public string W3W { get; set; }

		[ProtoMember(27)]
		public virtual ICollection<CallDispatchGroup> GroupDispatches { get; set; }

		[ProtoMember(28)]
		public virtual ICollection<CallDispatchUnit> UnitDispatches { get; set; }

		[ProtoMember(29)]
		public virtual ICollection<CallDispatchRole> RoleDispatches { get; set; }

		[ProtoMember(30)]
		public virtual ICollection<CallProtocol> Protocols { get; set; }

		[ProtoMember(31)]
		public virtual ICollection<CallReference> References { get; set; }

		[ProtoMember(32)]
		public virtual ICollection<CallContact> Contacts { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public bool Public { get; set; }

		public string ExternalIdentifier { get; set; }

		public string ReferenceNumber { get; set; }

		[NotMapped]
		public string ShortenedAudioUrl { get; set; }

		[NotMapped]
		public string ShortenedCallUrl { get; set; }

		[NotMapped]
		public DepartmentCallPriority CallPriority { get; set; }

		[NotMapped]
		public int PreviousDispatchCount { get; set; }

		public string CallFormData { get; set; }

		public DateTime? DispatchOn { get; set; }

		public bool? HasBeenDispatched { get; set; }

		public int? LinkedCallId { get; set; }

		public string DeletedReason { get; set; }

		public string DeletedByUserId { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallId; }
			set { CallId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Calls";

		[NotMapped]
		public string IdName => "CallId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ReportingUser", "ClosedByUser", "Department", "Dispatches", "Attachments", "CallNotes", "GroupDispatches", "UnitDispatches", "RoleDispatches", "Protocols", "ShortenedAudioUrl", "ShortenedCallUrl", "CallPriority", "PreviousDispatchCount", "References", "Contacts" };

		public string GetIdentifier()
		{
			if (!String.IsNullOrWhiteSpace(ExternalIdentifier))
				return ExternalIdentifier;

			return Number;
		}

		public bool HasUserBeenDispatched(string userId)
		{
			if (Dispatches != null && Dispatches.Any())
			{
				var dispatch = from d in Dispatches
							   where d.UserId == userId
							   select d;

				if (dispatch != null && dispatch.Any())
					return true;
			}

			return false;
		}

		public bool HasUnitBeenDispatched(int unitId)
		{
			if (UnitDispatches != null && UnitDispatches.Any())
			{
				var dispatch = from d in UnitDispatches
							   where d.UnitId == unitId
							   select d;

				if (dispatch != null && dispatch.Any())
					return true;
			}

			return false;
		}

		public bool HasGroupBeenDispatched(int groupId)
		{
			if (GroupDispatches != null && GroupDispatches.Any())
			{
				var dispatch = from d in GroupDispatches
					where d.DepartmentGroupId == groupId
					select d;

				if (dispatch != null && dispatch.Any())
					return true;
			}

			return false;
		}

		public string GetPriorityText()
		{
			if (CallPriority != null)
				return CallPriority.Name;
			else
			{
				if (Priority <= 3)
				{
					switch (((Resgrid.Model.CallPriority)Priority))
					{
						case Resgrid.Model.CallPriority.Low:
							return "Low";
						case Resgrid.Model.CallPriority.Medium:
							return "Medium";
						case Resgrid.Model.CallPriority.High:
							return "High";
						case Resgrid.Model.CallPriority.Emergency:
							return "Emergency";
						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}

			return "Unknown";
		}

		public string GetStateText()
		{
			switch (State)
			{
				case (int)CallStates.Active:
					return "Active";
				case (int)CallStates.Cancelled:
					return "Cancelled";
				case (int)CallStates.Closed:
					return "Closed";
				case (int)CallStates.Unfounded:
					return "Unfounded";
			}

			return "Unknown";
		}

		public bool HasAnyDispatches()
		{
			if (Dispatches != null && Dispatches.Any())
				return true;

			if (GroupDispatches != null && GroupDispatches.Any())
				return true;

			if (UnitDispatches != null && UnitDispatches.Any())
				return true;

			if (RoleDispatches != null && RoleDispatches.Any())
				return true;

			return false;
		}

		public void IncreaseDispatchCount()
		{
			PreviousDispatchCount = DispatchCount;
			DispatchCount++;
		}

		public bool DidDispatchCountChange()
		{
			if (PreviousDispatchCount == 0)
				return false;

			return PreviousDispatchCount == DispatchCount;
		}

		public bool HasValidGeolocationData()
		{
			if (!String.IsNullOrWhiteSpace(GeoLocationData) && GeoLocationData.Length > 3)
			{
				var geo = GeoLocationData.Split(char.Parse(","));
				if (geo.Length == 2)
				{
					double r1 = 0;
					double r2 = 0;

					if (double.TryParse(geo[0], out r1) && double.TryParse(geo[1], out r2))
						return true;
				}
			}

			return false;
		}
	}
}
