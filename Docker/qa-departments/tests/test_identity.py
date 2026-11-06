import base64
import struct
import unittest

from seeder.identity import Ids, hash_password, password_problems, totp, verify_password


class IdentityTests(unittest.TestCase):
    def test_password_hash_is_identity_v3_and_verifies(self):
        hashed = hash_password('QaPassw0rd', salt=b'\x01' * 16)
        data = base64.b64decode(hashed)
        self.assertEqual(data[0], 0x01)
        self.assertEqual(struct.unpack('>III', data[1:13]), (2, 100_000, 16))
        self.assertEqual(len(data), 13 + 16 + 32)
        self.assertTrue(verify_password(hashed, 'QaPassw0rd'))
        self.assertFalse(verify_password(hashed, 'qapassw0rd'))

    def test_password_rules_match_core_identity_options(self):
        self.assertEqual(password_problems('QaPassw0rd'), [])
        self.assertEqual(password_problems('short1A'), ['at least 8 characters'])
        self.assertIn('an upper-case letter', password_problems('lowercase1'))

    def test_totp_matches_rfc_6238_vector(self):
        # RFC 6238 appendix B, SHA-1, T = 59 s: 94287082 (8 digits) -> last 6 digits.
        secret = base64.b32encode(b'12345678901234567890').decode()
        self.assertEqual(totp(secret, at=59), '287082')

    def test_ids_are_stable_per_run_tag(self):
        self.assertEqual(Ids('abc').user('qa-base-admin-abc'), Ids('abc').user('QA-BASE-ADMIN-ABC'))
        self.assertNotEqual(Ids('abc').user('x'), Ids('abd').user('x'))
        self.assertEqual(Ids('abc').user('x'), Ids('abc').user('x').upper())


if __name__ == '__main__':
    unittest.main()
