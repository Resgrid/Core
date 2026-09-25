using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.AdminAssist
{
	public sealed record TextImportImpactRequest(string ExpectedRevision, string ProviderPath, string SourceNumber,
		bool CallsEnabled, bool CommandsEnabled);
	public sealed record TextImportImpactReport(string MaskedSource, string ProviderPath, ConfigurationImpactReport Impact);
	public interface ITextImportImpactService
	{
		Task<TextImportImpactReport> PreviewAsync(AdminAssistActor actor, TextImportImpactRequest request, CancellationToken ct = default);
	}
}
