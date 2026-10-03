# Windows baseline in this fork

This is an independent community port, not an official or endorsed Windows build. It starts from
[chenguisen's `compositor_win` snapshot](https://github.com/chenguisen/Compositor/tree/c51be1e57d699edce857115f43bbca579f18dcd4),
with its Git history retained. The macOS source and the original MIT copyright remain the reference.

## Sources checked

- macOS upstream: `robbietilton/Compositor`, main `11d8d7a50992b24fd9a760a1c13b1c01b70aaf30`.
- Windows source: `chenguisen/Compositor`, `compositor_win` `c51be1e57d699edce857115f43bbca579f18dcd4`.
- Common ancestor: `0787b60b9f430bf03451d5ec4aac6bfe9dcf2e9c`; Windows work begins at `616c08a`.
- The source port targets the macOS 1.3.7 feature set; upstream has reached 1.4.5. Matching project format
  does not establish matching rendering or features. See the known differences in [README.md](README.md).

## Reproduce the baseline

Install a .NET 10 SDK and use PowerShell on Windows x64, from the repository root:

```powershell
./windows/scripts/Test-Baseline.ps1 -OutputDirectory "$env:TEMP/compositor-baseline-new"
```

Choose a new output directory on each run. `global.json` keeps the SDK in the .NET 10 family, allowing newer
feature bands. The script builds with warnings as errors, runs the core suite, publishes a self-contained
Windows app under `windows/dist-app`, and runs that published executable. No installer, signature or release
is produced. The app can be opened with `windows/dist-app/Compositor.Desktop.exe`.

The script sets `COMPOSITOR_PROFILE` for its child processes, so recent files, tool preferences and shortcuts
belong to that run. The normal app still uses `%APPDATA%/Compositor` when this variable is unset.

Outputs include the test TRX, logs, PNG frames, and a small synthetic `baseline.comp` project:

- `--baseline`: open a three-layer canvas with a group and grayscale mask; toggle the mask; undo and redo;
  save; compare rendered pixels after disk readback; reload a clean external change; verify viewport and
  selection retention and cleared history; retain an unsaved human draft when the disk version changes;
  open the disk candidate in a separate tab.
- `--clicks`: the inherited headless pointer check, including painting, selections, guides, transforms,
  panel controls, color picking and Color Range.
- `--window`: construct and render the window with the native Windows platform initialized.
- Native startup: start the published app without a check flag, wait for its input-idle state, and stop that
  empty hidden probe. This confirms process startup, not visible desktop interaction.

The first two drive the real controls on Avalonia's headless platform with Skia rendering. They do not prove
native OS mouse delivery, file-picker behavior, accessibility, or human usability. The baseline invokes the
reload handler directly; the core suite separately tests package-change detection. The opt-in large export
spike runs only when `COMPOSITOR_SPIKE=1` and is outside the ordinary baseline.

## Initial findings and changes

The unmodified source built with .NET SDK 10.0.401 at zero warnings/errors. Its first core run passed 847 of
848 tests. `ManifestFormatTests.SavingWritesSortedKeysAndReSavingIsStable` failed because a Windows CRLF
checkout changes raw-string fixture line endings while the serializer writes LF. The expected fixture now
normalizes line endings; exact serialized key ordering and save/read/save equality remain checked.

The inherited optional memory report wrote to an author-specific absolute path. It now uses captured xUnit
test output. Allocation and memory-budget rules remain as they were in the source port.

The desktop baseline also exposed mutable mask settings shared with history snapshots: disabling a mask
changed the picture without recording an undo step, and mask links/placement mutated old snapshots too.
Layer copies now copy mask metadata while retaining the same immutable pixel asset. Three regression cases
cover enabled state, linking and placement through snapshot isolation, undo and redo.

The inherited pointer check assumed guides were already visible in the user's preferences. A fresh profile
starts with them hidden, so the check now explicitly shows and unlocks them without saving those settings.
Desktop check output uses UTF-8, independent of the machine's console code page.

## Verified local result, 2026-10-03

Windows x64, .NET SDK 10.0.401:

| Check | Observed result |
| --- | --- |
| Release solution build, warnings as errors | PASS, 0 warnings / 0 errors |
| Full core suite after the repairs | PASS, 851 passed / 0 failed / 0 skipped |
| New mask regression cases | 3 failed before the mask-copy fix; 3 passed after it |
| Published `--baseline` document journey | PASS, all six state/readback checks |
| Published `--clicks`, fresh isolated profile | PASS, full inherited pointer journey |
| Published `--window` | PASS, native-platform window construction and PNG output |
| Published app with no check flag | PASS, native startup reached input idle; probe stopped |

The checked local app includes its .NET runtime and a copy of the root MIT license. Frames and the synthetic
project were inspected locally; generated binaries, profiles, logs, TRX files and media are not source
publication material. macOS pixel equivalence, native OS input/file pickers, the opt-in huge export, GitHub CI
and binary redistribution remain unmeasured. These results establish this basic Windows journey, not feature
parity or a supported release.

## File interoperability limits

The Windows implementation reads versions 1–11 and writes v11. UUID output and asset filenames use uppercase
UUIDs. Images use straight-alpha sRGB RGBA; masks require grayscale without alpha, including uniform 1×1
masks. Blend mode names and transform metadata match the reference schema; this is not pixel-equivalence
evidence against macOS.

Saving stages all assets and the manifest, then swaps directories with rollback on failure. Windows needs
two renames, leaving a short gap; this is not a single atomic package swap. An external writer must finish
assets before replacing its manifest, as described in [the upstream writer guide](../docs/writing-comp-files.md).
The guide and [project-format.md](../docs/project-format.md) in this branch are the source snapshot's documents;
compare them with the upstream commit above before extending the schema.

A clean external reload clears undo/redo and preserves viewport and any still-present selected layer. A
dirty document remains in memory, with a status-line message. That change notification is consumed, so
undoing to a clean state does not retry it. Reopening offers a separate tab; a subsequent Save can still
overwrite the external disk version. There is no revision-aware conflict/adoption adapter yet.

Future integrations must negotiate a versioned, loss-aware projection, source revision/digests and dirty
state before writing; return edits as provenance-bearing candidates for an existing document owner to
adopt. A `.comp` file does not grant authority to change another system's current or promotion state.

## License and distribution scope

The original [MIT LICENSE](../LICENSE) and copyright remain intact. All project references are local and
relative; NuGet dependencies are public. The entire app bundle is not solely MIT: native and managed
dependencies have their own terms. In particular, the LibRaw native runtime declares
`LGPL-2.1-only OR CDDL-1.0`; Magick.NET declares Apache-2.0. This baseline is a local unsigned build.
Third-party notices, native dependencies and source/relinking obligations must be reviewed before a binary
release or redistribution. No binary release, upstream message or upstream pull request is part of this work.
