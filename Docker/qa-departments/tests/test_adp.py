import unittest

from seeder import adp


@unittest.skipIf(adp.AESGCM is None, 'cryptography is not installed (it is in the image)')
class AdpTests(unittest.TestCase):
    def test_wrapped_key_round_trips_and_is_bound_to_the_department(self):
        key = adp.new_data_key()
        wrapped = adp.wrap_data_key(42, key)
        self.assertEqual(adp.unwrap_data_key(42, wrapped), key)
        with self.assertRaises(Exception):
            adp.unwrap_data_key(43, wrapped)

    def test_envelope_format_and_binding(self):
        key = adp.new_data_key()
        value = adp.envelope(key, 1, 42, 'calls.natureofcall', '1001', 'Chest pain')
        self.assertTrue(value.startswith('rgdp:1:1:'))
        self.assertEqual(adp.open_envelope(key, 42, 'calls.natureofcall', '1001', value), 'Chest pain')
        with self.assertRaises(Exception):
            adp.open_envelope(key, 42, 'calls.natureofcall', '1002', value)


if __name__ == '__main__':
    unittest.main()
