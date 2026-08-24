# TKG PDA Flutter Client

Android industrial PDA client for TKG logistics operations. The backend is authoritative for permissions, slot eligibility, available transport actions and execution status.

## Local validation

```powershell
flutter pub get
dart format --output=none --set-exit-if-changed .
flutter analyze
flutter test
flutter build apk --debug --dart-define=TKG_API_BASE_URL=https://api.example.invalid
```

For Android emulator access to a host API, the development default is `http://10.0.2.2:5000`. Production builds must provide the HTTPS endpoint through `TKG_API_BASE_URL`. JWT and password are never persisted; only the provisioned Device Code is stored via platform secure storage.

See [`docs/PHASE_07B_FLUTTER_PDA.md`](../../docs/PHASE_07B_FLUTTER_PDA.md) for architecture, security boundaries and acceptance results.
