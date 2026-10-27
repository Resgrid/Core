# Incident Back Office schema and generators

`schema.py` is the table specification of the Incident Back Office (large-incident-logistics-finance-plan.md rev 4.1 §11): every
table, column, enum, index, scope and ADP classification. The other scripts turn it into the code that must agree with it.

| Script | Writes | Notes |
|---|---|---|
| `gen.py` | `Core/Resgrid.Model/IncidentLogistics/*Entities.cs` (and, with `--migrations`, the M0290–M0297 migrations) | Shipped migrations are immutable: `--migrations` refuses to overwrite an existing file. A new table goes in a **new** migration with the next physical number (registry §4C). |
| `gen_web.py` | `Web/Resgrid.Web/Areas/User/Models/IncidentBackOffice/IncidentLedgerCatalog.Generated.cs` | Desk descriptors: fields, list columns, filters, claims, lifecycles, read-only / append-only tables. |
| `gen_kinds.py` | `IncidentDeskKinds.Generated.cs` and the thin `*Controller.Generated.cs` desks | Desks with domain actions are listed in `HANDWRITTEN` and keep their own controllers. |
| `gen_resx.py` | `Core/Resgrid.Localization/Areas/User/IncidentBackOffice/IncidentBackOffice{,.en}.resx` and `out/en.json` | Curated UI text plus generated labels for fields, enums, tables, policies and triggers; fails when a key used in code has no text (`scan_keys.py`). |
| `write_locale_resx.py` | `IncidentBackOffice.{locale}.resx` | From a translated `{key: text}` map of `out/en.json`; fails on a missing key or a dropped `{n}` placeholder. |

Run them from this folder with Python 3 in this order after changing `schema.py`: `gen.py`, `gen_web.py`, `gen_kinds.py`,
`gen_resx.py`, then add the new keys of `out/en.json` to every `translations/{locale}.json` and run
`python write_locale_resx.py {locale} translations/{locale}.json` per locale. The translation maps use each locale's existing
product terms (for example the department is "Oddział" in Polish and "Підрозділ" in Ukrainian, as in `Common.*.resx`). Keep
`IncidentBackOfficeTables`, `IncidentBackOfficeProtectedFields`, the department cleanup and the ADP bindings in step — the
`IncidentBackOfficeTests` fixture checks they agree.
