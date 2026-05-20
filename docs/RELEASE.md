# DoubleMark Release Build

## Build Release EXE

Run from the repository root:

```powershell
.\scripts\build-release.ps1
```

Result:

```text
dist\DoubleMark\DoubleMark.exe
```

The release build is `win-x64`, self-contained, without debug symbols, and suitable for installer packaging. It does not require the .NET Runtime on the user's machine. ReadyToRun is enabled for non-obfuscated builds and disabled for obfuscated builds because Obfuscar cannot rewrite ReadyToRun mixed-mode assemblies.

During the build, `scripts/build-release.ps1` generates `dist\DoubleMark\appsettings.json` from environment variables, `.env.local`, `.env`, or `appsettings.local.json`. This file contains only the Supabase URL and anon/public key required by the installed app.

For a single-file EXE build, run:

```powershell
.\scripts\build-release.ps1 -SingleFile -SkipObfuscation
```

Use the default folder build for obfuscation and installer packaging, because Obfuscar works on published `.dll` files before they are bundled into a single-file executable.

## Build Installer

Install Inno Setup 6, then run:

```powershell
.\scripts\build-installer.ps1
```

Result:

```text
installer\Output\DoubleMarkSetup-2.1.0.exe
```

## Obfuscation

`obfuscar.xml` is configured conservatively:

- public API is preserved;
- only `DoubleMark.Core.dll` is obfuscated;
- WPF/XAML desktop assembly is not obfuscated because it relies on generated BAML and runtime binding names;
- private implementation details can be renamed where safe.

Install Obfuscar before release builds if obfuscation is required:

```powershell
dotnet tool install --global Obfuscar.GlobalTool
```

If Obfuscar is unavailable, the script warns and still produces a Release EXE.

## Code Signing

Keep certificates outside the repository. Set:

```powershell
$env:SIGN_CERT_PATH="C:\secure\codesign.pfx"
$env:SIGN_CERT_PASSWORD="certificate-password"
```

The scripts will use `signtool.exe` when available. Never commit `.pfx`, `.snk`, passwords, or signing logs.

## Do Not Commit

- `.env`
- `.env.local`
- `appsettings.local.json`
- `appsettings.json`
- `secrets.json`
- `*.pfx`
- `*.snk`
- `dist/`
- `installer/Output/`
- `.pdb`
- user tokens or Supabase sessions

## Production Checklist

Before shipping:

1. Build with `.\scripts\build-release.ps1`.
2. Build installer with `.\scripts\build-installer.ps1`.
3. Verify `dist\DoubleMark` does not contain `.pdb`, `.env`, `appsettings.local.json`, or `secrets.json`.
4. Install on a clean Windows VM.
5. Confirm DoubleMark icon in installer, shortcut, taskbar, Alt+Tab, and window.
6. Confirm Supabase login, profile, subscription, and device registration.
7. Confirm inactive subscriptions cannot use scanner, print, export, history, or templates.
8. Confirm active/trialing subscriptions can use expected workflows.
9. Confirm Raw Input, HID, COM, GS1/FNC1, AI 01/21/91/92, DataMatrix, print, export, and templates still work.

## Security Notes

- Desktop uses only Supabase anon/public key.
- Never embed or ship `service_role`.
- RLS must protect `profiles`, `subscriptions`, `payments`, and `user_devices`.
- Prefer server-side RPC for final production access decisions:
  - `check_app_access()`
  - `register_device_and_check_limit()`
