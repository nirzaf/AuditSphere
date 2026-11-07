# ADR-0016: The final signed report is certificate-signed with a firm-held certificate

**Status: APPROVED** (owner instruction of 2026-10-10 to implement certificate signing of the report) · Date: 2026-10-10 · Decider: repository owner

## Context

- Specification §4.4.1 and §4.4.2 call for the Partner's digital signature and a "certified, sealed, and digitally signed PDF".
- ADR-0005 embeds the Partner's PNG signature and the firm seal and records document hashes. An image proves nothing if the PDF is changed afterwards: the stored hash detects a change to AuditSphere's own copy, but a copy held by the client or a regulator is not tamper-evident.
- ADR-0005 listed "integrate an eSignature provider or PKI signing" as excluded by the owner. On 2026-10-10 the owner instructed that certificate signing of the report be implemented. The exclusion of eSignature *providers* and of Purview is unchanged.
- `PDFsharp` 6.2.4, already a pinned dependency, signs PDFs with an X.509 certificate. No package was added.

## Decision

- When the host supplies a report-signing certificate, `AuditDeliverableService.SignIndependentReportAsync` certificate-signs the final report PDF over its whole content (SHA-256). The PNG signature and seal remain the visible appearance; the certificate signature is invisible and is what a PDF reader validates.
- The certificate belongs to the firm. The operator supplies a PEM certificate and PEM private key on a role-specific mount through `ReportSigning:CertificatePath` and `ReportSigning:PrivateKeyPath`. AuditSphere never generates, stores or exports the key, and contacts no outside service to sign.
- `ReportSigning:Required` (default `false`) makes certificate signing mandatory. Signing fails closed: a required-but-missing certificate, or a configured certificate that cannot be loaded, is outside its validity dates, holds an RSA key under 2048 bits, has no private key or is not issued for digital signatures, blocks the Partner's signing. It never falls back to the image alone.
- With no certificate configured and `Required` false, behaviour is as ADR-0005: image and seal only. The record and the screen say so.
- `SignatureApplication` records `SignatureKind` (`VISUAL` or `CERTIFICATE`) and, for a certificate signature, the certificate subject, issuer, serial number, SHA-256 thumbprint and expiry. The row is append-only.
- A report is stored as certificate-signed only after its own signature verifies against the certificate just used. Bundle assembly re-verifies it, certificate-signs the certified financial statements with the same certificate source, and refuses an image-only report when certificate signing is required.
- `ReportSignatureVerifier` reports whether a signed PDF is unchanged since signing and which certificate signed it. It does not judge whether that certificate is trusted.

## Alternatives considered

- **Keep image-only signatures (ADR-0005 as written).** Rejected by the owner: the issued document is not tamper-evident.
- **An eSignature provider.** Still excluded (ADR-0005). It would add a provider, a cost and an outside dependency for a document the firm itself issues.
- **A separate certificate per Partner.** Not chosen now: it multiplies key custody. The firm certificate signs; the signature's stated reason names the Partner, and the Partner's authority is enforced and recorded by AuditSphere.

## Consequences

- This supersedes only the "PKI signing" exclusion in ADR-0005. Visual credentials, the eSignature-provider exclusion and the Purview exclusion stand.
- The firm must obtain the certificate and protect its private key. A self-issued certificate gives tamper evidence but PDF readers will show the signer as not trusted; a certificate from a recognised authority shows the firm as the trusted signer. Which one is legally sufficient is the firm's decision.
- No trusted timestamp is embedded. A reader judges the signature against the signing computer's clock, and long-term validation after the certificate expires is not provided. Adding a timestamp authority would be an outside call and needs its own decision.
- Reports signed before a certificate was configured stay `VISUAL`. Under `Required`, such a report cannot be bundled; a new report version must be generated and signed.
- Local evidence used a throwaway self-signed certificate. Acceptance with the firm's real certificate, and the firm's legal acceptance of the signature, remain open.
