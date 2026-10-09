# Vehicle Service Management (VSM)

A service-workshop management system built with **ASP.NET Core 8 MVC** and **SQL Server**. It covers the core workflow of a garage: scheduling service appointments with workshop-rule and conflict validation, closing out jobs with maintenance history, generating invoices with discounts and tax, tracking payments, and a dashboard of key metrics.

---

## 1. Project overview

VSM models a workshop that services customers' vehicles. The main flow is:

1. A customer's vehicle is registered (seeded for this release).
2. An appointment is scheduled for a service, assigned to a technician.
3. The appointment is completed with the final odometer reading, parts used and any discount.
4. The system records a maintenance history entry, updates the vehicle's mileage and generates an invoice.
5. Payments are recorded against the invoice until it is settled.
6. The dashboard shows revenue, outstanding balances, overdue invoices and today's workload.

### Status of this release

| Area | Status |
|---|---|
| Database schema (EF Core Code-First), indexes, constraints, seed data | Implemented |
| Service booking with workshop-hours, lead-time, Sunday and overlap validation | Implemented |
| Booking complete/cancel lifecycle | Implemented |
| Invoice generation (discount before tax, two-decimal rounding) | Implemented |
| Payment recording and invoice status transitions | Service layer implemented; UI not yet built |
| Maintenance history | Written on completion; list/history UI not yet built |
| Dashboard metrics | Implemented |
| Repository + Unit of Work, DI, AutoMapper, FluentValidation, Serilog | Implemented |
| Unit tests for billing and scheduling rules | Implemented |
| Customer and vehicle CRUD screens | Not yet built (see Future enhancements) |
| Role-based authorization (Admin / Technician / Customer portal) | Not yet built |

---

## 2. Technology stack

| Concern | Choice |
|---|---|
| Framework | ASP.NET Core 8 MVC (Razor views) |
| Language | C# 12 |
| Database | SQL Server (LocalDB, Express or full edition) |
| ORM | Entity Framework Core 8 (Code-First, migrations) |
| Mapping | AutoMapper 12 (with `ProjectTo` for query-side projections) |
| Validation | FluentValidation 11 (structural) + domain policy classes (business rules) |
| Logging | Serilog (console and rolling daily file) |
| UI | Bootstrap 5.3 via CDN |
| Tests | xUnit |

### NuGet dependencies (main project)

- `Microsoft.EntityFrameworkCore.SqlServer` 8.0.11
- `Microsoft.EntityFrameworkCore.Design` 8.0.11 (for `dotnet ef`)
- `AutoMapper.Extensions.Microsoft.DependencyInjection` 12.0.1
- `FluentValidation.AspNetCore` 11.3.0
- `Serilog.AspNetCore` 8.0.3, `Serilog.Sinks.File` 5.0.0

---

## 3. Setup instructions

### Prerequisites

- .NET 8 SDK (`dotnet --version` should report 8.x)
- SQL Server, or SQL Server LocalDB on Windows
- EF Core CLI: `dotnet tool install --global dotnet-ef`

### Step 1 - Configure the connection string

Edit `appsettings.json` (or use user secrets / environment variables in real deployments):

```json
"ConnectionStrings": {
  "Default": "Server=(localdb)\\mssqllocaldb;Database=VehicleServiceDb;Trusted_Connection=True;TrustServerCertificate=True"
}
```

For a named SQL Server instance use `Server=localhost\\SQLEXPRESS;...` and for SQL authentication use `User Id=...;Password=...`.

### Step 2 - Restore packages

```bash
cd VehicleServiceManagement
dotnet restore
```

### Step 3 - Create the initial migration

The repository intentionally ships without a `Migrations` folder so you generate it against your own environment:

```bash
dotnet ef migrations add InitialCreate
```

### Step 4 - Create the database

Either let the app apply migrations on startup (it does in `Program.cs`), or apply them explicitly:

```bash
dotnet ef database update
```

This creates `VehicleServiceDb` with all tables, indexes and seed data.

### Step 5 - Run the application

```bash
dotnet run
```

Open the URL printed in the console (for example `https://localhost:7xxx`). You land on the dashboard.

### Optional - Produce a SQL script for DBAs

```bash
dotnet ef migrations script --idempotent -o database/schema.sql
```

This generates a reviewable, re-runnable SQL script rather than hand-maintaining DDL.

---

## 4. Feature descriptions and usage

### 4.1 Service booking

**Page:** Bookings → New booking

**Rules enforced** (`Services/Booking/BookingPolicy.cs`, configurable under `Scheduling` in `appsettings.json`):

- Start must be at least `MinimumLeadTimeHours` (default 1) in the future.
- Sundays are closed.
- Appointments must start at or after `OpeningHour` (default 08:00) and finish at or before `ClosingHour` (default 18:00), on the same day.
- Duration comes from the service's `EstimatedHours`.

**Conflict rules** (`BookingService.ScheduleAsync`): the new slot is rejected if the same technician *or* the same vehicle has a non-cancelled booking that overlaps it. The overlap test is `existing.Start < newEnd AND newStart < existing.End`, executed in SQL against the stored `ScheduledEnd` column.

