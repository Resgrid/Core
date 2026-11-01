using Resgrid.Model;
using Resgrid.Model.Dashboard;

namespace Resgrid.Services.Dashboard.Widgets.Communications
{
	/// <summary>
	/// Maps a value that went through the module's IProtectedReadService resolve into DashboardProtectedText (contract §2.4):
	/// a value the resolver redacted — or one still carrying an rgdp envelope, which no DTO may ever hold — becomes a
	/// shield; anything else is plain. Neither the literal REDACTED nor an envelope reaches the client.
	/// </summary>
	internal static class WidgetProtectedText
	{
		internal static bool IsSealed(string value) =>
			value != null && (value == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(value));

		internal static DashboardProtectedText From(string resolvedValue, bool canReveal) =>
			IsSealed(resolvedValue) ? DashboardProtectedText.Hidden(canReveal) : DashboardProtectedText.Plain(resolvedValue);
	}
}
