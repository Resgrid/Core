"""Feature flag profiles: which department overrides a scenario seeds, and the state each flag should then evaluate to.

The flag list is read from the target database at seed time (every flag that is not archived), so a flag added by a
later migration is covered without a catalog change. Overrides are operator overrides (no author), which Core honours
even while a flag is off globally or below its plan gate (FeatureToggleService step 6). Archived flags, an environment
scope, a schedule window and prerequisites are decided before overrides; the prediction below follows the same order.
"""
from __future__ import annotations

import datetime as dt
from dataclasses import dataclass, field

PROFILES = ('server', 'all-on', 'all-off')


@dataclass(frozen=True)
class Flag:
    id: int
    key: str
    enabled_globally: bool
    archived: bool
    flag_type: int
    default_value: str | None
    off_value: str | None
    rollout: int | None
    environment: int | None
    enable_on: dt.datetime | None
    disable_on: dt.datetime | None
    minimum_plan: int | None
    permanent: bool
    prerequisites: tuple[tuple[int, str | None], ...] = field(default=())


@dataclass(frozen=True)
class Prediction:
    enabled: bool | None
    # override | global | rollout | plan | archived | environment | schedule | prerequisite | targeting
    source: str


def overrides_for(profile: str, explicit: dict[str, bool], flags: list[Flag]) -> dict[int, bool]:
    """flag id -> seeded override for a department with this profile plus per-key choices."""
    if profile not in PROFILES:
        raise ValueError(f'unknown flag profile "{profile}"; expected one of {PROFILES}')
    by_key = {flag.key.lower(): flag for flag in flags}
    unknown = [key for key in explicit if key.lower() not in by_key]
    if unknown:
        raise ValueError(f'flags {unknown} are not active flags on this server')
    chosen: dict[int, bool] = {}
    if profile != 'server':
        for flag in flags:
            if not flag.archived:
                chosen[flag.id] = profile == 'all-on'
    for key, value in explicit.items():
        chosen[by_key[key.lower()].id] = bool(value)
    return chosen


def predict(flags: list[Flag], overrides: dict[int, bool], now: dt.datetime, server_environment: int | None, targeted: set[int] | None = None) -> dict[str, Prediction]:
    """The state each flag should evaluate to for a department seeded with `overrides`, keyed by flag key."""
    by_id = {flag.id: flag for flag in flags}
    targeted = targeted or set()
    results: dict[int, Prediction] = {}

    def evaluate(flag: Flag, visiting: frozenset[int]) -> Prediction:
        if flag.id in results:
            return results[flag.id]
        if flag.id in visiting:
            return Prediction(False, 'prerequisite')
        prediction = _evaluate(flag, visiting | {flag.id})
        results[flag.id] = prediction
        return prediction

    def _evaluate(flag: Flag, visiting: frozenset[int]) -> Prediction:
        if flag.archived:
            return Prediction(False, 'archived')
        if flag.environment is not None:
            if server_environment is None:
                return Prediction(None, 'environment')
            if flag.environment != server_environment:
                return Prediction(False, 'environment')
        if (flag.enable_on and now < flag.enable_on) or (flag.disable_on and now >= flag.disable_on):
            return Prediction(False, 'schedule')
        for required_id, required_value in flag.prerequisites:
            required = by_id.get(required_id)
            if required is None:
                continue
            parent = evaluate(required, visiting)
            if parent.enabled is None:
                return Prediction(None, 'prerequisite')
            if not parent.enabled:
                return Prediction(False, 'prerequisite')
            if required_value:
                # The parent's value is only known for an on override or a global default.
                value = required.default_value
                if (value or '').lower() != required_value.lower():
                    return Prediction(False, 'prerequisite')
        if flag.id in overrides:
            return Prediction(overrides[flag.id], 'override')
        if flag.id in targeted:
            # Targeting rules depend on department attributes the seed does not model; the verify step records the outcome.
            return Prediction(None, 'targeting')
        if not flag.enabled_globally:
            return Prediction(False, 'global')
        if flag.minimum_plan is not None:
            # The plan gate reads the billing plan, which the seed does not control on a shared server.
            return Prediction(None, 'plan')
        if flag.rollout is not None and flag.rollout < 100:
            return Prediction(None, 'rollout')
        return Prediction(True, 'global')

    return {flag.key: evaluate(flag, frozenset()) for flag in flags}


def override_value(flag: Flag, enabled: bool) -> str | None:
    """FlagValue for an override: a multivariate flag carries its on/off variant, a boolean flag none."""
    if flag.flag_type == 0:
        return None
    return flag.default_value if enabled else flag.off_value
