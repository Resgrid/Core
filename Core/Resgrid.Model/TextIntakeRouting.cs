using System;

namespace Resgrid.Model
{
	public enum TextIntakePath { TwilioLegacy, SignalWire }
	public sealed record TextIntakeDecision(bool DispatchSource, bool CallBranch, bool CommandBranch);

	/// <summary>Routing only, after department resolution and the provider's plan gate. Not sender authentication.</summary>
	public static class TextIntakeRouting
	{
		public static TextIntakeDecision Decide(TextIntakePath path, bool patternMatched, bool callsEnabled, bool commandsEnabled)
		{
			return path switch
			{
				// The legacy Twilio consumer intentionally does not consult these switches.
				TextIntakePath.TwilioLegacy => new(patternMatched, patternMatched, !patternMatched),
				TextIntakePath.SignalWire => new(patternMatched || !commandsEnabled,
					(patternMatched || !commandsEnabled) && callsEnabled, !patternMatched && commandsEnabled),
				_ => throw new ArgumentOutOfRangeException(nameof(path))
			};
		}
	}
}
