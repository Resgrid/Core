using System.Globalization;
using System.Text.RegularExpressions;

namespace Resgrid.Ai;

/// <summary>
/// Operator inference rules shared by every Enhanced AI feature (enhanced-ai-addon-plan.md §1 decision 6, §5): one reviewed
/// open-weight model per GPU card, pinned by revision and runtime digest. Callers pass the Resgrid.Config values.
/// </summary>
public static class AiOperatorSettings
{
    /// <summary>The ai-text card serves the 14B for every Phase 1 feature once it passes the Admin Assist hardware gate; the 8B is the single-card fallback.</summary>
    public static IReadOnlyList<string> ReviewedModels { get; } = new[] { "Qwen/Qwen3-14B-AWQ", "Qwen/Qwen3-8B-AWQ" };

    public static bool IsReviewedModel(string? model) => model != null && ReviewedModels.Contains(model, StringComparer.Ordinal);

    public static bool IsPinned(string? modelRevision, string? runtimeDigest) =>
        Regex.IsMatch(modelRevision ?? "", "\\A[0-9a-f]{40}\\z") && Regex.IsMatch(runtimeDigest ?? "", "\\Asha256:[0-9a-f]{64}\\z");

    /// <summary>Whether <paramref name="departmentId"/> appears in an operator's comma-separated self-hosted department list.</summary>
    public static bool IsListedDepartment(string? departmentIds, int departmentId) =>
        (departmentIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(id => int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value == departmentId);
}
