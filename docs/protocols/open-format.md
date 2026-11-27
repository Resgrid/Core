# Resgrid protocols open format (version 2)

Resgrid exports and imports protocols in one documented JSON format, `resgrid-protocols`. An agency can take its whole library to
another system, or bring one in, without Resgrid's help. The JSON schema is [`resgrid-protocols-2.schema.json`](resgrid-protocols-2.schema.json).

Where it is used:

- **Protocols → Import** and the library's JSON export (one or more protocols).
- **Protocols → Sharing → Open format**: the whole library or one category (with its sub-categories) as a *set*, plus the same set as one
  HTML file or as the PDF book.
- **The Resgrid template library**: every starter set is a version 2 file with a `set` header, embedded in the product.
- **The community exchange**: a protocol listing's payload is a version 2 file of one protocol, stripped of department data.
- **The department import wizard** (Protocols step) reads the same files.

## Versions and compatibility

| `version` | Written by | Read by |
|---|---|---|
| 1 | P1 library export (2026-11) | every importer since P1 |
| 2 | P5 export (current) | P5 importers; every version 1 file still imports unchanged |

Version 2 only **adds optional fields**. A version 1 file is a valid version 2 file with `"version": 1`. An importer refuses an unknown
`format` and any `version` it does not know (`unknown_format`).

## Document

```json
{
  "$schema": "https://resgrid.com/schemas/resgrid-protocols-2.schema.json",
  "format": "resgrid-protocols",
  "version": 2,
  "exportedOn": "2026-11-25T12:00:00Z",
  "set": { "key": "category-12", "title": "Fireground", "publisher": "Anytown Fire Department", "setVersion": "2026-11-25",
           "license": "CC-BY-4.0", "licenseUrl": "https://creativecommons.org/licenses/by/4.0/" },
  "protocols": [ { "...": "one object per protocol, below" } ]
}
```

At most 500 protocols per file.

## Protocol

| Field | Since | Meaning |
|---|---|---|
| `code`, `name` | 1 | Required. Code: up to 16 characters, `A-Z 0-9 . _ / -` and spaces. |
| `kind` | 1 | `Dispatch`, `Sog`, `ClinicalGuideline`, `Policy`, `Procedure` or `Reference`. Unknown kinds import as `Dispatch`. |
| `number`, `category`, `tags`, `description` | 1 | Library fields. The category is matched to the importer's categories by name. |
| `minimumWeight`, `requiresAcknowledgment`, `reviewIntervalDays` | 1 | As in the library. |
| `excludeFromAi`, `isLicensedContent` | 1 | The "no AI" flag and the licensed-content flag. Licensed content is never shared, never on a public page and never in the template library or the exchange. |
| `versionLabel` | 1 | The exporter's version number (e.g. `3.1`). Imports always start as a **draft** with no number. |
| `content` | 1 | The version's content document (blocks and decision graph), below. |
| `triggers`, `questions`, `audience` | 1 | Call triggers (priority and call type **by name**), key questions, and the audience (group, role or shift **by name**). Anything that does not match the importer's names is left out and reported. |
| `attribution` | 2 | `source`, `author`, `license`, `licenseUrl`, `sourceVersion`. Kept on the imported version and on every later draft. |
| `locale`, `effectiveOn`, `contentHash` | 2 | The content language (BCP 47), when the version took force, and the SHA-256 (lower-case hex) of the version's stored HTML. |
| `history` | 2 | Published versions, newest first: `label`, `status` (`Published`, `Superseded`, `Retired`), `effectiveOn`, `supersededOn`, `changeSummary`, `contentHash`. No content. |
| `translations` | 2 | Approved translations of the exported version: `locale`, `content`, `contentHash`, `sourceContentHash`. Listed for the importer; an imported draft starts untranslated (translate it after it is published). |
| `attachments` | 2 | Manifest only: `kind` (`attachment` or `document`), `fileName`, `contentType`, `size`, `sha256`. File bytes are never in the JSON. |

## Content document

```json
{ "schema": 2,
  "blocks": [
    { "type": "heading", "key": "purpose", "text": "Purpose", "level": 2 },
    { "type": "paragraph", "key": "p1", "html": "<p>…</p>" },
    { "type": "steps", "key": "s1", "title": "On arrival", "items": [ { "key": "s1a", "html": "<p>…</p>" } ] },
    { "type": "caution", "key": "c1", "title": "…", "html": "<p>…</p>" } ],
  "anchors": [ { "id": "b-purpose", "text": "Purpose", "level": 2, "block": "purpose" } ],
  "graph": { "nodes": [ { "key": "q1", "root": true, "kind": 0, "text": "Is the patient breathing?", "answers": [ … ] } ] } }
```

- `schema` 1 is one `html` block (P1); `schema` 2 is structured blocks (P3): `heading`, `paragraph`, `steps`, `caution`, `warning`, `gate`,
  `table`, `image`, `video`, `attachment`, `link`, `medication` and `variants` (adult / pediatric groups holding blocks of their own).
- Keys are `a-z0-9`, up to 24 characters, unique in the version; block anchors are `b-{key}` (steps `b-{block}-{item}`).
- Rich text (`html`) is sanitized on import with the same allow-list as the editor. Unknown block types are ignored by readers that do not
  know them.
- **Medication doses are text**, shown exactly as authored. Nothing in Resgrid computes a dose.
- `link` blocks of kind `protocol`, `poi`, `inventory`, `checklist` or `training` point at the exporter's own records (`refId`); only `url`
  links mean the same thing elsewhere. `attachment` blocks and uploaded `image` blocks refer to the exporter's files.
- `graph` is the decision graph of a Dispatch protocol (the P4 case flow): question, instruction and outcome nodes with determinants.
  `priority` is the exporter's priority id (informational); `priorityName` travels by name.

## Import rules

1. Everything imports as a **draft**; nothing is published by an import. The department reviews and publishes it through its own approval steps.
2. Names (categories, priorities, call types, groups, roles, shifts) are matched case-insensitively; misses are reported, not created.
3. `attribution` (version 2) is kept on the imported version and carried to every later draft, so "import and modify" never loses the credit.
4. Files listed in `attachments` are not imported; add them to the draft.

## HTML and PDF of a set

The same set exports as one self-contained HTML file (the PDF book's rendering: a table of contents and every protocol in force with its
version, effective date, approver and content short code) and as the PDF book, sealed when document sealing is configured.

## The Resgrid template library

Starter sets are version 2 files written by Resgrid (fire SOGs, search and rescue, industrial hazards, business continuity: CC BY 4.0). The
EMS set is **structure only** (CC0): openly licensed model clinical guidelines join the library only after their redistribution licence has
been checked (plan decision 5), so the set carries section headings and placeholders, and the department's medical director writes every
clinical word.
