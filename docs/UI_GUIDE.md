# UI guide

The interface is Persian, right-to-left, and dark. It is built as a single design system in
`src/PersonalResearchLab.Web/wwwroot/app.css` — one file, semantic tokens, no CSS framework.

---

## 1. Design intent

An instrument panel for long research sessions. Dark surfaces reduce eye strain over hours;
accent colours carry meaning rather than decoration; density is high but never cramped.

Every colour is a semantic token. Changing the theme is a single-file edit.

---

## 2. Tokens

### Surfaces

| Token | Value | Use |
| --- | --- | --- |
| `--bg-base` | `#070A12` | Page background |
| `--bg-deep` | `#0A0E18` | Recessed areas |
| `--surface-1` | `#0F1420` | Cards |
| `--surface-2` | `#141A28` | Nested panels |
| `--surface-3` | `#1B2233` | Inputs, table headers |
| `--surface-raised` | `#212A3D` | Hover, raised elements |

### Borders

`--border-subtle` `#1E2637` · `--border-default` `#2A3448` · `--border-strong` `#3A4761`

### Text

`--text-primary` `#E8EDF7` · `--text-secondary` `#A3B0C7` · `--text-tertiary` `#6B7A94` ·
`--text-disabled` `#4A5568`

### Accents

| Token | Value | Semantic role |
| --- | --- | --- |
| `--accent-cyan` | `#22D3EE` | Evidence, primary action |
| `--accent-blue` | `#3B82F6` | Informational |
| `--accent-violet` | `#8B5CF6` | Concepts, knowledge graph |
| `--accent-amber` | `#F59E0B` | Uncertainty, warnings |
| `--accent-crimson` | `#EF4444` | Contradiction, failure, destructive |
| `--accent-emerald` | `#10B981` | Verified, success |

Aliased roles: `--color-evidence`, `--color-concept`, `--color-uncertainty`,
`--color-contradiction`, `--color-verified`.

### Geometry

`--radius-sm` 6px · `--radius-md` 10px · `--radius-lg` 14px · `--radius-xl` 20px

### Spacing

`--space-1` 4px · `--space-2` 8px · `--space-3` 12px · `--space-4` 16px · `--space-5` 24px ·
`--space-6` 32px · `--space-7` 48px · `--space-8` 64px

### Typography

`--font-ui`: `"Vazirmatn", "Segoe UI", "Tahoma", system-ui, sans-serif`
`--font-mono`: `"JetBrains Mono", "Cascadia Code", "Consolas", monospace`

Scale: `--text-xs` 0.75rem · `--text-sm` 0.8125rem · `--text-base` 0.9375rem ·
`--text-lg` 1.0625rem · `--text-xl` 1.375rem · `--text-2xl` 1.75rem · `--text-3xl` 2.25rem

Body line-height is 1.7 — Persian script needs more vertical room than Latin.

### Motion

`--ease-out` `cubic-bezier(0.16, 1, 0.3, 1)` · `--duration-fast` 120ms ·
`--duration-base` 200ms · `--duration-slow` 320ms

A `prefers-reduced-motion` media query disables transitions and animations.

### Layout

`--rail-width` 76px · `--sidebar-width` 300px · `--topbar-height` 60px

---

## 3. RTL rules

`<html lang="fa" dir="rtl">` in `Components/App.razor`, and `body { direction: rtl;
text-align: right; }`.

Consequences that must be respected when adding markup:

- **Use logical properties.** `margin-inline-start`, `padding-inline-end`,
  `border-inline-start` — not `left`/`right`. The rail is on the right in RTL.
- **Do not flip icons manually.** Directional glyphs are chosen to read correctly in RTL.
- **Numbers and code stay LTR.** `code`, `pre`, and `.mono` set `direction: ltr;
  unicode-bidi: embed` so identifiers and paths render correctly inside RTL text.
- **Persian digits.** `JalaliDate.ToPersianDigits` converts ASCII digits for display. Do not
  hand-roll digit conversion.
- **Mixed content.** A Latin word inside a Persian sentence is handled by the browser's bidi
  algorithm; do not insert manual direction marks.

---

## 4. Dates

`Formatting/JalaliDate.cs` is the only conversion point. Timestamps are stored in UTC and
converted at the display boundary.

| Method | Output |
| --- | --- |
| `FormatDate(dt)` | `۲۱ آبان ۱۴۰۵` |
| `FormatDateTime(dt)` | `۲۱ آبان ۱۴۰۵، ساعت ۱۴:۳۰` |
| `FormatShort(dt)` | `۱۴۰۵/۰۸/۲۱` |
| `FormatWeekday(dt)` | Persian weekday name |
| `FormatRelative(dt)` | `۳ ساعت پیش` |
| `ToPersianDigits(...)` | ASCII → Persian digits (string, int, long) |
| `FormatBytes(long)` | `۱۲٫۴ مگابایت` |

`DefaultTimeZone` resolves `Asia/Tehran` → `Iran Standard Time` → UTC, so it works on a
machine without Iran timezone data.

