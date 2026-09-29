<img src="banner.svg" alt="Taptic Music" width="640">

# Taptic Music

### Bring play back to Music.

Real players had knobs. Real players had buttons you could feel click under
your thumb. This app brings that feeling back — every tap, press, and slide
in Taptic Music responds like a real control, not a flat picture of one.

Underneath, it is a simple music player for Windows. It plays songs already
on your PC. No streaming. No account. No internet needed.

## Download and install

1. Go to the [Releases page](https://github.com/Burhanuddin-2001/TapticMusic/releases). Get the newest `.zip` file.
2. Unzip it. You get a folder with a few files inside.
3. Find the file named `Install.ps1`. Right-click it. Pick **Run with PowerShell**.
4. Windows asks for administrator permission. Click **Yes**.
5. PowerShell says it needs to install a certificate first. Type `Y`, then press Enter.
6. A second Windows security box appears, about the same certificate. Type `Y`, then press Enter.
7. Wait a moment. PowerShell prints "The certificate was successfully installed." Then it prints "Success: Your app was successfully installed."
8. Press Enter to close the window.
9. Open the Start Menu. Type **Taptic Music**. Open the app.

That's it. The app is now installed.

### A certificate? Is that safe?

Yes, in this case. A certificate is a small file. It tells Windows who made
the app. This app is not sold through the Microsoft Store, so Windows does
not know it yet. Installing this one certificate tells your PC: "trust this
one app." It does not affect any other app or website.

### "This app package's publisher certificate could not be verified"

You may see this message if you double-click the `.msixbundle` file by
itself, instead of running `Install.ps1`. The Install button also turns
grey. This is expected. Do not try to fix it there.

Close that window. Run `Install.ps1` instead, as shown above. It installs
the certificate first, then installs the app, in the right order.

## Uninstalling

To remove the app: open **Settings > Apps**, find **Taptic Music**, and
uninstall it like any other app.

The install also added a trusted certificate to your PC, separate from the
app itself. To remove that certificate too, see Microsoft's own cleanup
guide: https://go.microsoft.com/fwlink/?LinkId=243053

## For developers

Want to build this app from source, or make your own installer package?
See [DEVELOPMENT.md](DEVELOPMENT.md).

## Like it?

If you enjoy using Taptic Music, please consider giving this repo a star.
It costs nothing and it helps a lot.
