## Summary

Describe the user-visible or architectural outcome.

## Validation

- [ ] `dotnet build SharpClawCode.sln --configuration Release --warnaserror`
- [ ] `dotnet test SharpClawCode.sln --configuration Release`
- [ ] Relevant package, CLI, MCP, plugin, and cross-platform checks were run

## Risk and compatibility

- [ ] Durable JSON/session formats remain compatible or include a migration
- [ ] Dangerous file, shell, and network operations still pass through permissions
- [ ] No credentials, local state, or generated secrets are included
