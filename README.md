# Harvest evidence

Raw data for the harvest experiment in *AI: Programming Like a God: The Heavenly
Gates*. Published so any rate the book prints can be checked against the runs that
produced it.

## This branch is deliberately unrelated to the code

It is an orphan branch. It shares no commit with `main`, and it must never be
merged into it. Gatekeeper is the experiment's start tree: the harness checks out a
tag and hands that tree to an agent. If this data reached the main line, an agent
starting from a later tag could read earlier agents' transcripts and verdicts before
writing a line, and every affected result would be worthless.

## Files

- **`ledger.jsonl`** One line of JSON per run, append-only, never edited. Each row
  carries the run id, task, model, context level, start commit, prompt SHA-256,
  detection-suite SHA-256, result commit, per-defect verdict, cost, turn count, and,
  for an excluded run, the reason it was excluded.
- **`transcripts/<run-id>.jsonl`** The complete agent session behind that row: every
  message, tool call, and file read or written, in order.

## One edit, disclosed

These files are otherwise verbatim, with a single mechanical substitution applied
to every one of them on the way here:

The per-run temporary working directory is written as `<WORKTREE>`, and any
other path under the operator's home directory as `<HOME>`. Nothing else is
altered: no message, tool call, generated code, or verdict is touched. The
substitution removes the machine's account name and folder layout, which the
recorder captured incidentally and which no result depends on.

The unedited originals exist in the authoring repository. If a published path
matters to a question you are asking, ask for it.

## Reading it honestly

- **Excluded runs are here.** A run that was dropped keeps its row and its reason. A
  silently dropped run is a silently altered denominator.
- **Superseded rows are here.** When a run is re-scored, the original row keeps its
  original verdict and a new row is appended carrying `rescore_of`. Count the
  current view, never both.
- **`detection_sha256` is load-bearing.** Rows adjudicated by different versions of
  the detection suite are not comparable and must not be pooled.
- **Pilot runs are not corpus runs.** They are published anyway.

Licensed the same as Gatekeeper: MIT.
