# Setup Wizard, Setup Report and Admin Assist

<a id="start-setup"></a>
## Start or resume setup

Department administrators can open **Setup Wizard** from the Department or Help menu when setup is enabled for the deployment. An unfinished setup also appears on the dashboard. Dismissing that prompt affects only your account; the menu remains available. Setup Wizard and Setup Report are available independently of Admin Assist and do not require an AI add-on.

Choose **Fresh setup**, **Review existing setup**, or **Import or migration**. The choice records your intent; it does not import or overwrite data. Use the department operating profile to describe your organization and link approved local policies. Fire, EMS, mental health, SAR, emergency response, Hazmat, industrial, security and mutual-aid packs suggest areas to review. They do not establish qualifications or authorize clinical, tactical or hazardous work.

The nine-step journey covers goals and applications, the department profile, all-area orientation, people and access, operational essentials, selected workflows, add-ons, verification and practice, and review and handover. Pause and resume without creating sample incidents or overwriting existing configuration.

Selected operating packs suggest areas without changing your choices. Site references use existing department group IDs; policy references use existing, unexpired department document IDs. Saving checks those references and rejects concurrent overwrites. A valid reference does not prove that its contents are approved or sufficient. Verification checks whether linked policies or site groups later become unavailable, and flags documents scheduled for removal within 30 days. A reviewed profile without a continuity link receives an administrative review prompt; an external procedure may still exist. The checks do not read or certify policy contents.

An optional **Expected email polling interval** compares recorded mailbox polls with your declared maximum interval. Leave it blank when no interval has been established. Missing or future poll timestamps remain unknown, and low call volume alone does not imply failure. SMS, CAD push and API intake require separate telemetry; the expectation does not change polling or send a test.

<a id="choose-areas"></a>
## Choose the areas your department uses

Mark each area **Use now**, **Learn later**, or **Not applicable**. A not-applicable choice requires a reason: outside the mission, managed in another approved system, managed by a responsible partner, or no current need. Baseline security remains in scope. Explore Resgrid covers applications, calls, people, units and location, communication, contacts and site knowledge, records, maintenance, inventory, deployments and business, automation, security and plans. Each feature explains its purpose, value, example and adoption requirements.

Area choices are shared by department administrators. Learning and interest choices are personal. Another administrator's save can require you to reload and review before saving again. Learning a feature does not configure it, purchase an add-on, enable a module or send anything.

<a id="configure-and-verify"></a>
## Configure and verify

Use **Open owning screen** to configure a feature with its existing permissions and validation. Some settings include contextual help and a highlighted field when opened from Admin Assist. Save on that screen, then use **Return to setup and verify**. The return link does not submit the form. Its navigation context lasts 30 minutes and is tied to the current administrator and department, so it can survive the owning screen’s save and redirect. Returning to setup clears it. Choose **Verify again** after saving.

The report separates verified checks, failures and unknown evidence. Unpurchased optional add-ons do not reduce core completion. Deferring an area does not hide a verified critical failure or uncertainty in an active critical check. Selected areas with no automated checks are listed explicitly; they are not verified. Missing, restricted or unavailable source data remains unknown; it is never treated as zero. Critical unknowns prevent a fully verified result. Review the evidence time and refresh again if configuration changed during the read.

Record an administrative review to retain the report version, evidence revision, selected-scope revision, timestamp and unresolved-check counts. A changed configuration, scope or catalog makes that review visibly out of date. Each newly added administrator still has a separate orientation checklist. An optional revisit date is stored in the report; it does not schedule a one-off notification. Weekly follow-up is a separate preference.

Setup Report replaces the old numerical setup score. It is administrative configuration guidance, not certification of operational readiness or proof that a page was delivered. Start a Communication Test explicitly through its own screen when communication verification is needed.

**Open a fresh printable report** rechecks access and reloads the summary. It includes current findings, selected areas, personal feature interests and public add-on guidance. It omits protected notes, names, source records and credentials. Printing or saving a local copy does not create a managed server export; handle the copy under department policy.

<a id="understand-addons"></a>
## Understand optional add-ons

| Add-on | What it adds | Adoption considerations |
|---|---|---|
| Push-to-Talk | Voice channels in supported Resgrid clients | Review seats, devices and channel membership. Phone voice alerts and radio requirements are separate. |
| Advanced Data Protection | Additional protection and scoped disclosure for supported sensitive content | Purchase and enrollment are separate. Review MFA, recovery, supported fields and effects on search, exports and integrations. |
| Readiness Pro | Maintenance, work orders, preventive and corrective work, approvals and safety holds | Checklists remain available without the add-on. Maintenance also needs its module and permissions. |
| Business Operations | Invoicing, rates, contracts, bids, reimbursement and workforce costing | Certifications and Deployment Finance remain available without the add-on. Pay-data reporting also requires enabled ADP; payment providers need separate setup. |
| Enhanced AI | Planned summaries, drafts, knowledge assistance and optional conversation | Availability depends on release and rollout. Deterministic setup and Admin Assist do not require it. |

Availability can depend on subscription, rollout, module settings, permissions, protection enrollment and source availability. An unknown subscription is not confirmation of a free or paid plan. Only the managing member can perform subscription changes through the billing screen. Feature interest does not start a trial or purchase.

