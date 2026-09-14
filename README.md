# Office Web Launcher

Open local Microsoft Office files in Microsoft 365 for the web by double-clicking them in Windows or Ubuntu.

Office Web Launcher registers itself as a file handler, uploads the selected document directly to a restricted application folder in your OneDrive, and opens the URL returned by Microsoft Graph. Microsoft Office and the .NET runtime are not required on the computer running the packaged application.

> [!IMPORTANT]
> Opening a local file creates a copy in OneDrive. Changes made in Word, Excel, or PowerPoint for the web are saved to the OneDrive copy; they do not modify the original local file.

## How it works

```text
Double-click local file
          |
          v
Office Web Launcher --> Microsoft sign-in
          |
          v
OneDrive/Apps/Office Web Launcher
          |
          v
Word, Excel, PowerPoint, or Visio for the web
```

The first open launches Microsoft sign-in in your default browser. Later opens reuse a refresh token stored for the current operating-system user.

## Features

- Windows 10/11 and Ubuntu Linux support
- Intel/AMD 64-bit and ARM64 builds
- Per-user installation without Microsoft Office
- Browser sign-in using OAuth authorization code flow with PKCE
- Least-privilege `Files.ReadWrite.AppFolder` Microsoft Graph permission
- Resumable uploads, including files larger than 250 MB
- Automatic rename when a file with the same name already exists in OneDrive
- Reversible file associations with uninstall scripts
- No client secret, password collection, telemetry, or intermediary upload server

## Supported formats

The launcher recognizes these local extensions. Actual viewing and editing capabilities depend on Microsoft 365 for the web and the signed-in account.

| Product | Extensions |
| --- | --- |
| Word | `.doc`, `.docx`, `.docm`, `.dot`, `.dotx`, `.dotm`, `.odt`, `.rtf` |
| Excel | `.xls`, `.xlsx`, `.xlsm`, `.xlsb`, `.xlt`, `.xltx`, `.xltm`, `.csv`, `.ods` |
| PowerPoint | `.ppt`, `.pptx`, `.pptm`, `.pps`, `.ppsx`, `.ppsm`, `.pot`, `.potx`, `.potm`, `.odp` |
| Visio | `.vsd`, `.vsdx` |

Legacy, macro-enabled, and Visio formats may open with reduced functionality. VBA macros do not run in the Microsoft 365 web editors. Access, Publisher, Outlook message, OneNote package, and Project files are not currently registered.

## Microsoft Entra application setup

Office Web Launcher needs an Application (client) ID so Microsoft can authenticate it. Create the registration once and reuse the same client ID on all of your computers. A client ID is public application configuration; do not create or distribute a client secret.

