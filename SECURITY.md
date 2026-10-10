# Security Policy

## Supported versions

Security fixes are made for the latest release of MarkLens, which is the latest tag on `main`.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report them privately through GitHub instead: open the **Security** tab of this repository and choose
**Report a vulnerability**. Include the MarkLens and Notepad++ versions, steps to reproduce and, if possible,
a sample Markdown file that triggers the problem.

You can expect an acknowledgement within a week. Once a fix is released, the report will be credited unless
you prefer to stay anonymous.

## What the plugin guarantees

Reports about any of these are in scope:

- Raw HTML in a Markdown file is shown as text unless the user turns on **Render raw HTML**. With it off, a
  document cannot load remote content or embed other pages in the preview.
- The preview is served from a private virtual host that maps only the plugin's own assets and the folder of
  the open document.
- Links that leave the document open in the default browser, never inside the preview.
- The bundled JavaScript libraries are downloaded at build time from npm and verified against pinned
  SHA-256 checksums.
- Every release zip carries a build attestation that can be verified with
  `gh attestation verify <zip> --repo maarten-grassmid/marklens`.
