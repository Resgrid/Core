"""Self-signed certificates for the QA stack (runs inside the seeder image, which has `cryptography`).

  python certs.py <private dir>
writes tls/localhost.{crt,key} for the gateway and oidc-encryption.b64 / oidc-signing.b64 (base64 PKCS#12, no password)
for OidcConfig.EncryptionCert / SigningCert. Existing files are kept, so restarting the stack keeps issued tokens valid.
"""
import base64
import datetime as dt
import ipaddress
import sys
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID


def certificate(name: str, usage: x509.KeyUsage, sans: list[x509.GeneralName] | None = None):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, name)])
    now = dt.datetime.now(dt.timezone.utc)
    builder = (x509.CertificateBuilder().subject_name(subject).issuer_name(subject).public_key(key.public_key()).serial_number(x509.random_serial_number())
               .not_valid_before(now - dt.timedelta(days=1)).not_valid_after(now + dt.timedelta(days=3650)).add_extension(usage, critical=True))
    if sans:
        builder = builder.add_extension(x509.SubjectAlternativeName(sans), critical=False)
    return key, builder.sign(key, hashes.SHA256())


def usage(**flags: bool) -> x509.KeyUsage:
    names = ('digital_signature', 'content_commitment', 'key_encipherment', 'data_encipherment', 'key_agreement', 'key_cert_sign', 'crl_sign', 'encipher_only', 'decipher_only')
    return x509.KeyUsage(**{name: flags.get(name, False) for name in names})


def main(private: Path) -> None:
    tls = private / 'tls'
    tls.mkdir(parents=True, exist_ok=True)
    if not (tls / 'localhost.crt').exists():
        key, cert = certificate('localhost', usage(digital_signature=True, key_encipherment=True),
                                [x509.DNSName('localhost'), x509.IPAddress(ipaddress.ip_address('127.0.0.1')), x509.DNSName('gateway')])
        (tls / 'localhost.key').write_bytes(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.TraditionalOpenSSL, serialization.NoEncryption()))
        (tls / 'localhost.crt').write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    for name, flags in (('oidc-encryption', {'key_encipherment': True}), ('oidc-signing', {'digital_signature': True})):
        path = private / f'{name}.b64'
        if not path.exists():
            key, cert = certificate(f'Resgrid QA {name}', usage(**flags))
            path.write_text(base64.b64encode(pkcs12.serialize_key_and_certificates(name.encode(), key, cert, None, serialization.NoEncryption())).decode())


if __name__ == '__main__':
    main(Path(sys.argv[1]))
