import io, os, re, glob, json

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
WEB = os.path.join(ROOT, 'Web', 'Resgrid.Web', 'Areas', 'User')

files = []
for pattern in [os.path.join(WEB, 'Views', 'IncidentBackOffice', '**', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentPeriods', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentOrganization', '*.cshtml'),
                os.path.join(WEB, 'Views', 'IncidentParticipants', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentCheckIn', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentTeams', '*.cshtml'),
                os.path.join(WEB, 'Views', 'IncidentPositions', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentBackOfficeBilling', '*.cshtml'),
                os.path.join(WEB, 'Controllers', 'IncidentBackOffice', '*.cs'), os.path.join(ROOT, 'Web', 'Resgrid.Web', 'Views', 'IncidentSelfService', '*.cshtml'), os.path.join(ROOT, 'Web', 'Resgrid.Web', 'Controllers', 'IncidentSelfServiceController.cs'), os.path.join(WEB, 'Views', 'IncidentOperations', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentLogistics', '*.cshtml'), os.path.join(WEB, 'Views', 'IncidentFinance', '*.cshtml'), os.path.join(WEB, 'Controllers', 'IncidentBackOfficeBillingController.cs'),
                os.path.join(WEB, 'Models', 'IncidentBackOffice', '*.cs'), os.path.join(ROOT, 'Core', 'Resgrid.Services', 'IncidentLogistics', '*.cs')]:
    files += glob.glob(pattern, recursive=True)
extra = {os.path.join(WEB, 'Views', 'Shared', '_Navigation.cshtml'): 'incidentBackOfficeLocalizer', os.path.join(WEB, 'Views', 'Department', 'ModuleSettings.cshtml'): 'incidentLocalizer',
         os.path.join(WEB, 'Views', 'Subscription', 'Index.cshtml'): 'incidentLocalizer'}

keys = set()
rules = set()
notices = set()
for f in files:
    s = io.open(f, encoding='utf-8-sig').read()
    for m in re.finditer(r'(?:localizer|Strings|Desk\.Strings|_strings)\["([^"\]]+)"', s):
        keys.add(m.group(1))
    for m in re.finditer(r'IncidentRuleException\("([A-Za-z]+)"', s):
        rules.add(m.group(1))
    for m in re.finditer(r'IncidentNoticeText\.(?:Format\([^,]+,|Text\()\s*"([^"]+)"', s):
        notices.add(m.group(1))
    for m in re.finditer(r'(?:LabelKey|ConfirmKey) = "([^"]+)"', s):
        keys.add(m.group(1))
    for m in re.finditer(r'I\("[A-Za-z]+", "([A-Za-z.]+)"', s):
        keys.add(m.group(1))
for f, name in extra.items():
    s = io.open(f, encoding='utf-8-sig').read()
    for m in re.finditer(name + r'\["([^"\]]+)"', s):
        keys.add(m.group(1))

# also rule keys thrown from model / other services of the module
for f in glob.glob(os.path.join(ROOT, 'Core', 'Resgrid.Model', 'IncidentLogistics', '*.cs')):
    s = io.open(f, encoding='utf-8-sig').read()
    for m in re.finditer(r'IncidentRuleException\("([A-Za-z]+)"', s):
        rules.add(m.group(1))

dynamic_prefixes = ('Enum.', 'Field.', 'Kind.', 'Desk.', 'DeskIntro.', 'KindIntro.', 'Metric.', 'Window.', 'Policy', 'Notice.', 'Help.', 'Rule.', 'Error.', 'SeedCatalog.', 'Candidate', 'Section.', 'Nav.')
literal = sorted(k for k in keys if not k.endswith('.') and '{' not in k)
json.dump({'literal': literal, 'rules': sorted(rules), 'notices': sorted(notices)}, io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out', 'keys.json'), 'w', encoding='utf-8'), indent=1)
print(len(literal), len(rules), len(notices))
print([k for k in literal if not k.startswith(dynamic_prefixes)])
print(sorted(rules))
print(sorted(notices))
print(sorted(k for k in literal if k.startswith(dynamic_prefixes)))
