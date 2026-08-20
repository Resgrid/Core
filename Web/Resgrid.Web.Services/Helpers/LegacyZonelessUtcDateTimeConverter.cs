namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// TEMPORARY (RG-T132): serialises a UTC instant WITHOUT the trailing "Z".
	///
	/// Deployed app builds compute "time ago" for <c>CallResult.LoggedOnUtc</c> by parsing the value
	/// as device-local time and shifting "now" by the device's UTC offset -- math that is only
	/// correct when the value is zone-less. The "Z" added by <see cref="UtcDateTimeConverter"/> made
	/// those builds show call times off by the device's UTC offset. Updated app builds handle both
	/// formats, so once the fixed apps are rolled out, delete this class and restore
	/// <see cref="UtcDateTimeConverter"/> on the properties using it.
	///
	/// Reads are inherited from <see cref="UtcDateTimeConverter"/> and accept both formats.
	/// </summary>
	public class LegacyZonelessUtcDateTimeConverter : UtcDateTimeConverter
	{
		public LegacyZonelessUtcDateTimeConverter()
		{
			DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff";
		}
	}
}
