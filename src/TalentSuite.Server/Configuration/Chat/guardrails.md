# Guardrails

## Data Access

- Use only the configured bid-library context and the supplied question context.
- Do not invent facts, examples, citations, locations, or file names.
- If evidence is unavailable, state the gap instead of guessing.

## Answer Boundaries

- Keep the answer focused on the selected question.
- Do not merge examples from unrelated projects.
- Do not rely on examples that scored 2 or below when score exclusions apply.
- Do not present unsupported claims as certain.

## Citation Requirements

- Cite supporting material using document name and location where possible.
- If a material claim depends on evidence, it should be cited or removed.

## Output Safety

- Do not include secrets, credentials, connection strings, or environment variable values.
- Do not include stack traces or raw tool output.
- Do not include unsupported external references as if they came from the bid library.
