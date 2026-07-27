using System;

namespace Resgrid.Providers.Tracking.Protocols.Gt06
{
	internal static class Gt06Crc16
	{
		public static ushort Compute(
			ReadOnlySpan<byte> data)
		{
			ushort crc = 0xFFFF;
			foreach (var value in data)
			{
				crc ^= value;
				for (var bit = 0;
				     bit < 8;
				     bit++)
				{
					crc = (crc & 1) != 0
						? (ushort)((crc >> 1) ^ 0x8408)
						: (ushort)(crc >> 1);
				}
			}

			return (ushort)~crc;
		}
	}
}
