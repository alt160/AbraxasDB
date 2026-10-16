# Showcase web UI map

The showcase deliberately uses small, ordered browser modules without a bundler.
`index.html` is the dependency manifest, and each script owns one reason to change.

## Runtime and composition

- `../app.js` — shared state, WebView2 host calls, DOM roots, formatting, theme, and clipboard helpers.
- `navigation.js` — routes, history, breadcrumbs, page transitions, and the scenario-group rail.
- `page-output.js` — one-page HTML/print snapshots and a complete offline instructional guide with a table of contents and print page breaks.
- `page-output-runtime.js` — small host-free interactions embedded into saved HTML for disclosures, Show How, glossary, concepts, definitions, and safe copying.
- `bootstrap.js` — one-time event wiring and application startup.
- `global-search.js` — the shared search index, toolbar dropdown, keyboard controls, and full results page.

## Pages

- `pages/home.js` — welcome page and open-existing action.
- `pages/group.js` — group overview and group-page composition.
- `pages/catalog.js` — browsable scenario-group catalog and planned-entry deep links.
- `pages/scenario.js` — scenario explanation, execution, and result composition.
- `pages/what-if.js` — intentional contract and performance contrasts.
- `pages/what-if-remedies.js` — successful follow-up explanations and code variants for each What-if case.
- `type-reference.js` — full custom-type reference page and definition popover.

## Reusable components

- `components/scenario-types.js` — custom-type cards, usage highlighting, and group/scenario type panels.
- `components/group-browser.js` — store and property selectors for a prepared group.
- `components/group-setup-dialog.js` — setup confirmation, generation parameters, and progress transitions.
- `components/concept-dialog.js` — reusable secondary concept-detail lightbox.
- `components/commercial-edition-dialog.js` — keyboard-accessible commercial-library notice for blocked OSS execution, without changing workspace state.
- `components/browser-data.js` — type-agnostic paging, row projection, and selected-record inspection.
- `components/code-editor.js` — Monaco mounting, code-editor lifecycle, and disclosure animation.
- `components/what-if-remedy-popover.js` — draggable, copyable What-if solution popup.
- `components/object-explorer.js` — hydrated-object tree, lazy expansion, lightbox behavior, and toast errors.

## Change routing

Start with the owning file above. Read `../app.js` only when the change needs shared
state or native-host communication, and read `navigation.js` only when the change
alters routing or page composition. New pages and reusable UI patterns should be
added as new files in these folders and registered in `index.html` before
`bootstrap.js`.

The scripts share one browser global scope by design. Their explicit order keeps
the runtime dependency-free while the file boundaries keep feature work local.
