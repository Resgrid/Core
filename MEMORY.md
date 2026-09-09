# Project Memory

## Localization

- Use the languages in `SupportedLocales.SupportedLanguagesMap` for every new resource family. Provide real translations for every key, including Arabic; never populate non-English dictionaries with English placeholders. Translate interface labels and choices while preserving persisted response codes, user-authored content, product names and standard file-format identifiers. Verify key coverage, formatting placeholders and browser behavior across all supported languages.

## Integration and data protection

- Readiness features must emit registered Workflow domain events through the transactional outbox. Project only reviewed metadata before serialization; ADP-enforced Workflows receive REDACTED values and redaction metadata, never protected plaintext or a user grant.
- Inventory every readiness model, free-text/JSON slot, attachment and derived copy for PII/PHI and integrate it with the existing ADP catalog/read/write/lifecycle system. Durable protection state survives billing or feature-flag changes; unattended generation must use approved metadata and protected source references without a decryption bypass.