The add-on step opens a comparison of all five add-ons. Each card lists its related features. Mark a feature **Interested in this feature** to see it in Setup Report with its current prerequisites and next available action. **I understand this feature**, availability, purchased entitlement, configuration and verified checks are separate states. Buying an add-on does not configure or verify the feature. The feature-setup section distinguishes recorded configuration, supported check results, current entitlement and optional opportunities. Initial evidence mappings cover personnel, groups, units, run cards, check-in timers, weather zones and email intake. Other feature configuration remains unassessed; absence of setup evidence does not prove a feature is unused.

<a id="follow-up"></a>
## Follow up on findings

Admin Assist adds an administrative worklist. Claim an unresolved finding, set a review date, start a review, or record an accepted exception with a reason and expiry. Exceptions do not make the underlying check pass. Fresh verification resolves a finding; a later verified failure reopens it. An evidence outage cannot resolve it. Fix records, qualifications, inventory and other source tasks through their owning screens.

Weekly follow-up is opt-in and only runs when the deployment enables scheduled digests. Quiet hours use the department time zone. The message contains a generic link to Admin Assist; open the authenticated page to see current evidence. A notification handoff is not confirmation of delivery. Turning the preference off suppresses future handoffs.

<a id="reference-and-history"></a>
## Use Settings Reference and Change History

Settings Reference searches a release-pinned public documentation catalog. It does not search department records or attachments. If your language has no reference article, the result labels its source language.

Supported scalar settings offer **Preview a proposed value**. A preview compares values in memory, shows related health-check changes and lists its limits. It does not save configuration. Current operational previews cover v4 map-marker selection, the automatic-availability status projection and administrator MFA enrollment. These do not establish every client's behavior, actual physical availability or session/recovery readiness.

**Preview plan capacity** compares proposed total personnel and unit counts with observed base-plan headroom. It does not purchase capacity or add resources; add-on seats, provider quotas and pricing are separate.

**Preview dispatch routing** uses a saved call ID as a route scenario. Choose an explicit UTC roster time within seven days of now and all three proposed shift/crew/group options. It compares direct, group, role and unit crew/group recipients with the broadcaster's resolver, preserving direct duplicates and empty-shift fallback. Current membership and crews are not reconstructed historical assignments. Unit devices, printers, channel eligibility, provider handoff and delivery are outside these personnel counts. The preview sends nothing and rereads inputs to detect changes during evaluation.

**Preview a permission change** compares current members through the supported action or resource visibility gate. It includes roles, department and group administration, target-group ancestors, and ungrouped targets. It counts allowed actor/target pairs separately from members, so a narrowed group scope is visible even when the same members retain some access. Existing sessions, protected fields and other permission gates still require verification.

**Preview a module switch** compares web navigation and bounded primary-table counts. Hiding an entry keeps the underlying data; it does not prove API access is revoked or a worker stops. Mapping, Reports, Logs/Records and Inventory data totals remain unknown where a complete adapter is unavailable. Licensed maintenance, checklists and business switches require separate entitlement-aware previews.

**Preview a text sender scenario** compares a test number against current source patterns and the complete proposed call/command switches. Choose the provider path actually in use. The result returns a masked reference and distinguishes routing branches from actual successful acceptance. SignalWire and Twilio legacy paths share their production routing decisions with the preview, but do not consult the switches uniformly. Chatbot and master-number paths, number ownership, active SMS department, plan access, webhook authentication, verified identity, opt-out exceptions and parser success require separate verification. A missing dispatch-source pattern is not proof that verified members cannot use commands. The preview never receives or sends a text.

**Preview notification volume** compares the current staffing-suppression setting with a proposed toggle for a declared department-wide scenario. Set the future window and assumed events per member across that entire window. The result separates current membership, preference/contact gates, confirmed suppression, unknown profiles/staffing and possible channel-handoff ranges. It uses no historical event sample and does not resolve every notification-rule audience. Address/device validity, provider behavior, chat/voice, SMS segments and delivery remain separate. Changing this preview never saves settings, sends a notification or changes staffing.

**Preview sign-in and session policy** compares department-wide MFA, SSO-only policy, password age/minimum length, idle timeout or concurrent-session limits. Enrollment is distinct from verified factors and recovery. The SSO-only gate keeps its existing safety valve when no provider is enabled; an enabled provider is not a successful login test. Session estimates respect the host policy date and distinguish next-session limits from revocation. Password age uses the owning boundary and preserves its handling of untracked dates. No password, recovery secret, session ticket, IP address or provider credential is read by these projections, and the preview does not change credentials or sessions.

**Preview retention policy** compares a prospective Records default using a bounded sample of metadata. A blank default uses the system class default; zero means permanent. Historical policy and definition overrides remain in force, so older revisions do not simply inherit a shortened default. Known parent/preservation holds and permanent-content obligations are excluded; uncertain historical holds are identified. Remaining candidates need the owning lifecycle checks for children, disclosure copies, attachments, external storage, evidence, submissions, workflows and search erasure. These counts are not permission or proof of eligibility to purge. The preview does not read record bodies, purge content, change protection or cancel an add-on.

Counts and checks are only as current as their source evidence. Unquantified effects still need review on the owning screen. Refresh after a configuration conflict and verify again after an actual save. Reference results can expand the full release-pinned source text and identify its source language.

Change History starts when collection is enabled. Earlier changes are unavailable. History shows safe configuration differences; secrets and sensitive strings use presence/change markers. It does not expose another administrator's personal learning choices.