1. Sign in to the [Microsoft Entra admin center](https://entra.microsoft.com/).
2. Open **Identity > Applications > App registrations > New registration**.
3. Enter `Office Web Launcher` as the name.
4. Select **Accounts in any organizational directory and personal Microsoft accounts** under supported account types. An organization deploying only inside its own tenant can choose the single-tenant option instead.
5. Open **Authentication**, add the **Mobile and desktop applications** platform, and add `http://localhost` as the redirect URI.
6. Under **Advanced settings**, enable **Allow public client flows**.
7. Open **API permissions** and add the delegated Microsoft Graph permission `Files.ReadWrite.AppFolder`. Remove any broader file permissions that are not needed.
8. Copy the **Application (client) ID** from the app registration's Overview page.

Some work and school tenants prevent users from creating registrations or granting consent. In that case, a tenant administrator must create or approve the application. The app-folder permission confines the launcher to the `Apps/Office Web Launcher` area of the signed-in user's OneDrive.

The default installer setting uses Microsoft's `common` endpoint and therefore requires the multi-tenant account type selected above. For a single-tenant work or school registration, also copy its **Directory (tenant) ID** and pass it to Setup using the `Tenant` option shown below.

## Install on Windows

Download the `win-x64` archive for most Windows computers or `win-arm64` for Windows on ARM. Extract the archive to a stable folder, open PowerShell in that folder, and run:

```powershell
.\Setup.ps1 -ClientId 'YOUR-APPLICATION-CLIENT-ID' -OpenDefaultApps
```

For a single-tenant registration:

```powershell
.\Setup.ps1 -ClientId 'YOUR-APPLICATION-CLIENT-ID' `
  -Tenant 'YOUR-DIRECTORY-TENANT-ID'
```

The script registers the supported extensions for the current Windows user. Windows may retain an existing default application. If it does, select **Office Web Launcher** for the desired extension in the Default Apps screen opened by Setup.

The first double-click opens Microsoft sign-in and consent in the default browser. The refresh token is then stored in Windows Credential Manager for the current Windows user.

## Install on Ubuntu

Download `linux-x64` for most Intel/AMD Ubuntu computers or `linux-arm64` for an ARM64 system. Extract the archive, open a terminal in the extracted folder, and run:

```bash
chmod +x OfficeWebLauncher Setup.sh Uninstall.sh
./Setup.sh 'YOUR-APPLICATION-CLIENT-ID'
```

For a single-tenant registration, pass the Directory tenant ID as the second argument:

```bash
./Setup.sh 'YOUR-APPLICATION-CLIENT-ID' 'YOUR-DIRECTORY-TENANT-ID'
```

Setup installs the application for the current user under `~/.local/lib/office-web-launcher` and registers a standard freedesktop `.desktop` MIME handler. Administrator access is not required.

The installer requires `xdg-utils`, which is normally present on Ubuntu Desktop. Install it when necessary with:

```bash
sudo apt install xdg-utils
```

Linux stores the refresh token beneath `~/.config/office-web-launcher/credentials`. The credentials directory has mode `0700`, and token files have mode `0600`.

## Command-line use

The file path can be passed directly instead of using a file association:

```powershell
OfficeWebLauncher.exe "C:\Documents\Quarterly Report.xlsx"
```

```bash
~/.local/lib/office-web-launcher/OfficeWebLauncher "$HOME/Documents/Quarterly Report.xlsx"
```

Remove the saved Microsoft sign-in with:

```text
OfficeWebLauncher --signout
```

The next file open will request sign-in again.

## Build from source

[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer is required to build. Published packages are self-contained and do not require .NET on the destination computer.

On Windows, build all supported targets with:

```powershell
.\Build.ps1
```

Or build selected targets:

```powershell
.\Build.ps1 -Runtime win-x64,linux-x64
```

On Linux, build a Linux target with:

```bash
chmod +x Build.sh
./Build.sh linux-x64
```

Build output is written beneath `dist/`.

## Uninstall

To remove both the saved sign-in and file associations on Windows:

```powershell
.\OfficeWebLauncher.exe --signout
.\Uninstall.ps1
```

On Ubuntu:

```bash
~/.local/lib/office-web-launcher/OfficeWebLauncher --signout
~/.local/lib/office-web-launcher/Uninstall.sh
```

The uninstallers restore previously recorded file associations where possible. They do not delete documents already uploaded to OneDrive.

## Troubleshooting

### A file still opens in another application

On Windows, open **Settings > Apps > Default apps** and select Office Web Launcher for that extension. On Ubuntu, right-click the file, select **Open With**, choose Office Web Launcher, and make it the default.

### Microsoft reports a redirect URI error

Confirm that the Entra app registration has a **Mobile and desktop applications** platform with `http://localhost` as its redirect URI.

### Microsoft reports `AADSTS50194`

The registration is single-tenant while the launcher is configured for the `common` endpoint. Either change **Supported account types** to include multiple organizations and personal Microsoft accounts, or rerun Setup with the registration's Directory tenant ID using the single-tenant command above.

### Consent is blocked

A work or school administrator may need to approve the delegated `Files.ReadWrite.AppFolder` permission. The launcher does not need `Files.ReadWrite`, `Files.ReadWrite.All`, or a client secret.

### The browser editor has fewer features

Microsoft 365 for the web supports fewer features than the desktop Office applications. The launcher only handles association, upload, and navigation; it cannot add capabilities to Microsoft's browser editors.

## Security and privacy

- Files travel directly from the local computer to Microsoft Graph over HTTPS.
- The application requests access only to its dedicated OneDrive app folder.
- OAuth uses PKCE and a loopback redirect; the application never receives the Microsoft password.
- No client secret is embedded in the executable or configuration.
- Windows credentials use Windows Credential Manager. Linux credentials are restricted to the current Unix user through filesystem permissions.
- Source code is contained in [Program.cs](Program.cs) and can be reviewed before building.
