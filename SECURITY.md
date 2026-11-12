# Security policy

## Supported versions

| Version | Supported |
| ------- | --------- |
| 1.x     | Yes       |

## Reporting a vulnerability

Please do not open a public issue for security problems. Send a private report to
**support@rixel.dev** with:

- affected version and operating system,
- a short description of the issue,
- steps to reproduce,
- impact assessment (data loss, local privilege escalation, etc.).

We acknowledge reports within 3 business days.

## Security design

- RixelPOS is an offline desktop application. It does not expose a network
  service or a web server. The only optional network traffic is the receipt
  printer connection (TCP 9100) and optional product lookup when enabled.
- Passwords and PINs are hashed with bcrypt (work factors 11 and 8).
  They are never stored in plain text.
- The SQLite database is local to the user profile and protected by operating
  system permissions. Backups are written to a folder chosen by the
  administrator.
- The repository must never contain a real store database, logs, backups,
  signing certificates, API keys or customer data. See `.gitignore`.
- Builds should be signed before distribution. The packaging scripts accept
  signing material from the build environment; no certificate is committed.

## Hardening checklist for deployments

1. Use a dedicated Windows/Linux user for the till.
2. Restrict the data folder to that user.
3. Enable full-disk encryption.
4. Keep an off-device backup copy.
5. Use strong administrator passwords (8+ characters with a number).
6. Review the audit log periodically.