**Never** format a `DateTime` with `ToString()` in a Razor page. Always go through
`JalaliDate`.

---

## 5. Labels

`Formatting/Labels.cs` maps every domain enum to a Persian display string and a badge class.

| Method | Purpose |
| --- | --- |
| `Epistemic(status)` / `EpistemicHint` / `EpistemicBadge` | Epistemic status |
| `ClaimStatusLabel` / `ClaimStatusBadge` | Claim status |
| `ClaimTypeLabel` | Claim type |
| `StanceLabel` / `StanceBadge` | Evidence stance |
| `SourceKindLabel` | Source kind |
| `StatusLabel` / `StatusBadge` | Processing status |
| `RunStatusLabel` / `RunStatusBadge` | Research run status |
| `NodeKindLabel` / `RelationKindLabel` | Knowledge graph |
| `PlanStepLabel` | Research plan step |
| `DataRouteLabel` / `DataRouteBadge` | Data routing |
| `ProviderKindLabel` / `CapabilityLabel` | AI configuration |
| `StepMarkerClass` / `StepMarkerGlyph` | Job step marker |
| `RetrievalReasonLabel` | Retrieval reason tokens |
| `EventTypeLabel` / `JobTypeLabel` | Timeline and job keys |
| `ReportTemplate` | Report template keys |

`DataRouteLabel` is named that way, not `DataRoute`, because a method cannot share a name with
the enum type it takes.

**Never** render a raw enum value. Always go through `Labels`.

### 5.1 Text the Application layer persists

Some user-facing strings are written to the database rather than composed in the page: timeline
titles and details, processing-job titles, and per-stage summaries. Those are formatted with
`Application/Text/PersianFormat` at write time, so the stored text is already Persian and does
not depend on the request culture.

| Method | Purpose |
| --- | --- |
| `Digits(string)` | ASCII digits → Persian digits |
| `Number(long)` | Persian digits with the U+066C thousands separator |
| `SourceKind(SourceKind)` | Persian source-kind label |
| `Language(string)` | `fa` → فارسی, `en` → انگلیسی, `mixed` → ترکیبی |
| `EpistemicStatus(EpistemicStatus)` | Persian epistemic label for report bodies |

Use `PersianFormat.Number` rather than `ToString("N0")`. The invariant culture emits an ASCII
comma, which reads as a foreign character inside Persian text.

### 5.2 Text persisted by older builds

Rows written before the UI became Persian-first still carry English stage names and job titles.
Rewriting stored history would falsify the audit trail, so `Formatting/LegacyText` translates
them at render time. New rows pass through unchanged.

| Method | Purpose |
| --- | --- |
| `JobTitle(string?)` | `Ingest: …` → `پردازش: …` |
| `StageName(string?)` / `CurrentStage` | English stage names → Persian |
| `StageDetail(string?)` | English stage summaries → Persian |
| `TimelineTitle(string?)` / `TimelineDetail` | English timeline rows → Persian |

Any page that renders a persisted `Title`, `Detail`, `Name`, or `CurrentStage` must route it
through `LegacyText`.

---

## 6. Component classes

### Shell

`.app-shell` (grid) · `.app-rail` · `.rail-brand` · `.rail-item` (+ `.active`) · `.rail-icon` ·
`.rail-label` · `.rail-spacer` · `.app-main` · `.app-topbar` · `.topbar-title` ·
`.topbar-subtitle` · `.topbar-spacer` · `.app-content` (+ `.flush`)

### Containers

`.card` (+ `.tight`, `.flush`) · `.card-header` · `.card-title` · `.card-subtitle` · `.panel`

### Metrics

`.metric-grid` · `.metric` (+ `--metric-accent` custom property) · `.metric-label` ·
`.metric-value` · `.metric-hint`

### Buttons

`.btn` · `.btn-primary` · `.btn-danger` · `.btn-ghost` · `.btn-sm` · `.btn-icon`

Disabled state is styled (`.btn:disabled`) and must be used whenever an action is unavailable.

### Forms

`.field` · `.field-label` · `.field-hint` · `.input` · `.textarea` · `.select` ·
`.checkbox-row`

### Status

`.badge` + `.badge-neutral` / `-cyan` / `-violet` / `-amber` / `-crimson` / `-emerald` ·
`.dot` · `.dot-pulse` · `.progress` · `.progress-bar` (+ `.indeterminate`)

### Data

`.table-wrap` · `table.data` · `.empty-state` · `.empty-icon` · `.empty-title` · `.empty-text`

### Messaging

`.notice` + `.notice-info` / `.notice-warn` / `.notice-danger` · `.notice-icon`

### Layout helpers

`.stack` · `.stack-sm` · `.stack-lg` · `.row` · `.row-between` · `.row-wrap` · `.grid-2` ·
`.grid-3` · `.spacer` · `.divider` · `.split` · `.split-side` · `.split-main`

### Text utilities

