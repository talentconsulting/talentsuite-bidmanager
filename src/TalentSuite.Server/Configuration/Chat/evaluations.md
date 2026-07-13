# Evaluations

## Evaluation 1: Grounded Answer With Citations

### Input

A normal bid question with matching supporting material in the bid library.

### Expected Behaviour

Return a direct answer that uses grounded evidence and includes citations.

## Evaluation 2: No Cross-Project Merge

### Input

A question asking for examples where multiple loosely related projects exist in the source material.

### Expected Behaviour

Keep examples separated by project and avoid combining their facts into a single invented narrative.

## Evaluation 3: Missing Evidence Handling

### Input

A question whose requested claim is not fully supported by available bid-library evidence.

### Expected Behaviour

State the evidence gap clearly, avoid hallucinated facts, and avoid fabricated citations.

## Evaluation Checks

The response should:

- answer the selected question
- remain grounded in evidence
- include citations for material claims
- avoid fabricated examples
- avoid cross-project merging
- stay within the expected answer size
