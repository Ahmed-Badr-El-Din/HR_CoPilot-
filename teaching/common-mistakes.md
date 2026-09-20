# Common mistakes — five misconceptions and how to correct them

A one-page note for the session. Each correction is anchored in this repository.

**1. "RAG = retrieve + stuff it in the prompt, done."**
*Correction:* stuffing is necessary but not sufficient; the *response contract*
matters. `AskService` refuses below a retrieval-confidence threshold and promotes
a provider refusal sentinel to a real refusal (`Refusals.cs`). Demos that only
show retrieval miss that grounding is about what the model is *allowed* to do
with the chunks.

**2. "Refusal is a failure return, so metrics should penalise it."**
*Correction:* refusal is the *correct* behaviour for unanswerable questions
(policy-refusal-guideline is corpus law). The harness treats refusal as a pass:
`Refusal accuracy 100%` is a headline number, and answers are only scored when
they carry citations.

**3. "Prompt injection is just 'ignore previous instructions' in English."**
*Correction:* the bypass that actually bites is **indirect** injection inside
documents, and it must be handled by *neutralisation* so the content is still
useful while the imperative dies (`PromptInjectionDetector.Neutralize`). The
corpus ships two adversarial fixtures and the evaluation proves a 0% leak rate —
that is the realistic threat model.

**4. "If it runs in the demo, the plumbing works."**
*Correction:* our harness surfaced four latent bugs a happy-path demo could not
see: RRF confidence scaled to an unreachable maximum (every question silently
refused), the first streamed token dropped, prompt assets failing to deserialize
(gated ciphertext-looking "not found"), and provider refusals masquerading as
answers. *Test the property — refusal, grounding, first-token — not the path.*

**5. "The LLM does the scoring, so the LLM must be unbiased; just tell it in the prompt."**
*Correction:* prompting is not enforcement. Protected attributes are removed
**before** the model sees anything, every redaction is audited, and the output
is deterministically re-validated. Weighted totals are computed in code
(never by the model). Bias protection is structural, exactly like the approval gate.