## Agent skills

### Issue tracker

Issues live in GitHub Issues; external PRs are not a triage request surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Triage roles use Chinese GitHub labels: `待评估`, `待补充信息`, `可交给Agent`, `可交给人工`, `不处理`. See `docs/agents/triage-labels.md`.

### Domain docs

Multi-context repo: read root `CONTEXT-MAP.md`, then the relevant context `CONTEXT.md` and `docs/adr/` when present. See `docs/agents/domain.md`.

## Commit messages

Use short Chinese commit messages with this format:

```text
<type>: <summary>
```

Allowed types:

- `init`: initialize repository, configuration, or scaffolding
- `docs`: documentation-only changes
- `feat`: user-facing feature changes
- `fix`: bug fixes
- `chore`: maintenance changes that do not affect behavior

Keep the summary concise and imperative. Examples:

- `init: 初始化仓库配置`
- `docs: 添加提交规范`
- `fix: 修正统计结果计算`
