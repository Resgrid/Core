using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;

// Make sure that the unit tests can access internal classes
// so that it is possible to test them
[assembly: InternalsVisibleTo("OpenPopUnitTests")]

// This is used if creating a strong-named assembly
// Signing is done by going into each project properties and going to the signing tab
// There the checkbox for "Sign the assembly" should be checked and signing should be done
// by using the "OpenPopKeyFile.pfx". The password for this file is not publicly available.
//[assembly: InternalsVisibleTo("OpenPopUnitTests, PublicKey=002400000480000094000000060200000024000052534131000400000100010095984d584301546333c9edf1cfcf36cf48fd9c577c9b05dee7c51b39d858600849854bf02b5c40621f0848f97bdb0c0a92cbc049318e47bf50d54778ddeb639c1465484d5e71ce266504521849a1bdd18c0b8abdaf6bf47ddeee29514fc6000ba957a8480fa889eb4971687feb832e861ca3c4a615dfc2b1c516b560aa5e03a4")]

[assembly: AssemblyTitle("OpenPop POP3 Mail Library")]
[assembly: AssemblyDescription("POP3 Mail Library")]

// The Assembly is compliant to CLS rules
[assembly: CLSCompliant(true)]

// Allow that assemblies can reference this assembly
// even though they do not have the FullTrust priviledge.
// This will not defeat security, since normal security policies
// like DNS, IO permissions are still in effect
[assembly: AllowPartiallyTrustedCallers]