using System.Buffers;

namespace Resgrid.Providers.Tracking.Protocols.Teltonika
{
	internal static class TeltonikaCrc16
	{
		private const ushort Polynomial = 0xA001;

		public static ushort Compute(
			ReadOnlySequence<byte> data)
		{
			ushort crc = 0;
			foreach (var segment in data)
			{
				foreach (var value in segment.Span)
				{
					crc ^= value;
					for (var bit = 0;
					     bit < 8;
					     bit++)
					{
						crc = (crc & 1) != 0
							? (ushort)((crc >> 1) ^
							           Polynomial)
							: (ushort)(crc >> 1);
					}
				}
			}

			return crc;
		}
	}
}
