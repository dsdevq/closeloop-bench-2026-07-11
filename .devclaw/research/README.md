# Research citation convention

Every feature research artifact under `.devclaw/research/` (e.g. `.devclaw/research/<feature>.md`)
must contain these three sections in this order. Copy the headings verbatim and fill them in as
described below — do **not** omit or rename a section.

---

## Sources consulted

List every reference CRM product that was examined during research for this feature. The candidate
set for CRM benchmarking is: **Salesforce, HubSpot, Pipedrive, Attio, Zoho**. For each source:

- Name the product and the specific surface examined (e.g. "Salesforce — Activity Timeline UI in
  Lightning Experience", "Attio — record sidebar REST API v2 docs").
- State *how* you examined it: live product trial, public documentation, changelog, marketing
  comparison page, recorded demo, community post, etc.
- One bullet per distinct source. If a CRM was deliberately skipped, say so and why (e.g.
  "Pipedrive — skipped; no public API docs for this feature area at time of research").

This section is evidence of due diligence, not a summary. Future readers must be able to reproduce
the lookup.

---

## Borrowed

Describe **concretely** what pattern, design, or data-model decision was adopted and from which CRM
it came. Required fields per adopted item:

- **What**: the specific behaviour, schema shape, naming convention, interaction pattern, or API
  contract that was taken on.
- **From**: which CRM product and which surface (link to docs or screenshot if available).
- **Why it fits**: one or two sentences on why this pattern suits closeloop's constraints (stack,
  audience, existing conventions).

Avoid vague phrases like "inspired by Salesforce". If you can't name what you took and from where,
it doesn't belong here — move it to *Rejected & why* or leave it out.

---

## Rejected & why

For every alternative that was seriously considered but not adopted, record:

- **What was considered**: the concrete alternative (a pattern from a specific CRM, a design
  variation, a different data model, etc.).
- **Source**: which product / document surfaced it.
- **Reason rejected**: an argued judgment — not "we didn't like it" but a specific constraint,
  trade-off, or incompatibility (e.g. "Salesforce's activity-type enum is closed and would require
  a migration every time a new type is added; our event-sourced model needs open extensibility").

This section must represent genuine deliberation. An entry that is simply a copy of one CRM's
approach with no reasoning is not acceptable. Equally, an unsourced invention ("we thought about
doing X") must cite where X came from or be removed.
