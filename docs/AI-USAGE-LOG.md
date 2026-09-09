# AI Usage Log

## 2026-09-09

### Delegated to AI
- Helped clarify the assessment scope and the D5/T7 requirements.
- Suggested the initial Clean Architecture solution structure.
- Suggested project references between layers.
- Suggested checking the Microsoft.OpenApi vulnerability warning.

### Written / Verified Manually
- Created the GitHub repository.
- Initialized the local Git repository.
- Created the .NET solution and projects.
- Added the projects to the solution.
- Added project references.
- Ran build and dependency checks manually.

### AI Mistakes / Corrections
- AI suggested adding the latest Microsoft.OpenApi package directly to resolve a transitive vulnerability warning.
- This introduced a compatibility error with Microsoft.AspNetCore.OpenApi 10.0.2 and broke the API build.
- The direct package was removed and the build was restored.
- The vulnerability remains an open dependency issue to resolve with a compatible package upgrade.

### Verification
- Verified the project builds successfully.
- Verified the Clean Architecture project references compile.