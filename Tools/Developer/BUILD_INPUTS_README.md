# Reproduce the Unity build inputs

The release checkout contains earlier local asset/package edits that are deliberately not committed or reset. A release therefore records its exact Git commit **and** a separate source overlay. The overlay is a team build artifact, not a replacement branch and not something to add to Git.

Run these commands with Python 3.11 from the Unity repository root. On Windows, use `py -3.11` instead of `python3`.

## Export after the release commit

Close Unity before exporting so an import cannot change inputs during the snapshot. Commit the intended release source first; leave the preserved local edits in place.

```sh
python3 Tools/Developer/export_build_inputs.py export --output /absolute/path/outside-repository/REDBOOL_build_inputs.zip
python3 Tools/Developer/export_build_inputs.py verify-tree /absolute/path/outside-repository/REDBOOL_build_inputs.zip
```

The command never commits, resets, deletes, or replaces source files, and refuses to overwrite an existing ZIP. Unexpected untracked build inputs must be reviewed and committed before export; the only untracked exceptions are the required `Rql.Contracts`, `Rql.Execution`, and `Rql.Parser` DLLs and their three `.meta` files.

The ZIP contains:

- `manifest.json`: exact commit; every included tracked `Assets`, `Packages`, and `ProjectSettings` file's SHA-256 and size; all dirty tracked paths; explicit exclusions and deleted paths; aggregate asset, content, and runtime C# hashes.
- `overlay/`: only changed tracked build inputs and the six required untracked Rql files. Unchanged assets are restored from Git and Git LFS rather than copied again.

User editor settings such as `.vscode`, recordings, recognized credential files, and caches are excluded. Small changed configuration files are also checked for nonempty credential fields. The manifest describes these exclusions; this is not a full copy of the developer's computer or a general backup tool.

## Restore into a separate checkout

Read `git_commit` from the ZIP's `manifest.json`. Use that exact commit and the release branch, with Git LFS installed and resolved. Disable automatic CRLF conversion **before** checkout to preserve recorded source bytes on Windows:

```sh
git -c core.autocrlf=false clone --no-checkout https://github.com/Capstone-Design-2026/motion-fighter-ai.git REDBOOL-reproduce
cd REDBOOL-reproduce
git config core.autocrlf false
git lfs install --local
git checkout --detach RECORDED_FULL_COMMIT
git lfs pull
python3 Tools/Developer/export_build_inputs.py verify-archive /absolute/path/REDBOOL_build_inputs.zip
python3 Tools/Developer/export_build_inputs.py restore /absolute/path/REDBOOL_build_inputs.zip
python3 Tools/Developer/export_build_inputs.py verify-tree /absolute/path/REDBOOL_build_inputs.zip
```

Replace `RECORDED_FULL_COMMIT` with the manifest value. If it is not yet present locally, fetch the named release branch first. Keep the ZIP outside the checkout. `restore` refuses a different commit, dirty tracked files, untracked collisions, links, malformed ZIP paths, hash failures, and unresolved LFS or line-ending differences in untouched files. It verifies the archive and base inputs before applying changes; interrupted restoration should be retried in a new clean checkout.

After verification, use the existing native build procedure (`Tools/Developer/build_local.py`) and the Unity version in `ProjectSettings/ProjectVersion.txt`. A restored overlay intentionally leaves its local changes visible in Git. Do not commit those preserved edits just to make the checkout clean.

## Scope and validation

These checks reproduce and verify the included Unity **source bytes**. They do not promise byte-identical executables across operating systems, Unity installations, generated caches, or build timestamps. The camera backend has its own source/model provenance and native build artifact; it is not stored in this overlay. Ignored generated files and machine-specific configuration are outside this source manifest.

The exporter checks the archive content hashes and verifies the live input tree again before completing. The focused tests use temporary repositories and never change the real project:

```sh
python3 Tools/Developer/tests/test_export_build_inputs.py -v
```
