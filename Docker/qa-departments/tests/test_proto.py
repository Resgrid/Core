import unittest

from seeder import proto


class ProtoTests(unittest.TestCase):
    """Payloads checked against protobuf-net output for Core's settings classes."""

    def test_module_settings(self):
        self.assertEqual(proto.module_settings(['inventory', 'messaging', 'maintenance']), 'CAGYAQGoAQE=')
        with self.assertRaises(ValueError):
            proto.module_settings(['calls'])

    def test_terminology(self):
        self.assertEqual(proto.terminology({'module.units': {'text': 'Apparatus', 'singular': 'Apparatus'}}), 'CiQKDG1vZHVsZS51bml0cxIJQXBwYXJhdHVzGglBcHBhcmF0dXM=')

    def test_call_numbering(self):
        self.assertEqual(proto.call_numbering('{YYYY}-{SEQ}', 5), 'Cgx7WVlZWX0te1NFUX0QBQ==')


if __name__ == '__main__':
    unittest.main()
