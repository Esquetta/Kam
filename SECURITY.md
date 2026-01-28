# Security Policy

## Overview

This document outlines the security measures implemented in the Smart Voice Agent project and provides guidelines for reporting security vulnerabilities.

## Security Measures

### 1. Path Traversal Protection

All file system operations use path validation to prevent path traversal attacks:

- **SecurityUtilities.IsSafeFilePath()** - Validates file paths
- **FileAgentTools** - Validates paths before file operations
- Default working directory enforcement
- Blocked patterns: `..`, `//`, `\\`, URL-encoded traversal

### 2. Command Injection Prevention

Application launching is protected against command injection:

- **SecurityUtilities.IsSafeApplicationName()** - Validates app names
- Blocks dangerous characters: `;`, `|`, `&`, `>`, `<`, `` ` ``, `$`, etc.
- Blocks command sequences: `&&`, `||`, `|`, `;`
- Only allows alphanumeric, spaces, hyphens, and dots

### 3. Sensitive Data Protection

Logging has been hardened to prevent data leakage:

- API keys are never logged
- Authorization headers are not logged
- **SecurityUtilities.MaskSensitiveData()** - Masks sensitive data for logging
- **SecurityUtilities.SanitizeForLog()** - Sanitizes strings for safe logging

### 4. URL Validation

Open redirect and URL injection attacks are prevented:

- **SecurityUtilities.IsSafeUrl()** - Validates URLs before opening
- Only `http://` and `https://` protocols allowed
- Blocks: `javascript:`, `data:`, `vbscript:`, `file:` protocols
- URL length limited to 2048 characters

### 5. File Extension Validation

Dangerous file types are blocked:

- **Executable extensions blocked**: `.exe`, `.bat`, `.cmd`, `.sh`, `.msi`, `.dll`, `.com`, `.scr`, `.ps1`
- **Allowed extensions**: Text files, documents, images, code files

## Security Utilities

The `SecurityUtilities` class provides security validation methods:

```csharp
// Path validation
bool isSafe = SecurityUtilities.IsSafeFilePath(path, allowedBaseDir);

// Application name validation  
bool isSafe = SecurityUtilities.IsSafeApplicationName(appName);

// URL validation
bool isSafe = SecurityUtilities.IsSafeUrl(url);

// Data masking for logging
string masked = SecurityUtilities.MaskSensitiveData(apiKey);

// Log sanitization
string safe = SecurityUtilities.SanitizeForLog(userInput);
```

## Configuration Security

### API Keys and Secrets

- API keys are stored in User Secrets (not in code)
- Connection strings use encrypted storage
- No hardcoded credentials in source code

### Registry Operations

- Auto-start registry entries are validated
- Only HKCU (Current User) registry is modified
- No elevated privileges required

## Reporting Security Issues

If you discover a security vulnerability:

1. **Do NOT** open a public issue
2. Email security concerns to: [security@esquetta.com](mailto:security@esquetta.com)
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

## Security Checklist for Developers

- [ ] Validate all user inputs
- [ ] Never log sensitive data (API keys, passwords)
- [ ] Use parameterized queries for database operations
- [ ] Validate file paths before operations
- [ ] Sanitize application names before execution
- [ ] Use HTTPS for all API communications
- [ ] Validate URLs before opening in browser
- [ ] Review Process.Start calls for injection risks

## Known Security Considerations

1. **Platform-Specific Code**: Some features use platform APIs (Windows Registry, P/Invoke) that have platform-specific security models
2. **Process Execution**: Opening applications inherently involves executing processes - input validation is critical
3. **File System Access**: The agent can read/write files - path validation restricts operations to safe directories

## Security Updates

| Date | Change |
|------|--------|
| 2026-01-28 | Added SecurityUtilities class with path traversal and command injection protection |
| 2026-01-28 | Removed sensitive data logging from AI services |
| 2026-01-28 | Added file extension validation |

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CWE/SANS Top 25](https://cwe.mitre.org/top25/)
- [Microsoft Security Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/security/)
