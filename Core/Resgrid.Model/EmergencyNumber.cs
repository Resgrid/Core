using Resgrid.Config;

namespace Resgrid.Model
{
	/// <summary>
	/// The public emergency number Resgrid tells members of the public to call (texts, voice prompts, acknowledgements):
	/// <see cref="IntakeConfig.EmergencyNumber"/> (911 by default; 112 for a European deployment, 000 in Australia).
	/// </summary>
	public static class EmergencyNumber
	{
		public static string Current => string.IsNullOrWhiteSpace(IntakeConfig.EmergencyNumber) ? "911" : IntakeConfig.EmergencyNumber.Trim();
	}
}
