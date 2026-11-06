import unittest

from seeder import proto


class ProtoTests(unittest.TestCase):
    """Payloads checked against protobuf-net output for Core's settings classes."""

    def test_module_settings(self):
        self.assertEqual(proto.module_settings(['inventory', 'messaging', 'maintenance']), 'CAGYAQGoAQE=')
        with self.assertRaises(ValueError):
            proto.module_settings(['calls'])

    def test_module_settings_cover_the_visibility_switches(self):
        # RecordsDisabled = 29 ... ChatDisabled = 33: two-byte field keys (29 << 3 = 0xE8 0x01, 33 << 3 = 0x88 0x02).
        self.assertEqual(proto.module_settings(['chat', 'records']), '6AEBiAIB')
        self.assertTrue({'records', 'contacts', 'certifications', 'deployments', 'chat'} <= set(proto.MODULE_TAGS))

    def test_terminology(self):
        self.assertEqual(proto.terminology({'module.units': {'text': 'Apparatus', 'singular': 'Apparatus'}}), 'CiQKDG1vZHVsZS51bml0cxIJQXBwYXJhdHVzGglBcHBhcmF0dXM=')

    def test_call_numbering(self):
        self.assertEqual(proto.call_numbering('{YYYY}-{SEQ}', 5), 'Cgx7WVlZWX0te1NFUX0QBQ==')


if __name__ == '__main__':
    unittest.main()
