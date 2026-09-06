using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Command key times for a Call, read from the Incident Command module as a versioned snapshot (RMS plan
	/// section 4.2: command-derived times are prefilled with <see cref="RmsSourceKind.Derived"/> provenance,
	/// never treated as a dispatcher's or a device's time).
	/// </summary>
	public class IncidentCommandKeyTimes
	{
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public DateTime? EstablishedOn { get; set; }
		public DateTime? FirstResourceAssignedOn { get; set; }
		public DateTime? FirstBenchmarkCompletedOn { get; set; }
		public DateTime? LastBenchmarkCompletedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public int MutualAidResourceCount { get; set; }
		public List<IncidentCommandBenchmark> Benchmarks { get; set; } = new List<IncidentCommandBenchmark>();
		public DateTime CapturedOn { get; set; }
	}

	public class IncidentCommandBenchmark
	{
		public string Name { get; set; }
		public DateTime? CompletedOn { get; set; }
	}

	/// <summary>
	/// The Contacts/POI facts linked to a Call at the moment a report is started (RMS plan section 4.3: the
	/// contacts plan's ContactPreplan is the dispatch-facing view until the RMS occupancy master lands, so
	/// this feed is source-neutral and carries identity, role and place — never contact phone, email or
	/// other protected-candidate detail).
	/// </summary>
	public class IncidentPreplanSnapshot
	{
		public int CallId { get; set; }
		public List<IncidentPreplanContact> Contacts { get; set; } = new List<IncidentPreplanContact>();
		public IncidentPreplanPlace Place { get; set; }
		public DateTime CapturedOn { get; set; }
		public bool IsEmpty => Contacts.Count == 0 && Place == null;
	}

	public class IncidentPreplanContact
	{
		public string ContactId { get; set; }
		public string DisplayName { get; set; }
		/// <summary>Person or Company.</summary>
		public string ContactType { get; set; }
		public string CategoryName { get; set; }
		/// <summary>Primary or Additional, from the Call link.</summary>
		public string Role { get; set; }
	}

	public class IncidentPreplanPlace
	{
		public int PoiId { get; set; }
		public string Name { get; set; }
		public string TypeName { get; set; }
		public string Address { get; set; }
		public double? Latitude { get; set; }
		public double? Longitude { get; set; }
	}
}
