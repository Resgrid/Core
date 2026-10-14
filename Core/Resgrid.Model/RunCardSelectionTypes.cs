namespace Resgrid.Model
{
	/// <summary>
	/// Discriminator for RunCardAvailabilitySelection rows. Mirrors CustomStateTypes
	/// (Personnel=1, Unit=2, Staffing=3) but ordered by how the engine consumes them.
	/// </summary>
	public enum RunCardSelectionTypes
	{
		UnitStatus = 1,
		PersonnelStatus = 2,
		PersonnelStaffing = 3
	}

	/// <summary>
	/// A run card availability selection can name a status meaning instead of one status: a built-in (not custom) selection
	/// whose StateId is <see cref="Offset"/> + a base type counts every status with that base type, built-in or the
	/// department's own, in any status set (the same encoding as <see cref="StatusTarget"/>). Base types are
	/// <see cref="ActionBaseTypes"/> for statuses and <see cref="StaffingBaseTypes"/> for staffing levels.
	/// </summary>
	public static class RunCardSelectionMeaning
	{
		public const int Offset = StatusTarget.BaseTypeOffset;

		public static int StateIdFor(int baseType) => Offset + baseType;

		public static bool IsMeaning(RunCardAvailabilitySelection selection) =>
			selection != null && !selection.IsCustomState && selection.StateId >= Offset;

		/// <summary>True when the selection names this exact status, or a meaning the status has.</summary>
		public static bool Matches(RunCardAvailabilitySelection selection, int stateId, bool isCustom, int baseType)
		{
			if (selection == null)
				return false;

			if (IsMeaning(selection))
				return selection.StateId - Offset == baseType;

			return selection.StateId == stateId && selection.IsCustomState == isCustom;
		}
	}
}
