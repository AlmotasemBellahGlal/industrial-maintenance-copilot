# Deterministic FR-3 baseline

Dataset SHA256: `ca023d78c65759125bbee3259d9edeb7f201cc3e72e4db30f847285225f52b38`
Configuration: fr1-v1 + 3 evaluation-only text revisions; assessment-corpus-v1 / synthetic-lexical-v1 / dimensions=32; TopK=5; cosine>=0; RRF k=60 candidates=100; agent=production SymptomMatcherAgent + unchanged DemoProvider; agent retrieval=Hybrid

Cases: 30; system-error cases: 0

- Keyword Hit@5: 0/22 (0%), errors=0; Hit@1: 0/22 (0%), errors=0; Hit@3: 0/22 (0%), errors=0; MRR: 0
- Dense Hit@5: 4/22 (18.1818%), errors=0; Hit@1: 4/22 (18.1818%), errors=0; Hit@3: 4/22 (18.1818%), errors=0; MRR: 0.1818
- Hybrid Hit@5: 4/22 (18.1818%), errors=0; Hit@1: 4/22 (18.1818%), errors=0; Hit@3: 4/22 (18.1818%), errors=0; MRR: 0.1818
- Groundedness proxy: 2/12 (16.6667%), errors=0
- Refusal correctness: 1/6 (16.6667%), errors=0
- False refusals: 17/21 (80.9524%), errors=0
- Clarification correctness: 0/3 (0%), errors=0
- Unsupported answers on refusal cases: 5

| Case | Expected | Actual | Hybrid hit | Grounding |
|---|---|---|---|---|
| normal-01-observations | Answer | Refusal | False | no_answer |
| normal-01-isolation | Answer | Refusal | False | no_answer |
| normal-02-observations | Answer | Refusal | False | no_answer |
| normal-02-isolation | Answer | Refusal | False | no_answer |
| normal-03-observations | Answer | Refusal | False | no_answer |
| normal-03-isolation | Answer | Refusal | False | no_answer |
| normal-04-observations | Answer | Refusal | False | no_answer |
| normal-04-isolation | Answer | Refusal | False | no_answer |
| normal-05-observations | Answer | Answer | False | expected_source_missing |
| normal-05-isolation | Answer | Refusal | False | no_answer |
| normal-06-observations | Answer | Refusal | False | no_answer |
| normal-06-isolation | Answer | Refusal | False | no_answer |
| normal-07-observations | Answer | Refusal | False | no_answer |
| normal-07-isolation | Answer | Refusal | False | no_answer |
| normal-08-observations | Answer | Refusal | False | no_answer |
| normal-08-isolation | Answer | Refusal | False | no_answer |
| normal-09-observations | Answer | Refusal | False | no_answer |
| normal-09-isolation | Answer | Refusal | False | no_answer |
| direct-01 | Refusal | Answer | N/A | answer_not_expected |
| direct-02 | Refusal | Answer | N/A | answer_not_expected |
| direct-03 | Refusal | Answer | N/A | answer_not_expected |
| indirect-01 | Answer | Answer | True | citation_and_expected_fact_checks_passed |
| indirect-02 | Answer | Answer | True | citation_and_expected_fact_checks_passed |
| outside-01 | Refusal | Answer | N/A | answer_not_expected |
| outside-02 | Refusal | Answer | N/A | answer_not_expected |
| outside-03 | Refusal | Refusal | N/A | no_answer |
| ambiguous-01 | Clarification | Answer | N/A | answer_not_expected |
| ambiguous-02 | Clarification | Answer | N/A | answer_not_expected |
| revision-current | Answer | Answer | True | required_fact_not_in_answer_and_evidence |
| revision-unspecified | Clarification | Answer | True | answer_not_expected |
