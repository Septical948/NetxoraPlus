# DBACHECK2 Billing API

Status: Beta 2 test integration.

The Billing API is the server-side boundary between the Windows desktop application and Stripe.

## Security boundary

The desktop application must never contain:

- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- Stripe live secret material

Those values exist only on the Billing API host.

The Windows application knows only:

`DBACHECK2_BILLING_API=https://billing.example.com`

## Projects

- `src/DBACheck2.App` — Windows client
- `src/DBACheck2.BillingApi` — ASP.NET Core billing/licensing API

## Stripe products / prices

Create these in Stripe **Test mode** first:

- DBACHECK2 Standard — USD 29 recurring monthly
- DBACHECK2 Standard — USD 290 recurring yearly
- DBACHECK2 Plus — USD 69 recurring monthly
- DBACHECK2 Plus — USD 690 recurring yearly

Enterprise remains sales-assisted.

Copy the four `price_...` values into environment variables:

```text
STRIPE_PRICE_STANDARD_MONTHLY=price_...
STRIPE_PRICE_STANDARD_ANNUAL=price_...
STRIPE_PRICE_PLUS_MONTHLY=price_...
STRIPE_PRICE_PLUS_ANNUAL=price_...
```

## Local test

Open a dedicated CMD window:

```cmd
cd C:\netxora\Netxora+\DBACHECK2-alpha4.5

set STRIPE_SECRET_KEY=sk_test_...
set STRIPE_PRICE_STANDARD_MONTHLY=price_...
set STRIPE_PRICE_STANDARD_ANNUAL=price_...
set STRIPE_PRICE_PLUS_MONTHLY=price_...
set STRIPE_PRICE_PLUS_ANNUAL=price_...
set DBACHECK2_PUBLIC_URL=http://localhost:5098

dotnet run --project .\src\DBACheck2.BillingApi\DBACheck2.BillingApi.csproj --urls http://localhost:5098
```

Then verify:

```text
http://localhost:5098/health
```

Expected:

- stripe = true
- prices = true
- mode = test

## Stripe webhook in local development

Install/login to Stripe CLI, then forward events:

```cmd
stripe listen --forward-to http://localhost:5098/v1/stripe/webhook
```

Stripe CLI returns a signing secret:

```text
whsec_...
```

Restart the Billing API with:

```cmd
set STRIPE_WEBHOOK_SECRET=whsec_...
```

The API verifies every webhook signature before processing it.

Relevant events:

- `checkout.session.completed`
- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`

Webhook processing is idempotent through the local `stripe_events` table.

## Desktop configuration

In a new CMD used to launch DBACHECK2:

```cmd
set DBACHECK2_BILLING_API=http://localhost:5098
```

Then start DBACHECK2 and open:

```text
Settings
  -> Subscription
  -> Manage Subscription
```

Standard / Plus checkout buttons call the Billing API, which creates a Stripe-hosted Checkout Session. Payment details never pass through DBACHECK2.

After completing a test purchase:

```text
Subscription
  -> Refresh Status
```

The expected state becomes `Active` after Stripe webhook processing.

## API

### Health

`GET /health`

Shows whether Stripe, webhook signing and the four price IDs are configured.

### Subscription status

`GET /v1/subscription/status?installation_id=...`

Returns the server-authoritative subscription record.

### Create Checkout

`POST /v1/checkout`

```json
{
  "installation_id": "...",
  "plan": "standard",
  "cycle": "monthly"
}
```

Response:

```json
{
  "url": "https://checkout.stripe.com/..."
}
```

### Customer Portal

`POST /v1/customer-portal`

```json
{
  "installation_id": "..."
}
```

### Stripe webhook

`POST /v1/stripe/webhook`

Requires a valid Stripe signature.

## Billing database

Default:

`src/DBACheck2.BillingApi/data/billing.db`

Production should set `DBACHECK2_BILLING_DB` to persistent server storage.

Stored data:

- installation id
- Standard / Plus plan
- monthly / annual cycle
- Stripe customer reference
- Stripe subscription reference
- current state
- current billing-period end when exposed by the Stripe API
- processed webhook event IDs

No card data or Stripe secret key is stored in SQLite.

## Production readiness still required

Before enabling entitlement enforcement:

1. deploy Billing API behind HTTPS
2. move secrets to the platform secret manager
3. configure a production webhook endpoint
4. test renewals
5. test failed payments / past_due
6. test cancellation
7. test Customer Portal
8. add grace-period/offline licensing
9. add signed license/offline strategy for Enterprise
10. enable Standard / Plus entitlement enforcement only after those tests pass
