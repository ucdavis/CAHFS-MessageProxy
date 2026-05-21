# MessageProxyApi

A simple ASP.NET Core MVC application with a REST API endpoint, message logging, and NLog logging configuration.

## Project structure

- `Program.cs` - app startup and registration of services
- `Controllers/HomeController.cs` - home page and message log listing
- `Controllers/MessageProxyController.cs` - proxy API endpoint and message persistence
- `Data/ProxyDbContext.cs` - Entity Framework Core database context
- `Models/CProxyMessage.cs` - message log entity mapped to `C_Proxy_Message`
- `Views/Home/Index.cshtml` - message log filter, pagination, and table view
- `Views/Home/Details.cshtml` - message detail view
- `nlog.config` - NLog logging configuration
- `JenkinsFile` - CI/CD pipeline for restore, build, publish, and deploy

## Requirements

- .NET 10 SDK
- SQL Server database for `C_Proxy_Message`
- Optional AWS Systems Manager for configuration and AWS credentials XML based credential setup

## Getting started

1. Restore dependencies:
   ```powershell
   dotnet restore MessageProxyApi.csproj
   ```

2. Build the app:
   ```powershell
   dotnet build MessageProxyApi.csproj
   ```

3. Run the app locally:
   ```powershell
   dotnet run --project MessageProxyApi.csproj
   ```

4. Open the browser:
   - Home page: `https://localhost:5001/`
   - Status API: `https://localhost:5001/api/status`

## Database configuration

Update `appsettings.json` with a connection string for `ProxyDatabase`:

```json
"ConnectionStrings": {
  "ProxyDatabase": "Server=YOUR_SQL_SERVER;Database=YOUR_DATABASE;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
}
```

The app uses EF Core with `ProxyDbContext` and the `C_Proxy_Message` table.

## Jenkins CI/CD

The included `JenkinsFile` is configured to:

- restore and clean `MessageProxyApi.csproj`
- publish for `development` and `main` branches
- deploy published files to IIS folders
- archive published artifacts

## Notes

- Date filters and pagination are available on the home page
- `MessageId` values link to a detail view for each log entry
- The app uses NLog for console/file logging with `nlog.config`
