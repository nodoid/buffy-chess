# Buffy Chess

A complete cross-platform chess game in C# and MonoGame - the companion
source for the book *Buffy Chess: A Complete Cross-Platform Chess Game in
C# and MonoGame* (Paul F. Johnson).

One shared game, three fronts:

| Platform | Target | Shell |
|----------|--------|-------|
| Windows 10/11 desktop, macOS 26+ | `net10.0` | `src/Chess.Desktop` (DesktopGL) |
| Android (API 32+) | `net10.0-android` | `platforms/Android` (SqliteGameActivity-style bootstrap) |
| iOS 18+ | `net10.0-ios` | `platforms/iOS` (UIKit scene delegate) |

The rules engine, AI, five difficulty levels and networking live in
`src/Chess.Core` as a plain `netstandard2.0` library with NUnit tests in
`tests/`.

## Building

Everything is .NET CLI; no IDE required.

```sh
dotnet run --project Source/src/Chess.Desktop     # desktop
dotnet build -f net10.0-android Source/platforms/Android   # Android
dotnet build -f net10.0-ios Source/platforms/iOS           # iOS
dotnet test Source/tests/Chess.Core.Tests                  # engine + AI
```

Windows packagers: see `Source/tools/package-macos.sh` for the macOS .app
recipe (Info.plist + icns + ad-hoc signature).

## Layout

```
Source/
  src/Chess.Core/          engine: rules, search, persistence, network
  src/Chess.Desktop/       shared game shell (reused verbatim by both
                           mobile targets, compiled via <Compile Link>)
  platforms/Android/       Android bootstrap, resources, blur-free splash
  platforms/iOS/           UIKit scene delegate + device storage adapter
  tests/                   NUnit engine, AI and persistence fixtures
  tools/                   packaging scripts (macOS app bundle)
  Assets/Branding/         app icons generated from one master
  Assets/Skins/            board/piece skins shipped with the game
```
