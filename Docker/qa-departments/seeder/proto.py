"""The few protobuf-net settings payloads Core stores base64-encoded in DepartmentSettings (ObjectSerialization)."""
from __future__ import annotations

import base64

# DepartmentModuleSettings (Core/Resgrid.Model/DepartmentModuleSettings.cs): bool members, true = module disabled.
MODULE_TAGS = {
    'messaging': 1, 'mapping': 3, 'shifts': 5, 'logs': 7, 'reports': 9, 'documents': 11, 'calendar': 13, 'notes': 15,
    'training': 17, 'inventory': 19, 'maintenance': 21, 'checklists': 23, 'businessOperations': 24, 'ai': 25,
    'incidentBackOffice': 26, 'incidentBusiness': 27, 'incidentCoordination': 28,
}


def _varint(value: int) -> bytes:
    out = bytearray()
    while True:
        byte = value & 0x7F
        value >>= 7
        if value:
            out.append(byte | 0x80)
        else:
            out.append(byte)
            return bytes(out)


def _field_bool(tag: int, value: bool) -> bytes:
    return _varint(tag << 3) + _varint(1 if value else 0)


def _field_bytes(tag: int, value: bytes) -> bytes:
    return _varint((tag << 3) | 2) + _varint(len(value)) + value


def _field_string(tag: int, value: str) -> bytes:
    return _field_bytes(tag, value.encode('utf-8'))


def module_settings(disabled: list[str]) -> str:
    unknown = [name for name in disabled if name not in MODULE_TAGS]
    if unknown:
        raise ValueError(f'unknown modules {unknown}; expected some of {sorted(MODULE_TAGS)}')
    payload = b''.join(_field_bool(MODULE_TAGS[name], True) for name in sorted(disabled, key=MODULE_TAGS.get))
    return base64.b64encode(payload).decode('ascii')


def terminology(terms: dict[str, dict[str, str] | str]) -> str:
    """DepartmentTerminologyConfig {1: repeated DepartmentTermOverride {1 Key, 2 Text, 3 Singular}}."""
    payload = b''
    for key, value in terms.items():
        text, singular = (value, None) if isinstance(value, str) else (value['text'], value.get('singular'))
        term = _field_string(1, key) + _field_string(2, text) + (_field_string(3, singular) if singular else b'')
        payload += _field_bytes(1, term)
    return base64.b64encode(payload).decode('ascii')


def call_numbering(pattern: str, width: int) -> str:
    """CallNumbering {1 Pattern, 2 SequenceWidth, ...}; tokens {YYYY} {YY} {MM} {DD} {SEQ}."""
    payload = _field_string(1, pattern) + _varint(2 << 3) + _varint(width)
    return base64.b64encode(payload).decode('ascii')
