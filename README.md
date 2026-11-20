# Sample HRMS

A small ASP.NET Core 8 MVC HRMS with EF Core SQLite and ASP.NET Core Identity.

## Run

1. Install the .NET 8 SDK.
2. From the solution root run:

   ```powershell
   dotnet restore
   dotnet ef database update --project .\SampleHrms.Web\SampleHrms.Web.csproj --startup-project .\SampleHrms.Web\SampleHrms.Web.csproj
   dotnet run --project .\SampleHrms.Web\SampleHrms.Web.csproj
   ```

The app creates `hrms.db` in the web project working directory and seeds data on startup.

## Demo accounts

| Role | Email | Password |
|---|---|---|
| Admin | admin@demo.com | Demo@123 |
| HR | hr@demo.com | Demo@123 |
| Employee | employee@demo.com | Demo@123 |

## Tests

```powershell
dotnet test .\SampleHrms.sln
```

## Parth setup

Parth's UI shell and authenticated SignalR agent run without a configured model. With an empty OpenRouter key or model, the app logs one startup warning and Parth replies `AI is not configured yet`.

The private development settings file is `SampleHrms.Web\appsettings.Development.json` and is ignored by Git. A safe empty template is available at `SampleHrms.Web\appsettings.Development.example.json`. To enable OpenRouter, fill only these values in the private development file:

```json
"ApiKey": "",
"Model": ""
```

Keep the key on the server; do not put it in JavaScript, views, or a committed file. After saving the settings, restart the app:

```powershell
dotnet run --project .\SampleHrms.Web\SampleHrms.Web.csproj
```

The provider, fallback model, timeout, and max token settings are server-side. Parth's write operations use the regular HRMS forms and require an explicit confirmation.

## Parth voice conversation

Voice input and output are available in Chrome and Edge on localhost or another secure origin. Allow microphone access when prompted. Chrome's speech recognition uses Google's online speech service, so an internet connection is required. The panel defaults to Hindi/Hinglish (`hi-IN`); use the language selector to switch to English (India) (`en-IN`). Voice mode can listen, respond aloud, and resume after agent-driven page navigation; a tap may be needed after a reload if the browser blocks automatic microphone startup.

### Voice tips

For laptop speakers, keep “Allow interrupt by voice” off; Parth waits until each reply finishes and the speaker cooldown passes before listening again. “Hold to talk” is the most echo-resistant option: hold Mic or Space while the panel is focused, then release to send. Enable voice interruption only with headphones. If the voice service reports a network error, check the internet connection and tap Mic to retry.

## Interactive element agent IDs

| Page | Stable `data-agent-id` values |
|---|---|
| Shared layout | `nav-dashboard`, `nav-leave-apply`, `nav-leave-history`, `nav-attendance-my`, `nav-employees`, `nav-leave-pending`, `nav-leave-all`, `nav-attendance-manage`, `nav-leave-types`, `nav-logout`, `nav-toggle`, `flash-success`, `flash-error` |
| Login | `login-title`, `login-form`, `login-email`, `login-password`, `login-remember`, `login-submit` |
| Dashboard | `dashboard-title`, `dashboard-balances-table`, `balance-row-{leave-type}`, `dashboard-attendance-count` |
| Apply leave | `leave-apply-title`, `leave-apply-form`, `leave-from-date`, `leave-to-date`, `leave-type`, `leave-reason`, `leave-submit` |
| Leave history | `leave-history-title`, `leave-history-table`, `leave-history-row-{id}`, `leave-cancel-{id}` |
| Pending leave | `leave-pending-title`, `leave-pending-table`, `leave-pending-row-{id}`, `leave-approve-comment-{id}`, `leave-approve-{id}`, `leave-reject-comment-{id}`, `leave-reject-{id}` |
| All leave | `leave-all-title`, `leave-all-table`, `leave-all-row-{id}` |
| Employees | `employees-title`, `employee-create-link`, `employees-table`, `employee-row-{id}`, `employee-toggle-{id}` |
| Create employee | `employee-create-title`, `employee-create-form`, `employee-full-name`, `employee-email`, `employee-code`, `employee-department`, `employee-password`, `employee-role`, `employee-create-submit` |
| Leave types | `leave-types-title`, `leave-type-create-form`, `leave-type-name`, `leave-type-balance`, `leave-type-create-submit`, `leave-types-table`, `leave-type-row-{id}`, `leave-type-edit-name-{id}`, `leave-type-edit-balance-{id}`, `leave-type-edit-active-{id}`, `leave-type-update-{id}`, `leave-type-delete-{id}` |
| My attendance | `attendance-my-title`, `attendance-my-table`, `attendance-my-row-{id}` |
| Manage attendance | `attendance-manage-title`, `attendance-correct-form`, `attendance-user`, `attendance-date`, `attendance-check-in`, `attendance-check-out`, `attendance-status`, `attendance-save`, `attendance-manage-table`, `attendance-manage-row-{id}` |
| Access denied | `access-denied-message` |
