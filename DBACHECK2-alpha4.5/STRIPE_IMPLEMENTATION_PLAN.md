# DBACHECK2 Stripe Implementation Plan

Context: Windows desktop DBA SaaS with Stripe Payments + Billing.

Commercial model:
- Standard: USD 29 monthly / USD 290 annual
- Plus: USD 69 monthly / USD 690 annual
- Enterprise: sales-assisted contract, not automatic Checkout
- Desktop product remains read-only/diagnostic by default
- Billing secrets must never be present in the Windows executable

## Architecture

```text
DBACHECK2 Windows
    |
    | HTTPS + installation credential
    v
DBACHECK2 Billing API
    |
    | restricted/secret Stripe API key
    v
Stripe Checkout / Billing / Customer Portal
    |
    | signed webhooks
    v
DBACHECK2 Billing API
    |
    v
server-authoritative subscription state
```

## Phase 1 — Stripe sandbox setup

Create two Stripe Products in a sandbox/test environment:

1. DBACHECK2 Standard
   - recurring monthly USD 29
   - recurring yearly USD 290

2. DBACHECK2 Plus
   - recurring monthly USD 69
   - recurring yearly USD 690

Store only the four `price_...` identifiers in the Billing API configuration.

Enterprise is intentionally excluded from self-service Checkout.

## Phase 2 — Checkout

Use Stripe-hosted Checkout in `subscription` mode.

Server responsibilities:
- select the Price ID from a server-side allowlist
- never accept amount/currency from the desktop client
- associate Checkout with DBACHECK2 `installation_id`
- attach metadata to the resulting Subscription
- use idempotency for Checkout creation
- reuse an existing Stripe Customer when available

Desktop responsibilities:
- request Standard/Plus + monthly/annual
- open the returned Stripe-hosted URL
- never process card data
- never contain Stripe secret keys

## Phase 3 — Webhooks as billing authority

Verify `Stripe-Signature` using `STRIPE_WEBHOOK_SECRET`.

Minimum lifecycle events:
- checkout.session.completed
- customer.subscription.created
- customer.subscription.updated
- customer.subscription.deleted
- customer.subscription.paused
- customer.subscription.resumed
- invoice.paid
- invoice.payment_failed
- invoice.payment_action_required

Rules:
- subscription state is server-authoritative
- plan/cycle are resolved from the actual Stripe Price ID, not only metadata
- `invoice.paid` advances the local paid-through/access timestamp
- failed webhook processing remains retryable
- event IDs are idempotent

## Phase 4 — Customer Portal

Use Stripe Customer Portal for:
- billing details
- payment methods
- invoices
- cancellation
- Standard <-> Plus changes when configured

After a portal price change, DBACHECK2 resolves the new plan from the actual Subscription item Price ID.

## Phase 5 — Licensing / entitlement behavior

Do not enable hard entitlement enforcement until the billing lifecycle is tested.

Intended behavior:
- Trial / Active: normal access according to plan
- PastDue: temporary grace based on last paid-through date
- Canceled / Expired / Paused: de-provision according to contract/policy
- Enterprise: separate organization license path

The desktop cache is not authoritative. Production licensing state comes from the Billing API.

## Phase 6 — Security

- Stripe secret/restricted key only on the Billing API
- use a secrets vault in production
- prefer restricted API keys when practical
- never paste live keys into source code or commit history
- desktop uses an installation secret protected with Windows DPAPI
- Billing API stores only a SHA-256 hash of the installation secret
- status, checkout, and Customer Portal endpoints require installation authentication
- webhook signature verification is mandatory
- public health endpoint must not disclose local database paths or secret values

## Phase 7 — Testing

Sandbox test cases:

1. Standard monthly successful first payment
2. Standard annual successful first payment
3. Plus monthly successful first payment
4. Plus annual successful first payment
5. declined first payment
6. authentication / 3DS flow
7. renewal succeeds
8. renewal payment fails
9. Smart Retry / recovery
10. cancel at period end
11. immediate cancellation
12. Standard -> Plus via Customer Portal
13. Plus -> Standard via Customer Portal
14. duplicate webhook delivery
15. webhook handler failure followed by Stripe retry
16. desktop temporarily offline
17. Billing API temporarily offline

Use Stripe Billing simulations/test clocks for renewal and lifecycle testing.

## Phase 8 — Production rollout

Before Live mode:
- deploy Billing API behind HTTPS
- store Stripe keys in a secrets manager
- configure production webhook destination
- configure Customer Portal products/prices and cancellation behavior
- enable Stripe Billing recovery settings / Smart Retries as desired
- decide tax strategy before enabling Stripe Tax
- add monitoring and alerting for webhook failures
- add backup/HA for the billing database
- migrate SQLite to managed PostgreSQL when customer volume/availability requires it
- enable entitlements only after lifecycle QA passes

## Current repository status

Implemented:
- Stripe-hosted subscription Checkout
- Standard/Plus server-side Price allowlist
- Customer Portal session creation
- signed webhook verification
- subscription state persistence
- Checkout idempotency
- retry-safe webhook event processing
- plan/cycle reconciliation from Stripe Price ID
- paused/canceled lifecycle mapping
- invoice-paid access window tracking
- installation authentication
- DPAPI-protected desktop installation secret
- future PastDue grace support

Still required:
- create the four Stripe sandbox Prices
- configure Customer Portal in Stripe
- configure webhook event destination / Stripe CLI forwarding
- complete lifecycle QA
- deploy HTTPS Billing API
- production secrets vault
- production entitlement enforcement
