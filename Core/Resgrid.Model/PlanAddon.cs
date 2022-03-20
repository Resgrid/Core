using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public class PlanAddon : IEntity
	{
		public string PlanAddonId { get; set; }

		public int PlanId { get; set; }

		public virtual Plan Plan { get; set; }

		public int AddonType { get; set; }

		public double Cost { get; set; }

		public string ExternalId { get; set; }

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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Plan" };
	}
}
