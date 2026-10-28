# Code signing with SignPath

Releases are not signed yet. The [pipeline](../.github/workflows/ci.yml) already knows how to sign the
`.exe` with [SignPath](https://signpath.io); it only does so once the repository variable
`SIGNPATH_ORGANIZATION_ID` exists. Until then it publishes the unsigned file exactly as before.

SignPath Foundation signs open-source projects for free, but it has to approve the project first, so the setup is
partly yours to do. Their conditions are at <https://signpath.org/terms>.

## What you do

1. **Check the conditions.** The repository uses an OSI-approved license (MIT, see [`LICENSE`](../LICENSE)), has no
   proprietary components, and is already released in the form that will be signed. Every signer needs
   multi-factor authentication.
2. **Apply** at <https://signpath.org/apply>. SignPath may take a while to answer and is under no obligation to accept.
3. **Create the project** in SignPath once accepted:
   - Project slug `windows-optimizer` and signing policy slug `release-signing` (or set the repository variables
     `SIGNPATH_PROJECT_SLUG` and `SIGNPATH_SIGNING_POLICY_SLUG` to whatever you chose).
   - Artifact configuration: paste [`signpath/artifact-configuration.xml`](signpath/artifact-configuration.xml).
     It was written from SignPath's documentation and has not been run, so check it against
     <https://docs.signpath.io/artifact-configuration/> when you create the project.
   - Add the predefined **GitHub.com** trusted build system to the organization and link it to the project.
4. **Add the secrets to GitHub** (Settings > Secrets and variables > Actions):
   - Secret `SIGNPATH_API_TOKEN`: an API token of a user allowed to submit signing requests.
   - Variable `SIGNPATH_ORGANIZATION_ID`: your SignPath organization id. Creating this variable is what turns signing on.
5. **Publish a release** (push to `main`). SignPath asks an approver to confirm each signing request, so an
   automatic release waits for that approval (up to 10 minutes by default) and fails if nobody confirms in time.
6. **Update the README.** SignPath requires a "Code signing policy" section naming the roles and people
   (committers and reviewers, approvers), and the lines "Free code signing provided by SignPath.io, certificate by
   SignPath Foundation". The README has a placeholder for it.

## Why the checksum comes after signing

Signing changes the file, so the hashes in the Scoop and winget manifests must be of the signed file. They come from the
SHA-256 GitHub computes for the assets uploaded after the signing step.
