using System.Resources;

// English lives in the en satellite (X.en.resx), and most bases have no neutral X.resx. Without this,
// a lookup that falls through to the invariant culture (the API host and workers never set a request
// culture) finds nothing and IStringLocalizer returns the key name. With Satellite as the location, the
// invariant culture resolves from en and main-assembly neutral X.resx files are never read, so every
// string must be in X.en.resx. Guarded by Tests/Resgrid.Tests/Localization/NeutralResourcesFallbackTests.
[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.Satellite)]
