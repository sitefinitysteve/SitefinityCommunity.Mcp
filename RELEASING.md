# Releasing

1. Update `CHANGELOG.md` with a `## [X.Y.Z] — date` section (the workflow extracts it; missing section fails the release).
2. Bump `<Version>` in `src/SitefinityCommunity.Mcp/SitefinityCommunity.Mcp.csproj` and `"version"` in `npm/package.json` to the same X.Y.Z.
3. Commit, then tag and push:

       git tag vX.Y.Z
       git push origin master vX.Y.Z

   CI builds the self-contained binaries (win-x64, linux-x64, osx-x64, osx-arm64) and creates the
   GitHub release with the CHANGELOG notes and tarballs attached.

4. Once the release is up, publish the npm shim manually (browser 2FA, no stored tokens):

       cd npm
       npm publish --access public

   Order matters: the npm package's postinstall downloads binaries from the vX.Y.Z release,
   so publish only after the release assets exist. The npm version must equal the tag.
