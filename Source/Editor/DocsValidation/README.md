# Docs Validation Editor Hooks

These hooks are for the OpenClaw `uvcs-docs-workflow` plugin and Unity MCP.
They are intentionally safe/report-only by default:

- no PlayMode entry
- no scene switching
- no `AssetDatabase.Refresh`
- no checkin/update/undo operations

Future exclusive validators should be added as separate methods with explicit naming and workflow approval.
