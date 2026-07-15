# AICopilot runner platform attestation template

This template is the manual platform evidence record for `AI-SEC-010`.
It does not prove the platform by itself. The filled record must be validated
by the platform owner and kept with the release or infrastructure change log.

Do not commit a filled production record if it contains hostnames, user names,
secret names, ticket links, screenshots, or other internal operational details.

## Scope

- Environment: `<production>`
- Repository: `<owner/repository>`
- Git SHA or release tag: `<sha-...>`
- Runner host: `<host or asset id>`
- Attestation date: `<YYYY-MM-DD>`
- Platform owner: `<name>`
- Related infrastructure ticket: `<ticket id or change id>`

## Runner machine facts

- [ ] Runner service runs as a dedicated non-root account.
- [ ] Runner labels include `self-hosted` and `iiot-linux-prod`.
- [ ] Runner work root is `/data/iiot-platform/runners/aicopilot`.
- [ ] Docker Root Dir is `/data/iiot-platform/runtime/docker`.
- [ ] AICopilot deploy directory is `/srv/enterprise-ai/deploy`.
- [ ] Runner service account write access is limited to runner work root,
      Docker access needed for build/deploy, and AICopilot deploy support paths.
- [ ] The following command was run on the runner or deploy host and passed:

```bash
cd /srv/enterprise-ai/deploy
./scripts/check-runner-security-attestation.sh \
  --work-root /data/iiot-platform/runners/aicopilot \
  --docker-root /data/iiot-platform/runtime/docker \
  --deploy-dir /srv/enterprise-ai/deploy
```

Evidence summary:

```text
<paste non-secret command output summary here>
```

## GitHub production environment

- [ ] The `production` environment exists in GitHub.
- [ ] Environment secrets are restricted to AICopilot production and disaster workflows.
- [ ] Workflow permissions stay least-privilege: `contents: read` only.
- [ ] Production or secret-touching workflows use `runs-on: [self-hosted, iiot-linux-prod]`.
- [ ] No production or secret-touching workflow uses GitHub hosted runners.
- [ ] The following read-only checks were run by a platform owner and passed:

```bash
gh api repos/<owner>/<repo>/environments/production
gh secret list --env production
gh variable list --env production
```

Evidence summary:

```text
<paste non-secret environment protection and secret inventory summary here>
```

## Credential strategy

Select exactly one current state and keep the evidence below it.

- [ ] OIDC/Vault or equivalent short-lived credentials are implemented for AICopilot production workflows.
- [ ] OIDC/Vault rollout is tracked as an approved infrastructure exception for AICopilot production workflows.

Implementation or approved exception evidence:

```text
Credential strategy state: <implemented or approved infrastructure exception>
Ticket or change id: <ticket id or change id>
Exception owner: <name/team>
Due date: <YYYY-MM-DD>
Current mitigation: <restricted runner access, rotation window, or other current mitigation; do not paste secrets>
```

Current mitigation while long-lived GitHub environment secrets still exist:

- [ ] Secret inventory was validated by the platform owner.
- [ ] Runner host access is limited to the deployment operators.
- [ ] Rotation owner and next rotation date are recorded outside this repository.

## Sign-off

- [ ] Platform owner confirms the evidence above is current.
- [ ] Platform owner confirms no secret value is present in this record.
- [ ] Release owner confirms this record is linked from the release or infrastructure change log.

Platform owner: `<name/date>`

Release owner: `<name/date>`
