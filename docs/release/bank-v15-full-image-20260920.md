# PKHeX Bank v1.5 Full Image + Metadata Viewer — 2026-09-20

This release contains the Bank/BulkStorage legality-context and clone-all improvements from the prior Bank release plus native Pokémon Bank v1.5 full-image support.

## Highlights

- Native `0xBB518` Pokémon Bank v1.5 serialized-image recognition.
- `0xBB520` runtime object dumps are accepted and normalized to the canonical serialized body.
- Legacy `0xACA48` Bank containers remain supported.
- Read-only metadata model for header, groups, boxes, source records, slot format/source/timestamp arrays, transfer metadata, aggregate data, counters, and tail data.
- WinForms Bank Metadata Viewer with Overview, Source Records, Boxes, Slots, and Raw Structure tabs.
- Lossless no-op round-trip and single-slot edit preservation tests.
- BulkStorage no longer supplies a false active-trainer identity to legality checks.
- Ctrl + Shift + click clones the clicked Pokémon to all writable box slots while preserving existing shortcuts.
