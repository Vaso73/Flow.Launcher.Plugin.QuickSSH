## Summary

Describe what this Pull Request changes.

## User-facing impact

- [ ] This PR changes user-facing behavior
- [ ] This PR adds or changes commands
- [ ] This PR changes installation, shell, config parsing, import/export, or profile behavior
- [ ] `README.md` was updated if required

## Release intent

Apply exactly one GitHub label. The label is authoritative and must not be selected only in this checklist:

- [ ] `release:patch`
- [ ] `release:minor`
- [ ] `release:major`
- [ ] `skip-release`

For same-repository PRs, release automation prepares the exact `plugin.json` version from the latest strict SemVer tag. Do not manually choose another version. `skip-release` requires the manifest version to remain unchanged.

## Validation

- [ ] Required `build` check passes on the current PR SHA
- [ ] Tests were added or updated when needed
- [ ] Workflow files remain aligned with current `main`
- [ ] Release intent and prepared manifest version agree

## Notes

Add anything reviewers or agents should know.
