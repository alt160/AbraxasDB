# Changelog

## 1.0.2 — 2026-10-26

- Documentation-only package update: the README links to the separate Showcase and Meridian Trading repositories and portable ZIP releases, with screenshots and app prerequisites.
- The current library repository contains the library; companion apps build and release from their own repositories.
- Runtime implementation, public API, component dependencies and on-disk formats are unchanged from 1.0.1. The library continues to target .NET 8.
- Versions 1.0.0 and 1.0.1 remain available.

## 1.0.1 — 2026-10-16

- Documentation-only release: the packaged README now links directly to the maintained GitHub wiki guides.
- Runtime implementation, public API, component dependencies and on-disk formats are unchanged from 1.0.0.
- Version 1.0.0 remains available; this release does not deprecate or replace it.

## 1.0.0 — 2026-10-16

- Initial public open-source edition of AbraxasDB.
- Ordinary .NET objects, durable record storage, maintained indexes, and selective reads.
- OSS structural limits: 15 application stores and a 19-bit per-store identity counter.
- Apache-2.0; private commercial implementations and development harnesses are not included.

The first public release contains the OSS library, portable symbols, precompiled library and Windows companion applications.
