# Release guide

The public Aseprite extension and the Unity package share one version. Custom brush packs keep their own version. A Git tag named `vMAJOR.MINOR.PATCH` identifies the release.

## Prepare a version

1. Update the version in [the Unity manifest](../unity/package.json), [the public Aseprite profile](../packs/minimal/profile.json), and [the README install URL](../README.md).
2. Run `python scripts/build_release.py --tag v1.3.0` with the intended version. The builder checks that both components agree, builds the extension twice, and writes the archive and its SHA-256 checksum to `.build/release`.
3. Run the [Aseprite export smoke test and Unity Edit Mode tests](project-setup.md#verification). For changes affecting installation, also install the generated extension and test the Git URL in a Unity project. Run the Git URL check after pushing the tag and before publishing the release.
4. Commit the release changes to `main` and push an annotated tag. The tag annotation becomes the initial release notes.

```sh
git tag -a v1.3.0
git push origin main refs/tags/v1.3.0
```

## Publish the draft

GitHub Actions builds and checks the archive, then attaches it and `SHA256SUMS.txt` to a draft on the [Releases page](https://github.com/Ash2oPS/aseprite-tilemap-editor/releases). Review the notes, the two assets, and the installation checks before publishing. The Unity package is installed directly from the same tag via `?path=/unity#v1.3.0`.

Keep published tags fixed. Release a new patch for corrections. If a build fails before a draft is created, rerun the workflow after diagnosing the failure. If a draft exists, inspect its assets before publishing.
