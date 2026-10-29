# Riftevora 3.1

**A free Windows companion for exploring League of Legends artwork and managing community mod packages.**

[English](README.md) · [Türkçe](README.tr.md)

![Riftevora home](docs/screenshots/home.png)

Browse official skin artwork, compare two skins side by side, save favorites and collections, and open your own mod packages in LTK Manager.

## Highlights

- Five main areas: Home, Champions, My Mod Library, Favorites, and Settings.
- Compare skins for the same champion, switch between them, and save favorites.
- Pick another skin directly from the catalog.
- Turkish and English interfaces, reduced motion, and a compact-window layout.
- Catalog of 173 champions, 2,121 skins, and 7,119 chromas, with 2,121 distinct portraits.
- Local mod library with integrity checks, conflict inspection, and portable backups.
- One-click **Add game** flow that detects League, installs LTK from its official source if needed, and saves the game path in LTK.
- When a chroma has no separate splash image, the app shows the parent skin's official artwork and labels it accordingly.

## Windows release

Download a portable ZIP from [Releases](../../releases). Extract the complete archive, keep the Resources folder beside Riftevora.exe, and launch the app. Windows 10/11 x64 is supported; the published executable includes the .NET runtime.

To build locally, run <code>launch_riftevora.bat</code>.

## Add a game

Choose **Add game** on the home screen or under **Settings → Game**. Riftevora checks its saved path and Riot Client records first. If it cannot find League, it opens a folder picker.

If LTK is already installed, Riftevora reuses it. Otherwise, it downloads the official stable x64 installer, verifies its size and GitHub SHA-256 value, and launches the installer. Windows may ask for administrator approval.

The game path is saved in Riftevora and in LTK's <code>leaguePath</code> setting. Existing LTK settings are preserved, with a side-by-side <code>.riftevora-*.bak</code> backup created before changes. Riftevora does not write a different game path while LTK is open; close LTK and retry. Downloads can be cancelled and partial files are removed. A new LTK download is not attempted offline.

## Open a mod package

1. Add your own <code>.fantome</code> or <code>.modpkg</code> file from the library or by dragging it into the window.
2. Open the package in the library and choose **Open in LTK**.
3. Finish importing in LTK Manager, enable the mod, and choose **Start**.
4. Launch the game through Riot Client.

LTK Manager is a separate application; Riftevora can install it when it is missing. A success message only confirms that the package was sent to LTK. It does not verify that the change appears in-game or unlock paid Riot skins. Mod loading has not been tested in a live match.

## Shortcuts

Ctrl+K global search · Ctrl+F search the current page · Ctrl+I add a package · Ctrl+B toggle the sidebar · Esc close the topmost window

## Build from source

Requires the .NET 8 SDK and Python 3. The source repository excludes artwork binaries. Fetch the current official Data Dragon catalog and portraits before publishing:

<pre><code>python tools/refresh_catalog.py
dotnet build SkinForge.sln -c Release
dotnet test tests/SkinForge.Tests/SkinForge.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\build_exe.ps1 -NoOpen</code></pre>

The output is <code>dist/Riftevora-3.1.0-win-x64/Riftevora.exe</code>. Distribute the entire directory, including Resources.

Internal project namespaces and the <code>%LOCALAPPDATA%/SkinForge</code> data directory retain the previous internal identifier for compatibility with existing libraries. User packages, favorites, and personal state are not included in the source repository or portable release.

## Recorded validation

The recorded verification reports 99 passing xUnit tests, covering official download selection, hash and size checks, cancellation, failed installation, game detection, and LTK settings preservation. The Add game flow was exercised with League and LTK installed, using a disposable LTK settings file; repeating setup left matching settings unchanged.

A real official LTK 1.30.4 installer passed SHA-256 and Windows signature verification. A fresh-machine installation and UAC interaction were not exercised because LTK was already installed. Catalog, comparison, favorites, and responsive-layout checks are also retained. See [verification details](docs/VERIFICATION.md).

![Skin comparison](docs/screenshots/compare.png)

## Third-party work and trademarks

League of Legends, its artwork, and champion names belong to Riot Games. Riftevora is independent and is not endorsed by Riot. Artwork is downloaded from Riot's Data Dragon CDN; see <code>NOTICE.md</code>. LTK Manager binaries are not bundled.

[LTK Manager](https://github.com/LeagueToolkit/ltk-manager) · [Supported LTK integration](https://wiki.leaguetoolkit.dev/developers/ecosystem-integration/)
