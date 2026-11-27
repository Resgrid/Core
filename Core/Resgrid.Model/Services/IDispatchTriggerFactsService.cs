using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Resolves the P4 trigger facts of a call (Protocols P4 shared trigger engine): the department-local time, the geofences that
	/// contain its location, the POI type at its location and its source. Protocol matching and run card matching both use it, so the
	/// two consumers of <see cref="DispatchTriggerEngine"/> always see the same facts for the same call.
	/// </summary>
	public interface IDispatchTriggerFactsService
	{
		/// <summary>Fills the P4 facts of <paramref name="facts"/> (priority, type and time are the caller's) from the call's details.</summary>
		Task FillAsync(int departmentId, DispatchTriggerFacts facts, DispatchTriggerCallInput input);
	}

	/// <summary>The details of a call (saved or being written) that the P4 trigger criteria read.</summary>
	public sealed class DispatchTriggerCallInput
	{
		public DateTime? LoggedOnUtc { get; set; }
		public string Nature { get; set; }
		public string Notes { get; set; }

		/// <summary>"lat,lon" as calls store it (Call.GeoLocationData).</summary>
		public string GeoLocationData { get; set; }

		public double? Latitude { get; set; }
		public double? Longitude { get; set; }
		public int? LocationPoiId { get; set; }
		public int? CallSource { get; set; }

		public static DispatchTriggerCallInput FromCall(Call call)
		{
			if (call == null)
				return new DispatchTriggerCallInput();

			return new DispatchTriggerCallInput
			{
				LoggedOnUtc = call.LoggedOn > DateTime.MinValue.AddDays(1) ? call.LoggedOn : (DateTime?)null,
				Nature = call.NatureOfCall,
				Notes = call.Notes,
				GeoLocationData = call.GeoLocationData,
				LocationPoiId = call.LocationPoiId,
				CallSource = call.CallSource
			};
		}
	}
}
