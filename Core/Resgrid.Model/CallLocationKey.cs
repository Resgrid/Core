using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The location lookup row for one call (registry M0259): the parsed address key and coordinates that let "previous
	/// calls at this location" find calls whose free-text address was typed differently. Derived from Calls.Address and
	/// Calls.GeoLocationData, both Advanced Data Protection fields, so rows exist only while the department's protection
	/// policy is Disabled; enrolling purges them (see <see cref="CallLocationIndexState.IsSuppressed"/>).
	/// </summary>
	public class CallLocationKey : IEntity
	{
		public int CallId { get; set; }
		public int DepartmentId { get; set; }
		/// <summary><see cref="ParsedStreetAddress.IndexKey"/>; null when the address has no house number or intersection.</summary>
		public string AddressKey { get; set; }
		/// <summary><see cref="ParsedStreetAddress.ToCanonical"/>, so candidates are compared without re-reading or re-parsing the call.</summary>
		public string AddressCanonical { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		/// <summary>Copy of Calls.LoggedOn so candidates sort and cap without a join.</summary>
		public DateTime LoggedOn { get; set; }
		/// <summary><see cref="ParsedStreetAddress.Version"/> the row was written with.</summary>
		public int KeyVersion { get; set; }
		public DateTime IndexedOn { get; set; }

		public object IdValue { get => CallId; set => CallId = (int)value; }
		public string TableName => "CallLocationKeys";
		public string IdName => "CallId";
		public int IdType => 0;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Per-department progress of the call location backfill (worker 73). A department is fully indexed when
	/// <see cref="CompletedOn"/> is set at the current <see cref="ParsedStreetAddress.Version"/>; until then history
	/// covers the calls indexed so far, newest first.
	/// </summary>
	public class CallLocationIndexState : IEntity
	{
		public int DepartmentId { get; set; }
		public int KeyVersion { get; set; }
		/// <summary>Backfill cursor: calls with a lower id are still to do. Null before the first batch.</summary>
		public int? NextCallId { get; set; }
		public DateTime? CompletedOn { get; set; }
		/// <summary>True while the department's data protection policy is not Disabled: no rows are kept and none are written.</summary>
		public bool IsSuppressed { get; set; }
		public DateTime ModifiedOn { get; set; }

		public object IdValue { get => DepartmentId; set => DepartmentId = (int)value; }
		public string TableName => "CallLocationIndexStates";
		public string IdName => "DepartmentId";
		public int IdType => 0;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
