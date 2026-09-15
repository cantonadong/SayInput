# Repository working instructions

Before changing code, tests, scripts, documentation, or build output in this repository, read `docs/development-progress.md` completely. Treat it as the handoff record for the current implementation state, known problems, verification evidence, and next work.

After each development session, update `docs/development-progress.md` with material changes, newly discovered pitfalls, verification commands and results, manual checks still needed, and the next concrete action. Never place credentials, tokens, private endpoints, transcripts, or other user data in the progress document.

Preserve existing workspace changes and generated artifacts unless the user explicitly asks to remove them. The repository currently tracks many `bin` and `obj` files, so stage source and generated output deliberately rather than using a blanket add.
