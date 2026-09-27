# Security and Workspace Boundaries

## 1. Local-Only Execution Guarantee

AI Context Builder runs strictly locally. It does not:
- Upload source files to remote services.
- Call cloud LLMs or remote telemetry.
- Transmit source code outside the local machine.

## 2. Preflight Secret Scanner

The application includes an integrated preflight secret scanner that inspects candidate files before inclusion:
- **Detected Secret Categories**:
  - Private Keys (`-----BEGIN RSA/EC PRIVATE KEY-----`)
  - AWS Access Keys (`AKIA...`) and Secret Access Keys
  - GitHub Personal Access Tokens (`ghp_...`)
  - Generic API Key assignments (`api_key = '...'`)
  - Database Connection Strings with Passwords (`Password=...;Server=...`)
  - Database URIs (`postgres://user:password@...`)
  - Slack Tokens (`xoxb-...`)
- **Non-Disclosure Principle**: The scanner *never* outputs secret values to terminal logs or Markdown files. It reports only relative path, line number, category, and safe recommendation.

## 3. Path Traversal & Boundary Protection

1. **Path Canonicalization**: All target paths are resolved to their physical canonical paths.
2. **Workspace Containment**: Target paths must reside within the detected or explicitly configured workspace boundary. Symlinks that point outside the workspace boundary are rejected with a security violation.
3. **.git Protection**: Internal Git database paths (`.git/objects`, `.git/config`) are strictly excluded from inspection.
