# Alterlife — User Testing and Iteration

## Method

The team conducted formative testing with Andrea Monarrez and Lalo Molinar. This report reconstructs the supplied observations. Dates, duration, recordings, and a formal task script were not provided. Participant suitability for the target audience needs confirmation through French and VR experience.

The tasks below organize observed behavior and define repeatable future criteria. They are not claimed to be the original verbatim instructions.

## Tasks and results

| Task | Completion criterion for repeat testing | Andrea | Lalo |
| --- | --- | --- | --- |
| Navigate to Paris | Reach restaurant independently from globe and book | Arrival implied; independent navigation not recorded | Presence implied; independent navigation not recorded |
| Understand waiter | Explain the prompt or obtain appropriate assistance | Asked how to know what he was saying | Said he did not understand and laughed |
| Use companion | Open help and explain its purpose independently | Requested instruction | Not recorded |
| Order | Complete exchange and reach confirmation | Friction and confusion when thanks were omitted | Not recorded |
| Explore | Navigate accessible area and return | Not recorded | Encountered movement restriction |
| Finish and repeat | Review results and repeat or return | Not recorded | Not recorded |

“Not recorded” means missing evidence, not task failure. A complete success rate cannot be calculated.

## Comments

Andrea said “wow,” “como se que esta diciendo?” (“How do I know what he is saying?”), and “necesito ayuda para aprender a utilizar el zorro compañia” (“I need help learning how to use the companion fox.”).

Lalo said “no entiendo lo que dice” (“I don't understand what he is saying”) and laughed. He requested more countries and wanted to explore the whole scene. The exploration request is paraphrased from the team's account.

## Evidence-based iteration

| Finding | Change or decision | Status |
| --- | --- | --- |
| Andrea needed interaction guidance | Initial controller UI | Final-build implementation reported by team; not re-inspected |
| Andrea needed companion instruction | Fox tutorial before Paris | Final-build implementation reported; no retest supplied |
| Both struggled with comprehension | English subtitles, definitions, repeatable speech | Documented development features; participant improvement unmeasured |
| Andrea did not understand missing-thanks requirement | Need explicit progression feedback | Issue identified; specific resolution not established by supplied evidence |
| Lalo encountered restrictive movement | Expanded Paris exploration | Implementation reported; no measured before/after result |
| Lalo requested other countries | Retain future destinations | Deferred to maintain a complete restaurant route |
| Cards could bypass speaking | Model pronunciation and wait for speech | Later team-requested change, not falsely attributed to testers |

## Metrics supported by the account

| Measure | Value |
| --- | --- |
| Reported participants | 2 |
| Explicit dialogue comprehension difficulty | 2 of 2 |
| Explicit companion-use difficulty | 1 of 2 |
| Explicit restricted-exploration concern | 1 of 2 |
| Final-build changes attributed directly to sessions | 3: controller UI, fox tutorial, movement |
| Completion rate, time on task, errors, help frequency, satisfaction score | Not measured or not supplied |
| Post-change sessions / A/B comparison | Not supplied |

These are small descriptive counts, not population estimates. No percentage improvement or learning gain is supported.

## Follow-up protocol

Recruit beginner learners and record VR experience. For each task, record independent completion, assisted completion, or non-completion, alongside time, errors, help requests, and comments. Obtain consent for recordings and identifying information.

Prioritize discovering the fox after onboarding, understanding the politeness requirement, and exploring while finding the way back to the restaurant. Compare these results with the original issues. Do not describe an A/B experiment unless separate conditions were actually tested.

Automated tests and installation checks are engineering evidence. They do not measure comprehension or replace participant testing.
