using Resgrid.Model;

namespace Resgrid.Ai;

/// <summary>One entity property whose value may leave Resgrid for a third-party decision model.</summary>
public sealed record AiNonPrivateField(Type Entity, string Property);

/// <summary>
/// The fields a <c>DecisionDataClass.DepartmentConfiguration</c> request may be built from (Shawn's rule, 2026-10-17: a field is
/// non-private when Advanced Data Protection does not protect it). These are the names of department configuration lists — call
/// types, priorities, unit types, roles, groups, and status and staffing names — never calls, people or records. Built-in values
/// such as call states are Resgrid's own text and need no entry. A test keeps every entry outside the ADP protected-field catalog,
/// so a field ADP starts protecting fails the build instead of leaking. Add a field here only if ADP does not protect it; a
/// request built from anything else is DepartmentContent.
/// </summary>
public static class AiNonPrivateFields
{
    public static IReadOnlyList<AiNonPrivateField> ConfigurationLabels { get; } = new[]
    {
        new AiNonPrivateField(typeof(CallType), nameof(CallType.Type)),
        new AiNonPrivateField(typeof(DepartmentCallPriority), nameof(DepartmentCallPriority.Name)),
        new AiNonPrivateField(typeof(UnitType), nameof(UnitType.Type)),
        new AiNonPrivateField(typeof(PersonnelRole), nameof(PersonnelRole.Name)),
        new AiNonPrivateField(typeof(DepartmentGroup), nameof(DepartmentGroup.Name)),
        // Custom state sets and their entries: unit statuses, personnel statuses and staffing levels.
        new AiNonPrivateField(typeof(CustomState), nameof(CustomState.Name)),
        new AiNonPrivateField(typeof(CustomStateDetail), nameof(CustomStateDetail.ButtonText))
    };
}
