# Entity Framework Core: how the API talks to the database

The API uses **EF Core 10 code-first** with the **SQL Server provider**, which also works for Azure SQL. No C# file writes raw SQL; every read and write goes through `AppDbContext`.

```
appsettings / user-secrets ──► Program.cs  AddDbContext<AppDbContext>(UseSqlServer + retry)
                                     │  (one AppDbContext per HTTP request, injected by DI)
                                     ▼
Controllers ──► Services (AuthService, OtpService, ProfileService …) ──► AppDbContext ──► Azure SQL
                                                                         (DbSet<User> = dbo.Users …)
```

## 1. The pieces

| File | Role |
|---|---|
| `KalyanaVaran.Api.csproj` | Packages `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Design` (10.0.0) |
| `Entities/Entities.cs` | C# classes, one per table: `User`, `OtpRequest`, `RefreshToken`, `Profile`, … |
| `Data/AppDbContext.cs` | `DbSet<T>` per table, plus the table names, lengths, keys, indexes and relationships (Fluent API) |
| `Program.cs` | `AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, retry…))` |
| `Services/*.cs` | Take `AppDbContext db` in the constructor and query with LINQ |

Example from `AuthService` (A2 Send OTP):
```csharp
var user = await db.Users.FirstOrDefaultAsync(u => u.Mobile == mobile, ct);   // SELECT … WHERE Mobile = @p
db.OtpRequests.Add(new OtpRequest { … });                                     // INSERT INTO OtpRequests …
await db.SaveChangesAsync(ct);                                                // runs the INSERT
```

## 2. Connection string (Azure SQL)

The password never goes in `appsettings.json`. Set it once per PC:
```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=tcp:aspireright.database.windows.net,1433;Initial Catalog=KalyanaVaram;User ID=AspireAdmin;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True"
```
On a server, use the environment variable `ConnectionStrings__Default` (two underscores) or Azure App Service → Configuration → Connection strings.

If the password isn't set, the API stops at startup with a message telling you to set it.

## 3. Azure-specific settings (already in Program.cs)

- **`EnableRetryOnFailure(5, 10 s)`**: Azure SQL sometimes drops a connection for a few seconds. EF retries automatically instead of returning a 500.
- Because retries are on, the one place that uses a manual transaction (onboarding step save) runs inside `CreateExecutionStrategy().ExecuteAsync(...)`, so the whole save is retried together.
- **Command timeout**: 30 s.

## 4. Check the connection from Scalar

`GET /v1/dev/db-check` (Development only, under **Dev tools**) returns:
```json
{ "connected": true, "server": "tcp:aspireright.database.windows.net,1433", "database": "KalyanaVaram",
  "rowCounts": { "Users": 3, "OtpRequests": 7, "…": 0 },
  "appliedMigrations": [], "pendingMigrations": [] }
```
If it fails, the message says why:
- **40615**: your IP is blocked by the firewall.
- **18456**: wrong password.
- **4060**: wrong database name.
- **208** or **207**: a table or column is missing. Run `KalyanaVaram_AzureSQL.sql`.

## 5. Changing tables later: EF migrations

Your tables were created with `KalyanaVaram_AzureSQL.sql`, so EF has no migration history yet. Do this **once** to hand control to EF:

```bash
dotnet tool install --global dotnet-ef                 # one time per PC
dotnet ef migrations add InitialCreate                 # creates Migrations/<timestamp>_InitialCreate.cs
```
**Don't** run `dotnet ef database update` now. The tables already exist, so it would fail with "There is already an object named 'Users'".

Instead, mark the first migration as already applied. Run this in SSMS on **KalyanaVaram**, replacing the id with the file name EF created (without `.cs`):
```sql
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NULL
    CREATE TABLE dbo.__EFMigrationsHistory (
        MigrationId    NVARCHAR(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
        ProductVersion NVARCHAR(32)  NOT NULL);

INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
VALUES (N'20261103081500_InitialCreate', N'10.0.0');   -- ← your file name
```

From then on, every schema change works like this:
1. Change the C# entity, e.g. add `public string? Country { get; set; }` to `Profile`.
2. Run `dotnet ef migrations add AddCountryToProfile`.
3. Check the generated file in `Migrations/`.
4. Run `dotnet ef database update`, or just run the API in Development, which applies pending migrations at startup.
5. For production, generate a script for review: `dotnet ef migrations script --idempotent -o deploy.sql`.

`GET /v1/dev/db-check` shows `appliedMigrations` and `pendingMigrations`, so you can confirm.

## 6. Rules to keep EF and the database in sync
- Change tables **through C# and migrations**, not by hand in SSMS. Otherwise EF's model and the real table drift apart, which causes "Invalid column name" errors.
- Use `AsNoTracking()` for read-only queries (already done in profile reads).
- Never build SQL strings from user input. LINQ and `FromSql($"…")` are parameterised and safe.
