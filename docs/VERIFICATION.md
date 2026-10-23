# Riftevora 3.1 verification

- Release build succeeded.
- 99 xUnit tests passed; no failures, including 22 setup cases.
- Published Riftevora.exe loaded 173 champions and 9,240 skin/chroma entries in offline mode using isolated test data.
- Automated UI checks passed: champion search, distinct comparison selections, swap sides, champion switch, random selection, persisted favorites.
- Turkish 1440x900 and English 1080x760 layouts were visually inspected.
- All 2,121 bundled portraits were decoded successfully.
- No WPF binding or runtime errors were recorded in the checked runs.
- Add game UI command detected the real installed League and LTK, wrote the correct flat `leaguePath` preference to a disposable settings file and saved the installation in the isolated Riftevora workspace. Repeating setup left the matching configuration untouched.
- Official LTK 1.30.4 installer downloaded through the production download service: 22,784,104 bytes; SHA-256 `0007230f8772c846d586f456aedee5846f7dbf4719715940e73bb073ae4eff23`. Windows Authenticode signature was valid (Natoken LLC).
- Unit tests cover untrusted release URLs, missing/invalid digests, previews and malformed metadata, corrupt/truncated downloads, cancellation, failed installer exit, first-install settings defaults, backups, corrupt settings preservation and refusal to overwrite a running manager's settings.
- Fresh installation/UAC was not exercised: LTK 1.26.1 was already present, so the real UI correctly skipped reinstalling it. Installer execution is covered with an injected process runner in tests.

The configuration format was checked against the official [LTK Config](https://github.com/LeagueToolkit/ltk-manager/blob/v1.30.4/crates/ltk-manager-base/src/config.rs) and [Settings](https://github.com/LeagueToolkit/ltk-manager/blob/v1.30.4/src-tauri/src/state.rs) source. Setup uses the official [release API](https://api.github.com/repos/LeagueToolkit/ltk-manager/releases/latest), not a bundled or mirrored LTK binary.

LTK activation and Start are completed in LTK Manager. No live League match test was performed.
