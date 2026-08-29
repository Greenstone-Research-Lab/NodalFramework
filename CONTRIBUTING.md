# Contributing to Nodal Framework

Thank you for contributing. Please open an issue before a substantial change,
keep pull requests focused, add tests and XML documentation for public APIs,
and run the documented format, build, test, and quality checks locally.

By submitting a pull request, you agree that the contribution is your original
work or that you have the right to submit it, and that you accept the
Contributor License Agreement in [CLA.md](CLA.md).

Contributions must follow the repository's [engineering rules](ENGINEERING_RULES.md),
code of conduct, security policy, and provider capability boundaries.

Behavioral changes are specification-driven. Start with the lifecycle and
templates in [specs/README.md](specs/README.md), obtain acceptance before
implementation, and include the specification identifier in the pull request.
Use `Specification: N/A - <reason>` only for changes that cannot affect product
behavior, provider semantics, public APIs, packaging, or operational safety.

Run the specification gates locally before opening a pull request:

```powershell
./eng/verify-specifications.ps1
./eng/verify-pr-specification.ps1 -PullRequestBody 'Specification: ADR-0001'
```
