# Self-hosted Flutter fallback fonts

Flutter Web otherwise resolves fallback glyphs from `fonts.gstatic.com`. Phase 09 packages the two fallback subsets exercised by the Turkish TKG Operator UI so isolated production LANs make no public runtime request.

| Family | Upstream path | SHA-256 | License |
|---|---|---|---|
| Roboto v32 | `roboto/v32/KFOmCnqEu92Fr1Me4GZLCzYlKw.woff2` | `35B02CA266B79EB4996590F15817425A1CE9EBF48F84471843233FF614656BF2` | Apache License 2.0 |
| Noto Sans Symbols v43 | `notosanssymbols/v43/rP2up3q65FkAtHfwd-eIS2brbDN6gxP34F9jRRCe4W3gfQ8gb_VFRkzrbQ.woff2` | `08202E258EA583254C036CFF46A7077BB5AF4F82C41A6C0A6775F6E44D99F1AA` | SIL Open Font License 1.1 |

The paths intentionally mirror Flutter engine fallback URLs. Do not replace these files without rerunning the real production-build browser network audit and updating the hashes.