**Example:** Booking a 2-hour "Brake Pad Replacement" for technician "Ravi" at 10:00 on 7 Oct succeeds. A second request for Ravi at 11:00 the same day is rejected with *"The selected technician or vehicle already has an overlapping appointment in this time window."*

### 4.2 Booking lifecycle

- **Cancel** is available for any booking that is not completed.
- **Complete** (`/Bookings/Complete/{id}`) requires:
  - Odometer reading, not lower than the vehicle's recorded mileage.
  - Optional free-text list of parts used.
  - Parts cost (default 0).
  - Discount percentage (0–100).

Completing a booking, in one transaction:

1. Marks it `Completed` and updates the vehicle's mileage.
2. Writes a `MaintenanceHistory` row with labour hours, labour cost (the service base price), parts cost and technician.
3. Creates an `Invoice` (see 4.3).

### 4.3 Invoice generation

Calculation lives in `Services/Billing/InvoiceCalculator.cs` as a pure function, so it is trivially unit-tested.

```
subtotal        = service.BasePrice + partsCost
discountAmount  = round(subtotal × discountPercent / 100)
taxableAmount   = subtotal − discountAmount
taxAmount       = round(taxableAmount × taxRate)        // TaxRate from Billing:TaxRate, default 0.18
total           = taxableAmount + taxAmount
```

All rounding is to two decimals using `MidpointRounding.AwayFromZero`. Due date is `IssuedOn + Billing:PaymentTermsDays` (default 15).

**Example:** A ₹1,000 subtotal with a 10% discount gives ₹100 off, ₹900 taxable, ₹162 tax and a ₹1,062 total.

Each booking can be invoiced only once (unique index on `Invoices.ServiceBookingId`).

### 4.4 Payments

`IInvoiceService.RecordPaymentAsync` validates that the amount is positive and does not exceed the outstanding balance, then records a `PaymentRecord`. The invoice moves to `PartiallyPaid` or `Paid` depending on whether the balance reaches zero.

A payment-entry screen is not yet included; the method is ready to be exposed from an `InvoicesController`.

### 4.5 Maintenance history

Each completed job produces a `MaintenanceHistory` row, giving a per-vehicle audit trail. The data is queryable via `Vehicles.MaintenanceRecords`. A history page is planned (see Future enhancements).

### 4.6 Dashboard

**Page:** Dashboard (home)

| Metric | Definition |
|---|---|
| Revenue collected | Sum of all payment records |
| Outstanding balance | Sum of invoice totals minus revenue collected |
| Overdue invoices | Invoices not `Paid` whose due date has passed |
| Bookings today | Non-cancelled bookings starting today |
| Vehicles in workshop | Bookings currently `InProgress` |
| Customers | Total customer count |

### 4.7 Seed data

On first migration the database contains:

- 3 service categories: Routine Maintenance, Brakes & Tyres, Engine & Diagnostics.
- 5 services (oil change, full inspection, brake pads, wheel alignment, computer diagnostics).
- 2 customers and 2 vehicles.

Seed data uses fixed keys and dates so that migrations remain deterministic.

---

## 5. Project structure and architecture

```
VehicleServiceManagement/
├── Program.cs                         # Composition root: DI, Serilog, EF, AutoMapper, pipeline
├── appsettings.json                   # Connection string, billing and scheduling options
├── VehicleServiceManagement.csproj
├── Common/
│   └── Exceptions/BusinessRuleException.cs   # Domain-rule violations with user-safe messages
├── Domain/
│   └── Entities/Entities.cs           # Entities and enums (persistence-agnostic POCOs)
├── Data/
│   ├── AppDbContext.cs                # Fluent configuration, indexes, seed data
│   └── Repositories/
│       ├── Repository.cs              # IRepository<T> / Repository<T>
│       └── UnitOfWork.cs              # IUnitOfWork: one SaveChanges per use case
├── Services/                          # Business logic (no HTTP concerns)
│   ├── Billing/                       # BillingOptions, InvoiceCalculator (pure)
│   ├── Booking/                       # BookingPolicy, BookingService, requests/DTOs
│   ├── Invoicing/InvoiceService.cs    # Invoice creation, payments
│   └── Dashboard/DashboardService.cs  # Read-side metrics
├── Mapping/BookingProfile.cs          # AutoMapper profile
├── Validation/Validators.cs           # FluentValidation request validators
├── Controllers/                       # Thin MVC controllers
│   ├── BookingsController.cs
│   └── DashboardController.cs
└── Views/                             # Razor views (Bootstrap 5)

tests/VehicleService.Tests/
├── InvoiceCalculatorTests.cs
└── BookingPolicyTests.cs
```

### Layering and patterns

