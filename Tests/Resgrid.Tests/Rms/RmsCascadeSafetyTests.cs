using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The RepositoryBase cascade walks every public property that is a collection or an IEntity and ignores
	/// IgnoredProperties while doing so; a self-referencing collection once deleted the parent row (RMS plan
	/// section 5.11.1, "cascade landmine"). The RMS-1 entities therefore carry their self-references as plain
	/// ID columns — RmsRevision.PriorRevisionId, RmsOperationalRecord.AmendsRevisionId — and no navigation
	/// property at all, so a save or delete has nothing to cascade into. This pins that shape: a navigation
	/// collection added to one of these entities fails here before it reaches a database.
	/// </summary>
	[TestFixture]
	public class RmsCascadeSafetyTests
	{
		private static readonly Type[] Rms1Entities =
		{
			typeof(RmsOperationalRecord), typeof(RmsOperationalRecordDetail), typeof(RmsRevision), typeof(RmsRecordParticipant),
			typeof(RmsRecordUnitResponse), typeof(RmsRecordAttachment), typeof(RmsExternalReference), typeof(RmsRecordGroupScope),
			typeof(RmsRecordShare), typeof(RmsAccessAudit), typeof(RmsRecordSearchProjection), typeof(RmsDepartmentCutover),
			typeof(RmsDepartmentCutoverEvent), typeof(DomainEventOutboxEntry), typeof(RmsRecordPrintLayout)
		};

		[Test]
		public void Rms_entities_carry_no_navigation_properties_for_the_cascade_to_walk()
		{
			foreach (var type in Rms1Entities)
			{
				var navigations = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.Where(p => IsEntityNavigation(p.PropertyType))
					.Select(p => p.Name)
					.ToList();

				navigations.Should().BeEmpty($"{type.Name} must reference other rows by ID only (plan 5.11.1 cascade landmine)");
			}
		}

		[Test]
		public void The_self_references_are_plain_id_columns()
		{
			typeof(RmsRevision).GetProperty(nameof(RmsRevision.PriorRevisionId)).PropertyType.Should().Be(typeof(string));
			typeof(RmsOperationalRecord).GetProperty(nameof(RmsOperationalRecord.AmendsRevisionId)).PropertyType.Should().Be(typeof(string));
			typeof(RmsOperationalRecord).GetProperty(nameof(RmsOperationalRecord.CurrentRevisionId)).PropertyType.Should().Be(typeof(string));
		}

		[Test]
		public void Every_rms_entity_declares_the_tenant_and_protection_columns()
		{
			// Plan 5.9.1: DepartmentId and ProtectionId on every table, written at insert and read by nothing until
			// enrollment binds them into the AEAD tuple.
			foreach (var type in Rms1Entities.Where(t => t != typeof(DomainEventOutboxEntry)))
			{
				type.GetProperty("DepartmentId").Should().NotBeNull($"{type.Name} needs DepartmentId");
				if (type != typeof(RmsRecordGroupScope) && type != typeof(RmsDepartmentCutoverEvent) && type != typeof(RmsRecordPrintLayout) && type != typeof(RmsAccessAudit))
					type.GetProperty("ProtectionId").Should().NotBeNull($"{type.Name} needs ProtectionId");
			}
		}

		private static bool IsEntityNavigation(Type propertyType)
		{
			if (propertyType == typeof(string) || propertyType == typeof(byte[]))
				return false;

			if (typeof(IEntity).IsAssignableFrom(propertyType))
				return true;

			if (!typeof(IEnumerable).IsAssignableFrom(propertyType))
				return false;

			var elementType = propertyType.IsGenericType ? propertyType.GetGenericArguments().FirstOrDefault() : propertyType.GetElementType();
			return elementType != null && (typeof(IEntity).IsAssignableFrom(elementType) || elementType.IsClass && elementType != typeof(string) && elementType.Namespace?.StartsWith("Resgrid.Model") == true);
		}
	}
}
