using Newtonsoft.Json;
using ProtoBuf;
using Resgrid.Framework;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[ProtoContract]
	public class PlanAddon : IEntity
	{
		[ProtoMember(1)]
		public string PlanAddonId { get; set; }

		[ProtoMember(2)]
		public int? PlanId { get; set; }

		[ProtoMember(3)]
		public virtual Plan Plan { get; set; }

		[ProtoMember(4)]
		public int AddonType { get; set; }

		[ProtoMember(5)]
		public double Cost { get; set; }

		[ProtoMember(6)]
		public string ExternalId { get; set; }

		[ProtoMember(7)]
		public string TestExternalId { get; set; }

		[NotMapped]
		public bool IsCancelled { get; set; }

		[NotMapped]
		public DateTime? EndingOn { get; set; }

		[NotMapped]
		public long Quantity { get; set; }

		public string GetExternalKey()
		{
			if (Config.PaymentProviderConfig.IsTestMode)
				return TestExternalId;
			else
				return ExternalId;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PlanAddonId; }
			set { PlanAddonId = (string)value; }
		}

		[NotMapped]
		public string TableName => "PlanAddons";

		[NotMapped]
		public string IdName => "PlanAddonId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Plan", "IsCancelled", "EndingOn", "Quantity" };

		public DateTime GetEndDateFromNow()
		{
			if (Plan != null)
			{
				switch ((PlanFrequency)Plan.Frequency)
				{
					case PlanFrequency.Never:
						return DateTime.MaxValue;
					case PlanFrequency.Monthly:
						return DateTime.UtcNow.AddMonths(1).AddDays(7).SetToEndOfDay();
					case PlanFrequency.Yearly:
						return DateTime.UtcNow.AddYears(1).AddDays(14).SetToEndOfDay();
					default:
						throw new ArgumentOutOfRangeException();
				}
			}

			// No plan, which means no plan frequency, so default to 1 month.
			return DateTime.UtcNow.AddMonths(1).AddDays(7).SetToEndOfDay();
		}

		public string GetAddonName()
		{
			switch ((PlanAddonTypes)AddonType)
			{
				case PlanAddonTypes.PTT:
					return "Push-To-Talk";
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}
}
