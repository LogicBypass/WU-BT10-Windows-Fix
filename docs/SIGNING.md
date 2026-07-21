# Production signing and releases

The repository can build unsigned development artifacts without credentials.
Public releases should be signed by the verified publisher.

## Recommended route: Azure Artifact Signing

Azure Artifact Signing (formerly Trusted Signing) is Microsoft's managed
identity-based signing service. It avoids exporting a private signing key and
supports GitHub Actions through OpenID Connect.

1. Create an Azure Artifact Signing account.
2. Complete identity validation.
3. Create a public-trust certificate profile.
4. Create a Microsoft Entra application or managed identity.
5. Assign it **Artifact Signing Certificate Profile Signer** on the profile.
6. Add a federated credential restricted to this GitHub repository and the
   release environment.
7. Create a protected GitHub environment named `release-signing` with required
   reviewers.
8. Add these GitHub repository secrets:

   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`

9. Add these GitHub repository variables:

   - `ARTIFACT_SIGNING_ENABLED` = `true`
   - `AZURE_ARTIFACT_SIGNING_ENDPOINT`
   - `AZURE_SIGNING_ACCOUNT_NAME`
   - `AZURE_CERTIFICATE_PROFILE_NAME`
   - `SUPPORT_URL` (optional)

The release workflow uses:

- `azure/login` with OIDC—no Azure client secret;
- `azure/artifact-signing-action`;
- SHA-256 file digests;
- Microsoft's RFC 3161 timestamp service; and
- `Get-AuthenticodeSignature` verification before publishing.

## Important limitations

- A trusted signature cannot be produced without publisher identity validation.
- Do not use a self-signed certificate for public distribution.
- Signing reduces tampering risk, but a new publisher can still encounter
  SmartScreen warnings until reputation develops.
- Never commit PFX files, private keys, passwords, Azure secrets, or API keys.

## Alternative distribution

Microsoft Store signing is another strong option and does not require shipping
your own public-trust certificate. A conventional OV/EV Authenticode certificate
from a public CA also works when its private key is stored in compliant hardware
or a managed signing service.
