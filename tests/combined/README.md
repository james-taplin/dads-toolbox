# Combined parent checks

Run `tests/combined/Run.ps1` after building the final combined DLL. The fixture
loads that exact DLL with the installed Unity Mod Manager and Harmony under
.NET Framework. It does not compile a substitute parent.

It checks all 128 combinations of the seven persisted internal switches using
native UMM XML serialization, older parent settings, nested sound preferences,
the single parent callback and dependency subscription, off/disabled lifecycle,
default-selected cold dependency absence/inactivity, standalone conflicts,
owned patch absence, and fresh instances after unload/reload. Actual parent
sound migration uses an isolated sibling Settings.xml to check every existing
option, original bytes, one-time import, parent precedence and failure/retry.
An injected first-module cleanup failure proves every other cleanup is still
attempted, failed disable remains suspended and foreign Harmony owners survive.
The two Skin
Manager corrections remain independently selectable within the Skin Manager
feature.

Features are off or blocked by deliberately absent/inactive dependencies during
CLR-only parent activation. Audio activation needs real Unity APIs; each module's active behavior is covered separately at its
documented evidence level. These XML combinations are exhaustive preference
roundtrips, not 128 gameplay scenarios.

`evidence/combined/receipt.json` identifies the exact DLL, runner, source,
references and verdict hashes. `WriteValidation.ps1` requires this receipt as
well as the original four suites and all four remaining module suites, and rejects
changed/stale inputs. No optional-mod DLL is copied into the fixture or package.

No game process, installation, save access, live GUI, sound audition or clean
game startup is part of this suite.

Retirement checks verify the H6F module, setting and parent instance are absent, and exercise real UMM XML loading/saving of 0.4.0 settings with the obsolete ConsistFix element.
