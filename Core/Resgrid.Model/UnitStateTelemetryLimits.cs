using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Fits device-reported telemetry on a <see cref="UnitState"/> into the SQL Server <c>UnitStates</c> columns
	/// (Latitude/Longitude <c>decimal(10,7)</c>, Accuracy/AltitudeAccuracy <c>decimal(6,2)</c>, Altitude <c>decimal(7,2)</c>,
	/// Speed/Heading <c>decimal(5,2)</c>). A value those columns cannot hold fails the whole insert with "Arithmetic overflow
	/// error converting numeric to data type numeric" (8115) and the status is lost — e.g. a desktop browser's IP/Wi-Fi fix
	/// reporting an accuracy of 15,000 m. Only values that cannot be stored are touched; everything that fits is left as sent.
	/// </summary>
	public static class UnitStateTelemetryLimits
	{
		// Largest magnitude each column holds, after SQL Server rounds the value to the column's scale.
		public const decimal MaxCoordinate = 999.9999999m;    // decimal(10,7)
		public const decimal MaxAccuracy = 9999.99m;          // decimal(6,2)
		public const decimal MaxAltitude = 99999.99m;         // decimal(7,2)
		public const decimal MaxSpeedOrHeading = 999.99m;     // decimal(5,2)

		/// <summary>
		/// Brings <paramref name="state"/>'s telemetry within its column limits:
		/// <list type="bullet">
		/// <item>Accuracy and AltitudeAccuracy above the limit are capped at it (a very poor fix stays a very poor fix); a
		/// negative one beyond the limit is not an accuracy and is dropped.</item>
		/// <item>Altitude, Speed and Heading beyond the limit are not real readings and are dropped.</item>
		/// <item>Latitude and Longitude are a pair: if either cannot be stored, both are dropped.</item>
		/// </list>
		/// </summary>
		public static void FitToColumns(UnitState state)
		{
			if (state == null)
				return;

			if (!Fits(state.Latitude, MaxCoordinate, 7) || !Fits(state.Longitude, MaxCoordinate, 7))
			{
				state.Latitude = null;
				state.Longitude = null;
			}

			state.Accuracy = CapAccuracy(state.Accuracy);
			state.AltitudeAccuracy = CapAccuracy(state.AltitudeAccuracy);

			if (!Fits(state.Altitude, MaxAltitude, 2))
				state.Altitude = null;

			if (!Fits(state.Speed, MaxSpeedOrHeading, 2))
				state.Speed = null;

			if (!Fits(state.Heading, MaxSpeedOrHeading, 2))
				state.Heading = null;
		}

		private static decimal? CapAccuracy(decimal? value)
		{
			if (Fits(value, MaxAccuracy, 2))
				return value;

			return value > 0 ? MaxAccuracy : (decimal?)null;
		}

		/// <summary>
		/// True when the value is null or still within <paramref name="max"/> once rounded to <paramref name="scale"/>
		/// places the way SQL Server does (half away from zero) — 9999.995 rounds to 10000.00 and overflows decimal(6,2).
		/// </summary>
		private static bool Fits(decimal? value, decimal max, int scale)
		{
			if (!value.HasValue)
				return true;

			return Math.Abs(Math.Round(value.Value, scale, MidpointRounding.AwayFromZero)) <= max;
		}
	}
}
