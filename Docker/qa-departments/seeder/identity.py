"""Credentials for seeded users: ASP.NET Core Identity password hashes, stable ids and TOTP secrets."""
from __future__ import annotations

import base64
import hashlib
import hmac
import re
import secrets
import struct
import time
import uuid

# ASP.NET Core Identity V3 format (PasswordHasher<TUser>, .NET 7+ defaults): 0x01, PRF (2 = HMAC-SHA512),
# iteration count, salt length (all big-endian uint32), salt, 256-bit subkey.
_V3_MARKER = 0x01
_PRF_HMACSHA512 = 2
_ITERATIONS = 100_000
_SALT_BYTES = 16
_SUBKEY_BYTES = 32

# Matches Identity's options in the Core hosts (Tools/Resgrid.Console/Program.cs, Web/Resgrid.Web*/Startup.cs).
_PASSWORD_RULES = (
    (re.compile(r'.{8,}'), 'at least 8 characters'),
    (re.compile(r'[0-9]'), 'a digit'),
    (re.compile(r'[a-z]'), 'a lower-case letter'),
    (re.compile(r'[A-Z]'), 'an upper-case letter'),
)
USERNAME_CHARACTERS = re.compile(r'^[ a-zA-Z0-9\-._@]+$')


def password_problems(password: str) -> list[str]:
    return [rule for pattern, rule in _PASSWORD_RULES if not pattern.search(password)]


def hash_password(password: str, salt: bytes | None = None) -> str:
    salt = salt if salt is not None else secrets.token_bytes(_SALT_BYTES)
    subkey = hashlib.pbkdf2_hmac('sha512', password.encode('utf-8'), salt, _ITERATIONS, _SUBKEY_BYTES)
    header = bytes([_V3_MARKER]) + struct.pack('>III', _PRF_HMACSHA512, _ITERATIONS, len(salt))
    return base64.b64encode(header + salt + subkey).decode('ascii')


def verify_password(hashed: str, password: str) -> bool:
    data = base64.b64decode(hashed)
    if data[0] != _V3_MARKER:
        return False
    prf, iterations, salt_length = struct.unpack('>III', data[1:13])
    if prf != _PRF_HMACSHA512:
        return False
    salt = data[13:13 + salt_length]
    expected = data[13 + salt_length:]
    actual = hashlib.pbkdf2_hmac('sha512', password.encode('utf-8'), salt, iterations, len(expected))
    return hmac.compare_digest(actual, expected)


def generate_password() -> str:
    # Always satisfies the Identity rules above, whatever the random part contains.
    return 'Qa' + secrets.token_urlsafe(12).replace('-', 'x').replace('_', 'y') + '7z'


class Ids:
    """Stable GUIDs per run: the same run tag and catalog produce the same ids, so a generated script is reviewable."""

    _NAMESPACE = uuid.UUID('6f1b8a52-3e1c-4c55-9d43-0d3c4a2b7e10')

    def __init__(self, run_tag: str):
        self.run_tag = run_tag

    def guid(self, *parts: str) -> str:
        return str(uuid.uuid5(self._NAMESPACE, '/'.join((self.run_tag,) + parts)))

    def user(self, username: str) -> str:
        # Identity user ids are upper-case GUID strings in the existing data.
        return self.guid('user', username.lower()).upper()


# --- TOTP (RFC 6238, the authenticator-app codes the MFA flows verify) ----------------------------------------------

def generate_totp_secret() -> str:
    return base64.b32encode(secrets.token_bytes(20)).decode('ascii').rstrip('=')


def totp(secret: str, at: float | None = None, step: int = 30, digits: int = 6) -> str:
    key = base64.b32decode(secret + '=' * (-len(secret) % 8), casefold=True)
    counter = int((time.time() if at is None else at) // step)
    digest = hmac.new(key, struct.pack('>Q', counter), hashlib.sha1).digest()
    offset = digest[-1] & 0x0F
    code = (struct.unpack('>I', digest[offset:offset + 4])[0] & 0x7FFFFFFF) % (10 ** digits)
    return str(code).zfill(digits)