`.text-sm` · `.text-xs` · `.text-muted` · `.text-secondary` · `.text-cyan` · `.text-amber` ·
`.text-crimson` · `.text-violet` · `.text-emerald` · `.text-bold` · `.text-center` ·
`.truncate` · `.nowrap` · `.tabular`

### Domain-specific

`.citation` · `.passage` · `.passage-meta` · `.passage-text` (with `mark` highlighting) ·
`.chat-scroll` · `.msg` · `.msg-user` · `.msg-assistant` · `.msg-insufficient` · `.msg-meta` ·
`.chat-composer` · `.timeline` · `.timeline-item` · `.timeline-dot` · `.timeline-title` ·
`.timeline-time` · `.timeline-detail` · `.step` · `.step-marker` (+ `.succeeded`, `.running`,
`.failed`) · `.step-body` · `.step-title` · `.step-detail` · `.skeleton`

---

## 7. Page structure

Every page follows the same shape:

```razor
@page "/example"
@rendermode InteractiveServer

<PageTitle>عنوان — رصدخانه دانش</PageTitle>

<div class="stack-lg">
    <div class="row-between">
        <div>
            <h1>عنوان صفحه</h1>
            <p class="text-secondary text-sm">توضیح کوتاه</p>
        </div>
        <button class="btn btn-primary" disabled="@_busy">اقدام</button>
    </div>

    @if (_message is not null)
    {
        <div class="notice @(_isError ? "notice-danger" : "notice-info")">
            <span class="notice-icon">@(_isError ? "!" : "i")</span>
            <span>@_message</span>
        </div>
    }

    <div class="card">
        ...
    </div>
</div>
```

Rules:

- `@page` must start at column 0.
- `@rendermode InteractiveServer` on every page that has interactivity.
- The injected service property must **not** share a name with the page class (`CS0542`).
- A page file must **not** be named after an Application record type — it shadows the type.
- Local variables must not be named `page`, `code`, `inherits`, `inject`, `layout`, `using`,
  `implements`, `attribute`, or `typeparam` — those are reserved Razor directives.

---

## 8. Interaction rules

**Every visible interactive element must have a real behaviour or a clearly disabled state.**
This is a hard rule, not a preference.

- A button that cannot act is `disabled` and visually dimmed.
- A feature that is not implemented is stated in text, not hidden behind a dead control.
- A mutating action sets `_busy = true` for its duration and disables all mutating controls.
- Results are reported through the `.notice` banner, not silently.
- Empty states use `.empty-state` with an explanation of what to do next, never a blank panel.

Examples of honest disclosure already in the UI:

- The settings page states that reranking is not implemented.
- The research page states that evidence is local-only and web search is not used.
- The companion refuses with an explicit Persian message when there is no evidence or no
  compatible provider.

### 8.1 Binding text inputs that gate a button

`@bind` defaults to the `onchange` event, which only fires on blur. If a button's `disabled`
attribute depends on the bound field, the button stays disabled while the user types and only
enables after they click elsewhere — which reads as a broken form.

Any text input or textarea whose value gates a control must bind on input:

```razor
<textarea class="textarea" rows="3" @bind="_objective" @bind:event="oninput"></textarea>
<button class="btn btn-primary"
        disabled="@(string.IsNullOrWhiteSpace(_objective) || _busy)"
        @onclick="CreatePlanAsync">ساخت برنامه</button>
```

`<select>` and `<input type="checkbox">` are exempt: their `onchange` fires on selection.

---

## 9. Accessibility

| Concern | Handling |
| --- | --- |
| Focus visibility | `:focus-visible` outline in `--accent-cyan` |
| Reduced motion | `prefers-reduced-motion` disables transitions |
| Colour contrast | Text tokens are chosen against dark surfaces; accents are used for emphasis, not as the only signal |
| Semantic markup | Real `<button>`, `<table>`, `<label>` elements |
| Language | `<html lang="fa" dir="rtl">` |
| Screen readers | Badges carry text, not colour alone |

Not implemented: a skip link, ARIA live regions for the notice banner, and a keyboard
shortcut layer.

---

## 10. Responsive behaviour

- `.grid-2` and `.grid-3` use `auto-fit` with `minmax`, so they collapse naturally.
- Below 900px the rail narrows and labels are hidden, leaving icons.
- `body { overflow: hidden }` with `.app-content` scrolling keeps the rail and topbar fixed.

The layout targets a desktop viewport. It is usable on a tablet; it is not designed for a
phone.

---

## 11. Adding a page

1. Create `Components/Pages/YourPage.razor` — do not name it after an Application record.
2. Add `@page "/your-route"` at column 0 and `@rendermode InteractiveServer`.
3. Inject the service with a distinct property name.
4. Add the `@using` for the service namespace to `_Imports.razor` if it is new.
5. Add a `NavItem` to the `NavItems` array in `NavMenu.razor` with an icon, a full label, and
   a short label.
6. Use `JalaliDate` for every date and `Labels` for every enum.
7. Verify the route returns 200 and the page renders Persian content.
