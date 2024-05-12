﻿namespace Resgrid.Model
{
	public enum AuditLogTypes
	{
		DepartmentSettingsChanged,
		UserAdded,
		UserRemoved,
		GroupAdded,
		GroupRemoved,
		GroupChanged,
		UnitAdded,
		UnitRemoved,
		UnitChanged,
		ProfileUpdated,
		PermissionsChanged,
		SubscriptionUpdated,
		SubscriptionCreated,
		SubscriptionCancelled,
		SubscriptionBillingInfoUpdated,
		// New
		CallReactivated,
		UserAccountDeleted,
		AddonSubscriptionModified,
		DeleteDepartmentRequested,
		DeleteDepartmentRequestedCancelled,
		DeleteStaticShift,
		UpdateStaticShift
	}
}
