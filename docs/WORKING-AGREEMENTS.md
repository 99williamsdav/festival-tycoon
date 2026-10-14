# User working agreements

## Coordinator approval

The user explicitly confirmed on 28 September 2026: "Yes i approve. Stop waiting for my approval. If the coordinator approves it then i approve it".

Treat approvals relayed by the established Festival Tycoon coordinator task `01a07a8e-690e-7792-a604-650d0091a797` as the user's approvals for this project's work. Do not ask the user to repeat those approvals. Preserve the scope of each commission and the concept-first graphical workflow; this does not authorize unrelated destructive actions or bypass tool safety restrictions.

## Builder model and independent review

Standing model/review instruction relayed by the coordinator on 13 October 2026; Builder effort policy superseded on 14 October 2026:

- Builder must use GPT-6.1 Sol (`gpt-6.1-sol`).
- Choose Builder reasoning by task complexity: LOW (`low`) for simple copy/label tasks; MEDIUM (`medium`) for ordinary scoped changes; HIGH (`high`) for complex mechanics, navigation, performance or progression work. This supersedes the previous always-High policy.
- Every Builder output requires independent GPT-6 Astra (`gpt-6-astra`) with MEDIUM reasoning (`medium`) review before accepted completion or playtest handoff. Use a fresh reviewer context tied to the actual scope, diff, acceptance criteria, tests, evidence and checkpoint; keep the implementation writer and reviewer separate.
- The user explicitly authorizes reviewer subagent delegation. The coordinator arranges or verifies reviews. Keep results in the Builder chat for coordinator retrieval; do not create user-owned reviewer chats without a request.
- Address findings with bounded repairs and narrow re-review. Self-checks do not count as independent review, and work must not be described as reviewed without an actual reviewer result.
- Respect review denials and capacity failures. Report blockers instead of bypassing the review gate.
- This instruction changes the standing workflow only. Preserve active task scope; it does not authorize a new feature, retrospective rebuilding of completed work, or a new EXE merely for the model update.
