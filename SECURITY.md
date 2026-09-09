# Security policy

## Supported versions

Until the first stable release, security fixes are applied to the latest preview release and the `main` branch.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting for this repository. Do not open a public issue or include live credentials, private prompts, session files, or exploit details in public logs. Include the affected version, impact, reproduction conditions, and any suggested mitigation. Maintainers will acknowledge a complete report within five business days and coordinate remediation and disclosure.

Local provider credentials are protected for the current operating-system user. On Windows this uses DPAPI; on macOS and Linux it uses an AES-GCM key stored with user-only file permissions under the user SharpClaw directory. This protects against accidental disclosure at rest, but it does not protect against another process already running as the same user.
