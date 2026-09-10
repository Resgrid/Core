using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	// RMS-6 records analytics (plan section 6, RMS-6). The dashboard contracts live in the Model and are returned
	// as-is: every figure is already scoped, labeled and rounded by the service, and none carries record content.

	public class RecordsResponsePerformanceResult : StandardApiResponseV4Base { public RecordsResponsePerformance Data { get; set; } }
	public class RecordsWorkloadResult : StandardApiResponseV4Base { public RecordsWorkload Data { get; set; } }
	public class RecordsExecutiveSummaryResult : StandardApiResponseV4Base { public RecordsExecutiveSummary Data { get; set; } }
	public class RecordsAccreditationResult : StandardApiResponseV4Base { public RecordsAccreditation Data { get; set; } }
	public class RecordsCommunityRiskResult : StandardApiResponseV4Base { public RecordsCommunityRisk Data { get; set; } }
	public class RecordsReadinessResult : StandardApiResponseV4Base { public RecordsReadiness Data { get; set; } }
}
