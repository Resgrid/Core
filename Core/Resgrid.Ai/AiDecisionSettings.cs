using Resgrid.Llm;

namespace Resgrid.Ai;

/// <summary>
/// A configured, usable decision endpoint. <see cref="WireModel"/> is the request's <c>model</c> value; <see cref="AuditModel"/>,
/// <see cref="Revision"/> and <see cref="Digest"/> go on audit rows. <see cref="Remote"/> means a third party runs the model
/// (Workers AI): it may be sent only <see cref="DecisionDataClass.NonSensitive"/> and <see cref="DecisionDataClass.DepartmentConfiguration"/>
/// requests, never department content.
/// </summary>
public sealed record AiDecisionEndpoint(Uri Endpoint, bool Remote, string WireModel, string AuditModel, string? Revision, string? Digest);

/// <summary>
/// Operator rules for the decision model (Cloudflare Clef / Clef-Flash). Callers pass the Resgrid.Config values (AiConfig.Decision*).
/// Workers AI is Cloudflare's hosted copy: there is no weight revision or image digest to pin, so the audit records the Workers AI
/// model id instead, and the endpoint must be the direct REST route for the configured model. A self-hosted SGLang server is pinned
/// by revision and digest like the generative model.
/// </summary>
public static class AiDecisionSettings
{
    /// <summary>Null when the provider is unknown or anything is missing, unreviewed, unpinned or not the endpoint shape it must be.</summary>
    public static AiDecisionEndpoint? Resolve(string? provider, string? endpoint, bool allowPrivateEndpoint, string? apiKey, string? model, string? revision, string? digest)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !AiOperatorSettings.IsReviewedDecisionModel(model))
            return null;
        try
        {
            if (string.Equals(provider, DecisionProviders.WorkersAi, StringComparison.Ordinal))
            {
                var uri = OperatorEndpointPolicy.ValidateWorkersAiDecisionUri(endpoint ?? "", out var selector);
                // The route names the model; it must be the one configured, so the audit cannot record a different model than ran.
                return string.Equals("Cloudflare/" + selector, model, StringComparison.Ordinal)
                    ? new AiDecisionEndpoint(uri, true, selector, "@cf/cloudflare/" + selector, null, null)
                    : null;
            }
            if (string.Equals(provider, DecisionProviders.SelfHosted, StringComparison.Ordinal))
            {
                var uri = OperatorEndpointPolicy.ValidateUri(endpoint ?? "", allowPrivateEndpoint, OperatorEndpointPolicy.DecisionPath);
                return AiOperatorSettings.IsPinned(revision, digest) ? new AiDecisionEndpoint(uri, false, model!, model!, revision, digest) : null;
            }
        }
        catch (LlmUnavailableException)
        {
        }
        return null;
    }
}
