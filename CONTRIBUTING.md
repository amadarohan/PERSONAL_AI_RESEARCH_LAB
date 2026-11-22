# Contributing

This is a single-user, local-first application. Contributions are welcome, but the constraints
in `AGENTS.md` are not negotiable — they come from the product mandate, not from taste.

---

## 1. Before you start

Read, in this order:

1. `AGENTS.md` — the hard rules and the known pitfalls.
2. `docs/ARCHITECTURE.md` — how the layers fit together.
3. `docs/DEVELOPMENT.md` — setup, recipes, and the debugging playbook.
4. `docs/DECISIONS.md` — why things are the way they are. If you want to change a decision,
   add a new entry rather than silently reversing one.

---

## 2. The rules that get pull requests rejected

| Rule | Why |
| --- | --- |
| `EnsureCreated()` anywhere | Schema changes go through migrations, always |
| A Persian string hardcoded in a page | Labels live in `Labels.cs` |
| A date formatted inline | Every date goes through `JalaliDate` |
| A physical CSS property (`margin-left`) | RTL must not depend on physical sides |
| A button with no handler and no `disabled` | Every visible control must do something real |
| A fabricated citation, source, or confidence number | The product's entire value is traceability |
| A stage reporting `Succeeded` when it did no work | Use `Skipped` (D-021) |
| `Application` referencing `Infrastructure` | Breaks the one-way dependency chain |
| A singleton capturing a scoped service | Fails at startup; use `IServiceScopeFactory` |
| A secret returned from a service | `SettingsService` reports `HasSecret` only |
| A claim that something works without running it | Verify, then state |

---

## 3. Workflow

```powershell
# 1. Branch
git checkout -b feature/your-change

# 2. Build
powershell -ExecutionPolicy Bypass -File scripts\build.ps1

# 3. Run and exercise the path you changed
dotnet run --project src\PersonalResearchLab.Web

# 4. Verify (see the checklist below)

# 5. Commit
git add -A
git commit -m "Add <thing> so that <reason>"
```

Commit messages are English, imperative, and explain the *why*. `Fix bug` is not a commit
message.

---

## 4. Verification checklist

A change is not done until all of these hold. State explicitly which ones you ran.

1. `Get-Process -Name "PersonalResearchLab.Web"` returns nothing.
2. Build is clean: `0 Warning(s)`, `0 Error(s)`.
3. The app starts and every route returns HTTP 200 (or 404 for a deliberately bad path).
4. Any new page renders real data, not a hardcoded mock.
5. Any new interactive control has a working handler or a `disabled` attribute with a reason.
6. Documentation updated if behaviour, configuration, or schema changed.

There is **no automated test project**. If you add one, say so in `docs/ROADMAP.md` §13 and
update this section.

---

## 5. Documentation expectations

If your change alters any of the following, update the matching document in the same commit:

| Change | Document |
| --- | --- |
| A new entity, column, or index | `docs/DATA_MODEL.md` |
| A new service or public method | `docs/API_REFERENCE.md` |
| A new Persian label or enum | `docs/GLOSSARY.md` |
| Retrieval scoring or chunking | `docs/RETRIEVAL.md` |
| A provider kind or routing rule | `docs/AI_PROVIDERS.md` |
| A configuration key | `README.md` and `.env.example` |
| A design decision, or a reversal of one | `docs/DECISIONS.md` |
| A new page or component class | `docs/UI_GUIDE.md` |
| Something now implemented that was on the roadmap | `docs/ROADMAP.md` |

Documentation is written from verified code, not from intent (D-020). If you did not run it, do
not describe it as working.

---

## 6. Reporting a problem

Include:

- What you did, in order.
- What you expected.
- What happened, with the exact error text.
- Whether the build was clean before the failure.
- The relevant log lines from the terminal, not a paraphrase.

For a retrieval or ingestion problem, include the workspace name and the job id from
`/monitor`. For a data-routing concern, include the workspace's `DataRoute` and the provider
kind — that pair determines whether a call was allowed to leave the machine.

---

## 7. Security

Do not open a public issue for a vulnerability. Report it privately to the maintainer.

Never commit:

- A real connection string with credentials.
- A provider API key.
- A file from `storage/` — that directory holds user content and the DPAPI secret container.

`.gitignore` already excludes `storage/`, `.env`, and `appsettings.Local.json`. If you find a
path that leaks either, treat it as a defect and fix the ignore rule.
