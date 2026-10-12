# eNotice

eNotice (project name `GV23_Notice`) is the City of Johannesburg Property Branch application for producing and sending municipal valuation notices to property owners. It covers every notice from date configuration to delivery and statistics, for the General Valuation Roll 2023 (GV23) and the supplementary rolls (SUPP 1 to SUPP 4).

## Notice types

| Code | Notice |
|---|---|
| S49 | Section 49 notice |
| S51 | Section 51 notice |
| S52 | Section 52 notice (appeal and review decisions) |
| S53 / S53Rev | Section 53 Municipal Valuer's Decision, and the revised version |
| DJ | Dear Johnny letter |
| IN | Invalidity notices (invalid objection and invalid omission) |
| S78 | Section 78 notice |
| TPA | Third-Party Appeal Application notice |
| CLA_TPA | CLA Third-Party Application notice |

## How a notice run works

1. **Date Configuration**: set the letter date, the closing dates and the signature, then approve the date summary.
2. **Notice Preview and Approval**: check a sample PDF against the checklist and approve it.
3. **Batches**: create batches of properties. Each batch gets the next number, for example `S49_SUPP 4_0001`.
4. **Printing**: generate the PDF for every notice in the batch.
5. **Quality Check**: approve a sample of printed notices. For Section 49 this is done per batch.
6. **Sending**: email the notices. Every email is saved as an `.eml` copy next to the PDF.
7. **Notice Statistics**: sent, failed and no-email counts per batch, with a report that can be emailed.

Other tools: **Notice Search** (find and download issued notices) and **Notice Corrections** (correct and resend a notice).

## Email rules

- **Copy to Valuation Enquiries:** every email is copied (CC) to the configured `Email:CcAddress`.
- **Roll mailbox:** a roll can have a mailbox, set with `RollDb:Sources:{SourceDb}:RollMailbox`. SUPP 4 uses `GV23Supp4@joburg.org.za`. When a mailbox is set, every notice on that roll also goes to it:
  - Owner has an email: owner (To) and roll mailbox (hidden copy, BCC).
  - Owner has no email: roll mailbox only, recorded as Sent with a note.
- **Test mode:** when `Email:TestMode:Enabled` is `true`, the owner copy of batch notices goes to `Email:TestMode:Recipient` instead. The roll mailbox still gets the real copy. Third-Party Appeal, CLA and Correction emails do not use test mode.
- **Owner address:** when an owner has no postal address, the Section 49 letter is addressed to "THE OWNER" at the property street address.

## Technology

- ASP.NET Core MVC on .NET 10, Windows authentication (Negotiate)
- SQL Server with Entity Framework Core (notice database) and Dapper (roll databases and stored procedures)
- QuestPDF (Community licence) for the notice PDFs
- System.Net.Mail (SMTP) for sending
- xUnit, Moq and EF Core InMemory for tests

## Project structure

```
GV23_Notice.slnx
GV23_Notice/                 Web application
  Controllers/               Workflow (Steps 1–2), Step3 (batches, print, QA, send), Stats, Search, Corrections, Home
  Services/                  Notice PDFs, batches, printing, QA, email, rolls, statistics, search
  Domain/                    Entities and options (notice settings, batches, run logs, roll sources)
  Data/, Migrations/         EF Core AppDbContext and migrations
  Views/, wwwroot/           Razor pages, eNotice styles (css/enotice.css) and scripts (js/enotice.js)
GV23_Notice.Tests/           Unit tests (see GV23_Notice.Tests/README.md)
```

## Getting started

**Requirements**
- .NET 10 SDK
- Visual Studio 2026 or later, or the `dotnet` command line
- SQL Server with the notice database and the roll databases (`Objection`, `Objection_Supp1` to `Objection_Supp4`)

**Configuration**

Settings are in `GV23_Notice/appsettings.json`. The main sections are:

| Section | What it holds |
|---|---|
| `ConnectionStrings` | Notice database and user management database |
| `RollDb` | Roll database server and, per roll source: `RollTable`, `ContactTable`, `RollMailbox`, holidays, and Section 49 options (`EmailSentMode`, `AuditTable`, storage folders) |
| `Section49` | Batch size, quality check sample sizes, file name patterns |
| `Email` | SMTP, sender, CC, test mode, send limits, templates |
| `AccessControl` | Roles and the policies for each step |
| `NoticeStats`, `NoticeEmailArchive` | Statistics report recipients and email archive |

Keep passwords and connection strings out of the repository. Use `dotnet user-secrets` on your own machine and environment variables or a protected `appsettings.Production.json` on the server.

**Run**

```
dotnet restore
dotnet run --project GV23_Notice
```

Or open `GV23_Notice.slnx` in Visual Studio and press F5.

## Tests

```
dotnet test GV23_Notice.slnx
```

Or in Visual Studio: **Test → Run All Tests**. The tests use mocks and an in-memory database, so they don't need SQL Server, SMTP or file shares. See [GV23_Notice.Tests/README.md](GV23_Notice.Tests/README.md) for what is covered and how to add a test.

## Deployment

The app is published to IIS. Before each release, back up the production database and the published site folder. Then copy the new files without overwriting the production `appsettings.json`, and add any new settings to it by hand. Each release's steps are recorded in its change order.

---

City of Johannesburg, Property Branch – Valuation Services
