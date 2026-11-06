"""Advanced Data Protection key material and field envelopes, byte-compatible with Core's LocalDev provider.

Only for the LocalDev key wrapping provider (DataProtectionConfig.KeyWrappingProviderType = "LocalDev"), which Core
refuses to start in production. The wrapping key is the fixed development key from LocalDevKeyWrappingProvider.cs, so
seeded envelopes decrypt on any non-production server running LocalDev, and nowhere else.
"""
from __future__ import annotations

import base64
import hashlib
import secrets

try:
    from cryptography.hazmat.primitives.ciphers.aead import AESGCM
except ImportError:  # The image installs it; plain `sql` previews do not need it.
    AESGCM = None

_LOCALDEV_SEED = b'resgrid-localdev-key-wrapping-NOT-FOR-PRODUCTION'
_WRAPPING_KEY = hashlib.sha256(_LOCALDEV_SEED).digest()
ENVELOPE_FORMAT = 1
PROVIDER_TYPE = 'LocalDev'
PROVIDER_KEY_REFERENCE = 'localdev'

# DepartmentDataProtectionState / DepartmentDataProtectionKeyStatus (Core/Resgrid.Model).
STATE_ENABLED = 5
KEY_ACTIVE = 1

# The current ProtectedFieldCatalog.Version (highest AddedInCatalogVersion, Core/Resgrid.Services/ProtectedFieldCatalog.cs).
# A lower value makes the worker start a catalog-upgrade migration; override with --adp-catalog-version when Core moves.
CATALOG_VERSION = 36

# Catalog field ids (ProtectedFieldCatalog BuildV1) and the row key Core binds each envelope to.
CALL_FIELDS = {'NatureOfCall': 'calls.natureofcall', 'Notes': 'calls.notes', 'CompletedNotes': 'calls.completednotes', 'ContactName': 'calls.contactname', 'ContactNumber': 'calls.contactnumber'}
CALL_NOTE_FIELDS = {'Note': 'callnotes.note'}


def _require() -> None:
    if AESGCM is None:
        raise RuntimeError('ADP seeding needs the "cryptography" package (installed in the qa-departments image).')


def new_data_key() -> bytes:
    return secrets.token_bytes(32)


def _seal(key: bytes, plaintext: bytes, aad: bytes) -> bytes:
    _require()
    nonce = secrets.token_bytes(12)
    sealed = AESGCM(key).encrypt(nonce, plaintext, aad)
    # cryptography returns ciphertext | tag; Core stores nonce | tag | ciphertext.
    return nonce + sealed[-16:] + sealed[:-16]


def _open(key: bytes, payload: bytes, aad: bytes) -> bytes:
    _require()
    nonce, tag, ciphertext = payload[:12], payload[12:28], payload[28:]
    return AESGCM(key).decrypt(nonce, ciphertext + tag, aad)


def wrap_data_key(department_id: int, data_key: bytes) -> str:
    return base64.b64encode(_seal(_WRAPPING_KEY, data_key, f'resgrid-dept:{department_id}'.encode())).decode('ascii')


def unwrap_data_key(department_id: int, wrapped: str) -> bytes:
    return _open(_WRAPPING_KEY, base64.b64decode(wrapped), f'resgrid-dept:{department_id}'.encode())


def _field_aad(department_id: int, field_id: str, row_key: str) -> bytes:
    return f'rgdp|{department_id}|{field_id}|{row_key}|{ENVELOPE_FORMAT}'.encode()


def envelope(data_key: bytes, key_version: int, department_id: int, field_id: str, row_key: str, plaintext: str) -> str:
    payload = _seal(data_key, plaintext.encode('utf-8'), _field_aad(department_id, field_id, row_key))
    return f'rgdp:{ENVELOPE_FORMAT}:{key_version}:' + base64.b64encode(payload).decode('ascii')


def open_envelope(data_key: bytes, department_id: int, field_id: str, row_key: str, value: str) -> str:
    _, _, _, payload = value.split(':', 3)
    return _open(data_key, base64.b64decode(payload), _field_aad(department_id, field_id, row_key)).decode('utf-8')
