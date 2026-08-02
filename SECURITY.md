# Security policy

## Gatekeeper is a lab target. Do not deploy it.

Gatekeeper is the companion application for the book
**AI: Programming Like a God: The Heavenly Gates**. It exists to be broken and
then gated, in public, one chapter at a time.

It contains **deliberate, documented defects at every tag**, including silent
authorization failures and audit-integrity failures. The flagship one makes the
audit log record denied actions as allowed.

**Do not deploy Gatekeeper** to a public server, a shared network, or any
environment holding real data or real users. There is no supported production
configuration, and there will not be one. Run it locally, in a controlled lab,
and nowhere else.

Read that in the strongest terms the subject allows: an authorization service
that lies in its own audit log looks perfectly healthy from the outside. That is
the entire point of chapter 1, and it is exactly why this must never run
anywhere real. A deliberately vulnerable shop demo announces itself the moment
you attack it. This one does not.

## Reporting

**Defects we planted are not security issues.** They are the subject matter.
Every one is listed in the defect registry and in `defects.json` at the repo
root, which the startup banner reads. A report against a seeded defect will be
closed with a pointer to the registry entry, and no offence is taken; it means
the disclosure worked.

**A vulnerability we did not seed is a real report and is genuinely welcome.**
If you find one, open an issue describing it. There is a pleasing irony in
finding a real hole in a book's teaching app about holes, and it will be
credited.

## What you may lift

The MIT license stands, and ADR-0002's invitation to copy stands, narrowed to
what it always meant: **the gates are for lifting; the application is not.** The
pre-commit hook, the CI lanes, the analyzer configuration, the architecture
fitness tests, the branch-protection ruleset: take all of it into your own
projects. That is the deliverable.

Gatekeeper itself is the patient, not the medicine.

## Current tag

The running application logs its tag and its active seeded defects on boot. If
the console says defects are active, they are. Check `defects.json` for what
they are at the tag you have checked out.
