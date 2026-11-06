import unittest

from seeder.verify import _expected_sign_in, _module_off


def department(sso='none', mfa=False, disabled=()):
    return {'features': {'sso': sso, 'mfaRequired': mfa, 'disabledModules': list(disabled)}}


class VerifyRuleTests(unittest.TestCase):
    def test_sign_in_expectations_follow_the_department(self):
        self.assertEqual(_expected_sign_in(department(), {'totpSecret': None}), ('signed-in',))
        self.assertNotIn('signed-in', _expected_sign_in(department(sso='required'), {'totpSecret': None}))
        # MFA enforcement is a server switch: an unenrolled member may be sent to enrollment or let in.
        self.assertEqual(set(_expected_sign_in(department(mfa=True), {'totpSecret': None})), {'mfa_enrollment_required', 'signed-in'})
        self.assertEqual(_expected_sign_in(department(mfa=True), {'totpSecret': 'ABC'}), ('signed-in',))

    def test_only_switched_off_modules_may_refuse(self):
        off = department(disabled=('messaging', 'shifts'))
        self.assertTrue(_module_off(off, 'Messages/GetInboxMessages'))
        self.assertTrue(_module_off(off, 'Shifts/GetTodaysShifts'))
        self.assertFalse(_module_off(off, 'Calls/GetActiveCalls'))
        self.assertFalse(_module_off(department(), 'Messages/GetInboxMessages'))


if __name__ == '__main__':
    unittest.main()
