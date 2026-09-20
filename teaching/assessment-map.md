# Assessment map

Learning outcomes for the 90-minute session, mapped to the assessment items used
during the course.

| Learning outcome | Taught in slides | Assessed by | Pass criterion |
|---|---|---|---|
| Explain why grounding is an operational contract, not a retrieval artifact | 5–7 | Lab T1 (eval numbers), lab T2 (refusal event) | Reproduces 100% refusal accuracy and grounded answers |
| Implement a refusal path that treats "no evidence" as a correct answer | 6, 11 | Lab T2 + stretch 3 | Refused event precedes retrieval; no empty responses |
| Reason about prompt injection as two distinct threats (direct vs indirect) | 9–10 | Lab T3, stretch 4 | Detector + neutralisation tests green; leak rate 0% |
| Apply deterministic bias redaction + re-validation as a defence-in-depth | 8 | Stretch 2 (+ code read) | Explains the second line of defence after a bypassed redactor |
| Justify structural agency control (enumerated tools + approval gate) | 12 | Code reading + discussion | Names the gated tool and the approval role |
| Trace a correlation id from request to the outbound LLM call | 13 | Stretch 3 discussion | Finds `X-Correlation-Id` wiring in `OpenAiModelProvider` |
| Read and defend the honest gap table | 16 | Final discussion | Picks a row, gives the mitigation and the cost-to-close |
| Reproduce evaluation numbers without inventing them | 7, 17 | Lab T1 | JSON + markdown regenerated locally |

Assessment instruments: the lab sheet (`lab.md`), its answer key, and the closing
discussion prompts in `slides.md`.