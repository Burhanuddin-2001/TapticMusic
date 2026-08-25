# Developing Taptic Music

Just want to install and use the app? Go back to the [README](README.md).

This file is for building the app from source, and for making your own
installable package.

## What you need first

- Visual Studio, with the **Windows App SDK** (WinUI 3) workload installed.
- The .NET SDK version listed in `global.json` at the repo root.
- Windows 10 or 11.

## Build and run

1. Clone the repo:
   ```
   git clone https://github.com/YOUR-USERNAME/TapticMusic.git
   ```
2. Open `MusicPlayer.sln` in Visual Studio.
3. Press **F5** to run it, or use **Build > Build Solution**.

## Creating an installer package

This is how the app gets turned into the `.msixbundle` + certificate +
`Install.ps1` set that ships in each GitHub Release.

1. In Visual Studio, right-click the `MusicPlayer` project. Choose
   **Publish > Create App Packages**.
2. Pick **Sideloading** as the distribution method.
3. Choose **Create a self-signed test certificate**.
   - Publisher common name: your own name.
   - Set a password for the generated `MusicPlayer_TemporaryKey.pfx` file.
4. Package settings:
   - Output location: any folder you choose (for example, a new folder on your Desktop).
   - Version: `1.0.0.0` (raise this for each new release).
   - Generate app bundle: **Always**.
   - Architecture: **x64**. Solution configuration: **Release**.
   - Check **"Include public symbol files, if any, to enable crash analysis for the app."**
5. Visual Studio builds a folder for you containing the `.msixbundle` file,
   a matching `.cer` certificate file, and `Install.ps1`. Zip that folder —
   that zip is what gets attached to a GitHub Release.

## Installing your own build

Same steps a visitor follows, run against your own output folder instead of
a downloaded release:

1. Open the output folder Visual Studio created.
2. Right-click `Install.ps1` → **Run with PowerShell**.
3. Approve the administrator prompt.
4. Type `Y` and press Enter when PowerShell asks to install the certificate.
5. Type `Y` and press Enter on the second Windows security prompt about the certificate.
6. Wait for "Success: Your app was successfully installed."

If you double-click the `.msixbundle` file directly instead of running
`Install.ps1`, Windows will refuse it with a certificate error and grey out
the Install button. That's expected — the certificate has to be installed
first, and `Install.ps1` is what does that for you.

## Contributing

This project is open-source, and open to real collaboration, not just bug
reports.

- **Found a bug?** Open an issue. Say what you did, what you expected, and
  what happened instead.
- **Have an idea?** Open an issue for it too, even a rough one. New feature
  ideas are welcome.
- **Want to build something yourself?** Fork the repo, make your change, and
  open a pull request. Small fixes and big features are both welcome.
- **Want to go bigger?** This project does not have to stay Windows-only.
  A Linux version, a Mac version, an Android version — any of these would be
  a great direction for someone to take this project in. If that interests
  you, open an issue to start the conversation, or just start building and
  reach out.

Reach out any time — through an issue, a pull request, or a direct message
on GitHub. This project grows through people who want to shape it, not just
use it.

## Like it?

If this project or its code helped you, please consider giving the repo a
star. It costs nothing and it helps a lot.
