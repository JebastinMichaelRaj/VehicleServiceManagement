# Version 2: Future enhancements implemented

| # | Enhancement | Status | Where |
|---|---|---|---|
| 1 | Customer & vehicle CRUD, search, per-customer service history | Done | `Services/Customers`, `Controllers/CustomersController.cs`, `VehiclesController.cs`, `Views/Customers`, `Views/Vehicles` |
| 2 | Parts catalogue, job quantities, stock deduction, reorder alerts | Done | `Part`, `JobPart` entities; `Services/Inventory`; `Views/Inventory`; dashboard tile |
| 3 | Invoice PDF (QuestPDF), payment entry UI, overdue reminders | Done | `Services/Invoicing/InvoicePdfGenerator.cs`, `InvoicesController`, `OverdueReminderService/Worker` |
| 4 | Maintenance timeline, mileage chart, next-service estimate | Done | `Services/Maintenance`, `Views/Vehicles/History.cshtml` (Chart.js) |
| 5 | Identity with Admin/Technician/Customer roles, customer portal | Done | `Data/Identity/IdentitySeeder.cs`, `PortalController`, role checks on controllers |
| 6 | REST API, versioning, OpenAPI | Done | `Controllers/Api/*`, `Asp.Versioning`, Swashbuckle at `/swagger` in Development |
| 7 | Technician entity, working hours, leave, drag-and-drop calendar | NOT DONE | Planned as a separate phase (needs a calendar UI and a technician roster model) |
| 8 | Email/SMS confirmations and reminders via background worker | Partly done | SMS gateway with resilience (`SmsNotificationSender`), log channel, daily `PeriodicTimer` worker. No real email provider yet. |
| 9 | GST split, localisation | Partly done | CGST/SGST vs IGST by state; en-IN default culture. Multi-currency not done. |
| 10 | Health checks, OpenTelemetry, Polly retries | Done | `/health`; OpenTelemetry tracing (console exporter, toggle `Telemetry:Enabled`); `AddStandardResilienceHandler` on SMS |
| 11 | rowversion concurrency | Done | `RowVersion` on Part, ServiceBooking, Invoice; `UnitOfWork` maps conflicts to user-safe errors; part edits carry the version through the form |
| 12 | Testcontainers integration tests | Done (needs Docker) | `tests/VehicleService.IntegrationTests` |

## Setup changes

1. Regenerate migrations (the schema changed substantially):
   ```
   dotnet ef migrations add V2
   dotnet ef database update
   ```
2. Configure starter accounts with user-secrets, never in source control:
   ```
   dotnet user-secrets set "Seed:AdminEmail" "admin@yourworkshop.test"
   dotnet user-secrets set "Seed:AdminPassword" "<strong password>"
   dotnet user-secrets set "Seed:CustomerPassword" "<strong password>"
   ```
3. Run the tests:
   ```
   dotnet test tests/VehicleService.Tests
   dotnet test tests/VehicleService.IntegrationTests   # requires Docker
   ```

## Known limitations (be aware before production)

- Nothing above has been compiled or executed in the environment where it was written. Expect a first-build pass to fix small issues.
- Invoice payment concurrency: two simultaneous payments that both leave the invoice `PartiallyPaid` may not conflict, because the status value does not change. A future fix is to always bump the invoice's row version on payment.
- The overdue reminder worker runs every 24 hours from startup, not at a fixed time of day.
- Role checks are controller-level. The REST API uses the same cookie session, so it is intended for same-site clients; token authentication (JWT or OAuth) is still needed for third-party integrations.
- Technician availability (item 7) is not modelled, so conflicts are only detected from bookings.