- **Controllers** only translate HTTP to service calls and back. They hold no business logic.
- **Services** hold the business rules and orchestrate repositories. They depend on abstractions (`IUnitOfWork`, `IBookingPolicy`, `IInvoiceService`), and `TimeProvider` is injected so time-dependent rules are testable.
- **Repository + Unit of Work**: `IRepository<T>` wraps a generic `DbSet<T>` and exposes `IQueryable` for composition. `IUnitOfWork` gives each use case a single commit point, so a completed booking, its maintenance record and its invoice are saved together or not at all.
- **Validation is split in two.** FluentValidation handles structural input (required fields, lengths, ranges). `BookingPolicy` and the services handle rules that need context (opening hours, conflicts, invoice state).
- **Read models** (`BookingListItemDto`) are projected in the database by AutoMapper's `ProjectTo`, so list pages never load full entity graphs.
- **Dependency injection** is used throughout, with options bound from configuration (`Billing`, `Scheduling`).
- **Async everywhere**, with `CancellationToken` passed through from controllers to EF Core.
- **Logging** via `ILogger<T>` with structured properties (for example `{BookingId}`), written to console and to `logs/vsm-YYYYMMDD.log`.
- **Error handling**: `BusinessRuleException` carries messages safe to show users and is caught in controllers, which show them as validation errors. Unexpected exceptions go to the global exception handler (outside Development) and are logged by Serilog.

### Preparing for an API

Controllers return view models and redirect, but services return DTOs and plain values with no `ViewBag` or `ModelState` dependencies. To expose a REST API, add `ApiController`s that call the same services and return `IActionResult` with DTOs, leaving the business layer untouched.

---

## 6. Running the tests

```bash
dotnet test tests/VehicleService.Tests
```

Coverage in this release:

- **InvoiceCalculatorTests**: discount is applied before tax, rounding to two decimals, zero discount, and rejection of invalid discounts and negative subtotals.
- **BookingPolicyTests**: valid in-hours slot accepted; inside lead time rejected; Sunday rejected; overrunning closing time rejected; zero duration rejected.

Service-level tests for conflict detection and payment transitions would need an EF Core provider (SQLite in-memory or Testcontainers SQL Server) and are a recommended next step.

---

## 7. Assumptions and design decisions

- **Currency** is not localised. Amounts are `decimal` and formatted with the current culture (the seed uses Indian phone numbers and `C` formatting, so `en-IN` is a sensible default).
- **Tax** is a single flat rate applied to the taxable amount after discount. Multi-rate GST (CGST/SGST/IGST) is not modelled.
- **Labour** is charged as the service's base price, and labour hours are recorded as the estimated duration. Actual hours are not tracked separately.
- **Parts** are free text plus a single cost figure, not a parts catalogue or stock. This keeps the first release small; it is the most likely extension (see below).
- **Technicians** are represented as a name string, not a staff entity, to avoid premature complexity. Conflict detection relies on the name, so names must be unique per technician in practice.
- **Time zone**: all times are stored as local `DateTime` (`TimeProvider.GetLocalNow`). Multi-timezone deployments should switch to `DateTimeOffset` in UTC.
- **Migrations are not committed** in this release, so the first `dotnet ef migrations add` generates them for your environment.
- **Overdue** is computed at query time rather than stored, so it is always accurate.
- **Authorization** is not enforced in this release. Every page is open; add ASP.NET Core Identity with roles before deployment.

---

## 8. Future enhancements

1. **Customer and vehicle management**: CRUD screens, search, and per-customer service history.
2. **Parts catalogue and stock**: `Part` entity, quantities on jobs, automatic stock deduction, and reorder alerts.
3. **Invoice and payment screens**: invoice PDF export (QuestPDF), payment entry UI, and reminders for overdue invoices.
4. **Maintenance history view**: per-vehicle timeline with mileage charts and next-service-due estimates based on mileage or time.
5. **Authentication and roles**: ASP.NET Core Identity with Admin, Technician and Customer roles, plus a customer self-service portal to view bookings and invoices.
6. **REST API**: `/api/bookings`, `/api/invoices` and so on, with versioning and OpenAPI (Swashbuckle).
7. **Technician entity and availability**: working hours per technician, leave, and a calendar view with drag-and-drop rescheduling.
8. **Notifications**: email or SMS confirmations and reminders (background worker using `IHostedService` or a queue).
9. **Multi-tax and localisation**: GST split, multi-currency, and culture-aware date formats.
10. **Observability and resilience**: health checks, OpenTelemetry tracing, and Polly retries for external calls.
11. **Concurrency control**: `rowversion` columns on bookings and invoices to guard against lost updates.
12. **Test coverage**: integration tests with Testcontainers for SQL Server, covering conflict detection and the complete-to-invoice transaction.

---

## 9. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| `dotnet ef` not found | Run `dotnet tool install --global dotnet-ef` and reopen the terminal. |
| "No migrations" when running the app | Run `dotnet ef migrations add InitialCreate` first. |
| Login failed / cannot connect | Check the connection string and that the SQL Server service is running. |
| Bootstrap styles missing | The layout loads Bootstrap from a CDN; check network access. |
| Booking rejected with an unexpected hour | Check the `Scheduling` section of `appsettings.json`. |
