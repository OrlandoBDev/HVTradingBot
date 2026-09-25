# Security

## Secrets

Never commit:

- broker API keys
- broker usernames/passwords
- database passwords
- JWT signing secrets
- AI API keys

Use:

- the dashboard Settings page for broker credentials (stored encrypted with ASP.NET Core Data Protection;
  keys kept outside the database; the token is never returned by the API)
- environment variables
- .NET User Secrets for development
- production secret manager / Azure Key Vault

## Current Limitations

- The dashboard and API require a single login (HttpOnly, SameSite=Strict session cookie; first login created with a
  one-time setup code). JWT authentication and the roles below are not implemented yet. The API binds to 127.0.0.1,
  restricts allowed host names, and accepts JSON bodies only (blocking cross-site form posts).

## Authentication

Use JWT-based authentication for application users.

Roles:

- Administrator
- Trader
- Viewer

## Authorization

Viewer:

- read-only

Trader:

- approve/reject proposals
- view trading configuration

Administrator:

- change risk configuration
- broker configuration
- enable live modes
- control kill switch

## Trading Mode Security

PAPER is default.

APPROVAL and AUTO require explicit configuration.

AUTO should require:

- validated production environment
- valid broker connection
- healthy market-data feed
- passing risk-engine checks
- no active kill switch

## Audit

Audit:

- login
- config changes
- trading-mode changes
- kill-switch changes
- proposal approvals
- order submissions
- broker responses
- position changes

Never write secrets to logs.

## Email Notifications (Gmail SMTP)

Trade decision emails are sent through `smtp.gmail.com:587` (STARTTLS).

- Use a Gmail **App Password** (requires 2-Step Verification), never the account password.
- Enter it under Settings → Notifications (stored encrypted with Data Protection, never returned by the API), or supply
  it via `Notifications__Email__Password` (environment) as a fallback; never commit it.
- Notifications are informational only and cannot approve, reject, or execute trades.
